# Physical CD-ROM passthrough

The installer detects the optical drive once (`lib/hardware-detect.sh`) and
records it in `/etc/retrobox/hardware.env` (`CDROM_DEVICE`, `CDROM_STATUS`)
and `/data/retrobox/install-report.txt` (`cdrom.device`, `cdrom.status`,
`cdrom.qemu`). Both engines use that one result; no device name is guessed,
and the payload profiles stay portable.

- **86Box** gets `ioctl://<device>` written into its installed `.cfg` at
  install time and notices disc changes itself.
- **QEMU** gets the drive from the launcher (below) and is told about disc
  changes over QMP.

## QEMU VMs

`retrobox boot` appends the drive to a QEMU VM when `CDROM_STATUS=DETECTED`
and the device still exists at launch:

```text
-drive file=<CDROM_DEVICE>,if=ide,index=2,media=cdrom,format=raw
```

IDE index 2 is the secondary master; QEMU names it `ide1-cd0`. `qemu.args`
profiles never name a host device (ADR 0009). The `retrobox` user reads the
drive through the `cdrom` group.

### Disc changes

QEMU does not notice a swap in a passed-through drive: the guest keeps the old
disc until the drive is reopened. RetroBox reopens it automatically
([ADR 0010](decisions/0010-qemu-cdrom-refresh-via-udev.md)):

1. `/etc/udev/rules.d/90-retrobox-cdrom.rules` sets
   `events_poll_msecs=2000` on `sr*` drives (the kernel reports media changes
   only while polling) and, on a `change` event with `DISK_MEDIA_CHANGE=1`,
   runs `systemctl --no-block restart retrobox-cdrom-refresh@srN.service`.
2. The unit runs as `retrobox` only while `/run/retrobox/qemu-qmp.sock`
   exists, i.e. while a QEMU VM runs. 86Box VMs and the selector are left
   alone.
3. `retrobox cdrom refresh --device /dev/srN` ignores drives other than the
   installer's (symlinks resolved), waits up to 15 s while a new disc spins
   up, then over QMP:
   - disc present: `blockdev-change-medium` on `ide1-cd0` with the installer
     path, `format: raw`, `read-only-mode: read-only`. The guest sees
     `DEVICE_TRAY_MOVED` open/close and reads the new disc;
   - drive empty or tray open: `eject` on `ide1-cd0` with `force: true`.

Run it by hand to resynchronize the guest (`retrobox cdrom refresh`) and read
the history with `journalctl -u 'retrobox-cdrom-refresh@*'`.

### Validate on the appliance

With an XP (or other QEMU) VM running and a data CD in the drive:

1. Swap the disc. Within a few seconds the guest shows the new disc's label
   without any guest action; the journal shows one `Reloaded ... into
   ide1-cd0.` per swap (no repeated runs).
2. Eject without inserting another disc. The guest's drive shows as empty
   (`Drive is empty; ejected ide1-cd0.`).
3. Insert a disc and close the tray immediately; the refresh waits for the
   spin-up and reloads once.
4. Start an 86Box VM and swap the disc: no refresh unit runs
   (`systemctl status 'retrobox-cdrom-refresh@*'` shows the condition
   failed).

## 86Box validation

Use this procedure on the installed Linux appliance with the RetroBox 86Box
fork that supports Linux ioctl CD-ROM paths.

### Record the installed configuration

1. Read `/data/retrobox/install-report.txt` and record `cdrom.device` and
   `cdrom.status`. The status must be `DETECTED` for automatic configuration.
2. For each installed profile, inspect its optical entries:

   ```bash
   grep -H -E '^cdrom_[0-9][0-9]_(parameters|image_path) =' /data/vms/*/86box.cfg
   ```

   Confirm that only the first active slot (the lowest-numbered enabled slot
   whose bus is not `none`) has
   `cdrom_XX_image_path = ioctl://<cdrom.device>`. Profiles without an active
   optical slot and all later slots must be unchanged. With
   `cdrom.status=NOT_DETECTED`, no profile should gain an `ioctl://` path.

### Confirm permissions

The baseline is membership of the `retrobox` user in Linux's `cdrom` group:

```bash
id retrobox
getent group cdrom
ls -l "$(sed -n 's/^cdrom.device=//p' /data/retrobox/install-report.txt)"
```

Record the group membership and device owner/group/mode. If 86Box cannot open
the detected device despite that membership, capture the error and add the
narrowest device-specific udev or systemd permission rule that resolves it.
Record the rule's path and contents, reboot or reload it as appropriate, and
repeat every check below. Do not weaken permissions for unrelated block devices.

### Exercise real media

Use an existing DOS or Windows guest in an installed profile with the configured
drive.

1. Insert a known data CD, start 86Box, and verify that the guest can list and
   read a file from the CD. Record the guest, disc identification, command or
   application used, and result.
2. Boot or reset with no media inserted. Record the observed guest behavior
   (for example, no drive, empty drive, or a guest-visible read error).
3. While 86Box is running, eject the disc and insert a different data CD.
   Refresh or remount from the guest as required, then verify that the new
   disc is visible. Record whether the change was detected automatically and
   any guest action needed.

Keep the resulting evidence with the appliance test record: detected Linux
device, installed `ioctl://` configuration, permission result, inserted-disc
read result, no-media behavior, eject/change behavior, and any required narrow
udev/systemd rule.
