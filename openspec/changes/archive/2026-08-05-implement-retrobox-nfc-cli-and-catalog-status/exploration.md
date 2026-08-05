## Exploration: implement-retrobox-nfc-cli-and-catalog-status

### Current State

The RetroBox solution has a placeholder `nfc` command and a partial NFC/serial story, but no user-facing `nfc read` or `nfc write` implementation. The building blocks are:

- `RetroBoxArduinoSerialProtocol` in Core already builds `WRITE <id>,<mode>` and parses `INSERT`, `EJECT`, and `ERROR` events.
- `RetroBoxFloppyImporter` owns the only path for getting a floppy into the catalog (scratch → cataloged + YAML entry).
- `RetroBoxConfigStore` persists `config.yaml`, `vms.yaml`, and `floppies.yaml` with atomic save + backup behavior.
- The ESP8266 firmware implements `WRITE`, `TAGID`, `INSERT`, and `EJECT` commands, but **does not implement `READ` or `PING`**. The design doc (`docs/superpowers/specs/2026-07-28-esp8266-floppy-controller-design.md`) calls for `READ`, `WRITE`, `PING`, and `STATUS`, with `PING` returning `PONG`.
- GitHub issue #16 clarifies that `catalog status` means adding an `nfc: true` field to the floppy catalog entry after a successful write, not a separate `catalog status` subcommand.
- No serial-port client abstraction exists in Core or Cli yet.

### Affected Areas

- `src/RetroBox.Core/RetroBoxCatalogModels.cs` — `RetroBoxFloppy` needs an `Nfc` or `IsNfcProvisioned` property to store catalog status.
- `src/RetroBox.Core/RetroBoxConfigStore.cs` — save/load of the new floppy field is automatic via YAML, but validation may need to tolerate/validate it.
- `src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs` — `BuildReadCommand()` emits `READ`, which the firmware does not implement; needs update/removal per issue #16, and PING/TAGID response parsing may need to live here.
- `src/RetroBox.Core/` (new file) — serial transport abstraction and NFC client (e.g., `IRetroBoxNfcClient` / `RetroBoxNfcSerialClient`) using `System.IO.Ports`.
- `src/RetroBox.Core/` (new file) — NFC write service that validates catalog membership, sends the command, and flips `nfc: true` on success.
- `src/RetroBox.Cli/CliCommandFactory.cs` — replace placeholder `nfc` command with `nfc read` and `nfc write` subcommands, plus `--port` option.
- `src/RetroBox.Cli/Program.cs` — unchanged (thin wrapper), but command wiring affects it indirectly.
- `firmware/retrofloppy-esp8266/RetroFloppyCommandParser.cpp` and `RetroFloppyCommandHandler.cpp` — if PING/PONG is required by the proposal, firmware needs `PING` parsing and `PONG` response.
- `tests/RetroBox.Tests/CliHelpSmokeTests.cs` — `nfc --help` already tested; adding subcommands must keep it green.
- `tests/RetroBox.Tests/` (new tests) — transport doubles, NFC client tests, and CLI integration tests.

### Approaches

1. **Core NFC service + serial client, CLI is a thin wrapper**
   - Add `IRetroBoxNfcClient` with `WriteAsync`, `ReadTagIdAsync`, `PingAsync`, etc.
   - Add `System.IO.Ports` to `RetroBox.Core` and implement `RetroBoxNfcSerialClient`.
   - Add `RetroBoxNfcWriter` (Core) that loads the catalog, validates ID/mode, calls the client, and updates `nfc: true` on `OK`.
   - Add `nfc read` (maps to `TAGID`/UID) and `nfc write` subcommands in `CliCommandFactory`.
   - Pros: Keeps CLI thin and Core testable; matches existing `IRetroBoxFloppyControlClient` pattern; fake transport can be injected for tests.
   - Cons: Adds `System.IO.Ports` to Core; AOT/native publish compatibility must be verified; serial port disposal/timing logic can be tricky.
   - Effort: Medium

2. **CLI owns serial transport, Core keeps only protocol builders**
   - Core continues to only build/parse serial lines; CLI adds `SerialPort` usage directly.
   - Pros: Keeps Core free of I/O dependencies; matches the current "daemon uses Console.In/Out" style for the event loop.
   - Cons: Harder to unit-test the write-success → catalog-update flow without spinning up the CLI; duplicates transport concepts between CLI and future daemon needs; violates the existing Core-owns-domain pattern.
   - Effort: Medium-High

3. **Daemon exposes NFC operations, CLI talks to daemon**
   - Extend the daemon with NFC commands and have the CLI send requests to it.
   - Pros: Single owner of the serial port; no concurrent CLI/daemon access conflicts.
   - Cons: Large architectural change; not requested in issue #16; daemon currently reads from `Console.In` and is not designed for RPC.
   - Effort: High

### Recommendation

Choose **Approach 1**: put the NFC serial client and write service in Core, keep CLI as a thin command wrapper. This mirrors the established `IRetroBoxFloppyControlClient` / `RetroBoxFloppyControlClient` split, keeps the catalog-update logic unit-testable with injected transports, and preserves the Core/Cli separation. It also gives the proposal room to decide whether `nfc read` maps to `TAGID` (UID) or is deferred/removed.

### Risks

- **Firmware/command mismatch**: `BuildReadCommand()` currently emits `READ`, which the firmware does not implement. The proposal must explicitly decide whether to implement firmware `READ`, remove `BuildReadCommand()`, or repurpose `nfc read` to `TAGID`.
- **PING/PONG not implemented**: The user referenced PING/PONG as a health check, but the current firmware does not support it. Adding it requires both firmware and C# protocol changes.
- **AOT/System.IO.Ports**: `System.IO.Ports` on Linux with Native AOT may require additional runtime configuration or trimming annotations. The `publish-linux-x64` task must be exercised before claiming the change is safe.
- **Serial port sharing**: The daemon may hold the serial port; concurrent `retrobox nfc write` could conflict. The proposal should document whether the daemon must be stopped.
- **Catalog status ambiguity**: "catalog status" is not a separate command; it is the `nfc: true` field. If a user expects `retrobox catalog status`, that needs clarification.
- **Idempotency**: Re-writing the same tag should be allowed, but `nfc: true` should remain stable. Failed writes must not clear or alter the field.
- **Hardware test gap**: Real PN532/ESP8266 behavior (timeouts, `OK` vs `ERROR` formatting, line endings) can only be validated on hardware; tests should use fakes.

### Ready for Proposal

Yes. The core scope is clear from issue #16: implement `retrobox nfc read` and `retrobox nfc write <id> --mode <ro|rw>`, with a successful write setting `nfc: true` on an already-imported floppy. The orchestrator should ask the user to confirm two decisions before design/spec:

1. Should `nfc read` map to the existing `TAGID` command (prints UID) or should firmware `READ` be implemented first?
2. Is PING/PONG a required part of this change, and if so, should the firmware PING handler be implemented in the same PR or a prerequisite PR?
