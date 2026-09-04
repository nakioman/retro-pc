# Floppy NFC disk (3D printable)

A printable 3.5" floppy for the RetroBox floppy controller: two shell halves
(`top.stl` + `base.stl`, made in Shapr3D) joined into one part, with a round
NFC tag seated in the middle of the head-window zone so the PN532 reads the
`<id>,<mode>` payload on insert.

Source: [`floppy-nfc.scad`](floppy-nfc.scad). The shells (`top.stl` +
`base.stl`) are ["3.5" Floppy Disk" by polymatt](https://www.printables.com/model/1380130-35-floppy-disk)
(CC BY 4.0), made for [this video](https://www.youtube.com/watch?v=TBiFGhnXsh8),
and are committed here under that licence. They arrive already positioned as an
assembly — the base's walls rise to z = 2.4 and the top sits exactly on that
plane — so the union produces the closed disk with no repositioning. The
rendered `floppy-nfc.stl` is generated (`mise run floppy-stl`) and stays out of
git.

## What the model does

- **Joins the halves as designed.** Head window (open, cut through), leading
  edge notches, write-protect/ID holes, hub recess, walls: all exactly as the
  source shells define them. Nothing is cut into the outline.
- **Adds the NFC tag seat, opening on the top face.** The shells are too thin
  in the window zone for a blind seat, so a short pedestal hangs from the top
  plate and the ⌀25.4 mm × 1.2 mm seat is sunk into it from above. The sticker
  ends up 0.4 mm below the label field with a 0.6 mm floor behind it, and the
  pedestal reaches no further down than that floor — its underside stays a full
  millimetre inside the bottom face, so nothing shows on the back: through the
  open window you only see it deep inside, like the media of a real disk. Seat
  centre is 16 mm behind the leading edge on the disk centreline — the
  placement the bench validated. `tag_face = "bottom"` mirrors the seat onto
  the back face; `tag_seat = false` gives the raw union.

## Bill of materials

| Part | Notes |
| --- | --- |
| Round NTAG21x / MIFARE Ultralight sticker, 25 mm | Payload is raw `<id>,<mode>` bytes in pages 4–11 (ADR 0004). |
| ~10 g of PLA or PETG | **Not** carbon-filled or metallic filament — the filler shields the antenna and the tag stops reading. |

## Render and print

```bash
mise run floppy-stl
```

- Flat on the bed, tag seat up (back face on the bed), no supports, 0.15 mm
  layers.
- The shell is hollow like a real floppy (1.2 mm internal gap), so the top
  plate prints as a bridge anchored on the walls and the pedestal. Enable good
  bridging/cooling; a slight sag inside is cosmetic only.
- Elephant-foot compensation on — a splayed first layer is the usual reason a
  print that measures right still binds in the slot.

## First print

The seat is blind, so it matters which face the antenna looks at. Stick the tag
in, insert the disk, and watch the firmware:

```bash
mise run nfc-test -- monitor
```

`INSERT <id>,<mode>` is a good read; `ERROR TAG not read` is not. If it does
not read, set `tag_face = "bottom"` and print again — the seat mirrors onto the
back face.

Fit checklist:

- [ ] Slides in and out by hand; the tag reads only once the blank is fully
      seated (`INSERT` fires), and stops reading when it is pulled out
      (`EJECT` follows after the ~⅓ s polling hysteresis).
- [ ] The drive's shutter mechanism runs free through the open window band.
- [ ] `mise run nfc-test -- cycles 10` reports 10 ok, 0 missed.

## Programming the tag

On the appliance, program the tag from the web panel: put the blank in the
drive and use the panel's drive section, which writes it over the serial
connection the daemon already holds and records the tag's UID in the catalog
alongside `nfc: true`.

The commands below are for a bench rig with no daemon running — they need the
serial port to themselves, and neither of them records the tag's UID, so a
floppy tagged this way is left without the UID the panel uses to spot a tag
that already belongs to another disk.

```bash
mise run cli -- nfc write <id>            # looks the id up in floppies.yaml
mise run nfc-test -- write <id>,<mode>    # raw payload, no catalog lookup
```

The id must be lowercase letters, digits, and single hyphens; the mode is `ro`
or `rw` — `RetroBoxArduinoSerialProtocol` rejects anything else on insert.

## Parameters worth knowing

| Parameter | Default | Why you would change it |
| --- | --- | --- |
| `tag_diameter` | `25` | Match your sticker. |
| `tag_offset_from_leading_edge` | `16` | Re-aim the tag at the antenna if the module is not over the head window. |
| `tag_face` | `"top"` | Which face the seat opens on — the face the antenna looks at. |
| `tag_seat` | `true` | `false` renders the raw union of the two shells. |
| `pedestal_margin` | `2` | How far the solid backing extends beyond the seat. |

## Known constraint: shielding

The drive chassis is all-metal and blocks the antenna, which is why the bench
unit runs with the drive's outer cover removed and the tag placed at the head
opening (`docs/floppy-controller-wiring.md`). This disk keeps that geometry.
Reinstalling the metal cover will break reads no matter how the disk is printed.
