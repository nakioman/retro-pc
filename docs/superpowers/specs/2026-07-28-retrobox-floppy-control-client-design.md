# RetroBox Floppy Control Client Design

## Scope

Implement the local `retrobox` side of GitHub issue #13: a tested .NET client for the 86Box floppy-control Unix socket defined in `docs/86box-floppy-control-socket-contract.md`.

This design does not implement the 86Box socket server, NFC event handling, daemon orchestration, or VM launch behavior.

## Public API

Add `src/RetroBox.Core/RetroBoxFloppyControlClient.cs` with:

```csharp
public interface IRetroBoxFloppyControlClient
{
    Task<RetroBoxFloppyStatus> InsertAsync(int drive, string imagePath, bool readOnly, CancellationToken cancellationToken = default);
    Task<RetroBoxFloppyStatus> EjectAsync(int drive, CancellationToken cancellationToken = default);
    Task<RetroBoxFloppyStatus> StatusAsync(int drive, CancellationToken cancellationToken = default);
}
```

The concrete runtime class is:

```csharp
public sealed class RetroBoxFloppyControlClient(string socketPath) : IRetroBoxFloppyControlClient
```

The status result is a value model matching the one-drive contract response:

```csharp
public sealed record RetroBoxFloppyStatus(
    int Drive,
    bool Inserted,
    string? Path,
    bool ReadOnly,
    bool Busy,
    bool Changed);
```

Socket error responses throw a typed `RetroBoxFloppyControlException` that preserves `error.code`, `error.message`, and optional `error.details` as JSON for callers that need diagnostics.

## Transport And Framing

The public constructor opens a Unix domain socket with `System.Net.Sockets.Socket`, connects to `UnixDomainSocketEndPoint(socketPath)`, wraps it in a `NetworkStream`, and performs one request per operation.

Each operation writes exactly one UTF-8 JSON object followed by `\n`, then reads exactly one response line. The client does not keep a connection open across calls in this first implementation. That keeps cancellation, cleanup, and tests simple while still satisfying the contract requirement that each request and response is JSON Lines.

Request IDs are generated internally with a monotonic counter and included as strings. The client only relies on response shape, not on any particular server-side ID format.

## Commands

`InsertAsync` sends:

```json
{"id":"...","command":"floppy.insert","params":{"drive":0,"path":"/path/to.img","read_only":true}}
```

`EjectAsync` sends:

```json
{"id":"...","command":"floppy.eject","params":{"drive":0}}
```

`StatusAsync` sends:

```json
{"id":"...","command":"floppy.status","params":{"drive":0}}
```

On `ok: true`, the `result` object is parsed into `RetroBoxFloppyStatus`. On `ok: false`, the `error` object is parsed into `RetroBoxFloppyControlException`.

## Test Strategy

Add `tests/RetroBox.Tests/RetroBoxFloppyControlClientTests.cs`.

The production class exposes an internal constructor that accepts a stream factory for tests. Tests use an in-memory duplex stream or equivalent test stream to capture the outgoing JSON line and provide one response line.

Tests cover:

- `floppy.insert` serialization with `drive`, `path`, and `read_only`.
- `floppy.eject` serialization with `drive`.
- `floppy.status` serialization with `drive`.
- Success response parsing into `RetroBoxFloppyStatus`.
- Error response parsing into `RetroBoxFloppyControlException`.
- One request line per operation.

No daemon or NFC tests are added for this ticket.
