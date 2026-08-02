# Physical CD-ROM passthrough validation

Use this procedure on the installed Linux appliance with the RetroBox 86Box
fork that supports Linux ioctl CD-ROM paths. The installer only writes a host
path after it detects a physical drive; the payload templates remain portable.

## Record the installed configuration

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

## Confirm permissions

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

## Exercise real media

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
