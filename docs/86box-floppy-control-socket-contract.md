# 86Box Floppy Control Socket Contract

## Purpose

This document defines the first runtime control contract exposed by the Retro PC 86Box fork. The contract is intentionally narrow: it controls mounted floppy media for an already running 86Box process.

The supported commands are:

- `floppy.insert`
- `floppy.eject`
- `floppy.status`

The socket does not launch virtual machines, edit persistent emulator configuration, or create disk images.

## Socket Path

86Box creates one Unix stream socket per running VM process:

```text
/run/retrobox/86box-floppy.sock
```

Runtime rules:

- The parent directory is created by the service manager before 86Box starts.
- The socket is owned by the same runtime user that runs 86Box.
- The socket mode is `0600` unless the deployment explicitly grants access to a dedicated local control group.
- 86Box removes any stale socket at the same path immediately before binding.
- 86Box unlinks the socket during normal shutdown.
- If the socket cannot be created, 86Box may continue running, but control clients must treat the control interface as unavailable.

Only local Unix socket clients are supported. TCP is not part of this contract.

## Framing

The protocol uses JSON Lines over the Unix stream socket.

- Each request is one UTF-8 JSON object followed by `\n`.
- Each response is one UTF-8 JSON object followed by `\n`.
- Clients may keep a connection open for multiple sequential requests.
- Requests on a single connection are processed in received order.
- A request must fit in 64 KiB including the trailing newline.
- 86Box closes the client connection after a malformed JSON frame or an oversized frame.

## Request Format

Every request has this shape:

```json
{
  "id": "req-001",
  "command": "floppy.status",
  "params": {}
}
```

Fields:

- `id`: required string. It is opaque to 86Box and is echoed in the response.
- `command`: required string. It must be one of the supported command names.
- `params`: required object. Its schema depends on `command`.

Unknown top-level fields are ignored. Unknown fields inside `params` are ignored.

## Success Response Format

Every successful response has this shape:

```json
{
  "id": "req-001",
  "ok": true,
  "result": {}
}
```

Fields:

- `id`: copied from the request.
- `ok`: always `true`.
- `result`: command-specific object.

## Error Response Format

Every failed response has this shape:

```json
{
  "id": "req-001",
  "ok": false,
  "error": {
    "code": "invalid_drive",
    "message": "Drive must be an integer from 0 through 3.",
    "details": {
      "drive": 4
    }
  }
}
```

Fields:

- `id`: copied from the request when the request id can be parsed, otherwise `null`.
- `ok`: always `false`.
- `error.code`: stable machine-readable string.
- `error.message`: human-readable diagnostic text.
- `error.details`: optional object with non-sensitive diagnostic values.

## Drive Addressing

Drive numbers are zero-based and match 86Box's internal floppy drive array.

| Contract drive | 86Box drive |
| --- | --- |
| `0` | first floppy drive |
| `1` | second floppy drive |
| `2` | third floppy drive |
| `3` | fourth floppy drive |

Any other value returns `invalid_drive`.

## `floppy.insert`

Mounts a floppy image into one drive.

Request:

```json
{
  "id": "req-002",
  "command": "floppy.insert",
  "params": {
    "drive": 0,
    "path": "/data/floppies/disk-1.img",
    "read_only": true
  }
}
```

Parameters:

- `drive`: required integer from `0` through `3`.
- `path`: required absolute filesystem path visible to the 86Box process.
- `read_only`: optional boolean. Defaults to `false`.

Behavior:

- 86Box validates `drive` before touching emulator state.
- 86Box rejects an empty, relative, or missing `path`.
- 86Box rejects image formats that the configured floppy code cannot mount.
- 86Box rejects insertion while the target drive is actively transferring data.
- On success, 86Box closes any currently mounted image in that drive, applies the requested write-protect state, loads the new image, marks the drive as changed, and reports the resulting status.
- This command affects runtime state. Persistence to the VM configuration file is implementation-defined and must not be required by clients.

Success response:

```json
{
  "id": "req-002",
  "ok": true,
  "result": {
    "drive": 0,
    "inserted": true,
    "path": "/data/floppies/disk-1.img",
    "read_only": true,
    "busy": false,
    "changed": true
  }
}
```

## `floppy.eject`

Ejects any mounted image from one drive.

Request:

```json
{
  "id": "req-003",
  "command": "floppy.eject",
  "params": {
    "drive": 0
  }
}
```

Parameters:

- `drive`: required integer from `0` through `3`.

Behavior:

- 86Box validates `drive` before touching emulator state.
- 86Box rejects eject while the target drive is actively transferring data.
- Ejecting an already empty drive is successful and idempotent.
- On success, 86Box closes the mounted image, clears the runtime path, marks the drive empty, and reports the resulting status.
- Pending image writeback must be completed before the command returns success. If writeback fails, the command returns `internal_failure`.

Success response:

```json
{
  "id": "req-003",
  "ok": true,
  "result": {
    "drive": 0,
    "inserted": false,
    "path": null,
    "read_only": false,
    "busy": false,
    "changed": true
  }
}
```

## `floppy.status`

Reports runtime floppy state.

Request for one drive:

```json
{
  "id": "req-004",
  "command": "floppy.status",
  "params": {
    "drive": 0
  }
}
```

Request for all drives:

```json
{
  "id": "req-005",
  "command": "floppy.status",
  "params": {}
}
```

Parameters:

- `drive`: optional integer from `0` through `3`. When omitted, all drives are returned.

Success response for one drive:

```json
{
  "id": "req-004",
  "ok": true,
  "result": {
    "drive": 0,
    "inserted": true,
    "path": "/data/floppies/disk-1.img",
    "read_only": true,
    "busy": false,
    "changed": false
  }
}
```

Success response for all drives:

```json
{
  "id": "req-005",
  "ok": true,
  "result": {
    "drives": [
      {
        "drive": 0,
        "inserted": true,
        "path": "/data/floppies/disk-1.img",
        "read_only": true,
        "busy": false,
        "changed": false
      },
      {
        "drive": 1,
        "inserted": false,
        "path": null,
        "read_only": false,
        "busy": false,
        "changed": false
      }
    ]
  }
}
```

Status fields:

- `drive`: zero-based drive number.
- `inserted`: `true` when a runtime image path is mounted.
- `path`: mounted image path, or `null` when the drive is empty.
- `read_only`: current runtime write-protect state.
- `busy`: `true` when the drive is actively reading, writing, formatting, seeking, or flushing image state.
- `changed`: current runtime disk-change flag as observed by the emulator.

## Error Codes

| Code | Commands | Meaning |
| --- | --- | --- |
| `missing_image` | `floppy.insert` | The requested image path is empty, relative, does not exist, is not readable by 86Box, or cannot be opened. |
| `busy_drive` | `floppy.insert`, `floppy.eject` | The target drive is actively transferring data or flushing image state, so media cannot be changed safely. |
| `invalid_drive` | all commands | The drive parameter is missing, not an integer, or outside `0..3`. |
| `unsupported_mode` | `floppy.insert` | The requested image cannot be mounted by the configured floppy image loaders, or the request asks for an unsupported access mode. |
| `internal_failure` | all commands | 86Box encountered an unexpected runtime failure after the request passed validation. |

Malformed JSON, missing `command`, unknown `command`, missing `params`, or invalid parameter types also return `internal_failure` unless a more specific code above applies.

## Read-Only Behavior

`read_only` controls the runtime write-protect state requested for the mounted image.

Rules:

- `read_only: true` means guest writes to the mounted image must be rejected through the emulator's floppy write-protect path.
- `read_only: false` means guest writes may be allowed when the image format and host file permissions allow writes.
- If the host file or image format is read-only, 86Box may report `read_only: true` even when the request used `read_only: false`.
- `floppy.status` reports the effective runtime state, not merely the last requested value.
- Ejecting a drive clears the requested runtime write-protect state for that drive.

The contract does not guarantee copy-on-write snapshots. Clients that need image preservation must request `read_only: true`.

## 86Box Implementation Notes

The contract maps onto existing 86Box floppy runtime concepts:

- 86Box exposes four floppy slots through `FDD_NUM`.
- Runtime image paths are held in `floppyfns`.
- The user-requested write-protect state is represented by `ui_writeprot`.
- Effective image write-protect state is represented by `writeprot` and `fwriteprot`.
- Empty state is represented by `drive_empty` and an empty runtime path.
- Existing frontends mount media by closing the current image, setting write-protect intent, then calling `fdd_load`.
- Existing frontends eject media by calling `fdd_close`.

The socket implementation should use the same main-thread or emulator-thread serialization rules as existing media UI actions. It must not mutate floppy state concurrently with active floppy operations.
