# Tasks: Implement RetroBox NFC CLI and Catalog Status

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ≈380–480 (mid ~430) |
| Suggested split | PR 1 (T1–T2) → PR 2 (T3–T4) → PR 3 (T5–T6) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Protocol PING/parse + catalog `Nfc` (T1–T2) | PR 1 | `mise run test` (protocol + store tests) | N/A — pure functions + temp-dir YAML | Revert protocol/model edits; CLI untouched |
| 2 | NFC client + writer (T3–T4) | PR 2 | `mise run test` (Nfc client/writer tests) | N/A — fakes only, no serial port | Delete new Core files + package ref; CLI still placeholder |
| 3 | CLI wiring + AOT verify (T5–T6) | PR 3 | `mise run test` | `mise run cli -- nfc --help`; `mise run publish-linux-x64` | Revert CliCommandFactory to placeholder `nfc` |

## Phase 1: Foundation (Protocol + Catalog)

- [x] 1.1 T1 Protocol: PING builder + response parser; remove dead READ (~68 lines). Deps: none.
  - RED `tests/RetroBox.Tests/RetroBoxArduinoSerialProtocolTests.cs`: `BuildPingCommand()`→`PING`; `ParseResponse`: `PONG`→Pong, `OK`→Ok, `ERROR not written`→Error(msg), null/empty/other→Unknown; delete `Build_read_command` test (lines 73–79).
  - GREEN `src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs`: add `BuildPingCommand()`, `NfcResponse` records, `ParseResponse(string?)`; delete `BuildReadCommand()` (lines 58–61).
- [x] 1.2 T2 Catalog `Nfc` bool + YAML round-trip (~33 lines). Deps: none.
  - RED `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs`: save with Nfc=true → yaml `nfc: true`; absent key loads false; `nfc: true` loads true.
  - GREEN `src/RetroBox.Core/RetroBoxCatalogModels.cs`: add `Nfc` bool (default false) to `RetroBoxFloppy`. No YamlContext edit (auto-maps).

## Phase 2: NFC Client

- [x] 2.1 T3 Client abstraction + serial client + fake (~124 lines). Deps: T1.
  - RED: add `RecordingNfcClient` to `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`; client tests via internal-ctor port factory — sends `PING\n` / `WRITE id,mode\n`, parses reply, open failure→`NfcPortUnavailable`.
  - GREEN: create `src/RetroBox.Core/RetroBoxNfcClient.cs` (`IRetroBoxNfcClient` PingAsync/WriteAsync, `NfcWriteResult` records, `NfcPortUnavailable`, `RetroBoxNfcSerialClient` per-call open/write/readLine/close, internal ctor + InternalsVisibleTo mirroring `RetroBoxFloppyControlClient`); add `System.IO.Ports` PackageReference to `src/RetroBox.Core/RetroBox.Core.csproj`; `mise run restore`.

## Phase 3: Writer

- [x] 3.1 T4 `RetroBoxNfcWriter` (~110 lines). Deps: T1–T3.
  - RED new `tests/RetroBox.Tests/RetroBoxNfcWriterTests.cs` (RecordingNfcClient + temp-dir store): ro→Ok→Written + persisted `nfc: true`; rw→Ok same; unknown id→NotCataloged with zero client calls; Error→WriteFailed, Nfc unchanged; `NfcPortUnavailable` propagates.
  - GREEN new `src/RetroBox.Core/RetroBoxNfcWriter.cs`: Load→find (missing→NotCataloged, no port)→Mode→`client.WriteAsync`→Ok: flip Nfc + atomic `store.Save`.

## Phase 4: CLI Wiring

- [x] 4.1 T5 `nfc read`/`write` subcommands (~150 lines). Deps: T3–T4.
  - RED new `tests/RetroBox.Tests/CliNfcCommandTests.cs` (injected fake factory): read Pong→0 "alive"; no-Pong→dead≠0; port-unavailable→actionable≠0; write Ok→0; NotCataloged→≠0 no port; WriteFailed→≠0; missing `--port`→arg error. Add `nfc read --help`/`nfc write --help` to `CliHelpSmokeTests.cs`.
  - GREEN `src/RetroBox.Cli/CliCommandFactory.cs`: replace placeholder (lines 34–36) with parent `nfc` + `read`/`write`, required `--port` both, `--config-root` on write, optional nfc-client-factory param on `CreateRootCommand`; map results via `WriteError`.

## Phase 5: Verification

- [x] 5.1 T6 Build + AOT smoke (0–10 lines). Deps: T1–T5. `mise run test` green; `mise run publish-linux-x64` (-warnaserror; note trim warnings); smoke `mise run cli -- --help` and `mise run cli -- nfc --help`.
