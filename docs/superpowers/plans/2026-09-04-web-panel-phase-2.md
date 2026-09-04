# RetroBox Web Panel — Phase 2: Web Host and Library Management

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a LAN-reachable web panel that lists, uploads, edits and deletes cataloged floppy images, hosted inside the daemon process, with the daemon seeing catalog changes without a restart.

**Architecture:** A new `RetroBox.Web` assembly hosts a Minimal API and an embedded static panel; the CLI gains an ASP.NET Core framework reference and a `--web-port` option, and starts the host alongside the existing read loop. The daemon stops holding an immutable catalog snapshot and reads through a watching catalog source, so an upload from the panel — or a `retrobox import` over SSH — is visible immediately.

**Tech Stack:** .NET 10 / C# 13, ASP.NET Core Minimal APIs on `WebApplication.CreateSlimBuilder`, `System.Text.Json` source generation, `FileSystemWatcher`, xUnit. No Blazor, no MVC, no SignalR, no Node build step.

**Spec:** [`docs/superpowers/specs/2026-09-03-web-panel-design.md`](../specs/2026-09-03-web-panel-design.md)

## Global Constraints

- C# 13, `nullable enable`, implicit usings. **English identifiers and comments.**
- Flat `RetroBox*`-prefixed, file-per-concern classes. No comments unless they explain a non-obvious decision.
- Use `mise` tasks only. Never invoke `dotnet` directly for project workflows.
- Gates before every PR: `mise run test` **and** `mise run format-check`.
- **Native AOT must keep publishing.** CI runs `mise run publish-linux-x64` (`-p:PublishAot=true -warnaserror`) on Ubuntu. It **cannot** complete on macOS — it fails at the native link step (`llvm-objcopy` missing, or `-fuse-ld=bfd` unsupported by Apple clang). Do not treat that as a defect and do not run it as a gate.
- **Minimal APIs only.** Blazor Server, Razor components, MVC and SignalR are unsupported under Native AOT.
- **No authentication.** The panel is LAN-trusted; this is an explicit, recorded owner decision. Do not add auth, and do not add TLS.
- Static assets are **embedded resources**, so `/opt/retrobox/retrobox` stays a single file. No `wwwroot` for the installer to copy, no CDN, no Node.
- API errors are `{ "code": "...", "message": "..." }` with **camelCase** JSON. Error *codes* travel to the client; the client owns the human text.
- The NFC tag payload format is unchanged: `<catalog-id>,<mode>`.
- Default web port: **8080**. `--web-port 0` disables the panel.

## Verified toolchain facts (from a throwaway spike — do not re-derive these)

A spike on `main` confirmed the whole stack compiles AOT-clean, and surfaced four details that are easy to get wrong:

1. `Microsoft.NET.Sdk` accepts `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. **Do not switch any project to `Microsoft.NET.Sdk.Web`.**
2. Use `WebApplication.CreateSlimBuilder()` — the AOT-friendly builder.
3. `builder.WebHost.UseUrls(...)` needs `using Microsoft.AspNetCore.Hosting;`. Without it you get CS1061 on `ConfigureWebHostBuilder`.
4. **`app.RunAsync()` does not take a `CancellationToken`** — its overload takes `string? url`. Use `StartAsync(ct)` / `StopAsync()`.

With those, ILCompiler produced a linux-x64 native object with **zero AOT or trim warnings** under `-warnaserror`, alongside System.CommandLine in the same binary.

---

## PR stack

Every PR stacks on its predecessor. Each is independently reviewable, passes both gates, and leaves the appliance working.

| PR | Branch | Base | Task | Size |
| --- | --- | --- | --- | --- |
| 1 | `feat/live-catalog` | `main` | Task 1 — the daemon reads a live catalog | ~250 lines |
| 2 | `feat/web-host` | `feat/live-catalog` | Task 2 — `RetroBox.Web`, `--web-port`, read-only API | ~350 lines |
| 3 | `feat/library-endpoints` | `feat/web-host` | Task 3 — upload, delete, patch | ~400 lines |
| 4 | `feat/panel-ui` | `feat/library-endpoints` | Task 4 — the panel itself, es/en | ~450 lines |
| 5 | `feat/web-appliance-wiring` | `feat/panel-ui` | Task 5 — systemd, installer, docs | ~120 lines |

**Task 1 ships value on its own, independent of the web panel:** today a `retrobox import` run while the daemon is up is invisible to it until a restart. That is a live bug on the appliance right now.

## Two deliberate departures from the spec's phase list

- **The live catalog moves from "Phase 2 prerequisites" into Phase 2 (Task 1).** The spec framed it around the phase 3 write flow, but phase 2's own endpoints create the staleness: upload a floppy through the panel, insert it, and the daemon answers `Unknown floppy` forever. The panel would be broken from its first release.
- **Localization moves from spec phase 4 into Phase 4 of *this* plan (Task 4).** The spec paired i18n with cover art, but the UI is written here — deferring it means writing every string twice.

## File structure

**New:**

| File | Responsibility |
| --- | --- |
| `src/RetroBox.Core/RetroBoxCatalogSource.cs` | `IRetroBoxCatalogSource` + a static implementation |
| `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs` | Debounced reload on YAML change; keeps the previous catalog when a reload is invalid |
| `src/RetroBox.Core/RetroBoxFloppyLibrary.cs` | Transactional delete and label/mode updates |
| `src/RetroBox.Web/RetroBox.Web.csproj` | The web assembly |
| `src/RetroBox.Web/RetroBoxWebOptions.cs` | Port and config-root options |
| `src/RetroBox.Web/RetroBoxWebContracts.cs` | Wire DTOs + `JsonSerializerContext` |
| `src/RetroBox.Web/RetroBoxCatalogEndpoints.cs` | Pure view-building from the catalog source |
| `src/RetroBox.Web/RetroBoxLibraryEndpoints.cs` | Upload / delete / patch handlers |
| `src/RetroBox.Web/RetroBoxStaticAssets.cs` | Embedded-resource lookup |
| `src/RetroBox.Web/RetroBoxWebHost.cs` | Kestrel composition, start/stop, bound address |
| `src/RetroBox.Web/wwwroot/index.html` | The panel markup |
| `src/RetroBox.Web/wwwroot/app.css` | Hand-written styling (no Tailwind) |
| `src/RetroBox.Web/wwwroot/app.js` | Rendering, actions, i18n |

**Modified:** `RetroBoxFloppyEventHandler`, `RetroBoxDaemon`, `CliCommandFactory`, `RetroBox.Cli.csproj`, `RetroBox.slnx`, `FloppyControlTestDoubles.cs`, the daemon/event-handler test files, `retrobox-daemon.service`, `hardware-detect.sh`, `docs/architecture.md`, `appliance/README.md`.

---

## Task 1: The daemon reads a live catalog

**Branch:** `feat/live-catalog` off `main`

**Files:**
- Create: `src/RetroBox.Core/RetroBoxCatalogSource.cs`
- Create: `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs`
- Modify: `src/RetroBox.Daemon/RetroBoxDaemon.cs`
- Modify: `src/RetroBox.Cli/CliCommandFactory.cs`
- Modify: `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`
- Modify: `tests/RetroBox.Tests/RetroBoxDaemonTests.cs`, `tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs`
- Test: `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`

**Interfaces:**
- Consumes: `RetroBoxConfigStore.Load()`, `RetroBoxCatalogException` (both existing).
- Produces:
  - `public interface IRetroBoxCatalogSource { RetroBoxCatalogData Current { get; } string? LastError { get; } bool TryReload() => false; }`
  - `public static RetroBoxCatalogData RetroBoxCatalogData.Empty { get; }`
  - `public sealed class RetroBoxStaticCatalogSource(RetroBoxCatalogData catalog) : IRetroBoxCatalogSource`
  - `public sealed class RetroBoxWatchingCatalogSource : IRetroBoxCatalogSource, IDisposable` with constructor `(string rootPath, RetroBoxCatalogData initial, Action<string>? onReloadFailed = null, TimeSpan? debounce = null, bool watchFileSystem = true)`, `bool Reload()`, and `static readonly TimeSpan DefaultDebounce`
  - `RetroBoxFloppyEventHandler(IRetroBoxCatalogSource catalogSource, IRetroBoxFloppyControlClient floppyControlClient)`
  - `RetroBoxDaemon`'s first constructor parameter becomes `IRetroBoxCatalogSource catalogSource`

**A trick that keeps this diff small:** the three test helpers named `CreateCatalog` (in `FloppyControlTestDoubles.cs` and one private delegating copy in each of `RetroBoxDaemonTests.cs` and `RetroBoxFloppyEventHandlerTests.cs`) change their **return type** to `IRetroBoxCatalogSource`. Roughly fifteen call sites then keep their exact current shape. Check each call site still compiles — any test that reaches into the returned value's `.Floppies` needs the underlying data instead.

- [ ] **Step 1: Write the failing tests for the catalog source**

Create `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxWatchingCatalogSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-catalog-{Guid.NewGuid():N}");

    public RetroBoxWatchingCatalogSourceTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Current_returns_the_initial_catalog_before_any_reload()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();

        using var source = new RetroBoxWatchingCatalogSource(root, initial, watchFileSystem: false);

        Assert.Equal(["disk1"], source.Current.Floppies.Keys);
    }

    [Fact]
    public void Reload_publishes_a_floppy_added_after_construction()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, watchFileSystem: false);

        WriteCatalog("disk1", "disk2");

        Assert.True(source.Reload());
        Assert.Equal(["disk1", "disk2"], source.Current.Floppies.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Reload_keeps_the_previous_catalog_when_the_yaml_is_invalid()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        var failures = new List<string>();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, failures.Add, watchFileSystem: false);

        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: [ this is not a mapping");

        Assert.False(source.Reload());
        Assert.Equal(["disk1"], source.Current.Floppies.Keys);
        Assert.NotNull(source.LastError);
        Assert.Contains("keeping the previous catalog", Assert.Single(failures), StringComparison.Ordinal);
    }

    [Fact]
    public void LastError_clears_once_the_yaml_is_valid_again()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(
            root,
            initial,
            watchFileSystem: false,
            initialError: "broken on startup");

        Assert.NotNull(source.LastError);

        Assert.True(source.Reload());
        Assert.Null(source.LastError);
    }

    [Fact]
    public void An_empty_catalog_plus_a_startup_error_is_a_usable_state()
    {
        using var source = new RetroBoxWatchingCatalogSource(
            root,
            RetroBoxCatalogData.Empty,
            watchFileSystem: false,
            initialError: "floppies.yaml is invalid");

        Assert.Empty(source.Current.Floppies);
        Assert.Equal("floppies.yaml is invalid", source.LastError);
    }

    [Fact]
    public void Reload_keeps_the_previous_catalog_when_an_image_file_disappears()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, watchFileSystem: false);

        File.Delete(Path.Combine(root, "disk1.img"));

        Assert.False(source.Reload());
        Assert.Equal(["disk1"], source.Current.Floppies.Keys);
    }

    [Fact]
    public async Task Watcher_republishes_a_change_made_by_someone_else()
    {
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(
            root,
            initial,
            debounce: TimeSpan.FromMilliseconds(20));

        WriteCatalog("disk1", "disk2");

        for (var attempt = 0; attempt < 200 && source.Current.Floppies.Count < 2; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Equal(2, source.Current.Floppies.Count);
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
            lines.Add("    nfc: true");
        }

        File.WriteAllText(Path.Combine(root, "floppies.yaml"), string.Join('\n', lines) + '\n');
    }
}
```

The fourth test matters most for the appliance: `RetroBoxConfigStore.Validate` throws when a floppy's `image` no longer exists, and that exception must never reach a running daemon.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBoxWatchingCatalogSource` does not exist (compile error).

- [ ] **Step 3: Define the interface and the static source**

Create `src/RetroBox.Core/RetroBoxCatalogSource.cs`:

```csharp
namespace RetroBox.Core;

/// <summary>A read-through view of the catalog, which can change while the daemon runs.</summary>
public interface IRetroBoxCatalogSource
{
    RetroBoxCatalogData Current { get; }

    /// <summary>Why the last load or reload was rejected, or null when the catalog is good.</summary>
    string? LastError { get; }

    /// <summary>Reloads now if this source can. Returns true when the catalog was replaced.</summary>
    bool TryReload() => false;
}

public sealed class RetroBoxStaticCatalogSource(RetroBoxCatalogData catalog) : IRetroBoxCatalogSource
{
    public RetroBoxCatalogData Current => catalog;

    public string? LastError => null;
}
```

Also add an empty catalog to `src/RetroBox.Core/RetroBoxCatalogModels.cs`, on `RetroBoxCatalogData`. The web host needs something to serve when the YAML on disk cannot be loaded at all:

```csharp
    public static RetroBoxCatalogData Empty { get; } = new(
        new RetroBoxConfig(),
        new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal),
        new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal));
```

- [ ] **Step 4: Implement the watching source**

Create `src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs`:

```csharp
namespace RetroBox.Core;

/// <summary>
/// Republishes the catalog whenever the YAML under the config root changes, whoever changed it —
/// the web panel, `retrobox import`, or someone over SSH. A reload that does not validate is
/// discarded: a half-written or malformed file must not take down a running daemon.
/// </summary>
public sealed class RetroBoxWatchingCatalogSource : IRetroBoxCatalogSource, IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly RetroBoxConfigStore store;
    private readonly Action<string>? onReloadFailed;
    private readonly TimeSpan debounce;
    private readonly FileSystemWatcher? watcher;
    private readonly Lock gate = new();
    private volatile RetroBoxCatalogData current;
    private volatile string? lastError;
    private CancellationTokenSource? pendingReload;
    private bool disposed;

    public RetroBoxWatchingCatalogSource(
        string rootPath,
        RetroBoxCatalogData initial,
        Action<string>? onReloadFailed = null,
        TimeSpan? debounce = null,
        bool watchFileSystem = true,
        string? initialError = null)
    {
        store = new RetroBoxConfigStore(rootPath);
        current = initial;
        lastError = initialError;
        this.onReloadFailed = onReloadFailed;
        this.debounce = debounce ?? DefaultDebounce;

        if (!watchFileSystem)
        {
            return;
        }

        // FileSystemWatcher throws when the directory is missing, and a first boot can reach
        // here before anything has written the catalog.
        Directory.CreateDirectory(rootPath);

        watcher = new FileSystemWatcher(rootPath, "*.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnCatalogChanged;
        watcher.Created += OnCatalogChanged;
        watcher.Deleted += OnCatalogChanged;
        watcher.Renamed += OnCatalogChanged;
        watcher.EnableRaisingEvents = true;
    }

    public RetroBoxCatalogData Current => current;

    public string? LastError => lastError;

    public bool TryReload() => Reload();

    /// <summary>Reloads now. Returns false and keeps the previous catalog when the YAML is unusable.</summary>
    public bool Reload()
    {
        try
        {
            current = store.Load();
            lastError = null;
            return true;
        }
        catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
        {
            lastError = ex.Message;
            onReloadFailed?.Invoke($"Catalog reload failed, keeping the previous catalog: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pendingReload?.Cancel();
        }

        watcher?.Dispose();
    }

    private void OnCatalogChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleReload();
    }

    // A single save rewrites several YAML files and raises several events; debouncing coalesces
    // them into one reload and lets a partially written file settle before it is parsed.
    private void ScheduleReload()
    {
        CancellationTokenSource scheduled;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pendingReload?.Cancel();
            scheduled = new CancellationTokenSource();
            pendingReload = scheduled;
        }

        _ = ReloadAfterDebounceAsync(scheduled.Token);
    }

    private async Task ReloadAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(debounce, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Reload();
    }
}
```

The superseded `CancellationTokenSource` is cancelled but not disposed: a token source with no timer holds no unmanaged resource, and disposing one while a `Task.Delay` registration is still live can throw.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS for the five new tests. The rest of the suite still passes — nothing consumes the new types yet.

- [ ] **Step 6: Commit the catalog source**

```bash
git add src/RetroBox.Core/RetroBoxCatalogSource.cs \
        src/RetroBox.Core/RetroBoxWatchingCatalogSource.cs \
        tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs
git commit -m "feat(core): add a catalog source that reloads on YAML changes"
```

- [ ] **Step 7: Write the failing test for a live handler**

Append to `tests/RetroBox.Tests/RetroBoxFloppyEventHandlerTests.cs`:

```csharp
    [Fact]
    public async Task HandleAsync_sees_a_floppy_added_to_the_catalog_after_construction()
    {
        var client = new RecordingFloppyControlClient();
        var source = new MutableCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        var handler = new RetroBoxFloppyEventHandler(source, client);

        var before = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk2", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        Assert.Equal(RetroBoxFloppyEventHandlerAction.Failed, before.Action);

        source.Publish(
            FloppyControlTestCatalogs.CreateCatalog("disk2", "/data/floppies/disk2.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var after = await handler.HandleAsync(
            new RetroBoxArduinoInsertEvent("disk2", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Equal(RetroBoxFloppyEventHandlerAction.Inserted, after.Action);
        Assert.Equal("insert:0:/data/floppies/disk2.img:True", Assert.Single(client.Calls));
    }
```

Add this double to `tests/RetroBox.Tests/FloppyControlTestDoubles.cs`:

```csharp
internal sealed class MutableCatalogSource(RetroBoxCatalogData initial) : IRetroBoxCatalogSource
{
    private RetroBoxCatalogData current = initial;

    public RetroBoxCatalogData Current => current;

    public string? LastError => null;

    public void Publish(RetroBoxCatalogData catalog) => current = catalog;
}
```

- [ ] **Step 8: Run the test to verify it fails**

Run: `mise run test`

Expected: FAIL — `RetroBoxFloppyEventHandler` takes `RetroBoxCatalogData`, not a source (compile error).

- [ ] **Step 9: Make the handler and the daemon read through the source**

In `src/RetroBox.Daemon/RetroBoxFloppyEventHandler.cs`, change the primary constructor:

```csharp
public sealed class RetroBoxFloppyEventHandler(
    IRetroBoxCatalogSource catalogSource,
    IRetroBoxFloppyControlClient floppyControlClient)
```

and, as the first line of `HandleInsertAsync`, read the current catalog once so a mid-handler reload cannot split the decision:

```csharp
        var catalog = catalogSource.Current;
```

In `src/RetroBox.Daemon/RetroBoxDaemon.cs`, change the first primary-constructor parameter from `RetroBoxCatalogData catalog` to `IRetroBoxCatalogSource catalogSource`, and pass it through where the handler is built:

```csharp
        var handler = new RetroBoxFloppyEventHandler(catalogSource, floppyControlClient);
```

- [ ] **Step 10: Retype the three test helpers**

Change `FloppyControlTestCatalogs.CreateCatalog` to keep returning `RetroBoxCatalogData` (the new `MutableCatalogSource` test needs the raw data), and change **only the two private delegating helpers** — in `RetroBoxDaemonTests.cs` and `RetroBoxFloppyEventHandlerTests.cs` — to wrap it:

```csharp
    private static IRetroBoxCatalogSource CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog(floppyId, imagePath, mode, nfc));
    }
```

Every existing `CreateCatalog(...)` call site keeps its exact shape. If any call site uses the result as `RetroBoxCatalogData`, call `FloppyControlTestCatalogs.CreateCatalog` directly there instead.

- [ ] **Step 11: Wire the CLI**

In `src/RetroBox.Cli/CliCommandFactory.cs`, inside the `daemon` command action, replace the `store.Load()` call that produces `catalog` with a watching source rooted at the config root, and pass the source to `RetroBoxDaemon`:

```csharp
                var store = new RetroBoxConfigStore(request.ConfigRoot);
                RetroBoxCatalogData initial;
                string? startupError = null;

                try
                {
                    initial = store.Load();
                }
                catch (RetroBoxCatalogException ex)
                {
                    // A malformed catalog must not cost the owner the panel as well. Without it
                    // the only way back into the appliance is the GRUB recovery entry, so the
                    // daemon starts with an empty catalog and reports why.
                    initial = RetroBoxCatalogData.Empty;
                    startupError = ex.Message;
                    Console.Error.WriteLine($"Catalog is invalid; starting with an empty catalog: {ex.Message}");
                }

                using var catalogSource = new RetroBoxWatchingCatalogSource(
                    request.ConfigRoot,
                    initial,
                    message => Console.Error.WriteLine(message),
                    initialError: startupError);
```

**This is a behaviour change and it is deliberate**, required by the spec's error-handling
section: the daemon used to refuse to start on an invalid catalog. It now starts with an empty
one, so inserts fail with `Unknown floppy` — honest — while the panel comes up and can be used
to fix the problem. The watcher republishes the moment the YAML becomes valid again.

Where the code previously read `catalog.Config`, read `catalogSource.Current.Config`. Note the
config-derived values (`FloppyControlSocketPath`, `SerialPort`, `SerialBaud`) are resolved once
at startup as they are today; a reload does not re-open the serial port.

- [ ] **Step 12: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 13: Verify formatting**

Run: `mise run format-check`

- [ ] **Step 14: Commit and hand the branch back**

```bash
git add -A
git commit -m "feat(daemon): read the catalog through a live source"
```

---

## Task 2: The web host, `--web-port`, and a read-only catalog API

**Branch:** `feat/web-host` off `feat/live-catalog`

**Files:**
- Create: `src/RetroBox.Web/RetroBox.Web.csproj`, `RetroBoxWebOptions.cs`, `RetroBoxWebContracts.cs`, `RetroBoxCatalogEndpoints.cs`, `RetroBoxStaticAssets.cs`, `RetroBoxWebHost.cs`, `wwwroot/index.html`
- Modify: `RetroBox.slnx`, `src/RetroBox.Cli/RetroBox.Cli.csproj`, `src/RetroBox.Cli/CliCommandFactory.cs`, `tests/RetroBox.Tests/RetroBox.Tests.csproj`
- Test: `tests/RetroBox.Tests/RetroBoxCatalogEndpointsTests.cs`, `tests/RetroBox.Tests/RetroBoxWebHostTests.cs`

**Interfaces:**
- Consumes: `IRetroBoxCatalogSource` and `RetroBoxStaticCatalogSource` (Task 1).
- Produces:
  - `public sealed record RetroBoxWebOptions { public const int DefaultPort = 8080; public int Port { get; init; } = DefaultPort; public string ConfigRoot { get; init; } = RetroBoxConfigStore.DefaultRootPath; public string ScratchRoot { get; init; } = RetroBoxFloppyImporter.DefaultScratchRoot; public string CatalogedRoot { get; init; } = RetroBoxFloppyImporter.DefaultCatalogedRoot; }`
  - `public sealed record RetroBoxFloppyView(string Id, string Label, string Mode, string Size, bool Nfc)`
  - `public sealed record RetroBoxCatalogView(RetroBoxFloppyView[] Floppies, string? CatalogError)`
  - `public sealed record RetroBoxErrorView(string Code, string Message)`
  - `public sealed partial class RetroBoxWebJsonContext : JsonSerializerContext`
  - `public static RetroBoxCatalogView RetroBoxCatalogEndpoints.BuildCatalogView(IRetroBoxCatalogSource source)`
  - `public static bool RetroBoxStaticAssets.TryGet(string relativePath, out byte[] content, out string contentType)`
  - `public sealed class RetroBoxWebHost : IAsyncDisposable` with `static Task<RetroBoxWebHost> StartAsync(RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource, CancellationToken cancellationToken = default)` and `Uri BaseAddress { get; }`

- [ ] **Step 1: Create the project and register it**

Create `src/RetroBox.Web/RetroBox.Web.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\RetroBox.Core\RetroBox.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>

</Project>
```

Add it to `RetroBox.slnx` inside the `/src/` folder, keeping the alphabetical order:

```xml
    <Project Path="src/RetroBox.Web/RetroBox.Web.csproj" />
```

Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to `src/RetroBox.Cli/RetroBox.Cli.csproj` next to its `PackageReference`, plus a `ProjectReference` to `RetroBox.Web`. **Keep `Microsoft.NET.Sdk`** — do not switch to the Web SDK. Add a `ProjectReference` to `RetroBox.Web` in `tests/RetroBox.Tests/RetroBox.Tests.csproj` as well, and the same `FrameworkReference` there so the tests can reference ASP.NET types.

- [ ] **Step 2: Write the failing test for the catalog view**

Create `tests/RetroBox.Tests/RetroBoxCatalogEndpointsTests.cs`:

```csharp
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxCatalogEndpointsTests
{
    [Fact]
    public void BuildCatalogView_projects_every_floppy_field()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(source);

        var floppy = Assert.Single(view.Floppies);
        Assert.Equal("disk1", floppy.Id);
        Assert.Equal("Disk 1", floppy.Label);
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadOnlyMode, floppy.Mode);
        Assert.Equal(RetroBoxFloppyCatalogRules.DefaultImportSize, floppy.Size);
        Assert.True(floppy.Nfc);
    }

    [Fact]
    public void BuildCatalogView_orders_floppies_by_id()
    {
        var catalog = new RetroBoxCatalogData(
            new RetroBoxConfig { DefaultVm = "dos" },
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal)
            {
                ["dos"] = new() { Label = "DOS", Path = "/data/vms/dos" },
            },
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal)
            {
                ["zdisk"] = new() { Label = "Z", Image = "/z.img", Nfc = true },
                ["adisk"] = new() { Label = "A", Image = "/a.img", Nfc = true },
            });

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(new RetroBoxStaticCatalogSource(catalog));

        Assert.Equal(["adisk", "zdisk"], view.Floppies.Select(f => f.Id));
    }

    [Fact]
    public void BuildCatalogView_reports_the_catalog_error_so_the_panel_can_show_it()
    {
        using var source = new RetroBoxWatchingCatalogSource(
            Path.Combine(Path.GetTempPath(), $"retrobox-view-{Guid.NewGuid():N}"),
            RetroBoxCatalogData.Empty,
            watchFileSystem: false,
            initialError: "floppies.yaml is invalid");

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(source);

        Assert.Empty(view.Floppies);
        Assert.Equal("floppies.yaml is invalid", view.CatalogError);
    }

    [Fact]
    public void BuildCatalogView_reports_no_error_for_a_healthy_catalog()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Null(RetroBoxCatalogEndpoints.BuildCatalogView(source).CatalogError);
    }

    [Fact]
    public void BuildCatalogView_returns_an_empty_array_for_an_empty_catalog()
    {
        var catalog = new RetroBoxCatalogData(
            new RetroBoxConfig(),
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal),
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal));

        Assert.Empty(RetroBoxCatalogEndpoints.BuildCatalogView(new RetroBoxStaticCatalogSource(catalog)).Floppies);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBox.Web` types do not exist (compile error).

- [ ] **Step 4: Write the options, contracts, and the catalog view**

Create `src/RetroBox.Web/RetroBoxWebOptions.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Web;

public sealed record RetroBoxWebOptions
{
    public const int DefaultPort = 8080;

    public int Port { get; init; } = DefaultPort;

    public string ConfigRoot { get; init; } = RetroBoxConfigStore.DefaultRootPath;

    public string ScratchRoot { get; init; } = RetroBoxFloppyImporter.DefaultScratchRoot;

    public string CatalogedRoot { get; init; } = RetroBoxFloppyImporter.DefaultCatalogedRoot;
}
```

Create `src/RetroBox.Web/RetroBoxWebContracts.cs`:

```csharp
using System.Text.Json.Serialization;

namespace RetroBox.Web;

public sealed record RetroBoxFloppyView(string Id, string Label, string Mode, string Size, bool Nfc);

public sealed record RetroBoxCatalogView(RetroBoxFloppyView[] Floppies, string? CatalogError);

public sealed record RetroBoxErrorView(string Code, string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetroBoxCatalogView))]
[JsonSerializable(typeof(RetroBoxErrorView))]
public sealed partial class RetroBoxWebJsonContext : JsonSerializerContext;
```

Create `src/RetroBox.Web/RetroBoxCatalogEndpoints.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxCatalogEndpoints
{
    public static RetroBoxCatalogView BuildCatalogView(IRetroBoxCatalogSource source)
    {
        var floppies = source.Current.Floppies
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new RetroBoxFloppyView(
                entry.Key,
                entry.Value.Label,
                entry.Value.Mode,
                entry.Value.Size,
                entry.Value.Nfc))
            .ToArray();

        return new RetroBoxCatalogView(floppies, source.LastError);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS for the three new tests.

- [ ] **Step 6: Write the embedded asset lookup and the panel shell**

Create `src/RetroBox.Web/wwwroot/index.html` — a placeholder shell for now; Task 4 replaces its body:

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>RetroBox</title>
</head>
<body>
<h1>RetroBox</h1>
<p id="status">Cargando…</p>
</body>
</html>
```

Create `src/RetroBox.Web/RetroBoxStaticAssets.cs`:

```csharp
using System.Reflection;

namespace RetroBox.Web;

/// <summary>
/// Serves the panel from embedded resources so the appliance stays a single binary at
/// /opt/retrobox/retrobox with no wwwroot for the installer to copy.
/// </summary>
public static class RetroBoxStaticAssets
{
    private const string ResourcePrefix = "RetroBox.Web.wwwroot.";

    public static bool TryGet(string relativePath, out byte[] content, out string contentType)
    {
        content = [];
        contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var resourceName = ResourcePrefix + relativePath.Replace('/', '.');
        var assembly = typeof(RetroBoxStaticAssets).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return false;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        content = buffer.ToArray();
        contentType = ResolveContentType(relativePath);
        return true;
    }

    private static string ResolveContentType(string relativePath)
    {
        return Path.GetExtension(relativePath) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
    }
}
```

`GetManifestResourceStream` by name is AOT-safe — it is not reflection over types.

- [ ] **Step 7: Write the failing host test**

Create `tests/RetroBox.Tests/RetroBoxWebHostTests.cs`:

```csharp
using System.Net;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxWebHostTests
{
    [Fact]
    public async Task Get_catalog_returns_the_current_floppies_as_camel_case_json()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/catalog");

        Assert.Contains("\"floppies\"", body, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"disk1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"nfc\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_root_serves_the_embedded_panel()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("RetroBox", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_unknown_asset_returns_not_found()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.GetAsync("/nope.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_catalog_reflects_a_catalog_change_without_a_restart()
    {
        var source = new MutableCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.Contains("disk1", await client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);

        source.Publish(
            FloppyControlTestCatalogs.CreateCatalog("disk2", "/data/floppies/disk2.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var body = await client.GetStringAsync("/api/catalog");
        Assert.Contains("disk2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("disk1", body, StringComparison.Ordinal);
    }
}
```

`Port = 0` lets the OS pick a free port, so the tests never collide with a real panel or with each other.

- [ ] **Step 8: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBoxWebHost` does not exist (compile error).

- [ ] **Step 9: Implement the host**

Create `src/RetroBox.Web/RetroBoxWebHost.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RetroBox.Core;

namespace RetroBox.Web;

public sealed class RetroBoxWebHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private RetroBoxWebHost(WebApplication app, Uri baseAddress)
    {
        this.app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<RetroBoxWebHost> StartAsync(
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Bound to every interface on purpose: the panel is useless if it is not reachable from
        // a phone on the LAN.
        builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, RetroBoxWebJsonContext.Default));

        var app = builder.Build();

        app.MapGet("/api/catalog", () => RetroBoxCatalogEndpoints.BuildCatalogView(catalogSource));
        app.MapGet("/", () => ServeAsset("index.html"));
        app.MapGet("/{asset}", (string asset) => ServeAsset(asset));

        await app.StartAsync(cancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel reported no bound address.");

        // Kestrel reports 0.0.0.0; a client has to dial a routable host.
        return new RetroBoxWebHost(app, new Uri(address.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)));
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private static IResult ServeAsset(string relativePath)
    {
        return RetroBoxStaticAssets.TryGet(relativePath, out var content, out var contentType)
            ? Results.Bytes(content, contentType)
            : Results.NotFound();
    }
}
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 11: Add `--web-port` to the daemon command**

In `src/RetroBox.Cli/CliCommandFactory.cs`, add an option to `CreateDaemonCommand`:

```csharp
        var webPortOption = new Option<int>("--web-port")
        {
            Description = "Port for the web panel. 0 disables it.",
            DefaultValueFactory = _ => RetroBoxWebOptions.DefaultPort,
        };
```

Register it in the command's option list and add it to `RetroBoxDaemonCommandRequest`. In the action, start the host when the port is non-zero and stop it when the daemon returns:

```csharp
                var webPort = request.WebPort;
                await using var webHost = webPort == 0
                    ? null
                    : await RetroBoxWebHost.StartAsync(
                        new RetroBoxWebOptions { Port = webPort, ConfigRoot = request.ConfigRoot },
                        catalogSource,
                        cancellation.Token);
```

The action currently blocks with `GetAwaiter().GetResult()`. Convert it to an `async` lambda and `await daemon.RunAsync(cancellation.Token)` so the host's lifetime brackets the read loop. `catalogSource` is the same instance the daemon received, so the panel and the daemon always agree.

- [ ] **Step 12: Write the failing CLI smoke test**

Append to `tests/RetroBox.Tests/CliHelpSmokeTests.cs`:

```csharp
    [Fact]
    public void Daemon_help_documents_the_web_port_option()
    {
        var output = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();
        var parseResult = command.Parse(["daemon", "--help"]);
        parseResult.InvocationConfiguration.Output = output;

        Assert.Equal(0, parseResult.Invoke());
        Assert.Contains("--web-port", output.ToString(), StringComparison.Ordinal);
    }
```

The factory method is `CreateRootCommand()`, and output is captured by assigning
`parseResult.InvocationConfiguration.Output` — this is the pattern the CLI tests already
use (see `tests/RetroBox.Tests/CliVmTests.cs:12-15`).

- [ ] **Step 13: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 14: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "feat(web): host a read-only catalog API inside the daemon"
```

---

## Task 3: Upload, delete, and patch

**Branch:** `feat/library-endpoints` off `feat/web-host`

**Files:**
- Create: `src/RetroBox.Core/RetroBoxFloppyLibrary.cs`
- Create: `src/RetroBox.Web/RetroBoxLibraryEndpoints.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebContracts.cs`, `src/RetroBox.Web/RetroBoxWebHost.cs`
- Modify: `src/RetroBox.Core/RetroBoxCatalogValidation.cs`
- Test: `tests/RetroBox.Tests/RetroBoxFloppyLibraryTests.cs`, `tests/RetroBox.Tests/RetroBoxLibraryEndpointsTests.cs`

**Interfaces:**
- Consumes: `RetroBoxConfigStore`, `RetroBoxFloppyImporter`, `RetroBoxFloppyImportRequest`, `RetroBoxCatalogRules.IsValidId`, `RetroBoxFloppyCatalogRules.IsValidMode` (all existing); `RetroBoxWebOptions`, `RetroBoxErrorView` (Task 2).
- Produces:
  - `public static string RetroBoxCatalogRules.Slugify(string value)`
  - `public sealed class RetroBoxFloppyLibrary(RetroBoxConfigStore store)` with `void Delete(string id)` and `void UpdateLabelAndMode(string id, string? label, string? mode)`
  - `public sealed record RetroBoxFloppyPatch(string? Label, string? Mode)` (registered in `RetroBoxWebJsonContext`)
  - `public static class RetroBoxLibraryEndpoints` with `void Map(WebApplication app, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)`

- [ ] **Step 1: Write the failing tests for the slug**

Create `tests/RetroBox.Tests/RetroBoxCatalogRulesTests.cs` — verified: no test file currently
covers `RetroBoxCatalogRules`, so this is a new file:

```csharp
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxCatalogRulesTests
{
    [Theory]
    [InlineData("MONKEY1.IMG", "monkey1")]
    [InlineData("Monkey Island Disk 1.ima", "monkey-island-disk-1")]
    [InlineData("mi_d1.img", "mi-d1")]
    [InlineData("sm91__d1.DSK", "sm91-d1")]
    [InlineData("--weird--.img", "weird")]
    public void Slugify_produces_a_valid_catalog_id(string fileName, string expected)
    {
        var slug = RetroBoxCatalogRules.Slugify(Path.GetFileNameWithoutExtension(fileName));

        Assert.Equal(expected, slug);
        Assert.True(RetroBoxCatalogRules.IsValidId(slug));
    }

    [Fact]
    public void Slugify_returns_an_empty_string_when_nothing_usable_remains()
    {
        Assert.Equal(string.Empty, RetroBoxCatalogRules.Slugify("!!!"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `Slugify` does not exist.

- [ ] **Step 3: Implement the slug**

Append to `src/RetroBox.Core/RetroBoxCatalogValidation.cs`, inside `RetroBoxCatalogRules`:

```csharp
    /// <summary>Reduces a filename to something <see cref="IsValidId"/> accepts, or an empty string.</summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value)
        {
            var lowered = char.ToLowerInvariant(character);
            if (char.IsAsciiLetterLower(lowered) || char.IsAsciiDigit(lowered))
            {
                builder.Append(lowered);
                previousWasDash = false;
                continue;
            }

            if (builder.Length > 0 && !previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 5: Write the failing tests for the library**

Create `tests/RetroBox.Tests/RetroBoxFloppyLibraryTests.cs`:

```csharp
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-library-{Guid.NewGuid():N}");

    public RetroBoxFloppyLibraryTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Delete_removes_the_entry_and_then_the_image()
    {
        var image = WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.Delete("disk1");

        Assert.Empty(new RetroBoxConfigStore(root).Load().Floppies);
        Assert.False(File.Exists(image));
    }

    [Fact]
    public void Delete_leaves_a_loadable_catalog_when_the_image_cannot_be_removed()
    {
        var image = WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        using (File.Open(image, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                library.Delete("disk1");
            }
            catch (IOException)
            {
            }
        }

        // Whatever happened to the file, the catalog must still load: the daemon and
        // `retrobox boot` both call Load() and an orphaned entry would stop the appliance.
        Assert.Empty(new RetroBoxConfigStore(root).Load().Floppies);
    }

    [Fact]
    public void Delete_rejects_an_unknown_id()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxCatalogException>(() => library.Delete("nope"));
    }

    [Fact]
    public void UpdateLabelAndMode_changes_the_label_without_touching_nfc()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", "Monkey Island", null);

        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.Equal("Monkey Island", floppy.Label);
        Assert.True(floppy.Nfc);
    }

    [Fact]
    public void UpdateLabelAndMode_clears_nfc_when_the_mode_changes()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", null, RetroBoxFloppyCatalogRules.ReadWriteMode);

        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadWriteMode, floppy.Mode);
        Assert.False(floppy.Nfc);
        Assert.Null(floppy.NfcUid);
    }

    [Fact]
    public void UpdateLabelAndMode_keeps_nfc_when_the_mode_is_unchanged()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", "Renamed", RetroBoxFloppyCatalogRules.ReadOnlyMode);

        Assert.True(new RetroBoxConfigStore(root).Load().Floppies["disk1"].Nfc);
    }

    [Fact]
    public void UpdateLabelAndMode_rejects_an_invalid_mode()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxCatalogException>(() => library.UpdateLabelAndMode("disk1", null, "rx"));
    }

    private string WriteCatalog(string id)
    {
        var image = Path.Combine(root, $"{id}.img");
        File.WriteAllBytes(image, new byte[16]);
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $"floppies:\n  {id}:\n    label: {id}\n    image: {image}\n    mode: ro\n    size: 1.44M\n    nfc: true\n");
        return image;
    }
}
```

**You must add `NfcUid` first.** Verified: `RetroBoxFloppy` has no `NfcUid` property on
`main`. Add it to `src/RetroBox.Core/RetroBoxCatalogModels.cs` alongside `Nfc`:

```csharp
    public string? NfcUid { get; set; }
```

The spec introduces it for phase 3, but `UpdateLabelAndMode` has to clear it alongside `Nfc`
when the mode changes, and bolting it on later would mean revisiting this code. Nothing reads
it yet. `RetroBoxYamlContext` already registers `RetroBoxFloppy`, so no context change is
needed — and `DefaultValuesHandling.OmitNull` on the serializer keeps it out of the YAML until
something sets it.

- [ ] **Step 6: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `RetroBoxFloppyLibrary` does not exist.

- [ ] **Step 7: Implement the library**

Create `src/RetroBox.Core/RetroBoxFloppyLibrary.cs`:

```csharp
namespace RetroBox.Core;

/// <summary>Catalog mutations that keep the YAML loadable at every step.</summary>
public sealed class RetroBoxFloppyLibrary(RetroBoxConfigStore store)
{
    /// <summary>
    /// Removes a floppy. The catalog entry goes first and the image file last: a failed delete
    /// leaves an orphaned file, which is untidy, while the reverse order leaves
    /// RetroBoxConfigStore.Validate throwing on a missing image — and that stops both the daemon
    /// and `retrobox boot`.
    /// </summary>
    public void Delete(string id)
    {
        var data = store.Load();
        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            throw new RetroBoxCatalogException($"Unknown floppy '{id}'.");
        }

        var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal);
        floppies.Remove(id);
        store.Save(data with { Floppies = floppies });

        try
        {
            File.Delete(floppy.Image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RetroBoxCatalogException(
                $"Floppy '{id}' was removed from the catalog, but its image '{floppy.Image}' could not be deleted: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Updates the label and/or the mode. Changing the mode invalidates any written tag, because
    /// the tag payload is `<id>,<mode>`: a floppy written as `ro` and switched to `rw` would keep
    /// mounting read-only with no visible cause.
    /// </summary>
    public void UpdateLabelAndMode(string id, string? label, string? mode)
    {
        var data = store.Load();
        if (!data.Floppies.TryGetValue(id, out var floppy))
        {
            throw new RetroBoxCatalogException($"Unknown floppy '{id}'.");
        }

        if (mode is not null && !RetroBoxFloppyCatalogRules.IsValidMode(mode))
        {
            throw new RetroBoxCatalogException($"Invalid floppy mode '{mode}' for floppy '{id}'.");
        }

        var updated = floppy with { };
        if (!string.IsNullOrWhiteSpace(label))
        {
            updated.Label = label;
        }

        if (mode is not null && mode != floppy.Mode)
        {
            updated.Mode = mode;
            updated.Nfc = false;
            updated.NfcUid = null;
        }

        var floppies = new Dictionary<string, RetroBoxFloppy>(data.Floppies, StringComparer.Ordinal)
        {
            [id] = updated,
        };

        store.Save(data with { Floppies = floppies });
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS.

- [ ] **Step 9: Write the failing endpoint tests**

Create `tests/RetroBox.Tests/RetroBoxLibraryEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxLibraryEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-endpoints-{Guid.NewGuid():N}");

    public RetroBoxLibraryEndpointsTests()
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "scratch"));
        Directory.CreateDirectory(Path.Combine(root, "cataloged"));
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Post_floppies_imports_the_upload_and_it_appears_in_the_catalog()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("MONKEY1.IMG"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("monkey1", await context.Client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "cataloged", "MONKEY1.IMG")));
    }

    [Fact]
    public async Task Post_floppies_rejects_an_unsupported_extension()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/floppies", BuildUpload("notes.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsupported-extension", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_floppies_suffixes_a_colliding_id()
    {
        await using var context = await StartAsync();

        using var first = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img"));
        using var second = await context.Client.PostAsync("/api/floppies", BuildUpload("DISK.ima"));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"id\":\"disk\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"disk-2\"", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_floppy_removes_it_from_the_catalog()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var response = await context.Client.DeleteAsync("/api/floppies/disk");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("\"id\":\"disk\"", await context.Client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_unknown_floppy_returns_not_found()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.DeleteAsync("/api/floppies/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("unknown-floppy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_floppy_changing_the_mode_clears_nfc()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var patch = await context.Client.PatchAsync(
            "/api/floppies/disk",
            new StringContent("{\"mode\":\"rw\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var catalog = await context.Client.GetStringAsync("/api/catalog");
        Assert.Contains("\"mode\":\"rw\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"nfc\":false", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_floppy_rejects_an_invalid_mode()
    {
        await using var context = await StartAsync();
        using (var upload = await context.Client.PostAsync("/api/floppies", BuildUpload("disk.img")))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        }

        using var patch = await context.Client.PatchAsync(
            "/api/floppies/disk",
            new StringContent("{\"mode\":\"rx\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    private static MultipartFormDataContent BuildUpload(string fileName)
    {
        var file = new ByteArrayContent(new byte[64]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private async Task<EndpointContext> StartAsync()
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions
            {
                Port = 0,
                ConfigRoot = root,
                ScratchRoot = Path.Combine(root, "scratch"),
                CatalogedRoot = Path.Combine(root, "cataloged"),
            },
            source);

        return new EndpointContext(host, source, new HttpClient { BaseAddress = host.BaseAddress });
    }

    private sealed record EndpointContext(
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

Note the tests read `/api/catalog` after each mutation: the endpoints must reload the source after saving, or the panel would show stale data even though the YAML changed. With `watchFileSystem: false` there is no watcher to do it for them, which is deliberate — it forces the endpoints to be explicit rather than relying on a race with the debounce.

- [ ] **Step 10: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — the endpoints do not exist, so every request 404s.

- [ ] **Step 11: Add the patch DTO**

In `src/RetroBox.Web/RetroBoxWebContracts.cs`, add the record and register it:

```csharp
public sealed record RetroBoxFloppyPatch(string? Label, string? Mode);
```

```csharp
[JsonSerializable(typeof(RetroBoxFloppyPatch))]
```

- [ ] **Step 12: Implement the endpoints**

Create `src/RetroBox.Web/RetroBoxLibraryEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxLibraryEndpoints
{
    private static readonly string[] AllowedExtensions = [".img", ".ima", ".dsk"];

    public const long MaxUploadBytes = 4 * 1024 * 1024;

    public static void Map(WebApplication app, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)
    {
        app.MapPost("/api/floppies", (HttpRequest request) => UploadAsync(request, options, catalogSource));
        app.MapDelete("/api/floppies/{id}", (string id) => Delete(id, options, catalogSource));
        app.MapPatch("/api/floppies/{id}", (string id, RetroBoxFloppyPatch patch) => Patch(id, patch, options, catalogSource));
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource)
    {
        if (!request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "expected-multipart", "Expected a multipart form upload.");
        }

        var form = await request.ReadFormAsync();
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "missing-file", "No file was uploaded.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The image exceeds the upload limit.");
        }

        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "unsupported-extension",
                "Only .img, .ima and .dsk images can be imported.");
        }

        var slug = RetroBoxCatalogRules.Slugify(Path.GetFileNameWithoutExtension(fileName));
        if (slug.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "unusable-name", "The filename yields no usable catalog ID.");
        }

        var id = ResolveFreeId(slug, catalogSource);

        Directory.CreateDirectory(options.ScratchRoot);
        var scratchPath = Path.Combine(options.ScratchRoot, fileName);

        await using (var scratch = File.Create(scratchPath))
        {
            await file.CopyToAsync(scratch);
        }

        try
        {
            new RetroBoxFloppyImporter().Import(new RetroBoxFloppyImportRequest
            {
                Id = id,
                Label = Path.GetFileNameWithoutExtension(fileName),
                ImagePath = scratchPath,
                ConfigRoot = options.ConfigRoot,
                ScratchRoot = options.ScratchRoot,
                CatalogedRoot = options.CatalogedRoot,
            });
        }
        catch (RetroBoxCatalogException ex)
        {
            SafeDelete(scratchPath);
            return Error(StatusCodes.Status400BadRequest, "import-failed", ex.Message);
        }

        Refresh(catalogSource);
        return Results.Created($"/api/floppies/{id}", null);
    }

    private static IResult Delete(string id, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot)).Delete(id);
        }
        catch (RetroBoxCatalogException ex) when (ex.Message.StartsWith("Unknown floppy", StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogException ex)
        {
            Refresh(catalogSource);
            return Error(StatusCodes.Status500InternalServerError, "delete-incomplete", ex.Message);
        }

        Refresh(catalogSource);
        return Results.NoContent();
    }

    private static IResult Patch(
        string id,
        RetroBoxFloppyPatch patch,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot))
                .UpdateLabelAndMode(id, patch.Label, patch.Mode);
        }
        catch (RetroBoxCatalogException ex) when (ex.Message.StartsWith("Unknown floppy", StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid-patch", ex.Message);
        }

        Refresh(catalogSource);
        return Results.NoContent();
    }

    private static string ResolveFreeId(string slug, IRetroBoxCatalogSource catalogSource)
    {
        var existing = catalogSource.Current.Floppies;
        if (!existing.ContainsKey(slug))
        {
            return slug;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!existing.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    // The catalog file just changed underneath us. The watcher would notice, but only after its
    // debounce — and an immediate GET /api/catalog must not show stale data.
    private static void Refresh(IRetroBoxCatalogSource catalogSource)
    {
        catalogSource.TryReload();
    }

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static IResult Error(int statusCode, string code, string message)
    {
        // Pass the source-generated options explicitly: Results.Json must not fall back to a
        // reflection-based resolver, which is what would break the AOT publish.
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.Options,
            statusCode: statusCode);
    }
}
```

`ReadFormAsync` with `form.Files` is used instead of `IFormFile` parameter binding to keep the AOT surface small and explicit.

- [ ] **Step 13: Map the endpoints in the host**

In `src/RetroBox.Web/RetroBoxWebHost.cs`, after the `/api/catalog` mapping:

```csharp
        RetroBoxLibraryEndpoints.Map(app, options, catalogSource);
```

Also cap Kestrel's request body so a huge upload is refused before it is buffered, in the builder section:

```csharp
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Limits.MaxRequestBodySize = RetroBoxLibraryEndpoints.MaxUploadBytes);
```

- [ ] **Step 14: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS, whole suite.

- [ ] **Step 15: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "feat(web): add floppy upload, delete and patch endpoints"
```

---

## Task 4: The panel, in Spanish and English

**Branch:** `feat/panel-ui` off `feat/library-endpoints`

**Files:**
- Modify: `src/RetroBox.Web/wwwroot/index.html`
- Create: `src/RetroBox.Web/wwwroot/app.css`, `src/RetroBox.Web/wwwroot/app.js`
- Test: `tests/RetroBox.Tests/RetroBoxStaticAssetsTests.cs`

**Interfaces:**
- Consumes: `GET /api/catalog`, `POST /api/floppies`, `DELETE /api/floppies/{id}`, `PATCH /api/floppies/{id}` (Tasks 2-3); `RetroBoxStaticAssets.TryGet` (Task 2).
- Produces: nothing consumed by later tasks.

The source mockup used Tailwind from a CDN and `via.placeholder.com` for images. **Both are gone**: on an appliance with no internet the panel would render unstyled with broken images. The styling below is hand-written; there is no Node build step.

- [ ] **Step 1: Write the failing asset tests**

Create `tests/RetroBox.Tests/RetroBoxStaticAssetsTests.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxStaticAssetsTests
{
    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("app.css", "text/css; charset=utf-8")]
    [InlineData("app.js", "text/javascript; charset=utf-8")]
    public void TryGet_returns_each_embedded_asset(string relativePath, string expectedContentType)
    {
        Assert.True(RetroBoxStaticAssets.TryGet(relativePath, out var content, out var contentType));
        Assert.NotEmpty(content);
        Assert.Equal(expectedContentType, contentType);
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("nope.js")]
    [InlineData("")]
    public void TryGet_refuses_anything_that_is_not_an_asset(string relativePath)
    {
        Assert.False(RetroBoxStaticAssets.TryGet(relativePath, out _, out _));
    }

    [Fact]
    public void The_panel_loads_nothing_from_the_network()
    {
        Assert.True(RetroBoxStaticAssets.TryGet("index.html", out var html, out _));

        var markup = Encoding.UTF8.GetString(html);

        Assert.DoesNotContain("http://", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//cdn.", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Both_languages_define_exactly_the_same_keys()
    {
        Assert.True(RetroBoxStaticAssets.TryGet("app.js", out var js, out _));

        var script = Encoding.UTF8.GetString(js);
        var spanish = ExtractKeys(script, "es");
        var english = ExtractKeys(script, "en");

        Assert.NotEmpty(spanish);
        Assert.Equal(spanish, english);
    }

    private static string[] ExtractKeys(string script, string language)
    {
        var block = Regex.Match(
            script,
            $@"{language}:\s*\{{(?<body>.*?)\n  \}}",
            RegexOptions.Singleline);

        Assert.True(block.Success, $"Could not find the '{language}' dictionary in app.js.");

        return Regex.Matches(block.Groups["body"].Value, @"^\s{4}([A-Za-z0-9_]+):", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }
}
```

The last two tests are the ones worth having: one enforces that the panel stays offline-capable, the other catches a translation added to one language and forgotten in the other.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `mise run test`

Expected: FAIL — `app.css` and `app.js` are not embedded yet.

- [ ] **Step 3: Write the markup**

Replace `src/RetroBox.Web/wwwroot/index.html`:

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>RetroBox</title>
<link rel="stylesheet" href="app.css">
</head>
<body>
<header>
  <div class="brand">
    <span class="brand-mark">RB</span>
    <div>
      <h1>RetroBox</h1>
      <p class="subtitle" data-i18n="subtitle"></p>
    </div>
  </div>
  <div class="header-tools">
    <input type="search" id="search" data-i18n-placeholder="searchPlaceholder">
    <select id="language" aria-label="Language">
      <option value="es">Espanol</option>
      <option value="en">English</option>
    </select>
  </div>
</header>

<main>
  <section class="card drop">
    <h2 data-i18n="uploadTitle"></h2>
    <p class="hint" data-i18n="uploadHint"></p>
    <input type="file" id="file" accept=".img,.ima,.dsk" multiple hidden>
    <button id="pick" class="primary" data-i18n="uploadButton"></button>
    <p id="upload-status" class="hint" role="status"></p>
  </section>

  <section class="card">
    <div class="card-head">
      <h2 data-i18n="libraryTitle"></h2>
      <span id="stats" class="hint"></span>
    </div>
    <p id="catalog-error" class="error" role="alert" hidden></p>
    <p id="library-error" class="error" role="alert" hidden></p>
    <ul id="library" class="library"></ul>
    <p id="empty" class="hint" data-i18n="empty" hidden></p>
  </section>
</main>

<script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 4: Write the stylesheet**

Create `src/RetroBox.Web/wwwroot/app.css`:

```css
:root {
  --bg: #0f172a;
  --panel: #1e293b;
  --inset: #131c31;
  --line: #334155;
  --text: #e2e8f0;
  --muted: #94a3b8;
  --accent: #34d399;
  --warn: #fbbf24;
  --danger: #f87171;
}

* { box-sizing: border-box; }

body {
  margin: 0;
  padding: 1.5rem;
  background: var(--bg);
  color: var(--text);
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 14px;
  line-height: 1.5;
}

header {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 1.25rem;
  border-bottom: 1px solid var(--line);
}

.brand { display: flex; align-items: center; gap: 0.75rem; }

.brand-mark {
  display: grid;
  place-items: center;
  width: 2.5rem;
  height: 2.5rem;
  border: 1px solid var(--accent);
  border-radius: 0.375rem;
  color: var(--accent);
  font-weight: 700;
}

h1 { margin: 0; font-size: 1.125rem; color: var(--accent); }
h2 { margin: 0; font-size: 0.8125rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--muted); }
.subtitle, .hint { margin: 0; color: var(--muted); font-size: 0.75rem; }
.error { margin: 0 0 0.75rem; color: var(--danger); font-size: 0.75rem; }

.header-tools { display: flex; gap: 0.5rem; }

input[type="search"], select {
  padding: 0.4rem 0.6rem;
  border: 1px solid var(--line);
  border-radius: 0.25rem;
  background: var(--inset);
  color: var(--text);
  font: inherit;
}

input[type="search"]:focus, select:focus { outline: none; border-color: var(--accent); }

main { max-width: 60rem; margin: 1.5rem auto 0; display: grid; gap: 1.25rem; }

.card {
  padding: 1.25rem;
  border: 1px solid var(--line);
  border-radius: 0.5rem;
  background: var(--panel);
}

.card-head { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 1rem; }

.drop { display: grid; gap: 0.5rem; justify-items: center; text-align: center; border-style: dashed; }

button {
  padding: 0.4rem 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.25rem;
  background: var(--inset);
  color: var(--text);
  font: inherit;
  cursor: pointer;
}

button:hover { border-color: var(--accent); }
button.primary { border-color: var(--accent); color: var(--accent); font-weight: 700; }
button.danger:hover { border-color: var(--danger); color: var(--danger); }
button[disabled] { opacity: 0.5; cursor: progress; }

.library { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.5rem; }

.library li {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem;
  border: 1px solid var(--line);
  border-radius: 0.375rem;
  background: var(--inset);
}

.floppy-name { display: grid; }
.floppy-name strong { font-weight: 600; }
.floppy-name span { color: var(--muted); font-size: 0.75rem; }

.actions { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }

.badge {
  padding: 0.2rem 0.5rem;
  border: 1px solid currentColor;
  border-radius: 0.25rem;
  font-size: 0.6875rem;
}

.badge.tagged { color: var(--accent); }
.badge.untagged { color: var(--warn); }
```

- [ ] **Step 5: Write the script**

Create `src/RetroBox.Web/wwwroot/app.js`:

```javascript
"use strict";

const STRINGS = {
  es: {
    subtitle: "Biblioteca de disquetes",
    searchPlaceholder: "Buscar disquete...",
    uploadTitle: "Subir disquetes",
    uploadHint: "Imagenes .img, .ima o .dsk",
    uploadButton: "Seleccionar archivos",
    libraryTitle: "Biblioteca",
    empty: "Todavia no hay disquetes en el catalogo.",
    catalogBroken: "El catalogo tiene un error y no se pudo cargar: {message}",
    tagged: "Grabado",
    untagged: "Sin NFC",
    readOnly: "Solo lectura",
    readWrite: "Lectura y escritura",
    deleteAction: "Borrar",
    confirmDelete: "Borrar este disquete del catalogo?",
    uploading: "Subiendo...",
    uploaded: "Listo",
    stats: "{count} disquetes, {tagged} grabados",
    loadFailed: "No se pudo leer el catalogo.",
    "unsupported-extension": "Solo se aceptan imagenes .img, .ima y .dsk.",
    "file-too-large": "La imagen supera el limite de subida.",
    "unusable-name": "Ese nombre de archivo no da un ID valido.",
    "missing-file": "No se selecciono ningun archivo.",
    "expected-multipart": "La subida no llego como formulario.",
    "import-failed": "No se pudo importar la imagen.",
    "unknown-floppy": "Ese disquete no existe.",
    "invalid-patch": "El cambio no es valido.",
    "delete-incomplete": "Se quito del catalogo, pero el archivo quedo en disco.",
    unexpected: "Error inesperado."
  },
  en: {
    subtitle: "Floppy library",
    searchPlaceholder: "Search floppies...",
    uploadTitle: "Upload floppies",
    uploadHint: ".img, .ima or .dsk images",
    uploadButton: "Choose files",
    libraryTitle: "Library",
    empty: "No floppies in the catalog yet.",
    catalogBroken: "The catalog has an error and could not be loaded: {message}",
    tagged: "Tagged",
    untagged: "No NFC",
    readOnly: "Read-only",
    readWrite: "Read-write",
    deleteAction: "Delete",
    confirmDelete: "Delete this floppy from the catalog?",
    uploading: "Uploading...",
    uploaded: "Done",
    stats: "{count} floppies, {tagged} tagged",
    loadFailed: "Could not read the catalog.",
    "unsupported-extension": "Only .img, .ima and .dsk images are accepted.",
    "file-too-large": "The image exceeds the upload limit.",
    "unusable-name": "That filename yields no valid ID.",
    "missing-file": "No file was selected.",
    "expected-multipart": "The upload did not arrive as a form.",
    "import-failed": "The image could not be imported.",
    "unknown-floppy": "That floppy does not exist.",
    "invalid-patch": "That change is not valid.",
    "delete-incomplete": "Removed from the catalog, but the file is still on disk.",
    unexpected: "Unexpected error."
  }
};

let language = pickLanguage();
let floppies = [];

function pickLanguage() {
  const stored = window.localStorage.getItem("retrobox.lang");
  if (stored && STRINGS[stored]) {
    return stored;
  }

  return (navigator.language || "es").toLowerCase().startsWith("en") ? "en" : "es";
}

function t(key, replacements) {
  let text = STRINGS[language][key] || STRINGS.es[key] || key;
  if (replacements) {
    for (const name of Object.keys(replacements)) {
      text = text.replace("{" + name + "}", replacements[name]);
    }
  }

  return text;
}

function applyStaticText() {
  document.documentElement.lang = language;
  document.querySelectorAll("[data-i18n]").forEach((node) => {
    node.textContent = t(node.dataset.i18n);
  });
  document.querySelectorAll("[data-i18n-placeholder]").forEach((node) => {
    node.placeholder = t(node.dataset.i18nPlaceholder);
  });
}

async function readError(response) {
  try {
    const body = await response.json();
    return t(body.code) !== body.code ? t(body.code) : body.message || t("unexpected");
  } catch (error) {
    return t("unexpected");
  }
}

async function loadCatalog() {
  const problem = document.getElementById("library-error");
  try {
    const response = await fetch("/api/catalog");
    if (!response.ok) {
      throw new Error("catalog");
    }

    const payload = await response.json();
    floppies = payload.floppies;
    problem.hidden = true;

    const broken = document.getElementById("catalog-error");
    if (payload.catalogError) {
      broken.textContent = t("catalogBroken", { message: payload.catalogError });
      broken.hidden = false;
    } else {
      broken.hidden = true;
    }
  } catch (error) {
    problem.textContent = t("loadFailed");
    problem.hidden = false;
    floppies = [];
  }

  render();
}

function render() {
  const list = document.getElementById("library");
  const term = document.getElementById("search").value.trim().toLowerCase();
  const visible = floppies.filter(
    (floppy) => floppy.id.toLowerCase().includes(term) || floppy.label.toLowerCase().includes(term)
  );

  list.textContent = "";
  document.getElementById("empty").hidden = floppies.length > 0;
  document.getElementById("stats").textContent = t("stats", {
    count: floppies.length,
    tagged: floppies.filter((floppy) => floppy.nfc).length
  });

  for (const floppy of visible) {
    list.appendChild(renderRow(floppy));
  }
}

function renderRow(floppy) {
  const row = document.createElement("li");

  const name = document.createElement("div");
  name.className = "floppy-name";
  const label = document.createElement("strong");
  label.textContent = floppy.label;
  const meta = document.createElement("span");
  meta.textContent = floppy.id + " - " + floppy.size;
  name.append(label, meta);

  const actions = document.createElement("div");
  actions.className = "actions";

  const badge = document.createElement("span");
  badge.className = "badge " + (floppy.nfc ? "tagged" : "untagged");
  badge.textContent = floppy.nfc ? t("tagged") : t("untagged");

  const mode = document.createElement("button");
  mode.textContent = floppy.mode === "rw" ? t("readWrite") : t("readOnly");
  mode.addEventListener("click", () => patchFloppy(floppy.id, { mode: floppy.mode === "rw" ? "ro" : "rw" }, mode));

  const remove = document.createElement("button");
  remove.className = "danger";
  remove.textContent = t("deleteAction");
  remove.addEventListener("click", () => deleteFloppy(floppy.id, remove));

  actions.append(badge, mode, remove);
  row.append(name, actions);
  return row;
}

async function patchFloppy(id, patch, button) {
  button.disabled = true;
  const response = await fetch("/api/floppies/" + encodeURIComponent(id), {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(patch)
  });

  if (!response.ok) {
    window.alert(await readError(response));
  }

  button.disabled = false;
  await loadCatalog();
}

async function deleteFloppy(id, button) {
  if (!window.confirm(t("confirmDelete"))) {
    return;
  }

  button.disabled = true;
  const response = await fetch("/api/floppies/" + encodeURIComponent(id), { method: "DELETE" });
  if (!response.ok) {
    window.alert(await readError(response));
  }

  button.disabled = false;
  await loadCatalog();
}

async function uploadFiles(files) {
  const status = document.getElementById("upload-status");

  for (const file of files) {
    status.textContent = t("uploading") + " " + file.name;
    const body = new FormData();
    body.append("file", file, file.name);

    const response = await fetch("/api/floppies", { method: "POST", body });
    if (!response.ok) {
      status.textContent = file.name + ": " + (await readError(response));
      await loadCatalog();
      return;
    }
  }

  status.textContent = t("uploaded");
  await loadCatalog();
}

document.getElementById("pick").addEventListener("click", () => document.getElementById("file").click());
document.getElementById("file").addEventListener("change", (event) => {
  const files = Array.from(event.target.files);
  event.target.value = "";
  uploadFiles(files);
});
document.getElementById("search").addEventListener("input", render);
document.getElementById("language").addEventListener("change", (event) => {
  language = event.target.value;
  window.localStorage.setItem("retrobox.lang", language);
  applyStaticText();
  render();
});

document.getElementById("language").value = language;
applyStaticText();
loadCatalog();
```

The error path maps the API's `code` through the same dictionary, which is why the backend sends codes and not sentences.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `mise run test`

Expected: PASS. If `Both_languages_define_exactly_the_same_keys` fails, the two dictionaries have drifted — fix the dictionary, not the test.

- [ ] **Step 7: Verify formatting and commit**

```bash
mise run format-check
git add -A
git commit -m "feat(web): add the floppy library panel in Spanish and English"
```

---

## Task 5: Appliance wiring and docs

**Branch:** `feat/web-appliance-wiring` off `feat/panel-ui`

**Files:**
- Modify: `appliance/installer/payload/units/retrobox-daemon.service`
- Modify: `appliance/installer/lib/hardware-detect.sh:243-250`
- Modify: `docs/architecture.md`, `appliance/README.md`

**Interfaces:** none — configuration and documentation only.

- [ ] **Step 1: Make the serial device optional and pass the web port**

In `appliance/installer/payload/units/retrobox-daemon.service`:

- **Delete** the `ExecCondition=/bin/sh -c 'test -e "$SERIAL_DEVICE"'` line and the comment block above it that explains it. Without a floppy controller the unit must still start, because the panel has to be reachable regardless.
- Change `ExecStart` to pass the port:

```ini
ExecStart=/opt/retrobox/retrobox daemon --serial-port "$SERIAL_DEVICE" --serial-baud "$SERIAL_BAUD" --floppy-control-socket "$FLOPPY_CONTROL_SOCKET" --web-port "$WEB_PORT"
```

- Update the comment above `ExecStart` to say the daemon also hosts the LAN panel, and that an absent serial device leaves the panel running with the NFC path disabled.

`RetroBoxDaemon.ResolveSerialDeviceOptions` already returns `null` when the port is blank and `RunAsync` already falls back to `Console.In`, so an empty `SERIAL_DEVICE` needs no code change.

- [ ] **Step 2: Emit `WEB_PORT` from the installer**

In `appliance/installer/lib/hardware-detect.sh`, in the `daemon.env` heredoc (around line 243), add the variable and a line of explanation:

```sh
# Web panel port on the LAN. Set to 0 to disable the panel.
WEB_PORT=${RETROPC_WEB_PORT:-8080}
```

Follow the file's existing style for defaulted environment overrides — `SERIAL_BAUD` at line 120 is the pattern to copy.

- [ ] **Step 3: Verify the shell change**

Run: `shellcheck appliance/installer/lib/hardware-detect.sh`

Expected: no new findings. CI runs `shellcheck` as a separate job, so a regression here fails the PR.

- [ ] **Step 4: Document the panel**

In `docs/architecture.md`, add `RetroBox.Web` to the component list and a short paragraph covering: the panel is hosted inside the daemon process because the daemon owns the serial port; it is a Minimal API with embedded static assets so the AOT single-file binary is preserved; it listens on `WEB_PORT` (default 8080) on every interface; and it is **unauthenticated and LAN-trusted**. State that last point plainly — it is a deliberate decision, and a reader deserves to find it in the reference doc rather than discover it.

Also note that the daemon now reads the catalog through `RetroBoxWatchingCatalogSource`, so an edit from the panel, from `retrobox import`, or over SSH is picked up without a restart, and that a reload which fails validation is discarded so a malformed file cannot take down a running daemon.

In `appliance/README.md`, add the panel to the runtime behaviour section: the URL (`http://<appliance>:8080`), what it can do (list, upload, rename, switch mode, delete), that it needs no floppy controller attached, and that `WEB_PORT=0` in `/etc/retrobox/daemon.env` turns it off.

- [ ] **Step 5: Run the gates and commit**

```bash
mise run test
mise run format-check
git add -A
git commit -m "feat(appliance): serve the web panel from the daemon unit"
```

---

## Phase 2 exit criteria

- `mise run test` and `mise run format-check` pass; CI's AOT publish and `shellcheck` jobs pass.
- With no floppy controller attached, `retrobox daemon --web-port 8080` serves a panel that lists, uploads, renames, re-modes and deletes floppies.
- A catalog change made while the daemon runs is visible to the very next insert decision,
  with no restart — the behaviour Task 1 exists to deliver.

  This originally read "uploading through the panel and then inserting that floppy mounts it
  without restarting the daemon", which **cannot be met in this phase and was a defect in this
  plan**. An upload sets `nfc: false`, phase 1's mount guard refuses `nfc: false`, and nothing
  in phase 2 can set it true — so the only route runs through `retrobox nfc write`, which needs
  the daemon stopped and therefore contradicts the criterion's own wording. Task 1 delivers the
  mechanism correctly; the user-visible outcome belongs to phase 3.
- A malformed `floppies.yaml` written while the daemon runs leaves it serving the previous catalog instead of failing.
- A malformed `floppies.yaml` present **at startup** still brings the panel up, showing the
  validation message, so the owner can fix it without the GRUB recovery entry. This is a
  deliberate change: the daemon previously refused to start.
- The panel renders correctly with no internet access, in Spanish by default and in English when selected.
- Still out of scope, by design: NFC assignment, the `/api/drive` and `/api/drive/events`
  endpoints (they exist to serve the phase 3 assignment UI, so they ship with it), games
  grouping, cover art, the scraper settings screen, and authentication.

## Carried into Phase 3

These stay open and are recorded in the spec's "Phase 2 prerequisites" section:

- The orphan-window quarantine belongs in `RetroBoxSerialLineRouter`, not on the channel's semaphore.
- Reply attribution after a follow-up `STATUS` is ungated.
- The channel's `TextWriter` lifetime needs an explicit contract if the web host ever outlives the read loop. Task 2 keeps the host's lifetime inside the daemon command, which satisfies it for now — a future change that starts the host independently must revisit it.
- An ambiguous write result should be resolved with a `TAGID` read-back rather than reported as a definitive failure.
