# 86Box Floppy Control Integration Verification

This guide closes the parent verification loop for `nakioman/retro-pc#6`.
The protocol authority remains `docs/86box-floppy-control-socket-contract.md`; this guide records how to prove the RetroBox daemon path, the .NET socket client, and the merged 86Box socket server work together.

## Local Gates

Run the repository test suite through the project task interface:

```bash
mise run test
```

Confirm the daemon command is present:

```bash
mise run cli -- daemon --help
```

For development without serial hardware, feed controller events through stdin.
Use `--floppy-control-socket` to override the socket path for a local VM:

```bash
printf 'INSERT monkey1-disk1,ro\nEJECT\n' \
  | mise run cli -- daemon \
      --config-root /data/retrobox \
      --floppy-control-socket /Users/nacho/Games/86Box/86box.socket
```

The daemon uses this socket path precedence:

1. CLI `--floppy-control-socket`
2. `floppyControlSocketPath` in `config.yaml`
3. `/run/retrobox/86box-floppy.sock`

## Live 86Box Socket Check

Ask the operator to start the target 86Box VM before this section.
For the current local build, the socket path is:

```text
/Users/nacho/Games/86Box/86box.socket
```

Set a shell variable for the examples:

```bash
SOCK="/Users/nacho/Games/86Box/86box.socket"
```

### Status For All Drives

```bash
printf '{"id":"status-all","command":"floppy.status","params":{}}\n' | nc -U "$SOCK"
```

Expected: one JSON Lines response with `ok: true` and `result.drives` containing zero-based floppy drive status objects.

### Status For Drive 0

```bash
printf '{"id":"status-0","command":"floppy.status","params":{"drive":0}}\n' | nc -U "$SOCK"
```

Expected: one JSON Lines response with `ok: true` and one drive `0` status object.

### Insert

Choose a floppy image visible to the running 86Box process:

```bash
IMG="/path/to/test.img"
printf '{"id":"insert-0","command":"floppy.insert","params":{"drive":0,"path":"'"$IMG"'","read_only":true}}\n' | nc -U "$SOCK"
```

Expected: one JSON Lines response with `ok: true`, `inserted: true`, `drive: 0`, the image path, and effective `read_only`.
Also confirm the guest observes a disk insertion or media-change state.

### Eject

```bash
printf '{"id":"eject-0","command":"floppy.eject","params":{"drive":0}}\n' | nc -U "$SOCK"
```

Expected: one JSON Lines response with `ok: true`, `inserted: false`, `path: null`, and `changed: true`.
Also confirm the guest observes eject or media-change state.

## Error Probes

### Invalid Drive

```bash
printf '{"id":"bad-drive","command":"floppy.status","params":{"drive":4}}\n' | nc -U "$SOCK"
```

Expected: a structured error response with `ok: false` and stable error code `invalid_drive`.

### Missing Image

```bash
printf '{"id":"missing-image","command":"floppy.insert","params":{"drive":0,"path":"/missing/test.img","read_only":true}}\n' | nc -U "$SOCK"
```

Expected: a structured error response with `ok: false` and stable error code `missing_image` or a more specific contract-compatible failure.

### Unknown Command

```bash
printf '{"id":"unknown","command":"floppy.nope","params":{}}\n' | nc -U "$SOCK"
```

Expected: a structured error response with `ok: false`.

### Malformed JSON

```bash
printf '{"id":"malformed","command":"floppy.status","params":{}\n' | nc -U "$SOCK"
```

Expected: the server returns a structured failure when it can parse enough request context, or closes the client connection after the malformed JSON frame as allowed by the contract.
86Box must keep running.

## VM Start Floppy Re-sync

When the daemon runs against the real floppy controller (`--serial-port`), it
polls the 86Box socket and sends `STATUS` to the firmware as soon as the socket
becomes ready. The firmware replies with `INSERT <id>` or `EJECT`, which the
daemon applies like a normal event, so floppy swaps made while 86Box was off
are loaded when the VM powers on.

To verify:

1. Start the daemon with the serial device and the local socket.
2. Power on a VM and confirm, in the daemon journal, a status request followed
   by an insert/eject diagnostic matching the physical floppy in the drive.
3. Power the VM off, swap the floppy, and power it back on: the journal shows a
   second insert/eject applying the new floppy, and the guest sees it loaded.

## Closeout Evidence

Record these observations before closing `nakioman/retro-pc#6`:

- `mise run test` result.
- `mise run cli -- daemon --help` result.
- 86Box PR/server evidence: `nakioman/86Box#2` merged.
- Live socket path tested.
- Status-all response shape.
- Insert response and guest-observed media change.
- Eject response and guest-observed media change.
- Error-probe results for invalid drive, missing image, unknown command, and malformed JSON.
- Any unsupported read-only behavior, if observed.

## Evidence From 2026-07-28

- `mise run test` passed: 91 tests, 0 failures.
- `mise run cli -- daemon --help` passed and showed `--config-root` plus `--floppy-control-socket`.
- Live socket path tested: `/Users/nacho/Games/86Box/86box.socket`.
- Status-all returned `ok: true` with four zero-based drive status objects.
- Status for drive `0` returned `ok: true` with the mounted boot image.
- Invalid drive returned `ok: false` with `error.code: invalid_drive`.
- Unknown command returned `ok: false` with `error.code: internal_failure` and message `Unknown command.`
- Eject returned `ok: true`, `inserted: false`, and `changed: true`.
- Insert with `read_only: true` returned `ok: true`, `inserted: true`, `read_only: true`, and a `wp://`-prefixed effective image path.
- Missing image returned `ok: false` with `error.code: missing_image`.
- Malformed JSON returned `ok: false`, `id: null`, and `error.code: internal_failure`.
- Guest-visible media change was confirmed by the operator after a slow eject/insert sequence with 5-second pauses.
