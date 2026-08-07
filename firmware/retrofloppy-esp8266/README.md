# retrofloppy-esp8266

ESP8266 firmware for the modified floppy drive: reads and writes the NFC tag
glued inside a floppy shell with a PN532 over I2C, and reports insert/eject
events to the Retro PC over USB serial.

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

NodeMCU D6 (GPIO12) -> disk-present switch -> NodeMCU GND
```

D1/D2 are the ESP8266 default `Wire` pins; the firmware calls `Wire.begin()`
with no arguments, so the pins are not configurable without a code change.

**Reset pin: not used.** The PN532 `RSTPD_N` / `RST` pad is left unconnected and
the module is reset only by power cycling the NodeMCU. This keeps every
boot-sensitive GPIO free and avoids the `D0`/GPIO16 limitations. If a hard reset
line turns out to be necessary on the bench, pick a pin from the safe list below
and update this section.

`IRQ` is also unconnected — the firmware polls the PN532 instead.

### Disk-present switch

`D6` is configured as `INPUT_PULLUP` and debounced with Bounce2 (5 ms interval):

- `HIGH` (switch open) = floppy inserted.
- `LOW` (switch closed to GND) = drive empty.

So the switch must be arranged to close to ground while the drive is **empty**
and open when a floppy is seated.

### Pins to avoid

Do not move the PN532 or the switch onto these without checking boot behaviour:

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

`STATUS` reports the drive's current physical state on demand, reusing the same
event lines as the unsolicited insert/eject notifications so the daemon can
re-sync a VM that just started without extra parsing. It reads the debounced
disk-present switch and, when a floppy is seated, reads the NFC tag at that
instant.

Any other non-empty line is echoed back as `ERROR <line>`.

### Events emitted by the firmware

These are unsolicited, driven by the disk-present switch:

| Event | Meaning |
| --- | --- |
| `INSERT <payload>` | A floppy was seated and its tag read successfully. |
| `ERROR TAG not read` | A floppy was seated but no readable tag was found. |
| `EJECT` | The floppy was removed. |

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

And with the disk-present switch:

```text
INSERT monkey1-disk1,ro
EJECT
```

Use `Ctrl+C` to exit the monitor. Close it before running
`mise run firmware-upload`, since both need exclusive access to the port.
