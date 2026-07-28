# Wire NFC Floppy Control Design

## Context

Issue #15 connects parsed NFC/Arduino floppy events to the YAML catalog and the 86Box floppy control client. The existing code already provides:

- `RetroBoxArduinoSerialProtocol` and typed serial events in `RetroBox.Core`.
- `RetroBoxCatalogData` and validated floppy catalog entries in `RetroBox.Core`.
- `IRetroBoxFloppyControlClient` for `floppy.insert`, `floppy.eject`, and `floppy.status`.
- A stub `RetroBoxDaemon` in `RetroBox.Daemon`.

Opening real serial ports remains out of scope for this issue.

## Requirements

- Use drive `0` for RTM unless configuration adds another drive.
- `INSERT id,ro` looks up `floppies[id]` and calls 86Box `floppy.insert` read-only.
- `INSERT id,rw` calls 86Box read-write only when the catalog entry allows `rw`.
- Catalog `ro` cannot be overridden to `rw` by the NFC tag.
- Catalog `rw` mounts as `ro` when the NFC tag requests `ro`.
- Unknown floppy IDs must not crash and must not call 86Box.
- `EJECT` calls 86Box `floppy.eject`.
- `ERROR ...` records diagnostic state but does not call 86Box.
- Tests cover insert `ro`, insert `rw`, denied write, unknown ID, eject, and controller error event.

## 86Box Contract Check

Issue #31's latest comment shows the live 86Box socket behavior:

- Requests are JSON Lines sent to a Unix socket, for example with `nc -U`.
- `floppy.insert` uses params `{ "drive": 0, "path": "...", "read_only": false }`.
- `floppy.eject` uses params `{ "drive": 0 }`.
- Successful insert/eject responses return an individual drive status:
  `{ "id": "...", "ok": true, "result": { "drive": 0, "inserted": true|false, "path": string|null, "read_only": bool, "busy": false, "changed": true } }`.
- `floppy.status` without `drive` returns `{ "drives": [...] }`, but this issue only needs insert/eject against drive `0`; the existing client already models individual drive status for insert/eject.

The handler should rely on `IRetroBoxFloppyControlClient` rather than constructing socket JSON directly.

## Design

Create `RetroBoxFloppyEventHandler` in `RetroBox.Daemon`.

The handler accepts `RetroBoxCatalogData`, `IRetroBoxFloppyControlClient`, and an optional `drive` value that defaults to `0`. It exposes `HandleAsync(RetroBoxArduinoSerialEvent serialEvent, CancellationToken cancellationToken = default)` and returns a small result record describing what happened.

Result shape:

- `Action`: `inserted`, `ejected`, `ignored-error`, or `failed`.
- `Message`: diagnostic text for ignored/error cases.
- `Status`: optional `RetroBoxFloppyStatus` from 86Box.

Insert handling computes effective read-only mode as:

- NFC `ro`: always `readOnly: true`.
- NFC `rw` + catalog `rw`: `readOnly: false`.
- NFC `rw` + catalog `ro`: fail before calling 86Box.

Unknown IDs fail before calling 86Box. Arduino controller error events return `ignored-error` before calling 86Box.

`RetroBoxDaemon` should gain constructor injection points for future orchestration but should not open serial ports in this issue.

## Testing

Add `RetroBoxFloppyEventHandlerTests` with a fake `IRetroBoxFloppyControlClient`.

Tests assert externally visible behavior:

- insert `ro` calls `InsertAsync(0, image, readOnly: true)`.
- insert `rw` for catalog `rw` calls `InsertAsync(0, image, readOnly: false)`.
- catalog `ro` plus NFC `rw` returns failed and makes no call.
- unknown ID returns failed and makes no call.
- eject calls `EjectAsync(0)`.
- controller error returns ignored diagnostic and makes no call.

Run verification with `mise run test`.

## Scope Boundaries

- Do not open real serial ports.
- Do not implement the 86Box socket server.
- Do not change ESP8266 firmware.
- Do not add multi-drive configuration beyond defaulting this handler to drive `0`.
