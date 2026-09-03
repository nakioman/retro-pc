# retrofloppy-esp8266

ESP8266 firmware for the modified floppy drive: reads and writes the NFC tag
glued inside a floppy shell with a PN532 over I2C, and reports insert/eject
events to the Retro PC over USB serial.

The tag doubles as the disk-present sensor: the firmware polls the PN532
continuously, so a tag entering the field means a floppy was inserted and a
tag leaving the field means it was ejected. There is no mechanical
disk-present switch.

Target board: NodeMCU v2 (`esp8266:esp8266:nodemcuv2`).

## Hardware

### PN532 board mode

The firmware talks to the PN532 over **I2C only**, at 7-bit address `0x24`
(`0x48` shifted right by one). Set the board's mode selector to I2C before
wiring it up:

- Adafruit-style breakout: `SEL0 = ON`, `SEL1 = OFF`.
- Elechouse-style module: set the two DIP switches to the row silkscreened as
  I2C. Labels vary between clones, so follow the silkscreen rather than a
  remembered switch order.

Some boards ship with I2C pull-ups populated and some do not. If reads are
flaky, add 4.7 kΩ pull-ups from SDA and SCL to 3.3 V.

### Wiring

```text
NodeMCU 3V3       -> PN532 VCC
NodeMCU GND       -> PN532 GND
NodeMCU D1 (GPIO5) -> PN532 SCL
NodeMCU D2 (GPIO4) -> PN532 SDA
```

D1/D2 are the ESP8266 default `Wire` pins; the firmware calls `Wire.begin()`
with no arguments, so the pins are not configurable without a code change.

**Reset pin: not used.** The PN532 `RSTPD_N` / `RST` pad is left unconnected and
the module is reset only by power cycling the NodeMCU. This keeps every
boot-sensitive GPIO free and avoids the `D0`/GPIO16 limitations. If a hard reset
line turns out to be necessary on the bench, pick a pin from the safe list below
and update this section.

`IRQ` is also unconnected — the firmware polls the PN532 instead.

### Disk-present detection

There is no disk-present switch. The firmware polls the PN532 with fast-fail
activation (`setPassiveActivationRetries(0x01)`), and presence of the tag in
the field is what defines the drive state. The poll rate is asymmetric: every
100 ms while a disk is seated (`POLL_INTERVAL_INSERTED_MS`, a cheap presence
check) and every 250 ms while the drive is empty (`POLL_INTERVAL_EMPTY_MS`, a
full detect+read):

- A tag becoming readable emits `INSERT <payload>` — so the event fires only
  once the disk is seated well enough to actually read, never mid-insertion.
- A tag that is coupled but whose payload cannot be read for 4 consecutive
  polls (~1 s) emits `ERROR TAG not read` once.
- A seated tag missing **3 consecutive polls** (~300–400 ms) emits `EJECT`.
  The hysteresis absorbs the occasional single missed read of a seated tag.

The consequence to be aware of: a floppy without a tag (or with a dead tag
that does not couple at all) is indistinguishable from an empty drive.

### Pins to avoid

Do not move the PN532 onto these without checking boot behaviour:

| Pin | GPIO | Why |
| --- | --- | --- |
| `D0` | 16 | No interrupt and no internal pull-up; wired to RST for deep sleep. |
| `D3` | 0 | Must be HIGH at boot; LOW selects flash programming mode. |
| `D4` | 2 | Must be HIGH at boot; also the onboard LED. |
| `D8` | 15 | Must be LOW at boot; a module pulling it up blocks booting. |
| `TX`/`RX` | 1 / 3 | The USB serial link to the Retro PC. |

## Tag layout

Tags are NTAG21x / MIFARE Ultralight. The payload is written as **raw bytes into
pages 4 through 11** — 8 pages × 4 bytes = **32 bytes maximum** — zero-padded to
the end of the last page.

This is deliberately *not* an NDEF record, so the tags are not phone-readable as
text. Reads stop at the first `0x00` byte and trim trailing whitespace.

## Build and upload

The toolchain, the ESP8266 core, and every library are pinned in `sketch.yaml`
(profile `esp8266`), so no separate bootstrap step is needed — `arduino-cli`
installs what the profile asks for on the first compile. The PN532 and PN532_I2C
libraries are vendored under `lib/`.

```bash
mise install
mise run firmware-compile
```

Compile and upload in one command, replacing the port with the one used by the
connected board:

```bash
mise run firmware-upload -- /dev/cu.usbserial-XXXX
```

On macOS the port usually looks like `/dev/cu.usbserial-XXXX` or
`/dev/cu.SLAB_USBtoUART`.

## Serial protocol

115200 baud, 8N1, newline-terminated lines. On boot the firmware prints its
protocol version:

```text
INIT 1
```

### Commands accepted from the host

| Command | Success | Failure |
| --- | --- | --- |
| `WRITE <payload>` | `OK` | `ERROR not written` |
| `TAGID` | `Tag ID: <uid-hex>` | `ERROR no-tag-detected` |
| `STATUS` | `INSERT <payload>` (floppy present) / `EJECT` (drive empty) | `ERROR no-tag-detected` |

`WRITE` takes the payload verbatim — for RetroBox that is `<id>,<mode>`, e.g.
`WRITE monkey1-disk1,ro`. The firmware only rejects payloads longer than 32
bytes; it does not validate the `<id>,<mode>` shape, so the host is responsible
for sending a well-formed payload.

`STATUS` reports the drive's current state on demand, reusing the same event
lines as the unsolicited insert/eject notifications so the daemon can re-sync
a VM that just started without extra parsing. It answers from the state the
NFC polling loop already tracks — `INSERT <payload>` with the cached payload,
`EJECT` when empty, and `ERROR no-tag-detected` when a tag is coupled but
unreadable — so it costs no extra tag read.

Any other non-empty line is echoed back as `ERROR <line>`.

### Events emitted by the firmware

These are unsolicited, driven by the NFC presence-polling loop:

| Event | Meaning |
| --- | --- |
| `INSERT <payload>` | A tag entered the field and its payload read successfully. |
| `ERROR TAG not read` | A tag is coupled but stayed unreadable for ~1 s. |
| `EJECT` | The tag left the field for ~1 s (floppy removed). |

`INSERT`, `EJECT`, and `ERROR <message>` are the three lines the `retrobox`
daemon parses (`RetroBoxArduinoSerialProtocol`).

## Serial monitor test

```bash
arduino-cli monitor -p /dev/cu.usbserial-XXXX --config baudrate=115200
```

If `arduino-cli` is not on the shell path, run it through mise:

```bash
mise exec -- arduino-cli monitor -p /dev/cu.usbserial-XXXX --config baudrate=115200
```

Press the board's reset button after opening the monitor if the boot line does
not appear. Expected interaction with a tag held against the antenna:

```text
INIT 1
TAGID
Tag ID: 04A2B3C4D5E6F7
WRITE monkey1-disk1,ro
OK
HELLO
ERROR HELLO
```

And inserting/removing a tagged floppy (or just moving the tag in and out of
the antenna's range by hand):

```text
INSERT monkey1-disk1,ro
EJECT
```

The `EJECT` line arrives a few hundred milliseconds after the tag leaves the
field — that is the polling hysteresis, not a fault.

Use `Ctrl+C` to exit the monitor. Close it before running
`mise run firmware-upload`, since both need exclusive access to the port.
