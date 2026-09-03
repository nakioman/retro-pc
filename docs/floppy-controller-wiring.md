# Floppy Controller Wiring

Physical wiring and mechanical validation for the modified floppy drive that
hosts the NodeMCU + PN532 NFC reader. The NFC tag doubles as the disk-present
sensor, so there is no mechanical switch to wire.

The firmware side is documented in
[`firmware/retrofloppy-esp8266/README.md`](../firmware/retrofloppy-esp8266/README.md);
this file records the physical build inside the drive and the bench results.

## Bill of materials

| Part | Role |
| --- | --- |
| NodeMCU v2 (ESP8266) | USB serial bridge to the Retro PC host and PN532 controller. |
| PN532 NFC module (I2C) | Reads the NTAG21x / MIFARE Ultralight tag glued inside a floppy shell. |
| NTAG21x / MIFARE Ultralight tag | Carries the `<id>,<mode>` payload for the disk it is glued to, and doubles as the disk-present sensor. |
| 4.7 kΩ resistors (optional) | I2C pull-ups on SDA/SCL to 3.3 V, only if reads are flaky. |

## NodeMCU pin assignment

| NodeMCU pin | GPIO | Function |
| --- | --- | --- |
| `D1` | 5 | `Wire` SCL to PN532. |
| `D2` | 4 | `Wire` SDA to PN532. |
| `3V3` | — | PN532 VCC. |
| `GND` | — | Common ground for the PN532. |
| `TX` / `RX` | 1 / 3 | USB serial to the Retro PC host (115200 baud, 8N1). |

`D0`, `D3`, `D4`, and `D8` are deliberately avoided because of ESP8266 boot
strapping constraints (see `firmware/retrofloppy-esp8266/README.md`).

## PN532 interface and board settings

- **Interface:** I2C only, 7-bit address `0x24` (`0x48` >> 1).
- **Board mode selector:**
  - Adafruit-style breakout: `SEL0 = ON`, `SEL1 = OFF`.
  - Elechouse-style module: both DIP switches to the row silkscreened as I2C.
    Labels vary between clones, so follow the silkscreen, not a remembered
    switch order.
- **Reset (`RSTPD_N` / `RST`):** left unconnected. The module is reset only by
  power-cycling the NodeMCU. If a hard reset line turns out to be necessary
  on the bench, pick a safe GPIO and update this section before changing the
  firmware.
- **`IRQ`:** unconnected. The firmware polls the PN532 instead of using the
  interrupt line.
- **I2C pull-ups:** some boards ship with populated pull-ups and some do not.
  If reads become flaky, add 4.7 kΩ pull-ups from SDA and SCL to 3.3 V. The
  bench unit did not need them.

## Disk-present detection

There is no mechanical disk-present switch. An earlier build used the drive's
repurposed media-sensor lever on `D6`, but the switch never actuated reliably
in the modified drive, so it was removed. Instead the firmware polls the
PN532 — every 100 ms with a disk seated, every 250 ms with the drive empty —
and derives the drive state from tag presence:

- Tag becomes readable → `INSERT <payload>`. The event fires only once the
  disk is seated well enough to actually read, so there is no race between
  "detected" and "readable".
- Tag absent for 3 consecutive polls (~300–400 ms) → `EJECT`. The hysteresis
  absorbs single missed reads of a seated tag.

The mechanical consequence for tag placement is stricter than before: the tag
must couple with the antenna **only when the disk is fully seated**. If a
half-inserted disk already reads, move the antenna deeper into the drive or
increase the tag–antenna distance. A floppy without a tag is invisible — it
reads as an empty drive.

## Recommended NFC tag placement

The tag is glued **inside the floppy shell, against the inner face of the
sliding shutter, with the shutter held open when the
disk is inserted — i.e. the tag sits where the magnetic read/write head
originally was, not over the magnetic media and not under the closed shutter.

This placement was reached by elimination after the metal of the drive
interfered with the PN532 antenna:

1. Over the centre of the floppy disk: the drive chassis is all-metal and
   shields the antenna, so the PN532 cannot read the tag.
2. On the inner face of the open shutter (head area): reads reliably, and the
   shutter stays open while the disk is in the drive. This is the adopted
   placement.
3. With the external metal cover of the drive reinstalled: interference
   returns and reads fail again. The bench unit therefore runs with the
   drive's outer cover removed.

**Do not** reinstall the floppy drive's metal cover. The exposed PCB is the
known working condition; covering it reintroduces the NFC shielding failure
even with the correct tag placement.

## USB routing and power plan

| Rail | Path |
| --- | --- |
| NodeMCU 3V3 | Internal regulator from USB; also feeds the PN532 `VCC`. |
| PN532 VCC | NodeMCU `3V3` pin, so the PN532 boots when the NodeMCU is USB-powered. |
| Ground | NodeMCU `GND` is the common ground for the PN532. |
| USB cable | From the NodeMCU micro-USB port straight to the Retro PC host. The host provides both power and the 115200 baud serial link on `TX`/`RX`. |

The whole modified drive is powered through the NodeMCU's USB connection; no
separate 5 V rail is wired into the floppy chassis. The PN532 draws its
current from the NodeMCU `3V3` regulator, which is rated for the module's
standby and active current. Note the PN532 now polls continuously (RF field on
every 100–250 ms depending on state), so it sits at its active current more of
the time than the switch-triggered build did; the `3V3` regulator still has
headroom. If a future build adds pull-ups or a second I2C peripheral, re-check
the budget.

## Bench results

### 20 insert/eject cycles (switch-based build, historical)

These results predate the removal of the disk-present switch: they were run on
the switch-triggered firmware, after the 5 ms debounce fix, with the same
NTAG21x tag glued in the adopted position (open shutter, drive cover removed).
The NFC-polling build should be re-validated with a fresh 20-cycle run.

| Result | Count |
| --- | --- |
| `INSERT <payload>` emitted with correct tag read | 20 / 20 |
| `EJECT` emitted on removal | 20 / 20 |
| Missed reads (`ERROR TAG not read`) | 0 |
| False positives (spurious `INSERT` with no disk) | 0 |
| Bounce-induced phantom events | 0 |

All 20 cycles succeeded with no missed reads and no false positives.

### Tag position / orientation trials

Three positions were tested with the PN532 antenna before settling on the
final placement:

| # | Position | Result |
| --- | --- | --- |
| 1 | Tag over the centre of the floppy disk | **Fail** — the all-metal drive chassis shields the antenna; the PN532 cannot read the tag. |
| 2 | Tag on the inner face of the shutter, with the shutter open (head area) | **Pass** — reliable reads, adopted placement. |
| 3 | Same placement as #2 but with the floppy drive's external metal cover reinstalled | **Fail** — the cover reintroduces shielding; reads are lost again. |

### Issues observed

- **Disk-present switch (removed):** the initial build detected the floppy
  with a repurposed lever switch on `D6`. It first needed a 5 ms Bounce2
  debounce to stop phantom events, and later proved mechanically unreliable —
  the lever did not actuate consistently in the modified drive. The switch was
  removed entirely; presence is now derived from the NFC tag being readable
  (see “Disk-present detection”).
- **NFC shielding (ongoing design constraint):** the floppy drive chassis is
  all-metal and blocks the PN532 antenna. Tag placement had to be moved off
  the disk centre, onto the open metal shutter at the head opening, and the
  drive's outer metal cover had to be removed. Reinstalling the cover
  reintroduces the failure. This is the known working condition for the bench
  unit; any future enclosure (e.g. 3D-printed plastic) should be non-metallic
  on the antenna side.

## Acceptance checklist

- [x] Documents exact NodeMCU pins used.
- [x] Documents PN532 interface mode and jumper/switch settings.
- [x] Documents how disk presence is detected (NFC polling; no switch).
- [x] Documents recommended NFC tag placement inside a floppy shell.
- [x] Documents USB routing/power plan.
- [x] Records results of 20 insert/eject cycles.
- [x] Records at least three tag position/orientation tests.
- [x] Lists any false positives, missed reads, or mechanical issues.