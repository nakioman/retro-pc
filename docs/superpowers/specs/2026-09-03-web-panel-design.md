# Web panel for floppy library and NFC assignment

Date: 2026-09-03
Status: Approved — ready for implementation planning

## Context

Today the only ways to get a floppy image onto the appliance are the Samba
scratch share plus `retrobox import` over SSH, and the only way to assign a
disk to a physical NFC floppy is `retrobox nfc write` on the console. Both
require a terminal on a machine whose primary display is occupied by a
fullscreen 86Box VM.

This spec designs a LAN-reachable web panel that manages the floppy library
(upload, delete, organize by game, fetch box art) and assigns cataloged
floppies to physical NFC tags.

### Constraints discovered during design

These are properties of the existing system that shape the design. Each one
was verified against the code, not assumed.

1. **The daemon owns the serial port exclusively.**
   `RetroBoxSerialDeviceRunner.OpenAsync` opens the `SerialPort`, and
   `retrobox nfc write` opens *the same port* through `RetroBoxNfcSerialClient`.
   With `retrobox-daemon.service` running, a second process cannot write tags.
   Anything that writes NFC must go through the daemon.

2. **Native AOT rules out Blazor and SignalR.**
   `mise run publish-linux-x64` uses `-p:PublishAot=true -warnaserror`.
   ASP.NET Core supports Minimal APIs under AOT; Blazor Server, Razor
   components, MVC and SignalR are not supported. See ADR
   [0001](../../decisions/0001-native-aot-binary.md).

3. **A missing file in the catalog bricks the boot path.**
   `RetroBoxConfigStore.Validate` throws when any floppy's `image` does not
   exist on disk, and `Load()` is called by both `retrobox daemon` and
   `retrobox boot`. Deleting an image before removing its catalog entry leaves
   the appliance unbootable except through the `retropc.norun=1` GRUB entry.

4. **The daemon's read loop cannot currently see command responses.**
   `RetroBoxDaemon.RunAsync` parses every incoming line with `ParseEvent`.
   `OK` and `PONG` are not valid events, so they raise
   `RetroBoxArduinoSerialProtocolException` and set `exitCode = 1`. Worse,
   `ERROR ` is a prefix for both an event and a response
   (`RetroBoxArduinoSerialProtocol.cs:47` and `:95`).

5. **`WRITE` produces no follow-up event.**
   The firmware answers `OK` or `ERROR not written`, then calls
   `refreshInsertedPayload()`, which updates a cached value and emits nothing
   (`RetroFloppyApp.cpp:96-105`). The daemon is not notified that the tag
   changed.

6. **A blank tag never produces an `INSERT` event.**
   `readTag` returns `READ_FAILED` for an unwritten tag, and the firmware
   settles into `UNREADABLE` state. Assigning a *new* floppy to a *new* tag —
   the primary use case — is therefore invisible to the event stream.

7. **`TAGID` does report the tag UID.**
   The verb is in the firmware parser table and answers `Tag ID: <HEXUID>`
   (uppercase hex, no separators) or `ERROR no-tag-detected`
   (`RetroFloppyCommandHandler.cpp:22-30`). `RetroBoxArduinoSerialProtocol`
   does not know this response yet.

8. **The appliance may have no internet.**
   WiFi is optional and configured on first boot. Anything fetched from the
   network must be cached locally, and the panel must be fully usable offline.

## Decisions

| # | Decision | Rationale |
| --- | --- | --- |
| D1 | The web panel is hosted **inside the daemon process** | Constraint 1: one owner of the serial port, no IPC to design |
| D2 | **Minimal API + embedded static HTML/JS**, no Blazor | Constraint 2: keeps the AOT pipeline and the single-binary deployment |
| D3 | The serial port becomes **optional** for the daemon | Without a floppy controller the panel must still work |
| D4 | Tag assignment writes the **catalog id**, not a UID lookup | Preserves the existing tag format; no firmware or protocol change to the payload |
| D5 | The **tag UID is recorded** in the catalog | Enables the "this tag is already assigned" warning |
| D6 | Cover art is **downloaded and cached**, never hotlinked | Constraint 8 |
| D7 | **No authentication.** The panel is LAN-trusted | Explicit owner decision; see Assumptions |

## Non-goals

- Authentication, TLS, or user accounts (D7).
- Replacing the Samba scratch share. It keeps working unchanged.
- Managing VM profiles, CD-ROM passthrough, or boot configuration.
- Editing floppy image *contents*.
- Any change to the NFC tag payload format (see ADR
  [0004](../../decisions/0004-nfc-raw-bytes-not-ndef.md)).
- Automatic cover matching without user confirmation.

## Architecture

One process, three assemblies, with the dependency direction arranged so the
web layer never reaches the hardware:

```
RetroBox.Cli   (Microsoft.NET.Sdk + FrameworkReference Microsoft.AspNetCore.App)
  │   retrobox daemon --serial-port … --web-port 8080
  ├──► RetroBox.Web    (new)  ──► RetroBox.Core
  └──► RetroBox.Daemon        ──► RetroBox.Core
```

- **`RetroBox.Core`** gains the abstractions: `IRetroBoxDriveState` (what is in
  the drive right now) and `IRetroBoxNfcCommandChannel` (`ReadTagIdAsync`,
  `WriteTagAsync`).
- **`RetroBox.Daemon`** implements them. It remains the sole owner of the
  serial port.
- **`RetroBox.Web`** depends only on the abstractions. With no controller
  attached it receives a null implementation and the panel disables its NFC
  affordances.
- `Program.cs` wires the three together.

`RetroBox.Cli` keeps `Microsoft.NET.Sdk` and `IsAotCompatible`; it only adds
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
`mise run publish-linux-x64` is unchanged.

`retrobox daemon` gains `--web-port`, defaulting to **8080**. `--web-port 0`
disables the panel entirely, which keeps the pre-existing headless behaviour
available. The listener binds to all interfaces: the panel is useless if it is
not reachable from a phone on the LAN.

### Static assets

`index.html`, `app.js` and `app.css` are **embedded resources** in
`RetroBox.Web`, served by dedicated endpoints. This preserves the
single-binary deployment at `/opt/retrobox/retrobox` and means the installer
copies no `wwwroot`.

The source mockup loads Tailwind from `cdn.tailwindcss.com` and falls back to
`via.placeholder.com` for missing covers. **Both are removed**: on an offline
appliance the panel would render unstyled with broken images. The styling is
reimplemented as hand-written CSS — it is a small palette and a grid, which
does not justify adding a Node build step to `mise.toml`.

### systemd

`appliance/installer/payload/units/retrobox-daemon.service` changes in two
places:

- Remove `ExecCondition=/bin/sh -c 'test -e "$SERIAL_DEVICE"'`. The unit must
  start without a floppy controller so the panel is reachable (D3).
- Add `--web-port` to `ExecStart`, sourced from `/etc/retrobox/daemon.env`.

`RetroBoxDaemon.ResolveSerialDeviceOptions` already returns `null` when no port
is configured, and `RunAsync` already falls back to `Console.In`, so the
optional-serial path needs no new daemon logic.

## Serial line routing

Constraint 4 means the daemon's read loop must distinguish *events* from
*command responses*. A router is added inside the existing loop; no second
reader is opened on the port.

```
                  ┌─ command in flight? ──┐
line from serial ─┤                        │
                  │ OK / ERROR … / Tag ID: ┼──► response → TaskCompletionSource
                  │ INSERT / EJECT / INIT  ┼──► RetroBoxFloppyEventHandler
                  └────────────────────────┘
                     no command in flight:
                     every line goes to the handler (today's behaviour)
```

- A `SemaphoreSlim(1)` serializes commands. Only one is physically meaningful:
  there is one drive.
- A timeout (5 s) fails the pending command and releases the slot, so a wedged
  controller cannot hang an HTTP request forever.
- `INSERT` and `EJECT` are **never** diverted, even mid-command. The disk can
  be pulled at any moment.

### Protocol additions

`RetroBoxArduinoSerialProtocol` gains:

- `BuildTagIdCommand()` returning `TAGID`.
- `NfcResponse.TagId(string Uid)`, parsed from the `Tag ID: ` prefix.

The tag payload format is unchanged.

### Mounting after a write

Because the firmware emits nothing after `WRITE` (constraint 5), a successful
write is followed by `STATUS`. The firmware answers `INSERT <new payload>`,
which flows through the normal event path and mounts the newly assigned image
in 86Box. Assigning a tag from a phone leaves the disk mounted without
ejecting and reinserting it. This reuses the mechanism already present in
`RetroBoxDaemon.WatchSocketAsync`.

### Drive state for the UI

The daemon tracks the last `INSERT`/`EJECT` seen and exposes it through
`IRetroBoxDriveState`. Because a blank tag produces no event (constraint 6),
the panel additionally issues `TAGID` to detect a tag that is present but
carries no valid payload. The UI therefore distinguishes three states:

| State | Detected by | Panel shows |
| --- | --- | --- |
| Empty | `EJECT`, or `TAGID` → `ERROR no-tag-detected` | "No disk" |
| Known floppy | `INSERT <id>,<mode>` | The cataloged floppy |
| Unassigned tag | `TAGID` → UID, no valid payload | "Blank tag, ready to assign" |

State is published over SSE at `GET /api/drive/events`, with 2 s polling as a
fallback. SignalR is not an option (constraint 2).

## Data model

Three new files under `/data/retrobox/`. None is mandatory; an absent file
means empty, matching how `floppies.yaml` behaves today.

### `games.yaml`

```yaml
games:
  monkey-island:
    label: The Secret of Monkey Island
    cover: monkey-island.jpg      # relative to /data/retrobox/covers/
    screenScraperId: 12345        # re-fetch without searching again
    floppyIds: [mi-d1, mi-d2, mi-d3, mi-d4]
```

`RetroBoxGame` (currently dead code at `RetroBoxCatalogModels.cs:39`) is
activated with these changes:

- Add `Cover` and `ScreenScraperId`, both nullable.
- **Remove `DefaultVm`.** A VM is chosen at boot, not per disk. The property is
  read by nobody today.
- Change `init` accessors to `set` to match the other catalog records; the
  YamlDotNet static generator needs settable properties.
- Register `RetroBoxGame` and `RetroBoxGameCatalog` in `RetroBoxYamlContext`.
  Omitting this fails the AOT publish, which is the intended guard rail.

Validation rules, added to the existing `Validate`:

- Game id satisfies `RetroBoxCatalogRules.IsValidId`; label is non-empty.
- Every referenced `floppyId` exists in `floppies.yaml`.
- A floppy belongs to at most one game. Floppies in no game are valid and are
  grouped under "Ungrouped" in the UI.

**Covers are deliberately not validated.** A missing cover file renders a
placeholder. Validating it would give a decorative asset the power to throw
from `Load()` and take down both the daemon and `retrobox boot`
(constraint 3).

### `floppies.yaml`

One new field:

```yaml
floppies:
  mi-d1:
    label: Monkey Island - Disk 1
    image: /data/floppies/cataloged/mi-d1.img
    mode: ro
    nfc: true
    nfcUid: 04A13BFE      # new: UID of the physical tag
```

### `scraper.yaml` (mode 0600)

```yaml
devId: xxxx
devPassword: xxxx
ssId: xxxx
ssPassword: xxxx
regionPriority: [sp, wor, eu, us]
languagePriority: [es, en]
```

Credentials are **write-only over HTTP**: `GET /api/settings/scraper` returns
`{ "configured": true, "regionPriority": [...], "languagePriority": [...] }`
and never the secrets.

### Covers

Downloaded to `/data/retrobox/covers/<game-id>.<ext>`.

## HTTP API

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/catalog` | Games with their disks, ungrouped floppies, NFC flags |
| `GET` | `/api/drive/events` | SSE stream of drive state |
| `GET` | `/api/drive` | Current drive state (polling fallback) |
| `POST` | `/api/floppies` | Multipart upload |
| `PATCH` | `/api/floppies/{id}` | Label and `ro`/`rw` mode |
| `DELETE` | `/api/floppies/{id}` | Transactional delete |
| `POST` | `/api/nfc/write` | Write the seated tag |
| `POST` | `/api/games` | Create a game |
| `PATCH` | `/api/games/{id}` | Label and membership |
| `DELETE` | `/api/games/{id}` | Ungroup; does not delete floppies |
| `GET` | `/api/scraper/search?q=` | `jeuRecherche.php`, `systemeid=135` |
| `POST` | `/api/games/{id}/cover` | `jeuInfos.php` → download `box-2D` |
| `GET` `PUT` | `/api/settings/scraper` | Scraper settings, write-only secrets |
| `POST` | `/api/settings/scraper/test` | Credential check |

### Upload

The uploaded file is written to `/data/floppies/scratch/` and handed to
`RetroBoxFloppyImporter.Import(...)` unchanged — the same path the Samba share
already uses, including its move to `cataloged/` and its rollback on failure.

- Extension allowlist: `.img`, `.ima`, `.dsk`.
- Request size cap enforced by Kestrel.
- The catalog id is derived from the filename, slugified to satisfy
  `RetroBoxCatalogRules.IsValidId` (lowercase, digits, single hyphens), with a
  numeric suffix on collision.

### Delete — order is load-bearing

1. Remove the floppy from `games.yaml`.
2. Remove it from `floppies.yaml`.
3. `Save()` — which validates.
4. **Only then** delete the `.img`.

A failure at step 4 leaves an orphaned file: untidy but harmless. The reverse
order leaves `Validate` throwing on a nonexistent `image`, which stops both the
daemon and `retrobox boot` (constraint 3).

### Changing mode invalidates the tag

The tag stores `<id>,<mode>`. A floppy written as `ro` and later changed to
`rw` still mounts read-only until the tag is rewritten. A `PATCH` that changes
`mode` therefore sets `nfc: false` and clears `nfcUid`, and the UI marks the
floppy as needing a rewrite. Without this the panel would claim `rw` while the
VM behaves `ro`, with no visible cause.

### Writing a tag

`POST /api/nfc/write { floppyId, confirm? }`

1. `TAGID`. `ERROR no-tag-detected` → `409 no-tag-present`.
2. If the returned UID is already recorded on a **different** floppy → `409
   tag-already-assigned` with the current owner, unless `confirm: true`.
3. `WRITE <id>,<mode>` → expect `OK`.
4. Set `nfc: true` and `nfcUid` on the target floppy, and **clear `nfc` and
   `nfcUid` on the previous owner of that UID**. The tag is physical: once
   reassigned, the old floppy genuinely has no tag and the catalog must say so.
5. `STATUS`, which mounts the newly assigned image.

### Refusing to mount an unassigned floppy

A new guard in `RetroBoxFloppyEventHandler.HandleInsertAsync`, shaped like the
existing check that rejects an `rw` tag on an `ro` floppy:

```csharp
if (!floppy.Nfc) → Failed: "Floppy '<id>' has no assigned tag; rewrite it from the panel."
```

A tag claiming `mi-d1` while the catalog says `mi-d1` has no tag is a stale tag
from a floppy that was reassigned or deleted. Mounting it would mount a lie.

The guard checks `nfc` only, **not** `nfcUid`. `INSERT` events carry no UID
(constraint 7 — only `TAGID` reports it), and issuing a `TAGID` round trip on
every insertion would add serial traffic and latency to the hot path for a
case the `nfc` flag already covers. `nfcUid` is consulted at write time only.

### Cover lookup

Search is explicit and confirmed by the user; nothing is auto-matched. On
confirmation, `jeuInfos.php` returns the media list, and the `box-2D` entry is
chosen by walking `regionPriority`, then `languagePriority`, then falling back
to the first available. The image is downloaded and `screenScraperId` is
persisted so the cover can be re-fetched without searching again.

ScreenScraper requires `devid`/`devpassword` (a developer account requested
from their forum) and optionally `ssid`/`sspassword`, which govern the request
quota. PC DOS is `systemeid=135`.

## Localization

The panel ships in **Spanish (default) and English**. No dependencies: a
per-key JS dictionary, `data-i18n` attributes in the HTML, initial selection
from `navigator.language`, and a manual override persisted in `localStorage`.

API errors travel as **codes**, not prose — `no-tag-present`,
`tag-already-assigned`, `catalog-invalid`, `scraper-not-configured` — and the
frontend supplies the text. The backend keeps no message catalog.

## Error handling

Responses are flat JSON: `{ "code": "...", "message": "..." }`. Every DTO is
registered in a `JsonSerializerContext`; with `-warnaserror` the AOT publish
fails on any that is missed.

The case that matters most: **an invalid catalog must not take the panel down
with it.** `store.Load()` throws `RetroBoxCatalogException` on any
inconsistency. The web host starts regardless and serves a "catalog broken"
view carrying the validation message. Otherwise a malformed YAML leaves the
owner with neither the CLI nor the panel, and the only route back is the GRUB
recovery entry.

Other cases:

| Situation | Response |
| --- | --- |
| No scraper credentials | `409 scraper-not-configured` |
| No internet | Bounded timeout, never a hang |
| ScreenScraper quota exhausted | Surface the API's own message |
| Drive empty on write | `409 no-tag-present` |
| Controller not attached | NFC endpoints `503`, panel disables them |

## Testing

xUnit, TDD, no network and no hardware.

- **Line router** — `OK`/`ERROR` with and without a command in flight; an
  `INSERT` arriving mid-`WRITE`; the timeout path. Built on the existing
  `RetroBoxEchoTransportStream`.
- **`TAGID` parsing** — `Tag ID: 04A13BFE` and `ERROR no-tag-detected`.
- **Tag reassignment** — the previous owner is cleared; `confirm` is required.
- **Mount guard** — `nfc: false` refuses to mount.
- **Transactional delete** — a failure deleting the file leaves a consistent
  catalog, and no ordering leaves `Load()` throwing.
- **Mode change** — sets `nfc: false` and clears `nfcUid`.
- **Upload** — rejected extensions, id collisions, slugging of messy names.
- **Cover selection** — behind `IRetroBoxCoverSource` with a fake, asserting
  the `regionPriority`/`languagePriority` walk without touching the network.
- **Endpoints** — `WebApplicationFactory`.

## Implementation phases

The scope is wide enough that a single undifferentiated plan would be hard to
review. It decomposes into five phases, each independently shippable and each
leaving the appliance in a working state:

1. **Serial command channel.** The line router, the `TAGID` protocol
   additions, `IRetroBoxDriveState` and `IRetroBoxNfcCommandChannel`, the
   post-write `STATUS`, and the `nfc: false` mount guard. No web involved —
   this phase is verifiable through `mise run nfc-test` and the xUnit suite,
   and it is the highest-risk work.
2. **Web host and library management.** `RetroBox.Web`, the embedded static
   panel, optional serial in the daemon unit, and the catalog/upload/delete/
   patch endpoints. Ships a usable panel with no NFC and no games.
3. **NFC assignment.** Live drive state, `TAGID` presence detection, the
   write-tag flow with its reassignment warning, and the assign UI. Split out
   from games because nothing in it depends on games, and because until it
   ships a floppy uploaded through the panel cannot be inserted at all — the
   panel's own primary workflow produces something inert.
4. **Games grouping.** `games.yaml`, `RetroBoxGame` activation, and the grouped
   UI.
5. **Cover art.** `scraper.yaml`, the ScreenScraper client behind
   `IRetroBoxCoverSource`, and the search-and-confirm UI. It depends on games
   existing, which is why it follows them.

Localization is **not** a phase of its own. Spanish and English shipped with
the panel in phase 2, because the UI is written there and deferring the strings
would have meant writing every one of them twice.

Phase 1 is a prerequisite for phase 3, and phase 4 for phase 5. Phase 2 is
otherwise independent.

## Phase 2 prerequisites

These came out of building phase 1 and reviewing it as a whole. Each one is
unreachable today — nothing calls the command channel yet — and each one becomes
live the moment the web layer does. They are prerequisites, not nice-to-haves.

### The daemon's catalog is a snapshot, and the write flow depends on it not being one

`RetroBoxConfigStore.Load()` is called once in the CLI; the resulting
`RetroBoxCatalogData` is handed to `RetroBoxDaemon`, which builds one
`RetroBoxFloppyEventHandler` and never reloads. Trace the flow this spec
describes under "Writing a tag":

1. `POST /api/nfc/write { floppyId: "disk1" }` — `disk1` has `nfc: false`,
   because it has never been tagged. **This is the primary use case: a new
   floppy and a blank tag.**
2. `WriteTagAsync` → `OK` → the channel writes `STATUS`.
3. The web layer sets `nfc: true` and `nfcUid` in `floppies.yaml` (step 4 of
   that flow).
4. The firmware's poll loop reads the now-valid tag and emits
   `INSERT disk1,ro`.
5. The read loop reaches the handler — whose **snapshot still says
   `Nfc == false`** — and the mount guard refuses it.

The disk never mounts, until the daemon restarts. That is the exact outcome the
post-write `STATUS` exists to produce, defeated by the composition of two
individually-correct pieces.

**Required:** the event handler must read the catalog through a live accessor
(`Func<RetroBoxCatalogData>` or an `IRetroBoxCatalogSource` the web layer
invalidates on save), not a captured snapshot. The same staleness affects every
endpoint that mutates the catalog — upload, delete, `PATCH` — but the mount
guard is what makes it acute, because `Nfc` is on the hot path *and* is the one
field the panel changes at runtime.

### The orphan window needs a quarantine, and it belongs in the router

The router absorbs one late reply after a timeout, within a window. That window
defaults to the command timeout and opens at the cancel — the same instant a
retry may begin. So: dead controller → timeout → window opens → the caller
retries immediately → the controller recovers → the retry's *own* timely reply
is absorbed as the orphan → the retry times out → a fresh window opens. It heals
only on an idle gap longer than the window.

**Required:** absorption must happen while nothing else is in flight. Put the
quarantine in `RetroBoxSerialLineRouter`, which already owns the orphan slot —
the channel awaits a clear slot before `BeginCommand`. Do **not** hold the
channel's semaphore through the window instead: that would double a failing
command's latency before the caller sees its error, which cuts against the whole
reason this design has a timeout, and it would block the socket poll loop's
`SendStatusAsync` for the same period, delaying a VM's floppy re-sync.

### Write ordering is gated; reply attribution is not

Every writer to the serial line shares one semaphore, so bytes never interleave.
But the gate is released once the follow-up `STATUS` is written, while that
`STATUS`'s answer is still in flight. If the answer is `ERROR no-tag-detected`
and the panel has already issued another command, the router hands that `ERROR`
to the new command. Only `ERROR` is at risk — unambiguous replies are absorbed
or consumed — and the protocol has no request ids, so this cannot be closed by
correlation.

**Required:** either a short grace hold after writing a follow-up, or a rule
that the write endpoint does not chain a second command. Decide explicitly.

### Channel lifetime

The channel closes over the `TextWriter` from a `using var device` in the CLI's
daemon command. Anything holding the channel after `RunAsync` returns writes to
a disposed stream. If the web host outlives the read loop, that needs an
explicit contract.

### Reporting an ambiguous write result

A late `ERROR not written` answering a timed-out `WRITE` is indistinguishable
from an unprompted `ERROR`. The write endpoint should therefore not report a
definitive failure when a timeout was involved: a follow-up `TAGID` to read back
what is actually on the tag is cheap and turns an ambiguous failure into a known
state.

## Assumptions

- **The panel is unauthenticated and trusts the LAN** (D7). Anyone on the
  network can delete the library and POST scraper credentials. This was raised
  during design and accepted by the owner as consistent with the existing
  `guest ok = yes` Samba share. Credentials are never returned by the API,
  which limits the exposure to write-only.
- Traffic is plain HTTP, so scraper credentials cross the WiFi in the clear.
- The owner holds, or will request, a ScreenScraper developer account. Without
  it the rest of the panel works and cover lookup stays disabled.
- One floppy drive (`Drive = 0`), as the daemon already assumes.
