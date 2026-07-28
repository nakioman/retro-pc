# Retro PC Appliance Design

## Purpose

Build a real-feeling retro PC appliance around 86Box on a minimal Linux host. The user experience should feel like powering on a DOS-era computer, not launching an emulator from a modern desktop.

The system runs on real PC hardware with keyboard, mouse, physical CD-ROM, and a modified floppy drive. Linux stays mostly invisible. 86Box provides the actual retro machine experience.

## Experience

On power-on, the machine boots quietly with either a black screen or a minimal splash. If no key is pressed, it starts the current default 86Box VM in fullscreen.

If the Windows key is pressed during a short boot window, a minimal selector appears:

```text
Retro PC

> Pentium 100
  386SX-16
```

The selector uses arrow keys and Enter. Choosing a VM both boots it and saves it as the new default for future boots.

Normal use happens inside 86Box fullscreen. The user sees the VM BIOS, DOS, or Windows, not Linux. Maintenance is available through SSH and an emergency console path, but those are not part of the normal experience.

## Hardware Host

Target physical machine:

- Biostar H55HD motherboard
- Intel i3-540 class CPU
- 2 GB RAM
- 120 GB disk
- 4:3 iPad LCD over HDMI
- physical keyboard and mouse
- physical CD/DVD drive
- modified floppy drive with NFC reader/writer and insertion/eject detection

## Operating System

Use Debian stable minimal as an appliance base.

The root filesystem is read-only. Mutable state lives under `/data`. The system uses systemd, SSH, and only the services required to start and supervise the retro experience.

No desktop environment is installed. A minimal graphics stack is allowed if required for 86Box fullscreen and CRT shader support.

## Mutable Data Layout

```text
/data/retrobox/
  config.yaml
  vms.yaml
  floppies.yaml
  games.yaml

/data/vms/
  386sx16/
  pentium100/

/data/floppies/
  cataloged/
  scratch/

/data/snapshots/
  386sx16/
  pentium100/
```

YAML is the source of truth for configuration and catalogs. `retrobox` edits YAML in a structured way, but the files remain readable and manually editable over SSH.

`/data/floppies/scratch/` is exposed over Samba as a network drop folder for floppy images copied from another machine. Imported floppy images are moved from `scratch` into `/data/floppies/cataloged/` and registered in the YAML catalog.

## Retrobox

`retrobox` is a single .NET 10/C# project and single deployable binary with several modes and subcommands.

Examples:

```bash
retrobox boot
retrobox daemon
retrobox vm list
retrobox vm default pentium100
retrobox floppy list
retrobox nfc write monkey1-disk1 --mode ro
retrobox import floppy monkey1-disk1 --mode ro --size 720 --label "Monkey Island - Disk 1" --image "/data/floppies/scratch/monkey_island_disk_1.img"
```

Responsibilities:

- run the boot selector and default VM logic
- persist the selected default VM
- run as a daemon under systemd
- listen to the modified floppy drive over USB serial
- resolve NFC IDs through YAML catalogs
- talk to 86Box through a local control socket
- provide SSH administration commands
- prepare for future VM creation, imports, and snapshots
- import new floppies into the catalog

The .NET SDK does not need to be installed on the appliance. The binary should be published as a Linux self-contained single-file app, with Native AOT considered if dependencies allow it.

## 86Box Integration

Use the user's fork, `nakioman/86box`, as the integration target.

86Box receives a formal runtime control socket, preferably a Unix domain socket under `/run/retrobox/`. The first RTM scope covers floppy control only:

```text
floppy.insert
floppy.eject
floppy.status
```

86Box should not know about NFC, games, catalogs, or Arduino details. It only accepts explicit commands to insert, eject, and report status for floppy media.

CD-ROM remains separate from this control work. The Pentium VM uses the physical Linux CD-ROM device directly through the existing Linux ioctl support in the user's 86Box 6.0 work.

## Virtual Machines

### 386SX-16

The early machine should feel close to a modest 1990-era PC, influenced by the user's memory of owning a 286.

- CPU: 386SX-16
- RAM: 2 MB
- HDD: 54 MB
- Video: basic ISA VGA, 256 KB or 512 KB
- Sound: PC speaker
- Floppy: 3.5"
- CD-ROM: none
- Expected OS: DOS only, installed manually by the user

This machine is for early DOS games, old adventures, shareware, and pre-CD software.

### Pentium 100

The later machine should represent a good but realistic middle or upper-middle-class Argentine home PC from the mid-1990s, not an imported maximum-spec fantasy.

- CPU: Pentium 100
- RAM: 8 MB
- HDD: approximately 2.1 GB, in the spirit of a Quantum Bigfoot
- Video: Trident TGUI9440AGi 1 MB
- Sound: Sound Blaster 16
- CD-ROM: physical drive passed through from Linux
- Floppy: 3.5" through NFC-backed images
- Expected OS: DOS plus Windows 3.1, installed manually by the user

Avoid Voodoo, AWE32, large RAM, high-end MIDI, or other luxury hardware in the RTM profile unless the project later adds a separate fantasy or enhanced VM.

## Physical Media

### CD-ROM

The Pentium VM reads the real physical CD-ROM drive. The system does not rip, cache, or convert CDs to ISO for the RTM experience.

### NFC Floppies

Each physical floppy contains an NFC tag with a short editable payload:

```text
monkey1-disk1,ro
```

or:

```text
dos-save,rw
```

The tag stores an ID and optional mode, not an absolute path. If the mode is missing, `ro` is assumed.

The YAML catalog resolves the ID:

```yaml
floppies:
  monkey1-disk1:
    label: "Monkey Island - Disk 1"
    image: "/data/floppies/cataloged/monkey_island_disk_1.img"
    mode: "ro"
    size: "720K"
```

Rules:

- `ro` mounts the image read-only and is the default for game disks.
- `rw` allows writes for personal, save, utility, driver, or scratch disks.
- the catalog may forbid write access even if a tag requests `rw`.
- inserting a disk sends an insert event to `retrobox`.
- ejecting a disk sends an eject event to `retrobox`.

The Arduino or microcontroller remains simple. It sends events such as:

```text
INSERT monkey1-disk1,ro
EJECT
ERROR unreadable
```

It may also accept commands for NFC writing and reading:

```text
WRITE monkey1-disk1,ro
READ
```

## Video And CRT Target

The CRT target is a Samsung SyncMaster 3-like early 1990s VGA monitor, not a TV, arcade display, or composite video look.

Shader direction:

- VGA monitor
- subtle shadow mask
- moderate scanlines at 320x200 and 720x400
- softer scanlines at 640x480 and above
- mild curvature
- low bloom
- clean RGB
- no NTSC/composite artifacts
- legible DOS text

The graphics stack should be as small as possible while still supporting stable fullscreen and convincing CRT shader output.

## Sound

The 386SX-16 VM uses PC speaker only.

The Pentium 100 VM uses Sound Blaster 16.

General MIDI or Roland support is explicitly out of RTM scope, though it may be added later as an optional enhancement.

## RTM Scope

The first release should include:

- Debian minimal appliance with read-only root and mutable `/data`
- SSH maintenance access
- `retrobox` .NET 10 single-binary CLI/daemon
- default VM boot
- Windows-key boot selector
- fullscreen 86Box launch
- two predefined VM hardware profiles
- physical CD-ROM direct support for Pentium
- NFC floppy insert/eject flow
- YAML catalogs and config
- formal 86Box floppy control socket with insert, eject, and status

The RTM does not preinstall DOS, Windows, drivers, games, or copyrighted media. The VMs are hardware-ready and installable by the user.

## Designed For Later

These features should be considered in the design but not required for RTM:

- snapshots and restores of virtual HDDs
- `retrobox vm create`
- fuller game import workflow
- floppy batch import
- catalog browsing
- General MIDI or Roland support
- third or enhanced VM profile
- backup automation
- modern maintenance UI

Snapshots should initially require the VM to be powered off.

## Possible Post-86Box Expansion

After the 86Box PC appliance experience works reliably, the project may grow into a broader retro appliance with additional emulator families.

Possible later targets:

- Mac OS System 7
- Mac OS System 8
- Commodore 64
- Commodore Amiga

These are explicitly not part of the 86Box RTM. They should not influence the initial Linux, 86Box, floppy NFC, or VM profile design except where the choices are already neutral and reusable.

## Open Risks

- 86Box fullscreen and shader support may determine the exact minimal graphics stack.
- 86Box floppy hot-swap internals need code review before finalizing the socket command contract.
- NFC insertion and eject detection need hardware prototyping.
- Read-only floppy enforcement may require either 86Box support or a safe image handling strategy.
- Root read-only Debian layout must be tested with graphics, audio, input, serial, CD-ROM permissions, SSH, and 86Box runtime writes.
