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
    private volatile RetroBoxCatalogSnapshot snapshot;
    private volatile string? watcherFailureMessage;
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
        snapshot = new RetroBoxCatalogSnapshot(initial, initialError);
        this.onReloadFailed = onReloadFailed;
        this.debounce = debounce ?? DefaultDebounce;

        if (!watchFileSystem)
        {
            return;
        }

        // FileSystemWatcher throws when the directory is missing, and a first boot can reach
        // here before anything has written the catalog. A root that cannot be created must not
        // take down the daemon either: it starts without a watcher and reports why.
        try
        {
            Directory.CreateDirectory(rootPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onReloadFailed?.Invoke(
                $"Could not create catalog root '{rootPath}', catalog changes will not be picked up automatically: {ex.Message}");
            return;
        }

        watcher = new FileSystemWatcher(rootPath, "*.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnCatalogChanged;
        watcher.Created += OnCatalogChanged;
        watcher.Deleted += OnCatalogChanged;
        watcher.Renamed += OnCatalogChanged;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;
    }

    // The watcher's death is sticky and independent of the catalog snapshot: once inotify stops
    // delivering events, a later successful reload (the panel's own writes still call Reload
    // directly) must not make the outage look resolved when nothing fixed the watcher itself.
    // Both volatile fields are read into locals exactly once here, and the pair is composed from
    // those locals: reading either field twice (once for the catalog, again for the error) would
    // let a Reload() landing in between pair one snapshot's catalog with another's error.
    public RetroBoxCatalogSnapshot Snapshot
    {
        get
        {
            var current = snapshot;
            var watcherFailure = watcherFailureMessage;
            return current with { Error = Compose(watcherFailure, current.Error) };
        }
    }

    public RetroBoxCatalogData Current => Snapshot.Catalog;

    public string? LastError => Snapshot.Error;

    private static string? Compose(string? watcherFailure, string? snapshotError) =>
        watcherFailure is null ? snapshotError
        : snapshotError is null ? watcherFailure
        : $"{watcherFailure} {snapshotError}";

    public bool TryReload() => Reload();

    /// <summary>Reloads now. Returns false and keeps the previous catalog when the YAML is unusable.</summary>
    public bool Reload()
    {
        string? failure = null;

        // Serialized against itself: two overlapping loads could otherwise finish out of order
        // and leave the older snapshot published. Readers use the volatile field and never take
        // this lock.
        lock (gate)
        {
            try
            {
                snapshot = new RetroBoxCatalogSnapshot(store.Load(), null);
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
            {
                failure = $"Catalog reload failed, keeping the previous catalog: {ex.Message}";
                snapshot = snapshot with { Error = ex.Message };
            }
        }

        if (failure is not null)
        {
            onReloadFailed?.Invoke(failure);
            return false;
        }

        return true;
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
        watcherFailureMessage =
            $"Catalog watcher failed; catalog changes will no longer be noticed automatically: {error.Message}";

        onReloadFailed?.Invoke(watcherFailureMessage);
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
            try
            {
                await Task.Delay(debounce, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
            }

            Reload();
        }
        catch (Exception ex)
        {
            // An unobserved exception here would fault the fire-and-forget task silently,
            // leaving the daemon stuck on a stale catalog with no diagnostic at all -- the
            // original bug this type exists to fix, just made invisible.
            onReloadFailed?.Invoke(
                $"Catalog watcher failed unexpectedly, catalog changes will not be picked up automatically: {ex.Message}");
        }
    }
}
