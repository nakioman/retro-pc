using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialNfcCommandChannelTests
{
    [Fact]
    public async Task WriteTagAsync_sends_the_write_command_and_returns_the_reply()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));

        Assert.IsType<NfcResponse.Ok>(await write);
        Assert.Contains("WRITE disk1,ro", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTagIdAsync_sends_the_tagid_command_and_returns_the_uid()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var read = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await read);
        Assert.Equal("04A13BFE", tagId.Uid);
        Assert.Contains("TAGID", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadTagIdAsync_surfaces_an_empty_drive_as_an_error_reply()
    {
        var router = new RetroBoxSerialLineRouter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, new StringWriter());

        var read = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("ERROR no-tag-detected"));

        var error = Assert.IsType<NfcResponse.Error>(await read);
        Assert.Equal("no-tag-detected", error.Message);
    }

    [Fact]
    public async Task SendAsync_times_out_when_the_controller_never_replies()
    {
        var router = new RetroBoxSerialLineRouter();
        var channel = new RetroBoxSerialNfcCommandChannel(
            router,
            new StringWriter(),
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxNfcCommandTimeoutException>(
            async () => await channel.ReadTagIdAsync());

        Assert.False(router.HasPendingCommand);
    }

    [Fact]
    public async Task SendAsync_serializes_commands_so_a_second_one_waits()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var first = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);
        var second = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);

        Assert.False(second.IsCompleted);
        Assert.DoesNotContain("WRITE", serial.ToString(), StringComparison.Ordinal);

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        await first;

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        Assert.IsType<NfcResponse.Ok>(await second);
    }

    private static async Task WaitForPendingCommand(RetroBoxSerialLineRouter router)
    {
        for (var attempt = 0; attempt < 100 && !router.HasPendingCommand; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(router.HasPendingCommand, "The command was never registered with the router.");
    }
}
