using System.Net;
using System.Text;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

/// <summary>
/// Pins the fix for the critical review finding on task 4: RetroBoxNfcEndpoints must not
/// construct its own RetroBoxFloppyLibrary, because RetroBoxFloppyLibrary's lock is
/// per-instance. A private instance there would let a tag write race an in-flight
/// upload/delete/rename instead of serialising behind it -- reproduced in review as 37 of 40
/// concurrent upload+write pairs landing with the HTTP response claiming success while the
/// catalog disagreed.
/// </summary>
public sealed class RetroBoxWebHostSharedLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-shared-library-{Guid.NewGuid():N}");

    public RetroBoxWebHostSharedLibraryTests()
    {
        Directory.CreateDirectory(root);
        WriteCatalog("disk1");
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
    public async Task Nfc_write_is_excluded_by_the_same_lock_as_the_library_endpoints()
    {
        var configStore = new RetroBoxConfigStore(root);
        var library = new RetroBoxFloppyLibrary(configStore);

        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        using var source = new RetroBoxWatchingCatalogSource(root, configStore.Load(), watchFileSystem: false);
        await using var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source,
            nfcChannel: channel,
            floppyLibrary: library);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var heldLock = new ManualResetEventSlim(false);
        var releaseLock = new ManualResetEventSlim(false);

        // RetroBoxLibraryEndpoints.UploadAsync's own exclusive section has exactly this shape --
        // load near the top, save at the very end of a long critical section -- which is what let
        // the review's reproduction land 37 of 40 concurrent upload+write pairs with the tag
        // write answering 200 OK while the catalog said otherwise: AssignTag's load-then-save ran
        // entirely inside the upload's window and the upload's own stale-snapshot save overwrote
        // it. RunExclusively is the exact seam both RetroBoxLibraryEndpoints and this call go
        // through, so driving it directly here reproduces that shape without needing to steer a
        // real multipart upload through a matching delay.
        var blockingSection = Task.Run(() => library.RunExclusively(() =>
        {
            var staleSnapshot = configStore.Load();
            heldLock.Set();
            releaseLock.Wait();
            configStore.Save(staleSnapshot);
        }));

        Assert.True(heldLock.Wait(TimeSpan.FromSeconds(5)), "the blocking section never started");

        var writeBody = "{\"floppyId\":\"disk1\",\"confirm\":false}";
        var writeTask = client.PostAsync("/api/nfc/write", new StringContent(writeBody, Encoding.UTF8, "application/json"));

        // A generous window for an *unlocked* write to land on its own. It should not be able to
        // -- AssignTag needs the same lock the blocking section above is holding -- but this
        // gives a reintroduced private-library bug every real chance to finish before the
        // blocking section's stale save, rather than leaving the outcome to whatever the
        // scheduler happens to do with two independently-timed tasks.
        await Task.WhenAny(writeTask, Task.Delay(TimeSpan.FromSeconds(2)));

        releaseLock.Set();

        using var writeResponse = await writeTask.WaitAsync(TimeSpan.FromSeconds(30));
        await blockingSection.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.OK, writeResponse.StatusCode);

        var floppies = configStore.Load().Floppies;
        Assert.True(
            floppies["disk1"].Nfc,
            "the tag write was clobbered by the blocking section's later save -- RetroBoxNfcEndpoints " +
            "and RetroBoxLibraryEndpoints must share one RetroBoxFloppyLibrary instance.");
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
}
