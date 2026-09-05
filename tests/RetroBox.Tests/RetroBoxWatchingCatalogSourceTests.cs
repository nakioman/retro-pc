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

    [Fact]
    public void LastError_stays_reported_after_a_successful_reload_once_the_watcher_has_died()
    {
        // ReportWatcherFailure must not be undone by the panel's own next write: TryReload
        // succeeding must not make the outage look resolved when nothing fixed the watcher.
        WriteCatalog("disk1");
        var initial = new RetroBoxConfigStore(root).Load();
        using var source = new RetroBoxWatchingCatalogSource(root, initial, watchFileSystem: false);

        source.ReportWatcherFailure(new IOException("inotify watch limit reached"));

        WriteCatalog("disk1", "disk2");

        Assert.True(source.Reload());
        Assert.Equal(["disk1", "disk2"], source.Current.Floppies.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.NotNull(source.LastError);
        Assert.Contains("watcher", source.LastError, StringComparison.OrdinalIgnoreCase);
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
