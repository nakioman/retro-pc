# Design: Implement RetroBox NFC CLI and Catalog Status

## Technical Approach

Add an ESP8266-serial NFC path as a sibling of the 86Box Unix-socket `IRetroBoxFloppyControlClient`. Core owns a new `IRetroBoxNfcClient` + `RetroBoxNfcSerialClient` over `System.IO.Ports`, plus a `RetroBoxNfcWriter` that does catalog lookup → read `Mode` → `WRITE <id>,<mode>` → flip `Nfc=true` on `OK`. `RetroBoxArduinoSerialProtocol` gains `BuildPingCommand` and a discriminated `ParseResponse`. The CLI replaces the placeholder `nfc` command with `nfc read`/`nfc write` plus required `--port`. Firmware scope is ZERO (PING + WRITE already on main `863c4e9`).

## Architecture Decisions

| # | Decision | Alternatives | Rationale |
|---|----------|--------------|-----------|
| 1 | Response parsing on `RetroBoxArduinoSerialProtocol` (static) | Inline in client; new parser class | Pure functions, unit-testable without `System.IO.Ports`; matches existing `ParseEvent`/`BuildWriteCommand` placement |
| 2 | `ParseResponse` returns discriminated record: `NfcResponse` = `Pong` \| `Ok` \| `Error(string)` \| `Unknown(string)` | bool + out param; exceptions for error | One type covers all `PONG`/`OK`/`ERROR <msg>`/unknown cases; caller switch is exhaustive; `Unknown` surfaces parse failure without throwing |
| 3 | Port opened per-call (open → write line + `\n` → read line → close) | Per-client long-held port | Avoids holding the serial lock between CLI invocations; pairs with detect-and-error; CLI processes are short-lived |
| 4 | `NfcPortUnavailable` exception (distinct) for busy/EACCES/timeout | Generic IOException; boolean | CLI maps to actionable "stop the daemon and retry" message; matches `RetroBoxFloppyControlException` pattern |
| 5 | `RetroBoxNfcWriter.WriteAsync(id)` returns `NfcWriteResult` = `Written` \| `NotCataloged(string id)` \| `Error(string msg)`; no throw for expected outcomes | Throw `FloppyNotCataloged`; return bool | `NotCataloged` and firmware `ERROR` are normal control flow, not exceptions; CLI switches and prints; keeps `IRetroBoxNfcClient` exceptions (port) separate from writer outcomes |
| 6 | `nfc write` does NOT PING pre-flight | Pre-flight PING then WRITE | Spec forbids required pre-flight; let `WRITE` surface dead-device failure; a warn-and-proceed pre-flight could mask a dead device |
| 7 | `Nfc` bool on `RetroBoxFloppy`, default `false`; YamlDotNet `CamelCaseNamingConvention` → key `nfc` | Default `true`; separate table | Additive, backward-compatible; older binaries ignore the key; YamlStaticContext already maps `RetroBoxFloppy` so adding the property auto-maps |
| 8 | Remove dead `BuildReadCommand()` + its single test | Leave it | No firmware `READ` handler on main; design is NFC-only; leaving dead public API invites misuse. Blast radius: 1 test (`RetroBoxArduinoSerialProtocolTests`) caller — delete that assertion |
| 9 | `nfc` parent command with `read`/`write` children | Two top-level commands `nfc-read`/`nfc-write` | Matches `vm`/`import` parent pattern; `CliHelpSmokeTests` already exercises `nfc --help`; keep `nfc --help` green |
| 10 | `RetroBox.NfcWriter` not async for catalog load/save; `client.WriteAsync` is the only await | All-async writer | `RetroBoxConfigStore.Load/Save` is synchronous; no value wrapping in Task; only the serial I/O is async |

## Data Flow

```
nfc read --port /dev/ttyUSB0
   └─► CliCommandFactory ► new RetroBoxNfcSerialClient(port)
                              └─► PingAsync ► PING\n ──► [ESP8266] ──► PONG\n
                                  └─► ParseResponse("PONG") = Pong ► "alive" exit 0

nfc write monkey1-disk1 --port /dev/ttyUSB0 [--config-root /data/retrobox]
   └─► CliCommandFactory ► new RetroBoxNfcWriter(new RetroBoxNfcSerialClient(port), new RetroBoxConfigStore(root))
                              └─► store.Load() ► find floppy[id]
                                  ├─ missing ► NotCataloged ► "id 'X' not imported; run `retrobox import floppy`" exit 1 (NO port open)
                                  └─ found  ► floppy.Mode ► client.WriteAsync(id, mode)
                                             └─► WRITE id,mode\n ──► [ESP8266] ──► OK\n / ERROR not written\n
                                                 ├─ Ok      ► set floppy.Nfc=true ► store.Save(data) ► "id written (nfc: true)" exit 0
                                                 └─ Error   ► Nfc unchanged ► "firmware error: not written" exit 1
```

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs` | Modify | Add `BuildPingCommand()` → `PING`; add `NfcResponse` record + `ParseResponse(line)`; remove `BuildReadCommand()` |
| `src/RetroBox.Core/RetroBoxNfcClient.cs` | Create | `IRetroBoxNfcClient` (`PingAsync`, `WriteAsync`), `NfcWriteResult`/`NfcResponse`, `NfcPortUnavailable` exception, `RetroBoxNfcSerialClient` over `System.IO.Ports.SerialPort` |
| `src/RetroBox.Core/RetroBoxNfcWriter.cs` | Create | `RetroBoxNfcWriter(IRetroBoxNfcClient, RetroBoxConfigStore)`; `WriteAsync(id, ct)` → `NfcWriteResult` |
| `src/RetroBox.Core/RetroBoxCatalogModels.cs` | Modify | Add `Nfc` bool (default false) to `RetroBoxFloppy` |
| `src/RetroBox.Core/RetroBox.Core.csproj` | Modify | Add `<PackageReference Include="System.IO.Ports" ... />` |
| `src/RetroBox.Cli/CliCommandFactory.cs` | Modify | Replace placeholder `nfc` with parent + `read`/`write` subcommands and required `--port`; reuse `ConfigRootOption()` on `write` |
| `tests/RetroBox.Tests/FloppyControlTestDoubles.cs` (or new `NfcTestDoubles.cs`) | Create | `RecordingNfcClient : IRetroBoxNfcClient` fake (init-style errors like `RecordingFloppyControlClient`) |
| `tests/RetroBox.Tests/RetroBoxArduinoSerialProtocolTests.cs` | Modify | Add PING + `ParseResponse` cases; remove `BuildReadCommand` test |
| `tests/RetroBox.Tests/` (new) | Create | `RetroBoxNfcWriterTests`, `CliNfcCommandTests`, catalog `Nfc` round-trip/back-compat tests |

## Interfaces / Contracts

```csharp
public abstract record NfcResponse;
public sealed record NfcPong : NfcResponse;
public sealed record NfcOk : NfcResponse;
public sealed record NfcError(string Message) : NfcResponse;
public sealed record NfcUnknown(string Line) : NfcResponse;

public abstract record NfcWriteResult;
public sealed record NfcWritten : NfcWriteResult;
public sealed record NfcNotCataloged(string Id) : NfcWriteResult;
public sealed record NfcWriteFailed(string Message) : NfcWriteResult;

public interface IRetroBoxNfcClient
{
    Task<NfcResponse> PingAsync(CancellationToken ct = default);
    Task<NfcResponse> WriteAsync(string id, string mode, CancellationToken ct = default);
}

public sealed class NfcPortUnavailable : Exception { /* port path, inner IOException/UnauthorizedAccessException */ }
```

## Testing Strategy

| Layer | What | Approach |
|-------|------|----------|
| Unit | `BuildPingCommand`, `ParseResponse` (PONG/OK/ERROR/empty/unknown) | Pure-function xUnit, no I/O |
| Unit | `RetroBoxNfcWriter` (cataloged ro→OK flips Nfc; not-cataloged no-port-open; ERROR leaves Nfc; port exception propagates) | `RecordingNfcClient` fake + temp-dir `RetroBoxConfigStore` (mirror `FloppyControlTestDoubles`) |
| Unit | CLI `nfc read`/`write` exit codes + stdout/stderr + no `System.IO.Ports` instantiation | `CliCommandFactory.CreateRootCommand(...)` with an injectable NFC client factory hook (new optional param) |
| Unit | YAML round-trip `Nfc: true`/`false`; forward-compat (no `Nfc` key → false); backward-compat (older binary ignores key) | Temp `floppies.yaml` via existing store test setup |
| Integration (CI) | Native AOT publish + smoke help | `mise run publish-linux-x64`; existing `retrobox --help` smoke step |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. The only external boundary is `System.IO.Ports` (serial I/O), which is covered by the port-unavailable detect-and-error model.

## Migration / Rollout

No migration. `Nfc` is additive; older binaries ignore the key, new binary defaults absent key to `false`. Revert PR to roll back. No firmware changes to revert.

## Open Questions

- [ ] Exact `System.IO.Ports` baud/timeout values: assume 115200 8N1, read timeout ~2s (firmware design doc default). Confirm at apply time.
- [ ] Whether `--port` should also accept an optional `--baud` (recommend: no, hard-code 115200, defer until needed).