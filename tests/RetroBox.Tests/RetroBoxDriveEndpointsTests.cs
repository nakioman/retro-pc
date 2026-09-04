using System.Net;
using RetroBox.Core;
using RetroBox.Daemon;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxDriveEndpointsTests
{
    [Fact]
    public async Task Get_drive_reports_unavailable_when_no_controller_is_attached()
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
    public async Task Get_drive_reports_unavailable_when_the_channel_has_no_controller_attached()
    {
        // Mirrors RetroBoxNfcChannelHolder in RetroBox.Cli: a channel that cannot reach a
        // controller throws RetroBoxNfcCommandUnavailableException rather than answering, and
        // this must degrade to "unavailable" rather than a bodyless 500.
        var channel = new StubNfcCommandChannel
        {
            ThrowOnCall = new RetroBoxNfcCommandUnavailableException("No floppy controller is connected."),
        };

        await using var host = await StartAsync(new RetroBoxDriveStateTracker(), channel);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.Contains(
            "\"state\":\"unavailable\"", await client.GetStringAsync("/api/drive"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_drive_reports_unavailable_when_the_port_disappears_mid_command()
    {
        // SerialDeviceWriter does not wrap the writer's failures, so a yanked or disposed port
        // surfaces a raw ObjectDisposedException (an InvalidOperationException) straight out of
        // SerialPort.BaseStream.Write, not one of the two NFC exception types. This must degrade
        // to "unavailable" the same as a timeout, not a bodyless 500.
        var channel = new StubNfcCommandChannel { ThrowOnCall = new ObjectDisposedException("port") };

        await using var host = await StartAsync(new RetroBoxDriveStateTracker(), channel);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.GetAsync("/api/drive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "\"state\":\"unavailable\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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

        var first = await ReadNextDataLineAsync(reader, cancellation.Token);

        Assert.StartsWith("data: ", first, StringComparison.Ordinal);
        Assert.Contains("\"floppyId\":\"disk1\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drive_events_stream_sends_again_only_after_the_state_changes()
    {
        var tracker = new RetroBoxDriveStateTracker();
        var channel = new StubNfcCommandChannel { TagIdResponse = new NfcResponse.Error("no-tag-detected") };

        await using var host = await StartAsync(tracker, channel);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var response = await client.GetAsync(
            "/api/drive/events", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);

        var first = await ReadNextDataLineAsync(reader, cancellation.Token);
        Assert.Contains("\"state\":\"empty\"", first, StringComparison.Ordinal);

        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        // Blocks on the stream until the next changed payload arrives, whenever the poll notices
        // it - a bounded wait via the CancellationTokenSource above, not a fixed sleep.
        var second = await ReadNextDataLineAsync(reader, cancellation.Token);
        Assert.Contains("\"state\":\"loaded\"", second, StringComparison.Ordinal);
        Assert.Contains("\"floppyId\":\"disk1\"", second, StringComparison.Ordinal);
    }

    private static async Task<string> ReadNextDataLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            Assert.NotNull(line);

            if (line.Length > 0)
            {
                return line;
            }
        }
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
