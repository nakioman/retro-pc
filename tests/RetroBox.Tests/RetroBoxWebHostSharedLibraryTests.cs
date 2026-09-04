using System.Net;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

/// <summary>
/// Pins the fix for the critical review finding on task 4: RetroBoxNfcEndpoints and
/// RetroBoxLibraryEndpoints must share one RetroBoxFloppyLibrary instance, not each construct
/// their own, because RetroBoxFloppyLibrary's lock is per-instance -- a private instance in
/// either endpoint group would let a tag write race an in-flight upload/delete/rename instead of
/// serialising behind it.
///
/// This is proven deterministically rather than by racing two requests: RetroBoxWebHost.StartAsync
/// is given a RetroBoxFloppyLibrary backed by a *different* directory than options.ConfigRoot.
/// Every ordinary, sequential request that mutates the catalog lands in that injected directory
/// and leaves options.ConfigRoot's own catalog untouched. An endpoint group that instead built
/// "new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot))" internally would
/// provably write to the wrong place -- there is no interleaving or timing under which it could
/// accidentally pass.
/// </summary>
public sealed class RetroBoxWebHostSharedLibraryTests : IDisposable
{
    private readonly string optionsRoot = Path.Combine(Path.GetTempPath(), $"retrobox-options-root-{Guid.NewGuid():N}");
    private readonly string injectedRoot = Path.Combine(Path.GetTempPath(), $"retrobox-injected-root-{Guid.NewGuid():N}");

    public RetroBoxWebHostSharedLibraryTests()
    {
        Directory.CreateDirectory(optionsRoot);
        Directory.CreateDirectory(injectedRoot);
    }

    public void Dispose()
    {
        foreach (var directory in new[] { optionsRoot, injectedRoot })
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task Nfc_write_lands_in_the_injected_library_not_the_options_config_root()
    {
        WriteCatalog(optionsRoot, "disk1");
        WriteCatalog(injectedRoot, "disk1");

        var injectedLibrary = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(injectedRoot));
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        using var source = new RetroBoxWatchingCatalogSource(
            optionsRoot, new RetroBoxConfigStore(optionsRoot).Load(), watchFileSystem: false);
        await using var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = optionsRoot },
            source,
            nfcChannel: channel,
            floppyLibrary: injectedLibrary);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = "{\"floppyId\":\"disk1\",\"confirm\":false}";
        using var response = await client.PostAsync("/api/nfc/write", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(
            new RetroBoxConfigStore(injectedRoot).Load().Floppies["disk1"].Nfc,
            "the write never reached the injected library's catalog.");
        Assert.False(
            new RetroBoxConfigStore(optionsRoot).Load().Floppies["disk1"].Nfc,
            "the write landed in options.ConfigRoot's catalog -- RetroBoxNfcEndpoints must be " +
            "constructing its own RetroBoxFloppyLibrary instead of using the shared one.");
    }

    [Fact]
    public async Task Delete_lands_in_the_injected_library_not_the_options_config_root()
    {
        WriteCatalog(optionsRoot, "disk2");
        WriteCatalog(injectedRoot, "disk2");

        var injectedLibrary = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(injectedRoot));
        using var source = new RetroBoxWatchingCatalogSource(
            optionsRoot, new RetroBoxConfigStore(optionsRoot).Load(), watchFileSystem: false);
        await using var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = optionsRoot },
            source,
            floppyLibrary: injectedLibrary);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.DeleteAsync("/api/floppies/disk2");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.False(
            new RetroBoxConfigStore(injectedRoot).Load().Floppies.ContainsKey("disk2"),
            "the delete never reached the injected library's catalog.");
        Assert.True(
            new RetroBoxConfigStore(optionsRoot).Load().Floppies.ContainsKey("disk2"),
            "disk2 disappeared from options.ConfigRoot's catalog -- RetroBoxLibraryEndpoints must " +
            "be constructing its own RetroBoxFloppyLibrary instead of using the shared one.");
    }

    private static void WriteCatalog(string root, params string[] floppyIds)
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
}
