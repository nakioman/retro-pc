# RetroBox Web Panel — Phase 3: NFC Assignment

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let someone assign a physical NFC floppy to a cataloged disk from the panel, so that a floppy uploaded through the web actually mounts when they put it in the drive.

**Architecture:** The daemon gains a supervision loop that keeps reopening the serial device, so a controller that is absent at boot or re-plugged later comes back without a restart. The web layer gains a live view of what is in the drive and one command endpoint that drives the existing serial command channel. The panel gets a drive section: put a floppy in, pick a disk, write the tag.

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core Minimal APIs on `WebApplication.CreateSlimBuilder`, server-sent events, `System.Text.Json` source generation, xUnit. Plain JavaScript, no build step.

**Spec:** [`docs/superpowers/specs/2026-09-03-web-panel-design.md`](../specs/2026-09-03-web-panel-design.md)

## Global Constraints

- C# 13, `nullable enable`, implicit usings. **English identifiers and comments.**
- Flat `RetroBox*`-prefixed, file-per-concern classes. No comments unless they explain a non-obvious decision.
- Use `mise` tasks only. Never invoke `dotnet` directly.
- Gates before every PR: `mise run test` **and** `mise run format-check`.
- **Native AOT must keep publishing.** CI runs `mise run publish-linux-x64` (`-p:PublishAot=true -warnaserror`) on Ubuntu. It **cannot** complete on macOS — it fails at the native link step. Do not run it as a gate.
- **Minimal APIs only.** Blazor Server, Razor components, MVC and **SignalR** are unsupported under Native AOT — which is why live drive state is server-sent events, not a socket.
- **`[UnconditionalSuppressMessage]` is forbidden.** An earlier PR tried suppressing IL2026/IL3050 on the method that maps every endpoint; it was rejected. `<EnableRequestDelegateGenerator>true</EnableRequestDelegateGenerator>` in `RetroBox.Web.csproj` is what keeps the mappings clean — do not remove it.
- **No authentication and no TLS.** LAN-trusted; a recorded owner decision.
- Static assets are **embedded resources**, at the **root** of `wwwroot` — the host's asset route matches a single path segment.
- API JSON is **camelCase**; errors are `{ "code": "...", "message": "..." }`. Codes are stable and machine-readable; the panel owns the human text and maps every code in **both** dictionaries. A key present in one language only fails the parity test.
- The NFC tag payload format is unchanged: `<catalog-id>,<mode>`.
- Command timeout on the serial channel: **5 seconds**.

## What phase 2 shipped that this phase builds on

Verified working on the real appliance: the panel serves on `:8080`, uploads land in the catalog, and serial insert/eject events reach the daemon.

- `IRetroBoxCatalogSource` — `Current`, `LastError`, `TryReload()`. `RetroBoxWatchingCatalogSource` reloads on YAML change behind a debounce, discards a reload that fails validation, and serializes `Reload()` against itself.
- `RetroBoxSerialLineRouter` — splits command replies from floppy events; owns a single expiring orphan slot; `BeginCommand()`, `TryRoute(line)`, `CancelCommand(error, expectLateReply)`, `HasPendingCommand`.
- `RetroBoxSerialNfcCommandChannel` — `ReadTagIdAsync()`, `WriteTagAsync(id, mode)`, `SendStatusAsync()`; serializes commands behind a `SemaphoreSlim`, 5 s timeout, and writes a follow-up command inside the gate via `SendAsync(command, followUpOnOk, ct)`.
- `RetroBoxDriveStateTracker : IRetroBoxDriveState` — `Current` is `Unknown` / `Empty` / `Loaded(FloppyId, Mode)`, fed from `INSERT`/`EJECT`, reset to `Unknown` on `INIT`, and deliberately unchanged by `ERROR`.
- `RetroBoxFloppyLibrary` — `Delete`, `UpdateLabelAndMode`, `EnsureCatalogIsLoadable`, `RunExclusively`, all under one instance lock.
- `RetroBoxWebHost.StartAsync(RetroBoxWebOptions, IRetroBoxCatalogSource, CancellationToken)` → a host with `Uri BaseAddress`, disposed with `await using`.
- `RetroBoxFloppy` already carries `Nfc` and `NfcUid`.

## The user-visible problem this phase closes

An upload creates a catalog entry with `nfc: false`. The phase 1 mount guard refuses to mount `nfc: false` — correctly, since such a tag would be stale. So today a floppy uploaded through the panel **cannot be inserted at all**, and the only remedy is to stop the daemon, run `retrobox nfc write`, and start it again. This phase makes the panel able to write the tag.

---

## PR stack

Every PR stacks on its predecessor. Each is independently reviewable, passes both gates, and leaves the appliance working.

| PR | Branch | Base | Task | Size |
| --- | --- | --- | --- | --- |
| 1 | `feat/serial-supervision` | `main` | Task 1 — the daemon survives a missing or re-plugged controller | ~300 lines |
| 2 | `feat/drive-state-api` | `feat/serial-supervision` | Task 2 — live drive state over SSE, with blank-tag detection | ~350 lines |
| 3 | `feat/command-quarantine` | `feat/drive-state-api` | Task 3 — close the two phase 1 reply-attribution carry-forwards | ~200 lines |
| 4 | `feat/nfc-write-endpoint` | `feat/command-quarantine` | Task 4 — `POST /api/nfc/write` | ~400 lines |
| 5 | `feat/assign-ui` | `feat/nfc-write-endpoint` | Task 5 — the assign panel, es/en | ~350 lines |

**Task 1 ships value on its own**, independent of NFC: today a controller absent at boot is never retried, so a USB enumeration race after a power cut costs the floppy drive until someone notices and restarts the service.

## Carried-forward debt, and where it lands

Every open item from phases 1 and 2 is placed. Nothing is left floating:

| Item | Task |
| --- | --- |
| Serial device never reopened after a failed or lost open | 1 |
| No `watcher.Error` handler — inotify overflow silently freezes the catalog | 1 |
| `SaveYamlSet` leaves three `.bak` files per save, never cleaned | 1 |
| The `Current` / `LastError` torn pair in `BuildCatalogView` | 2 |
| `MapGet("/{asset}")` matches one path segment — a trap for phase 5's covers | 2 |
| Orphan-window quarantine belongs in the router, not the channel's semaphore | 3 |
| Reply attribution ungated after the follow-up `STATUS` | 3 |
| `RunExclusively` holds a lock across blocking IO — becomes acute with a 5 s serial round trip | 4 |
| A `TAGID` read-back should resolve an ambiguous write result | 4 |
| The mount guard's message and the "No NFC" badge describe a stop-the-service procedure | 5 |

---

## Task 1: The daemon survives a missing or re-plugged controller

**Branch:** `feat/serial-supervision` off `main`

**Files:**
- Modify: `src/RetroBox.Cli/CliCommandFactory.cs` — the `daemon` action
- Modify: `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs`
- Modify: `src/RetroBox.Core/RetroBoxConfigStore.cs`
- Test: `tests/RetroBox.Tests/CliHelpSmokeTests.cs`, `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`, `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs`

**Interfaces:**
- Consumes: `RetroBoxSerialDeviceRunner`, `RetroBoxSerialDevice`, `RetroBoxDaemon.RunAsync`, `RetroBoxWebHost` (all existing).
- Produces: nothing new for later tasks. This task is behaviour, not surface.

### Why the current shape is wrong

`appliance/installer/lib/hardware-detect.sh:130` writes `SERIAL_DEVICE=/dev/ttyUSB0` even when it detected nothing, so a controller-less appliance passes a real-looking path. The daemon opens it once: on failure it warns and continues with `device = null`, the read loop gets `Console.In` (which systemd gives `/dev/null`), hits EOF, and the process then parks on the panel forever — **with the controller lost until someone restarts the unit, and nothing saying so.** The same happens if the ESP8266 is unplugged mid-run. A boot-time USB enumeration race after a power cut therefore costs the floppy drive silently.

- [ ] **Step 1: Write the failing test for reconnection**

Append to `tests/RetroBox.Tests/CliHelpSmokeTests.cs`. It drives the real CLI against a device path that does not exist, then creates it, and asserts the daemon picks it up:

```csharp
    [Fact]
    public async Task Daemon_opens_the_serial_device_when_it_appears_after_startup()
    {
        var layout = CreateCatalogLayout();
        var devicePath = Path.Combine(Path.GetTempPath(), $"retrobox-serial-{Guid.NewGuid():N}");
        var error = new StringWriter();
        var originalIn = Console.In;
        var originalError = Console.Error;
        using var cancellation = new CancellationTokenSource();

        Console.SetIn(TextReader.Null);
        Console.SetError(error);

        try
        {
            var command = CliCommandFactory.CreateRootCommand();
            var parseResult = command.Parse([
                "daemon",
                "--config-root", layout,
                "--serial-port", devicePath,
                "--web-port", "0",
                "--echo",
            ]);

            var invocation = Task.Run(() => parseResult.InvokeAsync(cancellation.Token));

            await WaitForStderr(error, "retrying", invocation);

            // A FIFO is enough: the runner only needs a path it can open and read.
            CreateFifo(devicePath);

            await WaitForStderr(error, "Floppy controller connected", invocation);

            cancellation.Cancel();
            await AwaitWithinBound(invocation);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetError(originalError);
            SafeDeleteFile(devicePath);
        }
    }
```

Add the two helpers next to the existing bounded-poll helpers in that file:

```csharp
    private static void CreateFifo(string path)
    {
        using var mkfifo = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("mkfifo", path) { UseShellExecute = false });

        Assert.NotNull(mkfifo);
        mkfifo.WaitForExit();
        Assert.Equal(0, mkfifo.ExitCode);
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
```

`mkfifo` exists on both macOS and the Ubuntu CI runner. If `WaitForStderr` and `AwaitWithinBound` are not already in this file under those names, use whatever the existing bounded-poll helpers are called — read the file first, and do **not** introduce a fixed sleep.

- [ ] **Step 2: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL. The daemon opens the device once, warns, and never retries, so `"Floppy controller connected"` never appears and the wait times out.

- [ ] **Step 3: Replace the one-shot open with a supervision loop**

In `src/RetroBox.Cli/CliCommandFactory.cs`, add the interval next to the other daemon constants:

```csharp
    private static readonly TimeSpan SerialReopenInterval = TimeSpan.FromSeconds(5);
```

Replace the block that currently calls `TryOpenSerialDevice` once and then runs the daemon with a call to a new supervisor, keeping the web host started **before** it and disposed **after** it:

```csharp
                var webHost = await TryStartWebHost(
                    request.WebPort, request.ConfigRoot, catalogSource, cancellation.Token);

                try
                {
                    return await SuperviseSerialDeviceAsync(
                        catalogSource, client, request, serialOptions, webHost is not null, cancellation.Token);
                }
                finally
                {
                    if (webHost is not null)
                    {
                        await webHost.DisposeAsync();
                    }
                }
```

**Correct the comment above that block while you are here.** The existing one claims the panel is disposed first because "the host closes over the serial writer". That is false — `RetroBoxWebHost.StartAsync` receives `(options, catalogSource, cancellationToken)` and never sees a writer. Replace it with the reason that is true:

```csharp
                // The panel starts before the controller and is torn down after it. Starting
                // first is what keeps a controller-less appliance serving: the installer writes a
                // --serial-port even when it detected no controller, so opening the device first
                // would abort before the panel ever bound its port.
```

Then add the supervisor:

```csharp
    private static async Task<int> SuperviseSerialDeviceAsync(
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxFloppyControlClient client,
        RetroBoxDaemonCommandRequest request,
        RetroBoxSerialDeviceOptions? serialOptions,
        bool panelIsRunning,
        CancellationToken cancellationToken)
    {
        if (serialOptions is null)
        {
            // No controller configured at all: read events from stdin, which is what --echo and
            // piping events by hand rely on. Under systemd stdin is /dev/null, so this returns at
            // once and the panel, if any, keeps the process alive below.
            var exitCode = await new RetroBoxDaemon(
                catalogSource, client, Console.In, Console.Out, request.Echo).RunAsync(cancellationToken);

            if (panelIsRunning)
            {
                await WaitForCancellation(cancellationToken);
            }

            return exitCode;
        }

        var runner = new RetroBoxSerialDeviceRunner(serialOptions.Port, serialOptions.Baud);
        var reportedUnavailable = false;
        var lastExitCode = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            RetroBoxSerialDevice device;

            try
            {
                device = await runner.OpenAsync(cancellationToken);
            }
            catch (RetroBoxSerialDeviceException ex)
            {
                // Reported once per outage, not once per attempt: the installer writes a
                // --serial-port even with no controller detected, so an appliance that never had
                // one would otherwise fill the journal.
                if (!reportedUnavailable)
                {
                    reportedUnavailable = true;
                    Console.Error.WriteLine(
                        $"Floppy controller is unavailable, retrying every {SerialReopenInterval.TotalSeconds:0}s: {ex.Message}");
                }

                if (!await DelayAsync(SerialReopenInterval, cancellationToken))
                {
                    break;
                }

                continue;
            }

            reportedUnavailable = false;
            Console.Error.WriteLine("Floppy controller connected.");

            using (device)
            {
                try
                {
                    lastExitCode = await new RetroBoxDaemon(
                        catalogSource,
                        client,
                        device.Reader,
                        Console.Out,
                        request.Echo,
                        device.Writer).RunAsync(cancellationToken);
                }
                catch (RetroBoxSerialDeviceException ex)
                {
                    Console.Error.WriteLine($"Floppy controller went away: {ex.Message}");
                }
            }

            if (!await DelayAsync(SerialReopenInterval, cancellationToken))
            {
                break;
            }
        }

        return lastExitCode;
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
```

Delete `TryOpenSerialDevice` — the supervisor replaces it — and delete the now-unreachable `catch (RetroBoxSerialDeviceException ex) when (webHost is not null)` and the "Floppy event stream ended" block that used to sit around `RunAsync`. The supervisor owns both concerns now.

**Note what this changes on purpose:** the daemon no longer exits when the device is missing, whether or not a panel is running. It waits for the device instead. That removes the need for `Restart=on-failure` to do the retrying, and it makes the boot-race and the unplug cases identical.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, including the existing `Daemon_keeps_serving_the_panel_when_the_serial_device_is_unavailable` — it asserts the panel keeps answering, which the supervisor still does while it retries.

- [ ] **Step 5: Commit the supervisor**

```bash
git add src/RetroBox.Cli/CliCommandFactory.cs tests/RetroBox.Tests/CliHelpSmokeTests.cs
git commit -m "fix(cli): keep reopening the floppy controller until it appears"
```

- [ ] **Step 6: Write the failing test for a dead watcher**

Append to `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`:

```csharp
    [Fact]
    public void A_watcher_error_is_reported_so_a_frozen_catalog_is_not_silent()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        var failures = new List<string>();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, failures.Add, watchFileSystem: false);

        source.ReportWatcherFailure(new IOException("inotify watch limit reached"));

        Assert.Contains("catalog changes will no longer be noticed", Assert.Single(failures), StringComparison.Ordinal);
        Assert.NotNull(source.LastError);
    }
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — `ReportWatcherFailure` does not exist.

- [ ] **Step 8: Handle `FileSystemWatcher.Error`**

In `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs`, subscribe alongside the other handlers, and add the reporting method that the test drives directly:

```csharp
        watcher.Error += OnWatcherError;
```

```csharp
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        ReportWatcherFailure(e.GetException());
    }

    /// <summary>
    /// On inotify buffer overflow or watch-limit exhaustion the watcher stops raising events and
    /// can clear EnableRaisingEvents, which would silently return this source to the frozen
    /// snapshot it exists to replace. Explicit reloads (the panel's own writes) still work.
    /// </summary>
    internal void ReportWatcherFailure(Exception error)
    {
        lock (gate)
        {
            snapshot = snapshot with { Error = error.Message };
        }

        onReloadFailed?.Invoke(
            $"Catalog watcher failed; catalog changes will no longer be noticed automatically: {error.Message}");
    }
```

`ReportWatcherFailure` is `internal` so the test can drive it without provoking a real inotify overflow — the Core assembly already exposes internals to the test project.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 10: Write the failing test for backup retention**

Append to `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs`. Read the file first and reuse whatever helper it already has for building a layout:

```csharp
    [Fact]
    public void Save_keeps_only_the_most_recent_backups()
    {
        var root = CreateLayout();
        var store = new RetroBoxConfigStore(root);
        var data = store.Load();

        for (var save = 0; save < 6; save++)
        {
            store.Save(data);
        }

        var backups = Directory.GetFiles(root, "floppies.yaml.*.bak");

        Assert.Equal(RetroBoxConfigStore.BackupsKept, backups.Length);
    }
```

- [ ] **Step 11: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — six saves leave six backups, and `BackupsKept` does not exist.

- [ ] **Step 12: Prune old backups**

In `src/RetroBox.Core/RetroBoxConfigStore.cs`, add the constant and prune after a successful save:

```csharp
    /// <summary>
    /// Every save copies each YAML aside first. Before the web panel these accumulated slowly;
    /// now an upload, a delete and a rename each add three, on the appliance's only writable
    /// partition.
    /// </summary>
    public const int BackupsKept = 3;
```

```csharp
    private void PruneBackups(string fileName)
    {
        var stale = Directory.GetFiles(rootPath, $"{fileName}.*.bak")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(BackupsKept);

        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A backup that cannot be removed is clutter, never a reason to fail a save that
                // already succeeded.
            }
        }
    }
```

The backup filename is `{path}.{yyyyMMddHHmmssfffffff}.bak`, so an ordinal descending sort is newest-first. Call `PruneBackups` for each file name after `SaveYamlSet` has written all of them successfully — never before, or a failed save would have thrown away the backup it might need.

- [ ] **Step 13: Run the tests and verify formatting**

```bash
mise run test
mise run format-check
```

- [ ] **Step 14: Commit**

```bash
git add -A
git commit -m "fix(core): report a dead catalog watcher and prune stale backups"
```

---

## Task 2: Live drive state

**Branch:** `feat/drive-state-api` off `feat/serial-supervision`

**Files:**
- Modify: `src/RetroBox.Core/RetroBoxCatalogSource.cs`, `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs`
- Create: `src/RetroBox.Web/RetroBoxDriveEndpoints.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebContracts.cs`, `src/RetroBox.Web/RetroBoxWebHost.cs`, `src/RetroBox.Web/RetroBoxCatalogEndpoints.cs`
- Modify: `src/RetroBox.Cli/CliCommandFactory.cs` — pass the tracker and channel to the host
- Modify: `src/RetroBox.Daemon/RetroBoxDaemon.cs` — accept an externally owned tracker (already supported) and expose the channel to the caller
- Test: `tests/RetroBox.Tests/RetroBoxDriveEndpointsTests.cs`

**Interfaces:**
- Consumes: `IRetroBoxDriveState` and `RetroBoxDriveState.{Unknown,Empty,Loaded}`, `IRetroBoxNfcCommandChannel.ReadTagIdAsync` (all existing from phase 1).
- Produces:
  - `RetroBoxCatalogSnapshot(RetroBoxCatalogData Catalog, string? Error)` and `IRetroBoxCatalogSource.Snapshot`
  - `public sealed record RetroBoxDriveView(string State, string? FloppyId, string? Mode, string? TagUid)`
  - `RetroBoxWebOptions` gains nothing; `RetroBoxWebHost.StartAsync` gains two optional parameters: `IRetroBoxDriveState? driveState = null, IRetroBoxNfcCommandChannel? nfcChannel = null`
  - `public static class RetroBoxDriveEndpoints` with `void Map(WebApplication app, IRetroBoxDriveState? driveState, IRetroBoxNfcCommandChannel? nfcChannel)`

### The three states the panel must distinguish

| Panel shows | Source |
| --- | --- |
| No disk | `RetroBoxDriveState.Empty`, or `TAGID` answering `ERROR no-tag-detected` |
| A cataloged floppy | `RetroBoxDriveState.Loaded(id, mode)` from an `INSERT` event |
| A blank tag, ready to assign | `TAGID` returns a UID but no `INSERT` has been seen |

The third row is the whole reason `TAGID` exists in this design: **a blank tag never produces an `INSERT` event** (spec constraint 6 — `readTag` returns `READ_FAILED` and the firmware settles into `UNREADABLE`). A new floppy with a new tag — the primary use case — is invisible to the event stream, so the panel must ask.

- [ ] **Step 1: Write the failing test for the snapshot accessor**

Append to `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`:

```csharp
    [Fact]
    public void Snapshot_pairs_the_catalog_with_its_error_atomically()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, watchFileSystem: false);

        var healthy = source.Snapshot;
        Assert.Equal(["disk1"], healthy.Catalog.Floppies.Keys);
        Assert.Null(healthy.Error);

        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: [ not a mapping");
        Assert.False(source.Reload());

        var broken = source.Snapshot;
        Assert.Equal(["disk1"], broken.Catalog.Floppies.Keys);
        Assert.NotNull(broken.Error);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — `Snapshot` does not exist on the source.

- [ ] **Step 3: Expose the snapshot**

`RetroBoxWatchingCatalogSource` already publishes an immutable private record in one volatile write. Promote it so readers can take both halves at once. In `src/RetroBox.Core/RetroBoxCatalogSource.cs`:

```csharp
public sealed record RetroBoxCatalogSnapshot(RetroBoxCatalogData Catalog, string? Error);

public interface IRetroBoxCatalogSource
{
    RetroBoxCatalogSnapshot Snapshot { get; }

    RetroBoxCatalogData Current => Snapshot.Catalog;

    string? LastError => Snapshot.Error;

    bool TryReload() => false;
}
```

`Current` and `LastError` become default members over `Snapshot`, so every existing caller keeps working unchanged. Update `RetroBoxStaticCatalogSource` to implement `Snapshot` and drop its own `Current`/`LastError`, and rename the watching source's private `CatalogSnapshot` record to the new public type, removing its now-duplicated members.

Then take both halves in one read where they are used together — `RetroBoxCatalogEndpoints.BuildCatalogView` currently reads `source.Current` and `source.LastError` separately, so a reload landing between them can pair a fresh catalog with a stale banner:

```csharp
    public static RetroBoxCatalogView BuildCatalogView(IRetroBoxCatalogSource source)
    {
        var snapshot = source.Snapshot;

        var floppies = snapshot.Catalog.Floppies
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RetroBoxFloppyView(
                entry.Key,
                entry.Value.Label,
                entry.Value.Mode,
                entry.Value.Size,
                entry.Value.Nfc))
            .ToArray();

        return new RetroBoxCatalogView(floppies, snapshot.Error);
    }
```

Also update the `MutableCatalogSource` test double to implement `Snapshot`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 5: Write the failing tests for the drive endpoints**

Create `tests/RetroBox.Tests/RetroBoxDriveEndpointsTests.cs`:

```csharp
using System.Net;
using RetroBox.Core;
using RetroBox.Daemon;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxDriveEndpointsTests
{
    [Fact]
    public async Task Get_drive_reports_an_empty_drive_when_no_controller_is_attached()
    {
        await using var host = await StartAsync(driveState: null, nfcChannel: null);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/drive");

        Assert.Contains("\"state\":\"unavailable\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_drive_reports_a_cataloged_floppy_from_the_event_stream()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        await using var host = await StartAsync(tracker, new StubNfcCommandChannel());
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/drive");

        Assert.Contains("\"state\":\"loaded\"", body, StringComparison.Ordinal);
        Assert.Contains("\"floppyId\":\"disk1\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_drive_reports_a_blank_tag_that_the_event_stream_cannot_see()
    {
        // A blank tag never produces an INSERT, so the tracker knows nothing; only TAGID does.
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };

        await using var host = await StartAsync(new RetroBoxDriveStateTracker(), channel);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/drive");

        Assert.Contains("\"state\":\"blankTag\"", body, StringComparison.Ordinal);
        Assert.Contains("\"tagUid\":\"04A13BFE\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_drive_reports_empty_when_the_controller_sees_no_tag()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.Error("no-tag-detected") };

        await using var host = await StartAsync(new RetroBoxDriveStateTracker(), channel);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.Contains("\"state\":\"empty\"", await client.GetStringAsync("/api/drive"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drive_events_stream_sends_the_current_state_immediately()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        await using var host = await StartAsync(tracker, new StubNfcCommandChannel());
        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync(
            "/api/drive/events", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);

        var first = await reader.ReadLineAsync(cancellation.Token);

        Assert.NotNull(first);
        Assert.StartsWith("data: ", first, StringComparison.Ordinal);
        Assert.Contains("\"floppyId\":\"disk1\"", first, StringComparison.Ordinal);
    }

    private static Task<RetroBoxWebHost> StartAsync(
        IRetroBoxDriveState? driveState,
        IRetroBoxNfcCommandChannel? nfcChannel)
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        return RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0 }, source, driveState: driveState, nfcChannel: nfcChannel);
    }
}
```

Add the stub to `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`:

```csharp
internal sealed class StubNfcCommandChannel : IRetroBoxNfcCommandChannel
{
    public List<string> Calls { get; } = [];

    public NfcResponse TagIdResponse { get; init; } = new NfcResponse.Error("no-tag-detected");

    public NfcResponse WriteResponse { get; init; } = new NfcResponse.Ok();

    public Exception? ThrowOnCall { get; init; }

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add("TAGID");
        return Task.FromResult(TagIdResponse);
    }

    public Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add($"WRITE:{id}:{mode}");
        return Task.FromResult(WriteResponse);
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBoxWebHost.StartAsync` has no `driveState`/`nfcChannel` parameters (compile error).

- [ ] **Step 7: Add the drive view and the endpoints**

In `src/RetroBox.Web/RetroBoxWebContracts.cs`:

```csharp
public sealed record RetroBoxDriveView(string State, string? FloppyId, string? Mode, string? TagUid);
```

and register it: `[JsonSerializable(typeof(RetroBoxDriveView))]`.

Create `src/RetroBox.Web/RetroBoxDriveEndpoints.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxDriveEndpoints
{
    public const string Unavailable = "unavailable";
    public const string Empty = "empty";
    public const string Loaded = "loaded";
    public const string BlankTag = "blankTag";

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static void Map(WebApplication app, IRetroBoxDriveState? driveState, IRetroBoxNfcCommandChannel? nfcChannel)
    {
        app.MapGet("/api/drive", () => BuildViewAsync(driveState, nfcChannel));
        app.MapGet("/api/drive/events", (HttpContext context) => StreamAsync(context, driveState, nfcChannel));
    }

    /// <summary>
    /// A blank tag never raises an INSERT — the firmware cannot read a payload from it — so the
    /// event stream alone can never tell "no disk" from "a new disk waiting to be assigned".
    /// TAGID is the only way to ask.
    /// </summary>
    public static async Task<RetroBoxDriveView> BuildViewAsync(
        IRetroBoxDriveState? driveState,
        IRetroBoxNfcCommandChannel? nfcChannel,
        CancellationToken cancellationToken = default)
    {
        if (driveState is null || nfcChannel is null)
        {
            return new RetroBoxDriveView(Unavailable, null, null, null);
        }

        if (driveState.Current is RetroBoxDriveState.Loaded loaded)
        {
            return new RetroBoxDriveView(Loaded, loaded.FloppyId, loaded.Mode, null);
        }

        try
        {
            return await nfcChannel.ReadTagIdAsync(cancellationToken) switch
            {
                NfcResponse.TagId tag => new RetroBoxDriveView(BlankTag, null, null, tag.Uid),
                _ => new RetroBoxDriveView(Empty, null, null, null),
            };
        }
        catch (Exception ex) when (ex is RetroBoxNfcCommandTimeoutException or IOException)
        {
            // A controller that stops answering is reported as unavailable rather than as an
            // empty drive: "no disk" is a claim, and this code no longer knows.
            return new RetroBoxDriveView(Unavailable, null, null, null);
        }
    }

    private static async Task StreamAsync(
        HttpContext context,
        IRetroBoxDriveState? driveState,
        IRetroBoxNfcCommandChannel? nfcChannel)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        string? lastPayload = null;

        while (!context.RequestAborted.IsCancellationRequested)
        {
            var view = await BuildViewAsync(driveState, nfcChannel, context.RequestAborted);
            var payload = JsonSerializer.Serialize(view, RetroBoxWebJsonContext.Default.RetroBoxDriveView);

            if (payload != lastPayload)
            {
                lastPayload = payload;
                await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }

            try
            {
                await Task.Delay(PollInterval, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

The stream sends only on change, after an immediate first send — a panel left open on a phone must not put a `TAGID` round trip on the wire every two seconds for nothing beyond the poll itself.

- [ ] **Step 8: Wire the host and the CLI**

In `src/RetroBox.Web/RetroBoxWebHost.cs`, add the two optional parameters to `StartAsync` and map the endpoints:

```csharp
    public static async Task<RetroBoxWebHost> StartAsync(
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        CancellationToken cancellationToken = default,
        IRetroBoxDriveState? driveState = null,
        IRetroBoxNfcCommandChannel? nfcChannel = null)
```

```csharp
        RetroBoxDriveEndpoints.Map(app, driveState, nfcChannel);
```

**Also fix the single-segment asset route while you are in this file.** `MapGet("/{asset}")` matches one path segment, so a nested asset would 404 while looking correctly embedded — a trap set for the phase that serves cover images from a subdirectory:

```csharp
        app.MapGet("/{*asset}", (string asset) => ServeAsset(asset));
```

`RetroBoxStaticAssets.TryGet` already rejects any path containing `..`, and builds the resource name from the prefix, so a catch-all cannot escape the embedded set.

In `src/RetroBox.Cli/CliCommandFactory.cs`, the tracker and channel are created inside `RetroBoxDaemon.RunAsync` today, which the web host cannot reach. Construct them in the supervisor instead and pass them both to the daemon and to the host. The daemon's constructor already accepts `RetroBoxSerialLineRouter? lineRouter`, `RetroBoxDriveStateTracker? driveState` and `RetroBoxSerialNfcCommandChannel? nfcChannel` for exactly this.

Because the panel outlives any single device, build the tracker once outside the retry loop and
rebuild the router and channel per connection — a channel holds the writer of one open device.

The web host is started **before** the loop, so it cannot be handed a channel that does not exist
yet and will be replaced on every reconnect. Give it a stable indirection instead. Create
`src/RetroBox.Cli/RetroBoxNfcChannelHolder.cs`:

```csharp
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Cli;

/// <summary>
/// The panel outlives any one serial connection, so it holds this rather than a channel bound to
/// a device that may be unplugged. Null means "no controller right now", which is exactly what
/// the drive endpoints report as unavailable.
/// </summary>
internal sealed class RetroBoxNfcChannelHolder : IRetroBoxNfcCommandChannel
{
    private volatile IRetroBoxNfcCommandChannel? current;

    public void Set(IRetroBoxNfcCommandChannel? channel) => current = channel;

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default) =>
        Require().ReadTagIdAsync(cancellationToken);

    public Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default) =>
        Require().WriteTagAsync(id, mode, cancellationToken);

    private IRetroBoxNfcCommandChannel Require() =>
        current ?? throw new RetroBoxSerialDeviceException("No floppy controller is connected.");
}
```

Then declare both before the retry loop, and pass the holder to `TryStartWebHost` along with the
tracker:

```csharp
        var driveState = new RetroBoxDriveStateTracker();
        var channelHolder = new RetroBoxNfcChannelHolder();
```

and inside the loop, after a successful open, build the per-connection pieces and hand them to
the daemon explicitly rather than letting it build its own:

```csharp
                var router = new RetroBoxSerialLineRouter();
                var channel = new RetroBoxSerialNfcCommandChannel(router, device.Writer);
                channelHolder.Set(channel);

                try
                {
                    lastExitCode = await new RetroBoxDaemon(
                        catalogSource,
                        client,
                        device.Reader,
                        Console.Out,
                        request.Echo,
                        device.Writer,
                        socketProbe: null,
                        lineRouter: router,
                        driveState: driveState,
                        nfcChannel: channel).RunAsync(cancellationToken);
                }
                catch (RetroBoxSerialDeviceException ex)
                {
                    Console.Error.WriteLine($"Floppy controller went away: {ex.Message}");
                }
                finally
                {
                    channelHolder.Set(null);
                }
```

`driveState` is the single tracker built before the loop; `router` and `channel` are rebuilt per
connection because a channel holds the writer of one open device. Clearing the holder in a
`finally` is what makes the drive endpoints report `unavailable` the moment a controller goes
away, rather than timing out against a dead writer.

`TryStartWebHost` gains the two extra parameters and forwards them to `RetroBoxWebHost.StartAsync`,
so the host receives the tracker and the holder once and never has to know about reconnections.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 10: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "feat(web): report live drive state over server-sent events"
```

---

## Task 3: Close the reply-attribution carry-forwards

**Branch:** `feat/command-quarantine` off `feat/drive-state-api`

**Files:**
- Modify: `src/RetroBox.Daemon/RetroBoxSerialLineRouter.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs`
- Test: `tests/RetroBox.Tests/RetroBoxSerialLineRouterTests.cs`, `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`

**Interfaces:**
- Consumes: the existing router and channel.
- Produces: `RetroBoxSerialLineRouter.WaitForClearSlotAsync(CancellationToken)`; no change to the channel's public surface.

### The two problems, both recorded at the end of phase 1

**The orphan window is self-renewing.** After a timeout the router absorbs one late reply within a window that defaults to the command timeout and *starts at the cancel* — the same instant a retry may begin. So: dead controller, `WRITE` times out, window opens, the caller retries immediately, the controller recovers, and the **retry's own timely reply** is absorbed as the orphan; the retry then times out and opens a fresh window. It heals only on an idle gap longer than the window. The fix is to quarantine: absorption must happen while nothing else is in flight, so the channel waits for a clear slot before beginning a command. Putting the wait on the **router** rather than on the channel's semaphore is deliberate — holding the semaphore would also block `SendStatusAsync`, delaying the socket watcher's floppy re-sync for no reason.

**Reply attribution is ungated after a follow-up.** The gate is released as soon as the follow-up `STATUS` is written, while its answer is still in flight. If that answer is `ERROR no-tag-detected` and the panel has already issued another command, the router hands the `ERROR` to the new command. Only `ERROR` is at risk — unambiguous replies are absorbed or consumed — and the wire protocol has no request ids, so this cannot be closed by correlation. A grace hold after writing a follow-up closes it.

- [ ] **Step 1: Write the failing quarantine tests**

Append to `tests/RetroBox.Tests/RetroBoxSerialLineRouterTests.cs`:

```csharp
    [Fact]
    public async Task WaitForClearSlotAsync_returns_at_once_when_no_orphan_is_outstanding()
    {
        var router = new RetroBoxSerialLineRouter();

        await router.WaitForClearSlotAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForClearSlotAsync_waits_out_an_orphan_window()
    {
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromMilliseconds(200));
        router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var wait = router.WaitForClearSlotAsync(CancellationToken.None);

        Assert.False(wait.IsCompleted);
        await AwaitWithinBound(wait);
    }

    [Fact]
    public async Task WaitForClearSlotAsync_returns_once_the_late_reply_is_absorbed()
    {
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromSeconds(30));
        router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var wait = router.WaitForClearSlotAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        // The straggler arrives: the slot is clear immediately, without waiting out the window.
        Assert.True(router.TryRoute("OK"));

        await AwaitWithinBound(wait);
    }
```

Use the file's existing bounded-await helper; if it has none, add one that fails with a readable message rather than hanging.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `WaitForClearSlotAsync` does not exist.

- [ ] **Step 3: Add the quarantine to the router**

In `src/RetroBox.Daemon/RetroBoxSerialLineRouter.cs`:

```csharp
    /// <summary>
    /// Waits until no orphaned reply is still expected. Callers must do this before beginning a
    /// command: absorbing a late reply only works while nothing else is in flight, otherwise the
    /// retry's own timely reply is eaten instead and the failure renews itself.
    /// </summary>
    public async Task WaitForClearSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining;

            lock (gate)
            {
                if (orphanDeadline == 0)
                {
                    return;
                }

                var ticks = orphanDeadline - Stopwatch.GetTimestamp();
                if (ticks <= 0)
                {
                    orphanDeadline = 0;
                    return;
                }

                remaining = TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
            }

            await Task.Delay(remaining, cancellationToken);
        }
    }
```

The loop re-checks rather than trusting one delay, because `TryRoute` can absorb the straggler and clear the deadline early — the second test asserts exactly that.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 5: Write the failing tests for the channel's use of it**

Append to `tests/RetroBox.Tests/RetroBoxSerialNfcCommandChannelTests.cs`:

```csharp
    [Fact]
    public async Task A_retry_after_a_timeout_is_not_answered_by_the_previous_command_s_late_reply()
    {
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromSeconds(30));
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxNfcCommandTimeoutException>(
            async () => await channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var retry = channel.ReadTagIdAsync();

        // The retry must not even be on the wire yet: the quarantine holds it until the straggler
        // is accounted for.
        Assert.False(retry.IsCompleted);
        Assert.True(router.TryRoute("OK"));

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await retry);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public async Task A_follow_up_answer_is_not_handed_to_the_next_command()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromSeconds(5));

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        await write;

        var next = channel.ReadTagIdAsync();

        // The follow-up STATUS's own answer is still in flight; it must not complete the next
        // command.
        Assert.True(router.TryRoute("ERROR no-tag-detected"));
        Assert.False(next.IsCompleted);

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        Assert.IsType<NfcResponse.TagId>(await next);
    }
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — the first because the retry's reply is absorbed as the orphan, the second because the `ERROR` completes the next command.

- [ ] **Step 7: Use the quarantine and hold after a follow-up**

In `src/RetroBox.Daemon/RetroBoxSerialNfcCommandChannel.cs`, wait for a clear slot inside the gate, before registering the command:

```csharp
        await gate.WaitAsync(cancellationToken);

        try
        {
            await router.WaitForClearSlotAsync(cancellationToken);

            var reply = router.BeginCommand();
```

and, after writing a follow-up, keep the slot reserved long enough for the follow-up's own answer to arrive and be discarded rather than handed to whoever comes next:

```csharp
            if (response is NfcResponse.Ok && followUpOnOk is not null)
            {
                await serialOutput.WriteLineAsync(followUpOnOk.AsMemory(), cancellationToken);

                // The follow-up's answer is an event when it is INSERT/EJECT, but ERROR is
                // ambiguous and would otherwise land on the next command. Marking it orphaned
                // makes the quarantine hold the next command until it has been accounted for.
                router.CancelCommand(
                    new RetroBoxNfcCommandTimeoutException("Follow-up reply discarded."),
                    expectLateReply: true);
            }
```

`CancelCommand` with no pending command mints the window without faulting anything — check that the router's guard allows this, and if it does not, add a dedicated `ExpectOrphanedReply()` method rather than weakening the guard.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 9: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "fix(daemon): quarantine the orphan window and the follow-up reply"
```

---

## Task 4: `POST /api/nfc/write`

**Branch:** `feat/nfc-write-endpoint` off `feat/command-quarantine`

**Files:**
- Create: `src/RetroBox.Web/RetroBoxNfcEndpoints.cs`
- Modify: `src/RetroBox.Core/RetroBoxFloppyLibrary.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebContracts.cs`, `src/RetroBox.Web/RetroBoxWebHost.cs`
- Test: `tests/RetroBox.Tests/RetroBoxNfcEndpointsTests.cs`, `tests/RetroBox.Tests/RetroBoxFloppyLibraryTests.cs`

**Interfaces:**
- Consumes: `IRetroBoxNfcCommandChannel`, `IRetroBoxCatalogSource`, `RetroBoxFloppyLibrary` (existing).
- Produces:
  - `public sealed record RetroBoxNfcWriteRequest(string FloppyId, bool Confirm)`
  - `public sealed record RetroBoxNfcWriteResult(string Code, string? PreviousFloppyId)`
  - `RetroBoxFloppyLibrary.AssignTag(string id, string tagUid)` — sets `Nfc`/`NfcUid` on the target and clears both on any other floppy holding that UID
  - `public static class RetroBoxNfcEndpoints` with `void Map(WebApplication app, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource, IRetroBoxNfcCommandChannel? nfcChannel)`

### The flow, and the one constraint that is easy to get wrong

1. `TAGID`. `ERROR no-tag-detected` → `409 no-tag-present`. There is nothing in the drive to write.
2. If the returned UID already belongs to a **different** floppy → `409 tag-already-assigned`, naming the current owner, unless the request carries `confirm: true`.
3. `WRITE <id>,<mode>`, expecting `OK`.
4. Set `nfc: true` and `nfcUid` on the target, and **clear both on the previous owner of that UID**. The tag is physical: once reassigned, the old floppy genuinely has no tag and the catalog must say so — otherwise the phase 1 guard would happily mount it.
5. The channel's follow-up `STATUS` makes the firmware re-announce the tag, which mounts the newly assigned image.

**The constraint:** steps 1 and 3 are serial round trips bounded at 5 seconds each. `RetroBoxFloppyLibrary.RunExclusively` holds an in-process lock across its body, and every catalog mutation — upload, delete, rename — takes that same lock. **Holding it across a serial round trip would freeze the whole library for up to ten seconds on a wedged controller.** Do the serial work outside the lock and take it only for the catalog write in step 4.

- [ ] **Step 1: Write the failing library test**

Append to `tests/RetroBox.Tests/RetroBoxFloppyLibraryTests.cs`:

```csharp
    [Fact]
    public void AssignTag_records_the_uid_and_takes_it_from_the_previous_owner()
    {
        WriteCatalog("disk1", "disk2");
        var store = new RetroBoxConfigStore(root);
        var library = new RetroBoxFloppyLibrary(store);

        library.AssignTag("disk1", "04A13BFE");
        library.AssignTag("disk2", "04A13BFE");

        var floppies = store.Load().Floppies;

        Assert.True(floppies["disk2"].Nfc);
        Assert.Equal("04A13BFE", floppies["disk2"].NfcUid);

        // The tag is physical: disk1 no longer has one, and the mount guard must refuse it.
        Assert.False(floppies["disk1"].Nfc);
        Assert.Null(floppies["disk1"].NfcUid);
    }

    [Fact]
    public void AssignTag_rejects_an_unknown_floppy()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxUnknownFloppyException>(() => library.AssignTag("nope", "04A13BFE"));
    }
```

`WriteCatalog` in that file currently writes one floppy — extend it to `params string[] floppyIds` the way the watching-source test helper does, keeping existing call sites working.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `AssignTag` does not exist.

- [ ] **Step 3: Implement `AssignTag`**

In `src/RetroBox.Core/RetroBoxFloppyLibrary.cs`, following the shape of `UpdateLabelAndMode`:

```csharp
    /// <summary>
    /// Records a written tag. Any other floppy holding this UID loses it: the tag is a physical
    /// object, so once it is reassigned the old entry genuinely has no tag, and leaving it
    /// claiming one would let the mount guard accept a stale tag.
    /// </summary>
    public void AssignTag(string id, string tagUid)
    {
        RunExclusively(() =>
        {
            var data = LoadOrThrow();
            var floppy = RequireFloppy(data, id);

            var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal);

            foreach (var (otherId, other) in data.Floppies)
            {
                if (!string.Equals(otherId, id, StringComparison.Ordinal)
                    && string.Equals(other.NfcUid, tagUid, StringComparison.Ordinal))
                {
                    var released = other with { };
                    released.Nfc = false;
                    released.NfcUid = null;
                    floppies[otherId] = released;
                }
            }

            var assigned = floppy with { };
            assigned.Nfc = true;
            assigned.NfcUid = tagUid;
            floppies[id] = assigned;

            store.Save(data with { Floppies = floppies });
        });
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 5: Write the failing endpoint tests**

Create `tests/RetroBox.Tests/RetroBoxNfcEndpointsTests.cs`:

```csharp
using System.Net;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxNfcEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-nfc-{Guid.NewGuid():N}");

    public RetroBoxNfcEndpointsTests()
    {
        Directory.CreateDirectory(root);
        WriteCatalog("disk1", "disk2");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Write_refuses_when_the_drive_is_empty()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.Error("no-tag-detected") };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("no-tag-present", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("WRITE", string.Join(",", channel.Calls), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_assigns_a_blank_tag()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("WRITE:disk1:ro", channel.Calls.Last());

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"id\":\"disk1\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"nfc\":true", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_refuses_a_tag_that_belongs_to_another_floppy_without_confirmation()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using (var first = await PostAsync(context, "disk1", confirm: false))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await PostAsync(context, "disk2", confirm: false);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("tag-already-assigned", body, StringComparison.Ordinal);
        Assert.Contains("disk1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_reassigns_with_confirmation_and_takes_the_tag_from_the_previous_owner()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using (var first = await PostAsync(context, "disk1", confirm: false))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await PostAsync(context, "disk2", confirm: true);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var floppies = new RetroBoxConfigStore(root).Load().Floppies;
        Assert.True(floppies["disk2"].Nfc);
        Assert.False(floppies["disk1"].Nfc);
    }

    [Fact]
    public async Task Write_reports_a_controller_that_refuses_the_write()
    {
        var channel = new StubNfcCommandChannel
        {
            TagIdResponse = new NfcResponse.TagId("04A13BFE"),
            WriteResponse = new NfcResponse.Error("not written"),
        };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("write-failed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(new RetroBoxConfigStore(root).Load().Floppies["disk1"].Nfc);
    }

    [Fact]
    public async Task Write_reports_no_controller()
    {
        await using var context = await StartAsync(nfcChannel: null);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-controller", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> PostAsync(NfcContext context, string floppyId, bool confirm)
    {
        var body = $"{{\"floppyId\":\"{floppyId}\",\"confirm\":{(confirm ? "true" : "false")}}}";
        return context.Client.PostAsync("/api/nfc/write", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<NfcContext> StartAsync(IRetroBoxNfcCommandChannel? nfcChannel)
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source,
            nfcChannel: nfcChannel);

        return new NfcContext(host, source, new HttpClient { BaseAddress = host.BaseAddress });
    }

    private void WriteCatalog(params string[] floppyIds)
    {
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");

        var lines = new List<string> { "floppies:" };
        foreach (var id in floppyIds)
        {
            var image = Path.Combine(root, $"{id}.img");
            File.WriteAllBytes(image, new byte[16]);
            lines.Add($"  {id}:");
            lines.Add($"    label: {id}");
            lines.Add($"    image: {image}");
            lines.Add("    mode: ro");
            lines.Add("    size: 1.44M");
            lines.Add("    nfc: false");
        }

        File.WriteAllText(Path.Combine(root, "floppies.yaml"), string.Join('\n', lines) + '\n');
    }

    private sealed record NfcContext(
        RetroBoxWebHost Host,
        RetroBoxWatchingCatalogSource Source,
        HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync();
            Source.Dispose();
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — the endpoint does not exist, so every request 404s.

- [ ] **Step 7: Add the contracts**

In `src/RetroBox.Web/RetroBoxWebContracts.cs`:

```csharp
public sealed record RetroBoxNfcWriteRequest(string FloppyId, bool Confirm);

public sealed record RetroBoxNfcWriteResult(string Code, string? PreviousFloppyId);
```

and register both.

- [ ] **Step 8: Implement the endpoint**

Create `src/RetroBox.Web/RetroBoxNfcEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxNfcEndpoints
{
    public static void Map(
        WebApplication app,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel)
    {
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot));

        app.MapPost("/api/nfc/write", (RetroBoxNfcWriteRequest request, CancellationToken cancellationToken) =>
            WriteAsync(request, library, catalogSource, nfcChannel, cancellationToken));
    }

    private static async Task<IResult> WriteAsync(
        RetroBoxNfcWriteRequest request,
        RetroBoxFloppyLibrary library,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel,
        CancellationToken cancellationToken)
    {
        if (nfcChannel is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "no-controller", "No floppy controller is connected.");
        }

        var catalog = catalogSource.Snapshot.Catalog;
        if (!catalog.Floppies.TryGetValue(request.FloppyId, out var floppy))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", $"Unknown floppy '{request.FloppyId}'.");
        }

        // Every serial exchange happens outside RetroBoxFloppyLibrary's lock. Each is bounded at
        // five seconds, and that lock is also taken by upload, delete and rename — holding it
        // across two round trips would freeze the whole library on a wedged controller.
        string tagUid;

        try
        {
            var presence = await nfcChannel.ReadTagIdAsync(cancellationToken);
            if (presence is not NfcResponse.TagId tag)
            {
                return Error(StatusCodes.Status409Conflict, "no-tag-present", "There is no floppy in the drive.");
            }

            tagUid = tag.Uid;

            var currentOwner = catalog.Floppies
                .FirstOrDefault(entry =>
                    !string.Equals(entry.Key, request.FloppyId, StringComparison.Ordinal)
                    && string.Equals(entry.Value.NfcUid, tagUid, StringComparison.Ordinal));

            if (currentOwner.Key is not null && !request.Confirm)
            {
                return Results.Json(
                    new RetroBoxNfcWriteResult("tag-already-assigned", currentOwner.Key),
                    RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult,
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (await nfcChannel.WriteTagAsync(request.FloppyId, floppy.Mode, cancellationToken) is not NfcResponse.Ok)
            {
                return Error(StatusCodes.Status502BadGateway, "write-failed", "The controller could not write the tag.");
            }
        }
        catch (RetroBoxNfcCommandTimeoutException ex)
        {
            // The write may or may not have landed, so this is not reported as a failure. The
            // read-back the spec asks for happens through the drive stream rather than inline:
            // an inline TAGID would first have to wait out the orphan quarantine the timeout just
            // opened, turning one request into roughly fifteen seconds, and the panel is already
            // subscribed to /api/drive/events, which performs exactly that TAGID probe within a
            // couple of seconds and shows what is actually on the tag.
            return Error(StatusCodes.Status504GatewayTimeout, "write-unconfirmed", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "no-controller", ex.Message);
        }

        try
        {
            library.AssignTag(request.FloppyId, tagUid);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
        }

        catalogSource.TryReload();

        return Results.Json(
            new RetroBoxNfcWriteResult("written", null),
            RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult);
    }

    private static IResult Error(int statusCode, string code, string message)
    {
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.RetroBoxErrorView,
            statusCode: statusCode);
    }
}
```

Map it in `RetroBoxWebHost.StartAsync`:

```csharp
        RetroBoxNfcEndpoints.Map(app, options, catalogSource, nfcChannel);
```

- [ ] **Step 9: Write the failing read-back test**

The `write-unconfirmed` path is the phase 1 carry-forward that says an ambiguous write should be resolved by reading the tag back rather than reported as a failure. Append to `tests/RetroBox.Tests/RetroBoxNfcEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task Read_back_reports_what_is_actually_on_the_tag()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using (var write = await PostAsync(context, "disk1", confirm: false))
        {
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        }

        var drive = await context.Client.GetStringAsync("/api/drive");

        // The tag now carries disk1's id, so the drive endpoint's TAGID probe sees it as a known
        // UID rather than a blank tag the panel would offer to assign again.
        Assert.Contains("04A13BFE", drive, StringComparison.Ordinal);
    }
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 11: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "feat(web): assign a floppy to the tag in the drive"
```

---

## Task 5: The assign panel, in Spanish and English

**Branch:** `feat/assign-ui` off `feat/nfc-write-endpoint`

**Files:**
- Modify: `src/RetroBox.Web/wwwroot/index.html`, `app.css`, `app.js`
- Modify: `src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs` — the guard message
- Modify: `appliance/README.md`, `docs/architecture.md`
- Test: `tests/RetroBox.Tests/RetroBoxStaticAssetsTests.cs`

**Interfaces:**
- Consumes: `GET /api/drive`, `GET /api/drive/events`, `POST /api/nfc/write` (Tasks 2 and 4).
- Produces: nothing consumed by later tasks.

### What the drive section shows

The panel gains a section above the library, driven by the SSE stream:

| Drive state | Panel |
| --- | --- |
| `unavailable` | "No controller connected" — the assign control is hidden entirely |
| `empty` | "No disk in the drive" |
| `loaded` | The cataloged floppy's label, and an offer to reassign the tag |
| `blankTag` | "Blank tag, ready to assign", the UID, and a picker over the catalog |

- [ ] **Step 1: Write the failing string-parity test**

The existing `Both_languages_define_exactly_the_same_keys` already enforces parity and now matches quoted, hyphenated keys. Add the new codes to the assertion of what must exist, so a missing entry fails loudly rather than showing a raw key. Append to `tests/RetroBox.Tests/RetroBoxStaticAssetsTests.cs`:

```csharp
    [Theory]
    [InlineData("no-tag-present")]
    [InlineData("tag-already-assigned")]
    [InlineData("write-failed")]
    [InlineData("write-unconfirmed")]
    [InlineData("no-controller")]
    public void Every_nfc_error_code_has_text_in_both_languages(string code)
    {
        Assert.True(RetroBoxStaticAssets.TryGet("app.js", out var js, out _));

        var script = System.Text.Encoding.UTF8.GetString(js);
        var occurrences = System.Text.RegularExpressions.Regex.Matches(script, $"\"{code}\"\\s*:").Count;

        Assert.Equal(2, occurrences);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — the five codes appear zero times.

- [ ] **Step 3: Add the drive section to the markup**

In `src/RetroBox.Web/wwwroot/index.html`, immediately before the library `<section>`:

```html
  <section class="card drive" id="drive" hidden>
    <div class="card-head">
      <h2 data-i18n="driveTitle"></h2>
      <span id="drive-state" class="hint"></span>
    </div>
    <p id="drive-detail" class="hint"></p>
    <div class="actions" id="assign" hidden>
      <select id="assign-target" aria-label="Floppy"></select>
      <button id="assign-write" class="primary" data-i18n="assignButton"></button>
    </div>
  </section>
```

- [ ] **Step 4: Style it**

Append to `src/RetroBox.Web/wwwroot/app.css`:

```css
.drive { border-left: 3px solid var(--accent); }

.drive .actions { margin-top: 0.75rem; }

.drive select {
  min-width: 14rem;
  padding: 0.4rem 0.6rem;
  border: 1px solid var(--line);
  border-radius: 0.25rem;
  background: var(--inset);
  color: var(--text);
  font: inherit;
}
```

- [ ] **Step 5: Add the strings**

In `src/RetroBox.Web/wwwroot/app.js`, add to the `es` dictionary, keeping the four-space indentation the parity test matches:

```javascript
    driveTitle: "Disquetera",
    driveUnavailable: "Sin controlador conectado",
    driveEmpty: "No hay disco en la disquetera",
    driveLoaded: "Disco puesto: {label}",
    driveBlankTag: "Tag en blanco, listo para asignar ({uid})",
    assignButton: "Grabar tag",
    assignReassign: "Reasignar este tag",
    assignDone: "Tag grabado",
    confirmReassign: "Ese tag ya es de \"{owner}\". Reasignarlo a este disquete?",
    "no-tag-present": "No hay ningun disco en la disquetera.",
    "tag-already-assigned": "Ese tag ya esta asignado a otro disquete.",
    "write-failed": "El controlador no pudo grabar el tag.",
    "write-unconfirmed": "No se pudo confirmar si el tag quedo grabado. Fijate que dice la disquetera.",
    "no-controller": "No hay controlador de disquetes conectado.",
```

and the same keys to `en`:

```javascript
    driveTitle: "Drive",
    driveUnavailable: "No controller connected",
    driveEmpty: "No disk in the drive",
    driveLoaded: "Disk in the drive: {label}",
    driveBlankTag: "Blank tag, ready to assign ({uid})",
    assignButton: "Write tag",
    assignReassign: "Reassign this tag",
    assignDone: "Tag written",
    confirmReassign: "That tag already belongs to \"{owner}\". Reassign it to this floppy?",
    "no-tag-present": "There is no disk in the drive.",
    "tag-already-assigned": "That tag is already assigned to another floppy.",
    "write-failed": "The controller could not write the tag.",
    "write-unconfirmed": "Could not confirm whether the tag was written. Check the drive.",
    "no-controller": "No floppy controller is connected.",
```

- [ ] **Step 6: Subscribe to the drive stream and wire the button**

Append to `src/RetroBox.Web/wwwroot/app.js`:

```javascript
let drive = { state: "unavailable", floppyId: null, mode: null, tagUid: null };

function renderDrive() {
  const section = document.getElementById("drive");
  const state = document.getElementById("drive-state");
  const detail = document.getElementById("drive-detail");
  const assign = document.getElementById("assign");
  const target = document.getElementById("assign-target");

  section.hidden = drive.state === "unavailable";
  if (section.hidden) {
    return;
  }

  if (drive.state === "loaded") {
    const known = floppies.find((floppy) => floppy.id === drive.floppyId);
    state.textContent = t("driveLoaded", { label: known ? known.label : drive.floppyId });
    detail.textContent = t("assignReassign");
  } else if (drive.state === "blankTag") {
    state.textContent = t("driveBlankTag", { uid: drive.tagUid });
    detail.textContent = "";
  } else {
    state.textContent = t("driveEmpty");
    detail.textContent = "";
  }

  assign.hidden = drive.state === "empty";
  if (assign.hidden) {
    return;
  }

  const selected = target.value;
  target.textContent = "";
  for (const floppy of floppies) {
    const option = document.createElement("option");
    option.value = floppy.id;
    option.textContent = floppy.label;
    target.appendChild(option);
  }

  if (selected) {
    target.value = selected;
  }
}

function subscribeToDrive() {
  const source = new EventSource("/api/drive/events");

  source.addEventListener("message", (event) => {
    drive = JSON.parse(event.data);
    renderDrive();
  });

  // EventSource reconnects on its own; a failure only means the panel is momentarily blind.
  source.addEventListener("error", () => {
    drive = { state: "unavailable", floppyId: null, mode: null, tagUid: null };
    renderDrive();
  });
}

async function writeTag(confirm) {
  const button = document.getElementById("assign-write");
  const floppyId = document.getElementById("assign-target").value;

  if (!floppyId) {
    return;
  }

  button.disabled = true;

  try {
    const response = await fetch("/api/nfc/write", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ floppyId: floppyId, confirm: confirm })
    });

    if (response.ok) {
      document.getElementById("drive-detail").textContent = t("assignDone");
      await loadCatalog();
      return;
    }

    const body = await response.json().catch(() => null);

    if (body && body.code === "tag-already-assigned" && !confirm) {
      const owner = floppies.find((floppy) => floppy.id === body.previousFloppyId);
      if (window.confirm(t("confirmReassign", { owner: owner ? owner.label : body.previousFloppyId }))) {
        button.disabled = false;
        await writeTag(true);
        return;
      }

      return;
    }

    window.alert(await readError(response));
  } catch (error) {
    window.alert(t("networkError"));
  } finally {
    button.disabled = false;
  }
}

document.getElementById("assign-write").addEventListener("click", () => writeTag(false));
subscribeToDrive();
```

Call `renderDrive()` from `render()` as well, so a language change and a catalog reload both refresh the drive section's text and its picker.

Note the `tag-already-assigned` branch reads `body.previousFloppyId` from `RetroBoxNfcWriteResult`, not from the error shape — that response deliberately carries the owner so the confirmation can name it.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS. If the parity test fails, a key is missing from one dictionary — fix the dictionary.

- [ ] **Step 8: Correct the guard message and the docs**

The mount guard's message and the panel's "No NFC" badge both describe stopping the service and running `retrobox nfc write`. That procedure is now obsolete for anyone with a panel.

In `src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs`, replace the message so it points at the panel, and update the test that asserts on its text:

```csharp
                $"Floppy '{insert.Id}' has no assigned tag; put it in the drive and assign it from the web panel."
```

In `src/RetroBox.Web/wwwroot/app.js`, change `untaggedHelp` in both dictionaries to say the disk needs a tag and that it can be written from the drive section above, rather than that tag writing arrives later.

In `appliance/README.md` and `docs/architecture.md`, replace the paragraphs describing the stop-the-service procedure with what is now true: a floppy uploaded through the panel is listed untagged, and assigning it means putting it in the drive and using the panel's drive section. Keep every statement true of the code — games grouping and cover art still do not exist.

- [ ] **Step 9: Run the gates and commit**

```bash
mise run test
mise run format-check
git add -A
git commit -m "feat(web): assign tags from the panel"
```

---

## Phase 3 exit criteria

- `mise run test` and `mise run format-check` pass; CI's AOT publish and `shellcheck` jobs pass.
- A floppy uploaded through the panel can be assigned a tag from the panel and then **mounts when inserted** — the gap this phase exists to close, and the one the phase 2 plan wrongly claimed phase 2 would close.
- A controller absent at boot, or unplugged and re-plugged, is picked up without restarting the service.
- A tag already belonging to another floppy is refused until confirmed, and confirming takes the tag from the previous owner so the mount guard cannot accept a stale one.
- With no controller attached, the panel hides the drive section and everything else still works.
- Still out of scope, by design: games grouping, cover art, the scraper settings screen, and authentication.

## Carried into phase 4

- `RetroBoxGame` still has an unused `DefaultVm` and `init` accessors; phase 4 activates the record and should drop the former and change the latter, since the YamlDotNet static generator needs settable properties.
- The library's lock is in-process only, so `retrobox import` over SSH concurrent with a panel write can still clobber an entry.
- `Program.cs` invokes synchronously, so the daemon action's `CancellationToken` is `None` in production; shutdown is Ctrl+C or a signal. If a future change needs a clean `systemctl stop`, note that nothing currently awaits the generic host's `WaitForShutdown`, so SIGTERM may be absorbed until `TimeoutStopSec`.
