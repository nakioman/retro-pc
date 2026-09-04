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

        channel.Calls.Clear();
        using var second = await PostAsync(context, "disk2", confirm: false);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("tag-already-assigned", body, StringComparison.Ordinal);
        Assert.Contains("disk1", body, StringComparison.Ordinal);

        // The refusal must be a pure read: nothing goes out over the wire, and disk2's catalog
        // entry does not move, since the request was never confirmed.
        Assert.DoesNotContain("WRITE", string.Join(",", channel.Calls), StringComparison.Ordinal);
        Assert.False(new RetroBoxConfigStore(root).Load().Floppies["disk2"].Nfc);
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
    public async Task Write_reports_no_controller_when_the_host_has_no_channel_at_all()
    {
        // Defensive default for a host started without a channel. On a real appliance this
        // branch never runs -- CliCommandFactory always hands the web host a non-null
        // RetroBoxNfcChannelHolder -- so it must not be the only "no-controller" case covered.
        await using var context = await StartAsync(nfcChannel: null);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-controller", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_reports_a_disconnected_controller()
    {
        // This is the production shape: RetroBoxNfcChannelHolder throws
        // RetroBoxNfcCommandUnavailableException once its channel is unplugged, rather than the
        // host ever having a null IRetroBoxNfcCommandChannel to begin with.
        var channel = new StubNfcCommandChannel
        {
            ThrowOnCall = new RetroBoxNfcCommandUnavailableException("No floppy controller is connected."),
        };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("no-controller", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_reports_an_unconfirmed_write_after_a_timeout_and_leaves_the_catalog_untouched()
    {
        var channel = new StubNfcCommandChannel
        {
            TagIdResponse = new NfcResponse.TagId("04A13BFE"),
            ThrowOnWrite = new RetroBoxNfcCommandTimeoutException("WRITE timed out."),
        };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("write-unconfirmed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // The outcome on the tag is genuinely unknown, so the catalog must not move either way.
        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.False(floppy.Nfc);
        Assert.Null(floppy.NfcUid);
    }

    [Fact]
    public async Task Write_rejects_an_unknown_floppy()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "nope", confirm: false);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("unknown-floppy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(channel.Calls);
    }

    [Fact]
    public async Task Write_refuses_to_commit_when_a_concurrent_patch_changed_the_mode()
    {
        // Simulates a PATCH landing on disk1 while this write's serial round trip is still in
        // flight: the WRITE command already went out for "ro", but by the time AssignTag commits,
        // the catalog says "rw" -- and UpdateLabelAndMode already cleared Nfc/NfcUid for that mode
        // change on purpose, so this write must not resurrect them under a payload that no longer
        // matches.
        var channel = new StubNfcCommandChannel
        {
            TagIdResponse = new NfcResponse.TagId("04A13BFE"),
            BeforeWriteResponse = () =>
                new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root))
                    .UpdateLabelAndMode("disk1", null, RetroBoxFloppyCatalogRules.ReadWriteMode),
        };
        await using var context = await StartAsync(channel);

        using var response = await PostAsync(context, "disk1", confirm: false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("mode-changed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.False(floppy.Nfc);
        Assert.Null(floppy.NfcUid);
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadWriteMode, floppy.Mode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"confirm\":true}")]
    [InlineData("{\"floppyId\":null,\"confirm\":false}")]
    [InlineData("{\"floppyId\":\"\",\"confirm\":false}")]
    public async Task Write_rejects_a_request_with_no_floppy_id(string rawBody)
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using var response = await context.Client.PostAsync(
            "/api/nfc/write",
            new StringContent(rawBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid-request", responseBody, StringComparison.Ordinal);
        Assert.Contains("\"message\"", responseBody, StringComparison.Ordinal);
        Assert.Empty(channel.Calls);
    }

    [Fact]
    public async Task Read_back_reports_the_floppy_the_write_actually_assigned()
    {
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.TagId("04A13BFE") };
        await using var context = await StartAsync(channel);

        using (var write = await PostAsync(context, "disk1", confirm: false))
        {
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        }

        // On the real appliance, the write's follow-up STATUS command makes the firmware
        // re-announce the tag, which the daemon turns into an INSERT event; simulate that event
        // here rather than probing TAGID again, since the drive endpoint's TAGID branch just
        // echoes the stub's fixed UID and would pass this assertion even if AssignTag never ran.
        context.DriveState.Observe(new RetroBoxArduinoInsertEvent("disk1", "ro"));

        var drive = await context.Client.GetStringAsync("/api/drive");
        Assert.Contains("\"state\":\"loaded\"", drive, StringComparison.Ordinal);
        Assert.Contains("\"floppyId\":\"disk1\"", drive, StringComparison.Ordinal);

        // This is the assertion that actually depends on the write having landed: nothing about
        // the simulated INSERT event above requires AssignTag to have run.
        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.True(floppy.Nfc);
        Assert.Equal("04A13BFE", floppy.NfcUid);
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
        // A driveState is required for GET /api/drive (used by Read_back_reports_the_floppy_the_write_actually_assigned)
        // to reach its TAGID probe at all: BuildViewAsync short-circuits to "unavailable" when
        // driveState is null, the same way it does when nfcChannel is null. Its default Unknown
        // state matches "no controller has reported yet", which is exactly this test's setup, and
        // exposing it lets that test simulate the firmware's post-write INSERT event directly.
        var driveState = new RetroBoxDriveStateTracker();
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source,
            driveState: driveState,
            nfcChannel: nfcChannel);

        return new NfcContext(host, source, driveState, new HttpClient { BaseAddress = host.BaseAddress });
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
        RetroBoxDriveStateTracker DriveState,
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
