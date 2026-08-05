# Proposal: Implement RetroBox NFC CLI and Catalog Status (simplified)

## Intent

Issue #16 requests an NFC provisioning workflow. This change delivers two CLI commands — `nfc read` (connectivity check via PING/PONG) and `nfc write <id>` (catalog-driven WRITE that flips `nfc: true`) — plus a Core serial NFC client and a catalog `Nfc` field. Firmware scope is ZERO: PING→PONG and WRITE are already on main.

## Scope

### In Scope
- Core `BuildPingCommand()` → `PING`; parse `PONG`, `OK`, `ERROR <msg>` responses
- `IRetroBoxNfcClient` + `RetroBoxNfcSerialClient` over `System.IO.Ports` (distinct from 86Box Unix-socket client)
- `RetroBoxNfcWriter` service: catalog lookup of `<id>` (must exist), read `Mode` from entry, send `WRITE <id>,<mode>`, on `OK` flip `Nfc=true` and persist via `RetroBoxConfigStore`
- CLI `nfc read --port <p>`: send PING, print alive/dead, exit 0 on PONG / non-zero otherwise
- CLI `nfc write <id> --port <p>`: NO `--mode` option; mode comes from catalog entry
- `Nfc` bool on `RetroBoxFloppy`; additive YAML persistence (backward-compatible)
- Optional cleanup: remove dead `BuildReadCommand()` (no firmware handler)
- Tests via `RecordingNfcClient` fake; no hardware validation

### Out of Scope (non-goals)
- Firmware READ command. Firmware STATUS command. Any new firmware work
- Image-content read/write (only existing WRITE label payload)
- Separate `catalog status` subcommand
- Daemon RPC/lockfile/serialization — detect-and-error only
- `--mode` CLI option (removed — mode comes from catalog)
- Mode-match validation (moot — no user-supplied mode)
- Hardware validation — fakes only

## Capabilities

### New
- `retrobox-nfc-cli`: `nfc read`/`write` + Core serial NFC client/service
- `nfc-catalog-status`: `nfc: true` on floppy entries

### Modified
None (no existing specs).

## Approach

Core owns `IRetroBoxNfcClient` + `RetroBoxNfcSerialClient` (`System.IO.Ports`) and `RetroBoxNfcWriter` (catalog lookup → read mode → send WRITE → flip `nfc: true` on OK). CLI is a thin wrapper. `nfc read` sends PING and reports PONG receipt. `nfc write <id>` looks up the floppy in `floppies.yaml`, reads its `Mode`, sends `WRITE <id>,<mode>`, and on `OK` marks `nfc: true`. Tests inject a fake client; no hardware tests.

## Key Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `nfc read` = connectivity check (PING→PONG), NOT tag-payload read | User clarified: "el nfc read es para saber si esta andando o no el nfc, es un check". No firmware READ needed |
| 2 | `nfc write <id>` has NO `--mode`; mode read from catalog entry | Simplifies CLI; catalog is source of truth; eliminates mode-match validation |
| 3 | Firmware scope = ZERO (PING + WRITE already on main `863c4e9`) | No new firmware work; avoids flash/deploys |
| 4 | Serial vs daemon: detect-and-error with actionable message | Lockfile adds complexity; daemon RPC refactor out of scope |

## Affected Areas

| Area | Impact |
|------|--------|
| `RetroBoxArduinoSerialProtocol.cs` | Add `BuildPingCommand`, PONG/OK/ERROR response parse; optionally remove dead `BuildReadCommand` |
| `RetroBoxCatalogModels.cs` | Add `Nfc` bool to `RetroBoxFloppy` |
| Core (new) | `IRetroBoxNfcClient`, `RetroBoxNfcSerialClient`, `RetroBoxNfcWriter` |
| `CliCommandFactory.cs` | Replace placeholder `nfc` with `read`/`write` subcommands + `--port` |
| `CliHelpSmokeTests.cs` | Keep green under new subcommand shape |
| Tests | Fake transport, client/service/CLI tests |

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| `System.IO.Ports` + Native AOT on Linux | Med | `publish-linux-x64` in CI; verify trim annotations |
| Serial contention with daemon | Med | Detect-and-error + `--help` guidance |
| Hardware-only quirks (PN532 timing, line endings) | Med | Fakes for unit tests; hardware validation deferred |
| Optional PING pre-flight on `write` could mask dead device | Low | Recommend: skip pre-flight for `write`; let WRITE itself fail if device is dead |

## Rollback

Revert PR. `nfc: true` is additive and ignored by older binaries. No migration. No firmware changes to revert.

## Dependencies

- Firmware PING + WRITE already on main (`863c4e9`, pulled into worktree)

## Success Criteria

- [ ] `nfc read --port <p>` sends PING, prints alive/dead, exit 0 on PONG / non-zero on no PONG or port error
- [ ] `nfc write <id> --port <p>` with `<id>` in `floppies.yaml` → sends `WRITE <id>,<mode-from-yaml>` → on OK marks `nfc: true`, exit 0
- [ ] `nfc write <id>` with `<id>` NOT cataloged → fails clearly with actionable message, exit non-zero
- [ ] Firmware `WRITE` `ERROR not written` → CLI surfaces error, does NOT flip `nfc`
- [ ] `mise run test` passes; `CliHelpSmokeTests` stays green
- [ ] `mise run publish-linux-x64` succeeds
