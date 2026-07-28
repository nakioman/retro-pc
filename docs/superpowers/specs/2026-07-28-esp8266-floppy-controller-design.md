# ESP8266 NFC Floppy Controller Design

## Purpose

Design the physical floppy controller for the Retro PC appliance using a NodeMCU ESP8266 module instead of a plain Arduino.

The controller lives inside or attached to a modified 3.5" floppy drive. It reads and writes NFC tags embedded in physical floppy disks, detects insert/eject, and talks to `retrobox` over USB serial. WiFi is useful for diagnostics or future tools, but the RTM control path stays USB serial so the appliance works without depending on wireless networking.

## Scope

RTM scope:

- NodeMCU ESP8266 firmware.
- PN532 NFC reader/writer.
- NFC tag format for physical floppy disks.
- Insert and eject detection.
- USB serial protocol with `retrobox`.
- Simple diagnostics over serial.
- Optional WiFi diagnostics page only if it does not complicate the main flow.

Out of RTM scope:

- Transferring floppy images through the ESP8266.
- Using the ESP8266 as a WiFi bridge for the Retro PC.
- Web UI for full catalog management.
- OTA firmware update.
- Battery operation.

## Design Decision

Use the ESP8266 as the floppy drive controller, not as the file-transfer path.

Floppy images still enter the Retro PC through the designed host path:

```text
network/Samba -> /data/floppies/scratch -> retrobox import -> /data/floppies/cataloged
```

The ESP8266 only handles physical media identity and mechanical state:

```text
physical floppy inserted
-> ESP8266 detects insert
-> PN532 reads NFC tag
-> ESP8266 sends INSERT <id>,<mode> over USB serial
-> retrobox resolves YAML and tells 86Box to insert the image
```

This keeps the reliable, large-file workflow on Linux and keeps the embedded firmware small.

## Recommended Hardware

### Controller

Use the existing NodeMCU ESP8266MOD board.

Reasons:

- already available;
- programmable through Arduino ESP8266 core;
- USB serial bridge is built into the NodeMCU board;
- 3.3V logic matches PN532 modules well;
- WiFi is available for later diagnostics.

Constraints:

- ESP8266 is 3.3V only;
- WiFi is 2.4 GHz only;
- GPIO boot pins must be treated carefully;
- serial upload/debug shares the USB connection with `retrobox`.

### NFC Reader

Recommended NFC module: PN532 breakout/module.

Use I2C for the first build unless the specific PN532 board is unreliable. SPI is the fallback if I2C proves unstable.

Recommended I2C wiring:

```text
NodeMCU 3V3 -> PN532 VCC / 3.3V
NodeMCU GND -> PN532 GND
NodeMCU D1  -> PN532 SCL
NodeMCU D2  -> PN532 SDA
NodeMCU D0  -> PN532 RST / RSTPD_N, if available
```

Notes:

- Some PN532 boards include I2C pullups; if reads are flaky, add pullups from SDA/SCL to 3.3V.
- Set the PN532 board jumpers/switches for I2C mode.
- If using an Adafruit-style PN532 breakout, I2C mode is `SEL0 = ON`, `SEL1 = OFF`.
- Keep wiring short inside the floppy drive.

Fallback SPI wiring:

```text
NodeMCU 3V3 -> PN532 VCC / 3.3V
NodeMCU GND -> PN532 GND
NodeMCU D5  -> PN532 SCK
NodeMCU D6  -> PN532 MISO
NodeMCU D7  -> PN532 MOSI
NodeMCU D8  -> PN532 SS
```

SPI is more pins, but often more robust. Be careful with D8/GPIO15 boot behavior; if the PN532 module pulls it the wrong way during boot, choose a different chip-select pin.

### NFC Tags

Recommended tag family: NTAG213, NTAG215, or NTAG216.

Preferred for RTM: NTAG213 sticker or coin tags.

Reasons:

- NFC Forum Type 2 tags;
- ISO/IEC 14443 Type A compatible;
- phone-readable if encoded as NDEF;
- NTAG213 has 144 bytes of user memory, far more than needed for `id,mode`;
- cheap and easy to source.

Avoid for RTM:

- MIFARE Classic-only tags;
- random "13.56 MHz RFID" tags without NTAG/NFC Forum Type 2 compatibility;
- metal-mount tags unless testing proves they work inside the floppy shell.

Tag physical recommendation:

- Use round or square NTAG213 adhesive stickers.
- Start with 25 mm or 30 mm tags.
- Place the tag inside the floppy shell or under the label area.
- Keep the tag away from the metal shutter if possible.
- Mount the PN532 antenna close to the expected tag location.
- Build one sacrificial test floppy first and measure reliable read distance/orientation before modifying many disks.

## NFC Payload Format

The NFC payload is a short UTF-8 text value:

```text
monkey1-disk1,ro
```

or:

```text
dos-save,rw
```

Rules:

- `id` uses the catalog ID format from `retrobox`: lowercase ASCII letters, digits, and single hyphens.
- Mode is optional.
- Missing mode means `ro`.
- Valid modes are `ro` and `rw`.
- The ESP8266 does not resolve paths.
- The ESP8266 does not know YAML.
- The ESP8266 sends the same payload to `retrobox`.

Recommended storage format on tag:

- RTM: NDEF Text record containing the payload.
- Debug fallback: raw NTAG pages containing the payload with a simple prefix, only if NDEF library support becomes annoying.

Prefer NDEF because it is easier to inspect with a phone and less surprising long-term.

## Insert And Eject Detection

The modified drive needs one stable "disk present" signal. Eject is detected as transition from present to absent.

Recommended options, in order:

1. Reuse an existing floppy drive disk-present switch if accessible.
2. Add a small lever microswitch pressed by the inserted disk.
3. Add an optical interrupter near the insertion path.
4. Use a magnetic reed/Hall sensor only if mechanical mounting is easier.

Recommended first build: lever microswitch.

Reasons:

- easy to debug with a multimeter;
- works without depending on original floppy drive electronics;
- easy to debounce in firmware;
- clear physical state.

Suggested GPIOs:

```text
NodeMCU D5 -> INSERT_PRESENT switch input
NodeMCU D6 -> optional EJECT/MECHANISM switch input
GND        -> other side of switch
```

Use internal pullups:

```text
switch open  -> HIGH
switch closed -> LOW
```

Avoid relying on ESP8266 boot-sensitive pins for switches in the first build:

- D3 / GPIO0
- D4 / GPIO2
- D8 / GPIO15

They can affect boot mode if pulled incorrectly.

## Serial Protocol

USB serial remains the RTM control channel between ESP8266 and `retrobox`.

Default serial settings:

```text
baud: 115200
line ending: \n
encoding: UTF-8 ASCII subset
```

Events sent by ESP8266:

```text
READY retrofloppy-esp8266 0.1
INSERT monkey1-disk1,ro
INSERT dos-save,rw
EJECT
ERROR unreadable
ERROR no-tag
ERROR invalid-payload
```

Commands accepted from `retrobox`:

```text
READ
WRITE monkey1-disk1,ro
WRITE dos-save,rw
PING
STATUS
```

Responses:

```text
OK
OK monkey1-disk1,ro
PONG
STATUS present=1 tag=monkey1-disk1,ro
ERR unreadable
ERR invalid-payload
ERR write-failed
```

Protocol rules:

- ESP8266 emits `READY ...` after boot.
- ESP8266 sends `INSERT ...` once per stable insertion, not continuously.
- ESP8266 sends `EJECT` once per stable removal.
- Debounce insert/eject for at least 100 ms.
- After insert, retry NFC reads for a short window, for example 2 seconds.
- If no tag is readable after retries, send `ERROR no-tag`.
- `WRITE` only writes when a disk/tag is present.
- `READ` returns the current tag payload if readable.

## Firmware Architecture

Firmware modules:

```text
main loop
  - initializes serial, GPIO, PN532, optional WiFi
  - runs state machine

disk sensor
  - debounces present/absent state
  - emits insertion/removal transitions

nfc service
  - reads NDEF text payload
  - writes NDEF text payload
  - validates payload shape

serial protocol
  - parses READ/WRITE/PING/STATUS
  - writes event and response lines

diagnostics
  - LED blink/status codes
  - optional WiFi status endpoint later
```

State machine:

```text
BOOT
  -> NO_DISK

NO_DISK
  insert detected -> READING_TAG

READING_TAG
  valid tag -> DISK_PRESENT
  no tag/read failure -> DISK_PRESENT_UNREADABLE
  eject detected -> NO_DISK

DISK_PRESENT
  eject detected -> NO_DISK
  READ command -> read current tag
  WRITE command -> write current tag

DISK_PRESENT_UNREADABLE
  eject detected -> NO_DISK
  READ command -> retry read
  WRITE command -> attempt write
```

## Programming Workflow

Recommended first workflow: Arduino CLI or Arduino IDE with ESP8266 Arduino Core.

Development setup:

1. Install Arduino CLI or Arduino IDE.
2. Install ESP8266 Arduino Core.
3. Select a NodeMCU ESP8266 board profile.
4. Install PN532 and NDEF libraries chosen during firmware implementation.
5. Compile the sketch.
6. Upload over USB serial.
7. Open serial monitor at 115200 baud.

Representative Arduino CLI flow:

```bash
arduino-cli core update-index
arduino-cli core install esp8266:esp8266
arduino-cli lib install "Adafruit PN532"
arduino-cli compile --fqbn esp8266:esp8266:nodemcuv2 firmware/retrofloppy-esp8266
arduino-cli upload -p /dev/tty.usbserial-XXXX --fqbn esp8266:esp8266:nodemcuv2 firmware/retrofloppy-esp8266
```

The exact serial device differs by OS and USB serial chip.

## Wiring Plan

First prototype wiring:

```text
NodeMCU 3V3 -> PN532 VCC
NodeMCU GND -> PN532 GND
NodeMCU D1  -> PN532 SCL
NodeMCU D2  -> PN532 SDA
NodeMCU D0  -> PN532 RST, if the PN532 board exposes reset

NodeMCU D5  -> disk-present switch
Switch GND  -> NodeMCU GND

NodeMCU D4 / built-in LED -> diagnostics LED, if useful
```

Mechanical plan:

- Mount the PN532 antenna near the tag location.
- Mount the disk-present switch so insertion closes it reliably.
- Route USB from NodeMCU to the Retro PC motherboard rear/internal USB.
- Do not power the old floppy drive motor/electronics unless needed for the mechanism.
- Keep the modified drive mechanically satisfying, but electrically simple.

## Test Plan

### Bench Test

- Flash firmware.
- Confirm serial prints `READY`.
- Confirm `PING` returns `PONG`.
- Confirm switch press sends insertion state transition.
- Confirm switch release sends `EJECT`.
- Confirm PN532 is detected.
- Confirm an NTAG213 tag can be read.
- Confirm `WRITE monkey1-disk1,ro` writes the tag.
- Confirm `READ` returns `monkey1-disk1,ro`.

### Drive Test

- Mount PN532 in floppy drive.
- Insert test floppy with tag.
- Confirm one `INSERT` event.
- Eject test floppy.
- Confirm one `EJECT` event.
- Repeat 20 insert/eject cycles.
- Test tag orientations and positions.
- Verify no false insert/eject events while tapping the drive.

### Retrobox Integration Test

- Connect ESP8266 over USB serial to Retro PC.
- Run `retrobox daemon` in debug mode.
- Insert `monkey1-disk1,ro`.
- Confirm daemon receives event.
- Confirm daemon resolves YAML.
- Confirm daemon sends `floppy.insert` to 86Box.
- Eject floppy.
- Confirm daemon sends `floppy.eject`.

## Future WiFi Uses

Good future uses:

- diagnostics page showing present/tag/status;
- phone/laptop page for `READ` and `WRITE`;
- firmware version page;
- test mode for switch/NFC alignment;
- optional OTA update after RTM.

Avoid as RTM:

- uploading floppy image files through ESP8266;
- making ESP8266 responsible for `/data/floppies`;
- making gameplay depend on WiFi.

Reason: floppy images are large compared with the ESP8266's role, and Linux already has Samba/SSH for reliable file movement.

## Open Questions

- Which exact PN532 board/module is available or should be purchased?
- Does that PN532 board have I2C pullups already?
- Which physical tag shape fits best inside the floppy shell?
- Does the chosen floppy drive have an accessible disk-present switch?
- Will the PN532 antenna read reliably through the floppy plastic at the chosen mounting point?

## Recommended First Purchase/Test List

- PN532 NFC breakout/module with I2C/SPI support.
- NTAG213 stickers, 25 mm or 30 mm.
- Small lever microswitches.
- Thin hookup wire.
- Heat-shrink tubing.
- Double-sided foam tape or printed bracket for PN532 placement.
- One sacrificial 3.5" floppy for mechanical experiments.

## References

- ESP8266 Arduino Core documentation: https://arduino-esp8266.readthedocs.io/
- Arduino CLI documentation: https://github.com/arduino/arduino-cli
- Adafruit PN532 guide: https://learn.adafruit.com/adafruit-pn532-rfid-nfc
- NXP NTAG213/215/216 product page and datasheet: https://www.nxp.com/products/NTAG213_215_216
