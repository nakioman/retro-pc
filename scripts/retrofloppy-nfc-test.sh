#!/usr/bin/env bash
# Bench tester for the RetroFloppy ESP8266 controller.
#
# Speaks the same serial protocol as the retrobox daemon
# (firmware/retrofloppy-esp8266/README.md) but from a plain shell, so a flaky
# NFC read can be reproduced and measured with the daemon out of the loop.
#
# The two paths differ and that difference matters when hunting a missed read:
#   - `tagid` reads the tag on demand; `status` reports the firmware's state.
#   - `cycles` exercises the unsolicited insert/eject events the daemon
#     consumes, driven by the firmware's NFC presence polling.

set -uo pipefail

BAUD="${RETROPC_SERIAL_BAUD:-115200}"
PORT="${RETROPC_SERIAL_DEVICE:-}"
TIMEOUT=5
SETTLE=2

INSERTS=0
MISSES=0
EJECTS=0
LINE=""

# bash < 4 (the macOS system shell) rejects fractional `read -t` timeouts.
if [ "${BASH_VERSINFO[0]:-0}" -ge 4 ]; then
    DRAIN_TIMEOUT=0.3
else
    DRAIN_TIMEOUT=1
fi

if [ -t 1 ]; then
    C_RESET=$'\033[0m'
    C_DIM=$'\033[2m'
    C_OK=$'\033[32m'
    C_WARN=$'\033[33m'
    C_ERR=$'\033[31m'
else
    C_RESET=""
    C_DIM=""
    C_OK=""
    C_WARN=""
    C_ERR=""
fi

usage() {
    cat <<'USAGE'
Usage: retrofloppy-nfc-test.sh [options] <command> [args]

Options:
  -p, --port PATH      Serial device (default: autodetect, $RETROPC_SERIAL_DEVICE)
  -b, --baud RATE      Baud rate (default: 115200)
  -t, --timeout SECS   Reply timeout (default: 5)
      --no-settle      Skip the post-open wait for the board reset
  -h, --help           Show this help

Commands:
  ping                 PING -> PONG. Proves the link and the firmware are alive.
  status               STATUS -> INSERT <payload> | EJECT | ERROR <msg>.
  tagid                TAGID -> Tag ID: <uid>. Proves the PN532 sees the tag.
  write <id>,<mode>    Write a payload onto the tag under the antenna.
  raw <line>           Send an arbitrary line and print whatever comes back.
  monitor              Print every line the firmware emits (Ctrl+C to stop).
  soak [N] [DELAY]     Repeat STATUS N times (default 20) and tally the reads.
  cycles [N]           Tally N physical insert/eject cycles (default 5).

Examples:
  retrofloppy-nfc-test.sh ping
  retrofloppy-nfc-test.sh -p /dev/ttyUSB0 status
  retrofloppy-nfc-test.sh soak 30 0.5       # is the on-demand read reliable?
  retrofloppy-nfc-test.sh cycles 10         # is the insert-edge read reliable?
  retrofloppy-nfc-test.sh write monkey1-disk1,ro
USAGE
}

info() { printf '%s%s%s\n' "$C_DIM" "$*" "$C_RESET" >&2; }
warn() { printf '%s%s%s\n' "$C_WARN" "$*" "$C_RESET" >&2; }
die() { printf '%s%s%s\n' "$C_ERR" "$*" "$C_RESET" >&2; exit 1; }

detect_port() {
    local device
    for device in /dev/serial/by-id/* /dev/ttyUSB* /dev/ttyACM* \
        /dev/cu.usbserial-* /dev/cu.wchusbserial*; do
        [ -e "$device" ] || continue
        PORT="$device"
        info "Using serial device $PORT"
        return
    done
    die "No serial device found. Pass one with --port /dev/ttyUSB0"
}

# The daemon holds the port open for its whole lifetime, so anything it owns is
# invisible here (and vice versa). Say so instead of failing with EBUSY.
check_busy() {
    local holder=""

    if command -v systemctl >/dev/null 2>&1 \
        && systemctl is-active --quiet retrobox-daemon 2>/dev/null; then
        warn "retrobox-daemon is running and already owns $PORT."
        warn "Stop it first: sudo systemctl stop retrobox-daemon"
    fi

    if command -v fuser >/dev/null 2>&1; then
        holder="$(fuser "$PORT" 2>/dev/null | tr -s ' ')"
    elif command -v lsof >/dev/null 2>&1; then
        holder="$(lsof -t "$PORT" 2>/dev/null | tr '\n' ' ')"
    fi

    case "$holder" in
        ''|' ') ;;
        *) warn "Another process is holding $PORT (pids:$holder)" ;;
    esac
}

open_port() {
    [ -n "$PORT" ] || detect_port
    [ -e "$PORT" ] || die "Serial device not found: $PORT"
    check_busy

    exec 3<>"$PORT" \
        || die "Cannot open $PORT. Permissions? Try: sudo usermod -aG dialout $USER"

    stty raw -echo -hupcl clocal -crtscts -ixon "$BAUD" <&3 \
        || die "Cannot configure $PORT at $BAUD baud"

    if [ "$SETTLE" -gt 0 ]; then
        info "Opening the port resets the ESP8266; waiting ${SETTLE}s for INIT..."
        sleep "$SETTLE"
    fi

    drain
}

# Reads one non-empty line into $LINE. The firmware terminates with CRLF and
# sendInit() emits an extra blank line, so both are stripped here.
recv() {
    local timeout="$1" line
    while IFS= read -r -t "$timeout" line <&3; do
        line="${line%$'\r'}"
        [ -n "$line" ] || continue
        LINE="$line"
        return 0
    done
    LINE=""
    return 1
}

drain() {
    while recv "$DRAIN_TIMEOUT"; do
        printf '%s  <- %s%s\n' "$C_DIM" "$LINE" "$C_RESET" >&2
    done
}

send() {
    printf '%s\n' "$1" >&3
    printf '%s  -> %s%s\n' "$C_DIM" "$1" "$C_RESET" >&2
}

# Collects lines until one matches $1, echoing anything else (a late INIT, a
# stray event) so nothing is silently swallowed.
expect() {
    local pattern="$1" deadline=$((SECONDS + TIMEOUT))
    while [ "$SECONDS" -lt "$deadline" ]; do
        recv 1 || continue
        if [[ "$LINE" =~ $pattern ]]; then
            return 0
        fi
        printf '%s  <- %s%s\n' "$C_DIM" "$LINE" "$C_RESET" >&2
    done
    LINE=""
    return 1
}

print_event() {
    local line="$1" stamp
    stamp="$(date +%H:%M:%S)"
    case "$line" in
        INSERT*) printf '%s[%s] %s%s\n' "$C_OK" "$stamp" "$line" "$C_RESET" ;;
        ERROR*) printf '%s[%s] %s%s\n' "$C_ERR" "$stamp" "$line" "$C_RESET" ;;
        INIT*) printf '%s[%s] %s%s\n' "$C_DIM" "$stamp" "$line" "$C_RESET" ;;
        *) printf '[%s] %s\n' "$stamp" "$line" ;;
    esac
}

cmd_ping() {
    send PING
    if expect '^PONG$'; then
        printf '%sPONG — firmware alive on %s%s\n' "$C_OK" "$PORT" "$C_RESET"
        return 0
    fi
    printf '%sNo PONG within %ss — wrong port, wrong baud, or firmware not running%s\n' \
        "$C_ERR" "$TIMEOUT" "$C_RESET"
    return 1
}

cmd_status() {
    send STATUS
    if ! expect '^(INSERT |EJECT$|ERROR )'; then
        printf '%sNo reply within %ss%s\n' "$C_ERR" "$TIMEOUT" "$C_RESET"
        return 1
    fi
    print_event "$LINE"
    case "$LINE" in
        INSERT*) return 0 ;;
        EJECT) return 0 ;;
        *) return 1 ;;
    esac
}

cmd_tagid() {
    send TAGID
    if ! expect '^(Tag ID: |ERROR )'; then
        printf '%sNo reply within %ss%s\n' "$C_ERR" "$TIMEOUT" "$C_RESET"
        return 1
    fi
    print_event "$LINE"
    case "$LINE" in
        'Tag ID: '*) return 0 ;;
        *) return 1 ;;
    esac
}

cmd_write() {
    local payload="${1:-}"
    [ -n "$payload" ] || die "write needs a payload, e.g. write monkey1-disk1,ro"
    [ "${#payload}" -le 32 ] \
        || die "Payload is ${#payload} bytes; the tag holds 32 (pages 4-11)"

    case "$payload" in
        *,ro|*,rw) ;;
        *) warn "Payload is not <id>,<mode>; the daemon will reject it on insert" ;;
    esac

    send "WRITE $payload"
    if ! expect '^(OK$|ERROR )'; then
        printf '%sNo reply within %ss%s\n' "$C_ERR" "$TIMEOUT" "$C_RESET"
        return 1
    fi
    print_event "$LINE"
    [ "$LINE" = "OK" ]
}

cmd_raw() {
    local line="$*"
    [ -n "$line" ] || die "raw needs a line to send"
    send "$line"
    local deadline=$((SECONDS + TIMEOUT))
    while [ "$SECONDS" -lt "$deadline" ]; do
        recv 1 || continue
        print_event "$LINE"
    done
}

cmd_monitor() {
    info "Monitoring $PORT. Insert and remove a floppy. Ctrl+C to stop."
    while true; do
        recv 3600 || continue
        print_event "$LINE"
    done
}

cmd_soak() {
    local count="${1:-20}" delay="${2:-1}" index
    local ok=0 notag=0 ejected=0 timeouts=0

    info "STATUS x$count every ${delay}s — keep a floppy seated to test the read."
    for ((index = 1; index <= count; index++)); do
        send STATUS
        if ! expect '^(INSERT |EJECT$|ERROR )'; then
            timeouts=$((timeouts + 1))
            printf '%s%3d/%d  <timeout>%s\n' "$C_ERR" "$index" "$count" "$C_RESET"
        else
            case "$LINE" in
                INSERT*)
                    ok=$((ok + 1))
                    printf '%s%3d/%d  %s%s\n' "$C_OK" "$index" "$count" "$LINE" "$C_RESET"
                    ;;
                EJECT)
                    ejected=$((ejected + 1))
                    printf '%3d/%d  %s (no disk seated)\n' "$index" "$count" "$LINE"
                    ;;
                *)
                    notag=$((notag + 1))
                    printf '%s%3d/%d  %s%s\n' "$C_ERR" "$index" "$count" "$LINE" "$C_RESET"
                    ;;
            esac
        fi
        sleep "$delay"
    done

    printf '\nSTATUS reads: %d ok, %d failed reads, %d empty-drive, %d timeouts (of %d)\n' \
        "$ok" "$notag" "$ejected" "$timeouts" "$count"
    [ "$notag" -eq 0 ] && [ "$timeouts" -eq 0 ]
}

cycles_summary() {
    local total=$((INSERTS + MISSES))
    printf '\nInsert events: %d ok, %d missed (of %d); %d ejects\n' \
        "$INSERTS" "$MISSES" "$total" "$EJECTS"
    if [ "$MISSES" -gt 0 ]; then
        printf '%sINSERT only fires once the payload actually reads, so a miss\n' "$C_WARN"
        printf 'here means the tag never became readable in the seated position\n'
        printf '(antenna coupling), not a timing race.%s\n' "$C_RESET"
    fi
}

cmd_cycles() {
    local target="${1:-5}"

    trap 'cycles_summary; exit 0' INT
    info "Insert and remove a floppy $target times. Ctrl+C to stop early."
    while [ $((INSERTS + MISSES)) -lt "$target" ]; do
        recv 3600 || continue
        print_event "$LINE"
        case "$LINE" in
            INSERT*) INSERTS=$((INSERTS + 1)) ;;
            'ERROR TAG not read') MISSES=$((MISSES + 1)) ;;
            EJECT) EJECTS=$((EJECTS + 1)) ;;
        esac
    done
    trap - INT

    cycles_summary
    [ "$MISSES" -eq 0 ]
}

main() {
    while [ $# -gt 0 ]; do
        case "$1" in
            -p|--port) PORT="${2:-}"; shift 2 ;;
            -b|--baud) BAUD="${2:-}"; shift 2 ;;
            -t|--timeout) TIMEOUT="${2:-}"; shift 2 ;;
            --no-settle) SETTLE=0; shift ;;
            -h|--help) usage; exit 0 ;;
            --) shift; break ;;
            -*) usage >&2; die "Unknown option: $1" ;;
            *) break ;;
        esac
    done

    [ $# -gt 0 ] || { usage >&2; exit 1; }

    local command="$1"
    shift

    case "$command" in
        ping|status|tagid|write|raw|monitor|soak|cycles) ;;
        *) usage >&2; die "Unknown command: $command" ;;
    esac

    open_port
    trap 'exec 3>&-' EXIT

    "cmd_$command" "$@"
}

main "$@"
