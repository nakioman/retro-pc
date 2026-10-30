# VM Profiles

Retro PC VM profiles are native emulator profiles. Each VM in `vms.yaml` names
its `engine` (`86box`, the default, or `qemu`), and that engine's native file
in the profile directory is the hardware source of truth
([ADR 0008](decisions/0008-native-profile-per-engine.md)):

| Engine | Profile file | Launched as |
| --- | --- | --- |
| `86box` | `86box.cfg` | `86box.AppImage --vmpath <profile> --rompath /opt/86Box/roms` |
| `qemu` | `qemu.args` | `qemu-system-i386 <qemu.args> <host args>` inside a bare Xorg |

RetroBox YAML refers to a profile for display and selection and declares the
VM's disks; it never describes hardware.

## Disks

`vms.yaml` lists the disk images RetroBox must create for a VM:

```yaml
vms:
  pentium100:
    label: "Pentium 100"
    path: "/data/vms/pentium100"
    disks:
      - file: hdd.raw      # relative to path
  winxp:
    label: "Windows XP"
    path: "/data/vms/winxp"
    engine: qemu
    disks:
      - file: xp.qcow2
        size: 20G          # bytes, or a K/M/G/T binary multiple
        format: qcow2      # raw (default) | qcow2
```

`retrobox vm create-disks [id]` creates every missing disk and never opens an
existing one, so installed systems are safe; the installer runs it on every
install. `raw` disks are sparse files of the exact size; `qcow2` disks need
`/opt/qemu-retrobox/bin/qemu-img` (override with `--qemu-img`), and the
command says so when it is missing.

For 86Box the `.cfg` geometry is authoritative: each declared file must match
an `hdd_NN_fn`, and its size is `sectors * heads * cylinders * 512` from
`hdd_NN_parameters`. Leave `size` out (the payload does); if you give it, it
must equal that value. 86Box disks are always raw. QEMU disks must declare a
`size`.

## QEMU profiles

`qemu.args` holds one argument per line with no shell quoting; blank lines and
lines starting with `#` are ignored. A value with spaces is just one line:

```text
-name
Windows XP
-machine
pc,accel=kvm,vmport=on
-drive
file=xp.qcow2,if=ide,index=0,media=disk,format=qcow2
```

QEMU runs with the profile as its working directory, so relative disk paths
work. The launcher adds only host-specific arguments:

- `-qmp unix:/run/retrobox/qemu-qmp.sock,server=on,wait=off`
- `-drive file=<CDROM_DEVICE>,if=ide,index=2,media=cdrom` with the optical
  drive the installer detected (`/etc/retrobox/hardware.env`, the same
  detection that writes `ioctl://` into 86Box profiles). It is attached only
  when `CDROM_STATUS=DETECTED` and the device still exists, so a removed drive
  never stops the VM from starting. Keep IDE index 2 free in the profile.

`retrobox boot` writes the complete argument list to
`/run/retrobox/qemu-session.args` and runs
`xinit /opt/retrobox/retrobox qemu-session ... -- :0 vt<N> -keeptty -nolisten tcp`
on the boot console's VT. Inside X, `qemu-session` turns off blanking
(`xset s off -dpms`), starts `matchbox-window-manager -use_titlebar no
-use_cursor no`, and runs `/opt/qemu-retrobox/bin/qemu-system-i386` with
`NO_AT_BRIDGE=1` and `LD_LIBRARY_PATH=/opt/qemu-retrobox/lib`. When QEMU exits,
X exits and the selector returns, as with 86Box. QEMU's and Xorg's stderr go
to the `retrobox-boot` journal.

The appliance installs the catalog at `/data/retrobox/vms.yaml` and profiles at
`/data/vms/<id>`. The catalog intentionally has no `defaultVm`; `retrobox boot`
selects a VM when needed. 86Box is launched with the selected profile as its
working directory, so relative disk paths remain valid; shader paths use the
absolute `/opt/86Box/shaders/crt/crt-easymode.glslp` form and monitor EDID (when
used) uses `/data/vms/<id>/syncmaster3.edid`.

## 386SX-16

Path: `profiles/386sx16`

This is the RTM modest DOS VM profile: a 386SX-16 class machine with a blank
54 MB hard disk and no installed operating system or bundled media.

Profile files:

- `86box.cfg`: native 86Box configuration.
- `hdd.raw`: blank disk (declared in `vms.yaml`, sized from `hdd_01_parameters`).
- `README.md`: setup notes and hardware summary.

## Pentium 100

Path: `profiles/pentium100`

This is the RTM mid-1990s gaming VM profile: a Pentium 100 with 8 MB of RAM,
Trident TGUI9440AGi PCI video, Sound Blaster 16, a blank approximately 2.1 GB
disk, and an ATAPI CD-ROM slot. The portable payload does not contain a host
device path. During appliance installation, a detected physical drive configures
only the first active optical slot in the installed copy with
`ioctl://<detected-device>`; no detected drive leaves profiles unchanged. See
[`cdrom-passthrough.md`](cdrom-passthrough.md) for target validation.

Profile files:

- `86box.cfg`: native 86Box configuration and hardware source of truth.
- `hdd.raw`: blank disk (declared in `vms.yaml`, sized from `hdd_01_parameters`).
- `README.md`: setup notes, hardware summary, and media policy.

## Pentium II 350

Path: `profiles/pentium2-350`

This is the Windows 98 SE VM profile: a Pentium II Deschutes 350 MHz (3.5x,
dynarec `new`) with 128 MB RAM, Voodoo3 3000 AGP with SyncMaster EDID, Sound
Blaster AWE64 Gold, and a blank ~20 GB disk (63, 255, 2434). Uses the same
`crt-easymode.glslp` shader as the other profiles.

Profile files:

- `86box.cfg`: native 86Box configuration and hardware source of truth.
- `hdd.raw`: blank disk (declared in `vms.yaml`, sized from `hdd_01_parameters`).
- `syncmaster3.edid`: 128-byte binary EDID for the CRT shader setup.
- `README.md`: setup notes, hardware summary, and media policy.
