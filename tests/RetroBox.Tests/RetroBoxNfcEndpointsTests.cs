using System.Net;
using System.Text;
using RetroBox.Core;
using RetroBox.Daemon;
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

    private static Task<HttpResponseMessage> PostAsync(NfcContext context, string floppyId, bool confirm)
    {
        var body = $"{{\"floppyId\":\"{floppyId}\",\"confirm\":{(confirm ? "true" : "false")}}}";
        return context.Client.PostAsync("/api/nfc/write", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private async Task<NfcContext> StartAsync(IRetroBoxNfcCommandChannel? nfcChannel)
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        // A driveState is required for GET /api/drive (used by Read_back_reports_what_is_actually_on_the_tag)
        // to reach its TAGID probe at all: BuildViewAsync short-circuits to "unavailable" when
        // driveState is null, the same way it does when nfcChannel is null. Its default Unknown
        // state matches "no controller has reported yet", which is exactly this test's setup.
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source,
            driveState: new RetroBoxDriveStateTracker(),
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
