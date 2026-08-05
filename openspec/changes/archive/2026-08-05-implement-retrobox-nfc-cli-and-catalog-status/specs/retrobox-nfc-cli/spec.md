# retrobox-nfc-cli Specification

## Purpose

The NFC CLI surface for RetroBox: two subcommands — `nfc read` (ESP8266 connectivity check via PING/PONG) and `nfc write <id>` (catalog-driven WRITE that flips `Nfc=true` on OK) — backed by a Core serial NFC client and writer service, testable via fakes. Firmware scope is zero (PING + WRITE already on main).

## ADDED Requirements

### Requirement: NFC Read Connectivity Check

The CLI `retrobox nfc read --port <p>` MUST open the ESP8266 serial port, send `PING`, and report whether `PONG` is received. On `PONG` it SHALL print an "alive" message and exit 0. On any port error or absence of `PONG` it SHALL print a "dead"/actionable message and exit non-zero. It MUST NOT attempt to read tag payload content.

#### Scenario: Live device returns PONG

- GIVEN the ESP8266 at `<p>` responds to `PING` with `PONG`
- WHEN the user runs `retrobox nfc read --port <p>`
- THEN the CLI prints an "alive" message
- AND exits with code 0

#### Scenario: No device or port error

- GIVEN the port `<p>` does not exist or fails to open
- WHEN the user runs `retrobox nfc read --port <p>`
- THEN the CLI prints a "dead"/actionable message
- AND exits with a non-zero code

#### Scenario: Device responds without PONG

- GIVEN the port opens but no `PONG` is received
- WHEN the user runs `retrobox nfc read --port <p>`
- THEN the CLI prints dead and exits non-zero

### Requirement: NFC Write Catalog-Driven

The CLI `retrobox nfc write <id> --port <p>` MUST load the catalog via `RetroBoxConfigStore`, require `<id>` to be present/imported, read the floppy's `Mode` from the catalog entry (no `--mode` option SHALL exist), send `WRITE <id>,<mode>` via `RetroBoxArduinoSerialProtocol.BuildWriteCommand`, and on `OK` set `Nfc=true` on the floppy and persist the catalog. On `ERROR <msg>` it MUST surface the error, MUST NOT flip `Nfc`, and exit non-zero. A PING pre-flight MUST NOT be required.

#### Scenario: Cataloged floppy Mode ro, OK response

- GIVEN `floppies.yaml` contains `<id>` with `Mode: ro`
- WHEN the user runs `retrobox nfc write <id> --port <p>` and firmware returns `OK`
- THEN the CLI sends `WRITE <id>,ro`
- AND persists `Nfc: true` for `<id>`
- AND exits 0

#### Scenario: Cataloged floppy Mode rw, OK response

- GIVEN `floppies.yaml` contains `<id>` with `Mode: rw`
- WHEN the user runs `retrobox nfc write <id> --port <p>` and firmware returns `OK`
- THEN the CLI sends `WRITE <id>,rw` and marks `Nfc: true`, exit 0

#### Scenario: Firmware returns ERROR not written

- GIVEN `floppies.yaml` contains `<id>`
- WHEN firmware returns `ERROR not written`
- THEN the CLI surfaces the error, leaves `Nfc` unchanged
- AND exits non-zero

### Requirement: NFC Not-Imported Id

A `<id>` not present in `floppies.yaml` MUST fail before any serial port write attempt with an actionable message and non-zero exit.

#### Scenario: Unknown id

- GIVEN `floppies.yaml` does not contain `<id>`
- WHEN the user runs `retrobox nfc write <id> --port <p>`
- THEN the CLI prints an actionable "id not imported" message
- AND does NOT open or write the serial port
- AND exits non-zero

### Requirement: NFC Port Option

Both subcommands MUST accept a required `--port <p>` option. A missing or invalid `--port` SHALL produce a clear argument error and non-zero exit.

#### Scenario: Missing --port

- GIVEN the user runs `retrobox nfc read` or `retrobox nfc write <id>` without `--port`
- THEN the CLI prints a clear argument error
- AND exits non-zero

#### Scenario: Invalid --port value

- GIVEN `--port` is supplied but the path is invalid
- WHEN the subcommand runs
- THEN the CLI prints a clear port error and exits non-zero

### Requirement: NFC Serial Contention

When the serial port is unavailable (busy/EACCES, e.g. daemon holds it), the CLI MUST detect it and surface an actionable message; it MUST NOT hang.

#### Scenario: Port held by daemon

- GIVEN the port `<p>` is open by the daemon (EACCES/busy)
- WHEN the user runs `retrobox nfc read --port <p>` or `nfc write <id> --port <p>`
- THEN the CLI prints an actionable contention message
- AND exits non-zero without hanging

### Requirement: NFC Protocol Builders

Core `RetroBoxArduinoSerialProtocol` SHALL expose `BuildPingCommand()` returning `PING` and MUST reuse `BuildWriteCommand(id, mode)` returning `WRITE {id},{mode}`.

#### Scenario: BuildPingCommand

- WHEN code calls `BuildPingCommand()`
- THEN it returns `PING`

#### Scenario: BuildWriteCommand reuse

- WHEN code calls `BuildWriteCommand("007", "ro")`
- THEN it returns `WRITE 007,ro`

### Requirement: NFC Protocol Parser

Core SHALL parse firmware responses into: `PONG` (ping success), `OK` (write success), `ERROR <msg>` (write failure). Unknown responses SHALL be surfaced as parse failure.

#### Scenario: Parse PONG

- WHEN the parser receives `PONG`
- THEN it yields a ping-success result

#### Scenario: Parse OK

- WHEN the parser receives `OK`
- THEN it yields a write-success result

#### Scenario: Parse ERROR

- WHEN the parser receives `ERROR not written`
- THEN it yields a write-failure result carrying the message

### Requirement: NFC Client Abstraction

Core SHALL define `IRetroBoxNfcClient` (testable, fake-able) and `RetroBoxNfcSerialClient` over `System.IO.Ports`. `RetroBoxNfcWriter` and CLI handlers MUST depend on `IRetroBoxNfcClient`, not the concretion. The NFC client MUST be distinct from the 86Box Unix-socket `IRetroBoxFloppyControlClient`.

#### Scenario: Writer depends on interface

- WHEN tests construct `RetroBoxNfcWriter`
- THEN they inject a fake `IRetroBoxNfcClient` (e.g. `RecordingNfcClient`)
- AND no `System.IO.Ports` instance is required

#### Scenario: Distinct from 86Box client

- GIVEN the existing `IRetroBoxFloppyControlClient` (86Box Unix-socket)
- WHEN the NFC client is introduced
- THEN it is a separate interface and concretion with no shared type

### Requirement: NFC CLI Help Surface

`retrobox nfc` and its `read`/`write` subcommands SHALL appear in CLI help (`CliHelpSmokeTests` stays green). The placeholder `nfc` command MUST be replaced cleanly.

#### Scenario: Help lists nfc subcommands

- WHEN the user runs `retrobox nfc --help`
- THEN `read` and `write` are listed
- AND no placeholder command remains