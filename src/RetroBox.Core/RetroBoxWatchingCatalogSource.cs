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
