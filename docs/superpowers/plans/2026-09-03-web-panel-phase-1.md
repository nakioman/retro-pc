# RetroBox Web Panel — Phase 1: Serial Command Channel

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the daemon send commands to the floppy controller and read their
replies without disturbing the insert/eject event stream, so a later web layer
can write NFC tags through the process that owns the serial port.

**Architecture:** A line router sits inside the daemon's existing read loop and
diverts command responses (`OK`, `ERROR …`, `Tag ID: …`) to whichever command
is in flight, letting every other line continue to the event handler
untouched. A command channel serializes commands behind a semaphore, enforces a
timeout, and issues a `STATUS` after a successful tag write so the newly
assigned image mounts. No web code is written in this phase.

**Tech Stack:** .NET 10 / C# 13, xUnit, `System.IO.Ports`, YamlDotNet static
context. All commands run through `mise`.

**Spec:** [`docs/superpowers/specs/2026-09-03-web-panel-design.md`](../specs/2026-09-03-web-panel-design.md)

## Global Constraints

- C# 13, `nullable enable`, implicit usings. **English identifiers and comments.**
- Flat `RetroBox*`-prefixed, file-per-concern classes. No comments unless they
  explain a non-obvious decision.
- Use `mise` tasks only. Never invoke `dotnet` directly.
- Gates before every PR: `mise run test` **and** `mise run format-check`.
- Native AOT must keep publishing: `mise run publish-linux-x64` uses
  `-p:PublishAot=true -warnaserror`.
- [Conventional Commits](https://www.conventionalcommits.org/), scoped by area
  (`feat(core):`, `feat(daemon):`, `test(daemon):`).
- The NFC tag payload format is **unchanged**: `<catalog-id>,<mode>`.
- One floppy drive. `Drive = 0`, as the daemon already assumes.
- Command timeout: **5 seconds**.
- `TAGID` response prefix is exactly `Tag ID: ` and the UID is uppercase hex
  with no separators (e.g. `04A13BFE`).

---

## PR stack

Every PR branches off `main` and stacks on its predecessor. Each one is
independently reviewable, passes both gates, and leaves the appliance working.

| PR | Branch | Base | Task | Size |
| --- | --- | --- | --- | --- |
| 0 | `docs/web-panel-design` | `main` | The design spec (already committed) | ~450 lines of docs |
| 1 | `feat/tagid-protocol` | `main` | Task 1 — `TAGID` command and `Tag ID:` response | ~40 lines |
| 2 | `feat/unassigned-tag-guard` | `main` | Task 2 — refuse to mount a floppy with `nfc: false` | ~30 lines |
| 3 | `feat/serial-line-router` | `feat/tagid-protocol` | Task 3 — route responses away from the event path | ~120 lines |
| 4 | `feat/nfc-command-channel` | `feat/serial-line-router` | Task 4 — `IRetroBoxNfcCommandChannel` with timeout | ~150 lines |
| 5 | `feat/drive-state-tracking` | `feat/nfc-command-channel` | Task 5 — drive state + post-write `STATUS` | ~120 lines |

PR 2 touches only `RetroBoxFloppyEventHandler` and is independent of PRs 1
and 3–5; it can be reviewed and merged in any order relative to them.

Later phases get their own plan document once this one lands, because their
task decomposition depends on the interfaces these tasks produce:

- **Phase 2** — web host and library management (~4 PRs)
- **Phase 3** — games and NFC assignment (~4 PRs)
- **Phase 4** — cover art and localization (~3 PRs)

---

## Task 1: `TAGID` command and `Tag ID:` response

Teaches the C# protocol the one firmware verb it does not know yet. Pure
functions, no I/O, no daemon changes.

**Branch:** `feat/tagid-protocol` off `main`

**Files:**
- Modify: `src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs`
- Test: `tests/RetroBox.Tests/RetroBoxArduinoSerialProtocolTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `RetroBoxArduinoSerialProtocol.BuildTagIdCommand()` → `string` (returns `"TAGID"`)
  - `NfcResponse.TagId(string Uid)` — a new case on the existing `NfcResponse`
    abstract record.

**Background:** the firmware's verb table already contains `TAGID`
(`firmware/retrofloppy-esp8266/RetroFloppyCommandParser.cpp:13`) and answers
`Tag ID: <HEXUID>` or `ERROR no-tag-detected`
(`firmware/retrofloppy-esp8266/RetroFloppyCommandHandler.cpp:22-30`). Today
`ParseResponse` returns `NfcResponse.Unknown` for that line.

- [ ] **Step 1: Write the failing tests**

Append to `tests/RetroBox.Tests/RetroBoxArduinoSerialProtocolTests.cs`:

```csharp
    [Fact]
    public void BuildTagIdCommand_returns_the_firmware_verb()
    {
        Assert.Equal("TAGID", RetroBoxArduinoSerialProtocol.BuildTagIdCommand());
    }

    [Fact]
    public void ParseResponse_reads_a_tag_id()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("Tag ID: 04A13BFE");

        var tagId = Assert.IsType<NfcResponse.TagId>(response);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public void ParseResponse_trims_the_tag_id_line()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("  Tag ID: 04A13BFE\r\n");

        var tagId = Assert.IsType<NfcResponse.TagId>(response);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public void ParseResponse_rejects_a_tag_id_with_no_uid()
    {
        Assert.IsType<NfcResponse.Unknown>(RetroBoxArduinoSerialProtocol.ParseResponse("Tag ID: "));
    }

    [Fact]
    public void ParseResponse_keeps_no_tag_detected_as_an_error()
    {
        var response = RetroBoxArduinoSerialProtocol.ParseResponse("ERROR no-tag-detected");

        var error = Assert.IsType<NfcResponse.Error>(response);
        Assert.Equal("no-tag-detected", error.Message);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL. `BuildTagIdCommand` and `NfcResponse.TagId` do not exist, so
this is a compile error, not an assertion failure.

- [ ] **Step 3: Add the `TagId` response case**

In `src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs`, extend the
`NfcResponse` record at the bottom of the file:

```csharp
public abstract record NfcResponse
{
    public sealed record Pong() : NfcResponse;
    public sealed record Ok() : NfcResponse;
    public sealed record TagId(string Uid) : NfcResponse;
    public sealed record Error(string Message) : NfcResponse;
    public sealed record Unknown(string? Line) : NfcResponse;
}
```

- [ ] **Step 4: Add the command builder**

Next to the existing `BuildStatusCommand`:

```csharp
    public static string BuildTagIdCommand()
    {
        return "TAGID";
    }
```

- [ ] **Step 5: Parse the response**

In `ParseResponse`, add the `Tag ID: ` branch **before** the `ERROR ` branch so
the ordering reads consistently with the firmware's own handler:

```csharp
        const string TagIdPrefix = "Tag ID: ";
        if (trimmedLine.StartsWith(TagIdPrefix, StringComparison.Ordinal))
        {
            var uid = trimmedLine[TagIdPrefix.Length..].Trim();
            if (uid.Length > 0)
            {
                return new NfcResponse.TagId(uid);
            }
        }
```

An empty UID deliberately falls through to `Unknown`: a truncated line is not a
usable tag id, and treating it as one would let the command channel resolve
with a meaningless value.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, including every pre-existing protocol test.

- [ ] **Step 7: Verify formatting and the AOT publish**

```bash
mise run format-check
mise run publish-linux-x64
```

Expected: both succeed. The publish matters because `-warnaserror` catches
AOT-incompatible additions early.

- [ ] **Step 8: Commit and open the PR**

```bash
git checkout -b feat/tagid-protocol main
git add src/RetroBox.Core/RetroBoxArduinoSerialProtocol.cs \
        tests/RetroBox.Tests/RetroBoxArduinoSerialProtocolTests.cs
git commit -m "feat(core): parse the TAGID command and Tag ID response"
gh pr create --base main --title "feat(core): parse the TAGID command and Tag ID response" \
  --body "Teaches RetroBoxArduinoSerialProtocol the TAGID verb the firmware already supports, so the daemon can ask whether a tag is seated and read its UID.

Part of the web panel work. Spec: docs/superpowers/specs/2026-09-03-web-panel-design.md"
```

---

## Task 2: Refuse to mount a floppy with no assigned tag

A tag claiming `disk1` while the catalog says `disk1` has no tag is stale — it
belongs to a floppy that was reassigned or deleted. Mounting it would mount the
wrong image.

**Branch:** `feat/unassigned-tag-guard` off `main`

**Files:**
- Modify: `src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs`
- Modify: `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`
- Test: `tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `FloppyControlTestCatalogs.CreateCatalog(string floppyId, string
  imagePath, string mode, bool nfc = true)` — the added fourth parameter is
  used by Task 5's tests.

**Critical detail:** `RetroBoxFloppy.Nfc` is a `bool` defaulting to `false`,
and `FloppyControlTestCatalogs.CreateCatalog` never sets it. Adding this guard
without touching that helper **breaks every existing insert test** in
`RetroBoxFloppyEventHandlerTests` and `RetroBoxDaemonTests`. The helper gains a
defaulted parameter in the same PR so existing call sites keep passing.

There are **three** helpers to update, not one. Both test classes declare their
own private `CreateCatalog` that delegates to the shared one
(`RetroBoxFloppyEventHandlerTests.cs:126` and `RetroBoxDaemonTests.cs:340`).
The new test calls `CreateCatalog(..., nfc: false)` against the *private*
helper, so it needs the parameter too.

- [ ] **Step 1: Add the `nfc` parameter to the catalog helper**

In `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`:

```csharp
    public static RetroBoxCatalogData CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return new RetroBoxCatalogData(
            new RetroBoxConfig { DefaultVm = "dos" },
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal)
            {
                ["dos"] = new() { Label = "DOS", Path = "/data/vms/dos" },
            },
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal)
            {
                [floppyId] = new()
                {
                    Label = "Disk 1",
                    Image = imagePath,
                    Mode = mode,
                    Nfc = nfc,
                },
            });
    }
```

- [ ] **Step 2: Add the same parameter to both private delegating helpers**

In `tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs:126` and
`tests/RetroBox.Tests/RetroBoxDaemonTests.cs:340`, replace each private helper
with:

```csharp
    private static RetroBoxCatalogData CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return FloppyControlTestCatalogs.CreateCatalog(floppyId, imagePath, mode, nfc);
    }
```

- [ ] **Step 3: Run the tests to confirm the suite is still green**

Run: `mise run test`

Expected: PASS. Defaulting to `nfc: true` preserves today's behaviour at every
existing call site, so nothing should change yet.

- [ ] **Step 4: Write the failing test**

Append to `tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs`:

```csharp
    [Fact]
    public async Task HandleAsync_refuses_to_mount_a_floppy_with_no_assigned_tag()
    {
        var client = new RecordingFloppyControlClient();
        var handler = new RetroBoxFloppyEventHandler(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode, nfc: false),
            client);

        var result = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Failed, result.Action);
        Assert.Contains("has no assigned tag", result.Message, StringComparison.Ordinal);
        Assert.Empty(client.Calls);
    }
```

`Assert.Empty(client.Calls)` is the load-bearing assertion: the 86Box socket
must never be touched for a rejected insert.

- [ ] **Step 5: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL. The handler still mounts, so `result.Action` is `Inserted` and
`client.Calls` has one entry.

- [ ] **Step 6: Add the guard**

In `RetroBoxFloppyEventHandler.HandleInsertAsync`, immediately after the
catalog lookup and **before** the existing writability check:

```csharp
        if (!floppy.Nfc)
        {
            return new RetroBoxFloppyEventHandlerResult(
                RetroBoxFloppyEventHandlerAction.Failed,
                $"Floppy '{insert.Id}' has no assigned tag; rewrite it from the panel.",
                null);
        }
```

The guard checks `Nfc` only, never a UID: `INSERT` events carry no UID, and a
`TAGID` round trip on every insertion would add latency to the hot path.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, all tests.

- [ ] **Step 8: Verify formatting**

Run: `mise run format-check`

- [ ] **Step 9: Commit and open the PR**

```bash
git checkout -b feat/unassigned-tag-guard main
git add src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs \
        tests/RetroBox.Tests/FloppyControlTestDoubles.cs \
        tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs
git commit -m "feat(daemon): refuse to mount a floppy with no assigned tag"
gh pr create --base main --title "feat(daemon): refuse to mount a floppy with no assigned tag" \
  --body "A tag whose catalog entry has nfc: false is stale — it belongs to a floppy that was reassigned or deleted. Mounting it would mount the wrong image, so the handler now rejects the insert without touching the 86Box socket.

Part of the web panel work. Spec: docs/superpowers/specs/2026-09-03-web-panel-design.md"
```

---

## Task 3: Route command responses away from the event path

`RetroBoxDaemon.RunAsync` parses every incoming line with `ParseEvent`. `OK`
and `PONG` are not valid events, so today they raise
`RetroBoxArduinoSerialProtocolException` and set `exitCode = 1`. This task adds
the router that makes command replies possible at all.

**Branch:** `feat/serial-line-router` off `feat/tagid-protocol`

**Files:**
- Create: `src/RetroBox.Daemon/RetroBoxSerialLineRouter.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxDaemon.cs`
- Test: `tests/RetroBox.Tests/RetroBoxSerialLineRouterTests.cs`

**Interfaces:**
- Consumes: `RetroBoxArduinoSerialProtocol.ParseResponse` (existing),
  `NfcResponse.TagId` (Task 1).
- Produces:
  - `public sealed class RetroBoxSerialLineRouter`
  - `Task<NfcResponse> BeginCommand()` — throws `InvalidOperationException` if
    one is already in flight
  - `bool TryRoute(string line)` — `true` when the line was consumed as a
    response
  - `void CancelCommand(Exception error)` — fails the pending command
  - `bool HasPendingCommand { get; }`

**The router must be `public`, not `internal`.** It appears as a parameter on
`RetroBoxDaemon`'s public constructor and on `RetroBoxSerialNfcCommandChannel`'s
public constructor; an internal type in a public signature is CS0051,
"inconsistent accessibility". The `[assembly: InternalsVisibleTo("RetroBox.Tests")]`
in `RetroBoxSerialDeviceRunner.cs:5` is therefore not what makes this testable.

- [ ] **Step 1: Write the failing tests**

Create `tests/RetroBox.Tests/RetroBoxSerialLineRouterTests.cs`:

```csharp
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialLineRouterTests
{
    [Fact]
    public void TryRoute_ignores_responses_when_no_command_is_in_flight()
    {
        var router = new RetroBoxSerialLineRouter();

        Assert.False(router.TryRoute("OK"));
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_ok()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("OK"));

        Assert.IsType<NfcResponse.Ok>(await pending);
        Assert.False(router.HasPendingCommand);
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_a_tag_id()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await pending);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_an_error()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("ERROR not written"));

        var error = Assert.IsType<NfcResponse.Error>(await pending);
        Assert.Equal("not written", error.Message);
    }

    [Theory]
    [InlineData("INSERT disk1,ro")]
    [InlineData("EJECT")]
    [InlineData("INIT 1.0")]
    public void TryRoute_never_diverts_events_even_mid_command(string line)
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.False(router.TryRoute(line));

        Assert.False(pending.IsCompleted);
        Assert.True(router.HasPendingCommand);
    }

    [Fact]
    public void BeginCommand_rejects_a_second_command_in_flight()
    {
        var router = new RetroBoxSerialLineRouter();
        router.BeginCommand();

        Assert.Throws<InvalidOperationException>(() => router.BeginCommand());
    }

    [Fact]
    public async Task CancelCommand_fails_the_pending_command_and_frees_the_slot()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        router.CancelCommand(new TimeoutException("no reply"));

        await Assert.ThrowsAsync<TimeoutException>(async () => await pending);
        Assert.False(router.HasPendingCommand);
    }
}
```

The `[Theory]` is the most important test in this file: an insert or eject must
reach the event handler even while a write is waiting for its reply, because
the disk can be pulled at any moment.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBoxSerialLineRouter` does not exist (compile error).

- [ ] **Step 3: Write the router**

Create `src/RetroBox.Daemon/RetroBoxSerialLineRouter.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>
/// Splits the controller's serial lines between command replies and floppy
/// events. Events are never diverted: the disk can be pulled while a command
/// is still waiting for its reply.
/// </summary>
public sealed class RetroBoxSerialLineRouter
{
    private readonly Lock gate = new();
    private TaskCompletionSource<NfcResponse>? pending;

    public bool HasPendingCommand
    {
        get
        {
            lock (gate)
            {
                return pending is not null;
            }
        }
    }

    public Task<NfcResponse> BeginCommand()
    {
        lock (gate)
        {
            if (pending is not null)
            {
                throw new InvalidOperationException("A floppy controller command is already in flight.");
            }

            pending = new TaskCompletionSource<NfcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        }
    }

    public bool TryRoute(string line)
    {
        TaskCompletionSource<NfcResponse> completion;
        NfcResponse response;

        lock (gate)
        {
            if (pending is null)
            {
                return false;
            }

            response = RetroBoxArduinoSerialProtocol.ParseResponse(line);
            if (response is NfcResponse.Unknown)
            {
                return false;
            }

            completion = pending;
            pending = null;
        }

        completion.TrySetResult(response);
        return true;
    }

    public void CancelCommand(Exception error)
    {
        TaskCompletionSource<NfcResponse>? completion;

        lock (gate)
        {
            completion = pending;
            pending = null;
        }

        completion?.TrySetException(error);
    }
}
```

`ParseResponse` returns `Unknown` for `INSERT`, `EJECT` and `INIT`, which is
what keeps events on the event path without a second dispatch table.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 5: Write the failing daemon integration test**

Append to `tests/RetroBox.Tests/RetroBoxDaemonTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_does_not_treat_a_command_reply_as_a_malformed_event()
    {
        var client = new RecordingFloppyControlClient();
        var output = new StringWriter();
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader(
                """
                OK
                INSERT disk1,ro

                """),
            output,
            lineRouter: router);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.IsType<NfcResponse.Ok>(await pending);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }
```

This encodes the bug being fixed: today the `OK` line makes `RunAsync` return
`1` and print "Malformed Arduino serial event 'OK'".

- [ ] **Step 6: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — `RetroBoxDaemon` has no `lineRouter` parameter (compile error).

- [ ] **Step 7: Wire the router into the daemon loop**

In `src/RetroBox.Daemon/RetroBoxDaemon.cs`, add the parameter to the primary
constructor, after `socketProbe`:

```csharp
    RetroBoxSerialLineRouter? lineRouter = null)
```

Inside `RunAsync`, hold the router alongside the handler:

```csharp
        var router = lineRouter ?? new RetroBoxSerialLineRouter();
```

Then, in the read loop, immediately after the `IsNullOrWhiteSpace` check and
**before** the `try` that calls `ParseEvent`:

```csharp
                if (router.TryRoute(line))
                {
                    continue;
                }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, including every pre-existing daemon test. Nothing changes when
no command is in flight, which is the case in all of them.

- [ ] **Step 9: Verify formatting and the AOT publish**

```bash
mise run format-check
mise run publish-linux-x64
```

- [ ] **Step 10: Commit and open the PR**

```bash
git checkout -b feat/serial-line-router feat/tagid-protocol
git add src/RetroBox.Daemon/RetroBoxSerialLineRouter.cs \
        src/RetroBox.Daemon/RetroBoxDaemon.cs \
        tests/RetroBox.Tests/RetroBoxSerialLineRouterTests.cs \
        tests/RetroBox.Tests/RetroBoxDaemonTests.cs
git commit -m "feat(daemon): route controller command replies away from the event path"
gh pr create --base feat/tagid-protocol \
  --title "feat(daemon): route controller command replies away from the event path" \
  --body "The read loop parsed every line as an event, so a command reply like OK raised a protocol exception and set exit code 1. A router now diverts replies to whichever command is in flight; INSERT, EJECT and INIT are never diverted, because the disk can be pulled mid-command.

Stacked on the TAGID protocol PR. Spec: docs/superpowers/specs/2026-09-03-web-panel-design.md"
```

---

## Task 4: The NFC command channel

Gives callers a typed, serialized, timeout-bounded way to send `TAGID` and
`WRITE` without knowing anything about serial ports.

**Branch:** `feat/nfc-command-channel` off `feat/serial-line-router`

**Files:**
- Create: `src/RetroBox.Core/RetroBoxNfcCommandChannel.cs`
- Create: `src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs`
- Test: `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`

**Interfaces:**
- Consumes: `RetroBoxSerialLineRouter` (Task 3),
  `RetroBoxArduinoSerialProtocol.BuildTagIdCommand` (Task 1),
  `RetroBoxArduinoSerialProtocol.BuildWriteCommand` (existing).
- Produces:
  - `public interface IRetroBoxNfcCommandChannel` with
    `Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default)`
    and
    `Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default)`
  - `public sealed class RetroBoxNfcCommandTimeoutException : Exception`
  - `public sealed class RetroBoxSerialNfcCommandChannel : IRetroBoxNfcCommandChannel`
    with constructor
    `(RetroBoxSerialLineRouter router, TextWriter serialOutput, TimeSpan? timeout = null)`
  - `RetroBoxSerialNfcCommandChannel.DefaultTimeout` → `TimeSpan.FromSeconds(5)`

- [ ] **Step 1: Write the failing tests**

Create `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`:

```csharp
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialNfcCommandChannelTests
{
    [Fact]
    public async Task WriteTagAsync_sends_the_write_command_and_returns_the_reply()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));

        Assert.IsType<NfcResponse.Ok>(await write);
        Assert.Contains("WRITE disk1,ro", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTagIdAsync_sends_the_tagid_command_and_returns_the_uid()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var read = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await read);
        Assert.Equal("04A13BFE", tagId.Uid);
        Assert.Contains("TAGID", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTagIdAsync_surfaces_an_empty_drive_as_an_error_reply()
    {
        var router = new RetroBoxSerialLineRouter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, new StringWriter());

        var read = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("ERROR no-tag-detected"));

        var error = Assert.IsType<NfcResponse.Error>(await read);
        Assert.Equal("no-tag-detected", error.Message);
    }

    [Fact]
    public async Task SendAsync_times_out_when_the_controller_never_replies()
    {
        var router = new RetroBoxSerialLineRouter();
        var channel = new RetroBoxSerialNfcCommandChannel(
            router,
            new StringWriter(),
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxNfcCommandTimeoutException>(
            async () => await channel.ReadTagIdAsync());

        Assert.False(router.HasPendingCommand);
    }

    [Fact]
    public async Task SendAsync_serializes_commands_so_a_second_one_waits()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var first = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        var second = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);

        Assert.False(second.IsCompleted);
        Assert.DoesNotContain("WRITE", serial.ToString(), StringComparison.Ordinal);

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        await first;

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        Assert.IsType<NfcResponse.Ok>(await second);
    }

    private static async Task WaitForPendingCommand(RetroBoxSerialLineRouter router)
    {
        for (var attempt = 0; attempt < 100 && !router.HasPendingCommand; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(router.HasPendingCommand, "The command was never registered with the router.");
    }
}
```

`WaitForPendingCommand` exists because `SendAsync` registers with the router
inside an `async` method; polling the router is how the test synchronizes
without sleeping a fixed amount.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — neither the channel nor the timeout exception exists.

- [ ] **Step 3: Define the interface and the timeout exception**

Create `src/RetroBox.Core/RetroBoxNfcCommandChannel.cs`:

```csharp
namespace RetroBox.Core;

/// <summary>Sends commands to the floppy controller and awaits its reply.</summary>
public interface IRetroBoxNfcCommandChannel
{
    Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default);

    Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default);
}

public sealed class RetroBoxNfcCommandTimeoutException : Exception
{
    public RetroBoxNfcCommandTimeoutException(string message)
        : base(message)
    {
    }
}
```

- [ ] **Step 4: Implement the channel**

Create `src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>
/// Serializes controller commands over the single serial line the daemon owns.
/// One command at a time is not a limitation but the physical reality: there is
/// one drive and one reader.
/// </summary>
public sealed class RetroBoxSerialNfcCommandChannel : IRetroBoxNfcCommandChannel
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly RetroBoxSerialLineRouter router;
    private readonly TextWriter serialOutput;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1, 1);

    public RetroBoxSerialNfcCommandChannel(
        RetroBoxSerialLineRouter router,
        TextWriter serialOutput,
        TimeSpan? timeout = null)
    {
        this.router = router;
        this.serialOutput = serialOutput;
        this.timeout = timeout ?? DefaultTimeout;
    }

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildTagIdCommand(), cancellationToken);
    }

    public Task<NfcResponse> WriteTagAsync(
        string id,
        string mode,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode), cancellationToken);
    }

    private async Task<NfcResponse> SendAsync(string command, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            var reply = router.BeginCommand();
            await serialOutput.WriteLineAsync(command.AsMemory(), cancellationToken);

            try
            {
                return await reply.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                var error = new RetroBoxNfcCommandTimeoutException(
                    $"The floppy controller did not answer '{command}' within {timeout.TotalSeconds:0.##}s.");
                router.CancelCommand(error);
                throw error;
            }
            catch (OperationCanceledException)
            {
                router.CancelCommand(new OperationCanceledException(cancellationToken));
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
```

`Task.WaitAsync(TimeSpan, CancellationToken)` throws `TimeoutException` on
expiry and `OperationCanceledException` on caller cancellation, which is why
the two cases are caught separately: only the first is a controller fault.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 6: Verify formatting and the AOT publish**

```bash
mise run format-check
mise run publish-linux-x64
```

- [ ] **Step 7: Commit and open the PR**

```bash
git checkout -b feat/nfc-command-channel feat/serial-line-router
git add src/RetroBox.Core/RetroBoxNfcCommandChannel.cs \
        src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs \
        tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs
git commit -m "feat(daemon): add a serialized NFC command channel with a timeout"
gh pr create --base feat/serial-line-router \
  --title "feat(daemon): add a serialized NFC command channel with a timeout" \
  --body "IRetroBoxNfcCommandChannel lets a caller send TAGID and WRITE without knowing about serial ports. Commands are serialized behind a semaphore — there is one drive — and bounded by a 5s timeout so a wedged controller cannot hang a caller forever.

Stacked on the line router PR. Spec: docs/superpowers/specs/2026-09-03-web-panel-design.md"
```

---

## Task 5: Drive state tracking and the post-write `STATUS`

The firmware emits nothing after a `WRITE`
(`firmware/retrofloppy-esp8266/RetroFloppyApp.cpp:96-105`), so the daemon never
learns the tag changed. Sending `STATUS` after a successful write makes the
firmware answer `INSERT <new payload>`, which flows through the normal event
path and mounts the newly assigned image.

**Branch:** `feat/drive-state-tracking` off `feat/nfc-command-channel`

**Files:**
- Create: `src/RetroBox.Core/RetroBoxDriveState.cs`
- Create: `src/RetroBox.Daemon/RetroBoxDriveStateTracker.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxDaemon.cs`
- Test: `tests/RetroBox.Tests/RetroBoxDriveStateTrackerTests.cs`
- Test: `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`

**Interfaces:**
- Consumes: `RetroBoxSerialNfcCommandChannel` (Task 4),
  `RetroBoxFloppyEventHandlerResult` (existing).
- Produces:
  - `public abstract record RetroBoxDriveState` with cases `Unknown()`,
    `Empty()`, and `Loaded(string FloppyId, string Mode)`
  - `public interface IRetroBoxDriveState { RetroBoxDriveState Current { get; } }`
  - `public sealed class RetroBoxDriveStateTracker : IRetroBoxDriveState` with
    `void Observe(RetroBoxArduinoSerialEvent serialEvent)`

- [ ] **Step 1: Write the failing tracker tests**

Create `tests/RetroBox.Tests/RetroBoxDriveStateTrackerTests.cs`:

```csharp
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxDriveStateTrackerTests
{
    [Fact]
    public void Current_starts_unknown_before_any_event()
    {
        Assert.IsType<RetroBoxDriveState.Unknown>(new RetroBoxDriveStateTracker().Current);
    }

    [Fact]
    public void Observe_records_an_inserted_floppy()
    {
        var tracker = new RetroBoxDriveStateTracker();

        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var loaded = Assert.IsType<RetroBoxDriveState.Loaded>(tracker.Current);
        Assert.Equal("disk1", loaded.FloppyId);
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadOnlyMode, loaded.Mode);
    }

    [Fact]
    public void Observe_records_an_eject()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        tracker.Observe(new RetroBoxArduinoEjectEvent());

        Assert.IsType<RetroBoxDriveState.Empty>(tracker.Current);
    }

    [Fact]
    public void Observe_leaves_the_state_alone_for_other_events()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        tracker.Observe(new RetroBoxArduinoErrorEvent("no-tag-detected"));

        Assert.IsType<RetroBoxDriveState.Loaded>(tracker.Current);
    }
}
```

The last test matters: `ERROR no-tag-detected` means the firmware could not
*read* the tag, not that the disk left. Treating it as an eject would make the
panel flicker to "empty" on a marginal read.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — the tracker does not exist.

- [ ] **Step 3: Define the state**

Create `src/RetroBox.Core/RetroBoxDriveState.cs`:

```csharp
namespace RetroBox.Core;

public abstract record RetroBoxDriveState
{
    /// <summary>No controller attached, or no event seen yet.</summary>
    public sealed record Unknown() : RetroBoxDriveState;

    public sealed record Empty() : RetroBoxDriveState;

    public sealed record Loaded(string FloppyId, string Mode) : RetroBoxDriveState;
}

public interface IRetroBoxDriveState
{
    RetroBoxDriveState Current { get; }
}
```

- [ ] **Step 4: Implement the tracker**

Create `src/RetroBox.Daemon/RetroBoxDriveStateTracker.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Daemon;

public sealed class RetroBoxDriveStateTracker : IRetroBoxDriveState
{
    private volatile RetroBoxDriveState current = new RetroBoxDriveState.Unknown();

    public RetroBoxDriveState Current => current;

    public void Observe(RetroBoxArduinoSerialEvent serialEvent)
    {
        current = serialEvent switch
        {
            RetroBoxArduinoInsertEvent insert => new RetroBoxDriveState.Loaded(insert.Id, insert.Mode),
            RetroBoxArduinoEjectEvent => new RetroBoxDriveState.Empty(),
            _ => current,
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 6: Write the failing post-write `STATUS` test**

Append to `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`:

```csharp
    [Fact]
    public async Task WriteTagAsync_asks_for_status_after_a_successful_write()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        await write;

        Assert.Contains("STATUS", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteTagAsync_does_not_ask_for_status_after_a_failed_write()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("ERROR not written"));
        await write;

        Assert.DoesNotContain("STATUS", serial.ToString(), StringComparison.Ordinal);
    }
```

- [ ] **Step 7: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL on the first of the two — no `STATUS` is ever written.

- [ ] **Step 8: Send `STATUS` after a successful write**

In `RetroBoxSerialNfcCommandChannel`, replace the body of `WriteTagAsync`:

```csharp
    public async Task<NfcResponse> WriteTagAsync(
        string id,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode),
            cancellationToken);

        if (response is NfcResponse.Ok)
        {
            // The firmware answers a WRITE and then stays quiet, so the daemon
            // would never learn the tag changed. STATUS makes it re-announce
            // the seated tag, which mounts the newly assigned image.
            await serialOutput.WriteLineAsync(
                RetroBoxArduinoSerialProtocol.BuildStatusCommand().AsMemory(),
                cancellationToken);
        }

        return response;
    }
```

`STATUS` is written outside `SendAsync` on purpose: its reply is `INSERT …`,
an event, so it must reach the event handler rather than a pending command.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 10: Feed the tracker from the daemon loop**

In `src/RetroBox.Daemon/RetroBoxDaemon.cs`, add a parameter to the primary
constructor after `lineRouter`:

```csharp
    RetroBoxDriveStateTracker? driveState = null)
```

In `RunAsync`, alongside the router:

```csharp
        var tracker = driveState ?? new RetroBoxDriveStateTracker();
```

And in the loop, immediately after `ParseEvent` succeeds and before
`handler.HandleAsync`:

```csharp
                    tracker.Observe(serialEvent);
```

- [ ] **Step 11: Write the failing daemon tracker test**

Append to `tests/RetroBox.Tests/RetroBoxDaemonTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_tracks_the_drive_state_from_events()
    {
        var tracker = new RetroBoxDriveStateTracker();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            new RecordingFloppyControlClient(),
            new StringReader(
                """
                INSERT disk1,ro

                """),
            new StringWriter(),
            driveState: tracker);

        await daemon.RunAsync();

        var loaded = Assert.IsType<RetroBoxDriveState.Loaded>(tracker.Current);
        Assert.Equal("disk1", loaded.FloppyId);
    }
```

- [ ] **Step 12: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, all tests.

- [ ] **Step 13: Verify formatting and the AOT publish**

```bash
mise run format-check
mise run publish-linux-x64
```

- [ ] **Step 14: Commit and open the PR**

```bash
git checkout -b feat/drive-state-tracking feat/nfc-command-channel
git add src/RetroBox.Core/RetroBoxDriveState.cs \
        src/RetroBox.Daemon/RetroBoxDriveStateTracker.cs \
        src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs \
        src/RetroBox.Daemon/RetroBoxDaemon.cs \
        tests/RetroBox.Tests/RetroBoxDriveStateTrackerTests.cs \
        tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs \
        tests/RetroBox.Tests/RetroBoxDaemonTests.cs
git commit -m "feat(daemon): track drive state and re-announce the tag after a write"
gh pr create --base feat/nfc-command-channel \
  --title "feat(daemon): track drive state and re-announce the tag after a write" \
  --body "The firmware stays quiet after a WRITE, so the daemon never learned the tag changed. A successful write now sends STATUS, which makes the firmware re-announce the seated tag and mounts the newly assigned image. Adds IRetroBoxDriveState so a later web layer can show what is in the drive.

Stacked on the NFC command channel PR. Spec: docs/superpowers/specs/2026-09-03-web-panel-design.md"
```

---

## Phase 1 exit criteria

- `mise run test` and `mise run format-check` pass.
- `mise run publish-linux-x64` still produces the Native AOT binary.
- With hardware attached, `mise run nfc-test` still exercises the NFC path.
- No web server exists yet. Two user-visible behaviours change:
  - A floppy whose catalog entry has `nfc: false` no longer mounts.
  - A stray `OK` / `PONG` / `Tag ID:` line arriving with no command in flight
    is now consumed silently, instead of printing "Malformed Arduino serial
    event" and setting the daemon's exit code to 1. An improvement, and
    intended — but a change.
