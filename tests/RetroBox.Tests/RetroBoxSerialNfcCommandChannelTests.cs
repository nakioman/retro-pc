using System.Text;
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

    [Fact]
    public async Task SendAsync_frees_the_slot_without_minting_an_orphan_when_the_write_itself_fails()
    {
        var router = new RetroBoxSerialLineRouter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, new ThrowingTextWriter());

        await Assert.ThrowsAsync<IOException>(async () => await channel.ReadTagIdAsync());

        Assert.False(router.HasPendingCommand);

        // If the failed write had minted an orphan, this reply would be silently absorbed
        // instead of completing the next command.
        var next = router.BeginCommand();
        Assert.True(router.TryRoute("OK"));
        Assert.IsType<NfcResponse.Ok>(await next);
    }

    [Fact]
    public async Task SendAsync_absorbs_a_late_reply_to_a_timed_out_write_so_the_retry_gets_its_own_reply()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxNfcCommandTimeoutException>(
            async () => await channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var retry = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);

        // The controller's late OK now arrives, answering the timed-out WRITE, not the retry.
        Assert.True(router.TryRoute("OK"));
        Assert.False(retry.IsCompleted);

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        var tagId = Assert.IsType<NfcResponse.TagId>(await retry);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    private static async Task WaitForPendingCommand(RetroBoxSerialLineRouter router)
    {
        for (var attempt = 0; attempt < 100 && !router.HasPendingCommand; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(router.HasPendingCommand, "The command was never registered with the router.");
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            throw new IOException("the serial port is gone");
        }
    }
}
