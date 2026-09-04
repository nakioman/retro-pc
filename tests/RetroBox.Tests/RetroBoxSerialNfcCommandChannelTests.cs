using System.Text;
using RetroBox.Core;
using RetroBox.Daemon;
using static RetroBox.Tests.RetroBoxSerialLineRouterTestHelpers;

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
    public async Task A_retry_after_a_timeout_is_not_answered_by_the_previous_command_s_late_reply()
    {
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromSeconds(30));
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<RetroBoxNfcCommandTimeoutException>(
            async () => await channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var retry = channel.ReadTagIdAsync();

        // The retry must not even be on the wire yet: the quarantine holds it until the straggler
        // is accounted for. (These two are the ones that actually fail if the quarantine wait is
        // removed — IsCompleted alone stays false either way, since the retry still has to wait
        // for its own reply.)
        Assert.False(router.HasPendingCommand);
        Assert.DoesNotContain("TAGID", serial.ToString(), StringComparison.Ordinal);
        Assert.False(retry.IsCompleted);
        Assert.True(router.TryRoute("OK"));

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await retry);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public async Task A_follow_up_answer_is_not_handed_to_the_next_command()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromSeconds(5));

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        await write;

        var next = channel.ReadTagIdAsync();

        // The next command must not even be on the wire yet: the quarantine holds it until the
        // follow-up's own answer has been accounted for. (These two are the ones that actually
        // fail if the quarantine wait is removed.)
        Assert.False(router.HasPendingCommand);
        Assert.DoesNotContain("TAGID", serial.ToString(), StringComparison.Ordinal);

        // The follow-up STATUS's own answer is still in flight; it must not complete the next
        // command.
        Assert.True(router.TryRoute("ERROR no-tag-detected"));
        Assert.False(next.IsCompleted);

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        Assert.IsType<NfcResponse.TagId>(await next);
    }

    [Fact]
    public async Task A_follow_up_s_designed_insert_event_closes_the_hold_immediately()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromSeconds(5));

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        await write;

        // The ordinary happy path: the follow-up STATUS's answer arrives as the INSERT event it
        // was designed to produce, not as ERROR. That must close the hold immediately rather than
        // riding out the whole window — otherwise every command right after a write stalls for
        // the window's full duration.
        Assert.False(router.TryRoute("INSERT disk1,ro"));

        var next = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);

        Assert.Contains("TAGID", serial.ToString(), StringComparison.Ordinal);

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        Assert.IsType<NfcResponse.TagId>(await next);
    }

    [Fact]
    public async Task WriteTagAsync_asks_for_status_after_a_successful_write()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("OK"));
        await write;

        Assert.Contains("STATUS", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteTagAsync_does_not_ask_for_status_after_a_failed_write()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var write = channel.WriteTagAsync("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode);
        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("ERROR not written"));
        await write;

        Assert.DoesNotContain("STATUS", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendStatusAsync_waits_for_a_command_holding_the_gate()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        var read = channel.ReadTagIdAsync();
        await WaitForPendingCommand(router);

        var status = channel.SendStatusAsync();
        await Task.Delay(50);

        Assert.False(status.IsCompleted);
        Assert.DoesNotContain("STATUS", serial.ToString(), StringComparison.Ordinal);

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        await AwaitWithinBound(read);
        await AwaitWithinBound(status);

        Assert.Contains("STATUS", serial.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_status_poll_s_own_answer_is_not_handed_to_the_next_command()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial, TimeSpan.FromSeconds(5));

        await channel.SendStatusAsync();

        var next = channel.ReadTagIdAsync();

        // The quarantine must hold the next command until the STATUS answer is accounted for.
        Assert.False(router.HasPendingCommand);
        Assert.DoesNotContain("TAGID", serial.ToString(), StringComparison.Ordinal);

        // STATUS answers ERROR when the drive is empty -- the one reply shape TryRoute would
        // otherwise hand to whatever command is pending, completing a TAGID as "empty" and
        // dropping the real Tag ID line that follows.
        Assert.True(router.TryRoute("ERROR no-tag-detected"));
        Assert.False(next.IsCompleted);

        await WaitForPendingCommand(router);
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));
        Assert.IsType<NfcResponse.TagId>(await next);
    }

    [Fact]
    public async Task SendStatusAsync_does_not_register_a_pending_command()
    {
        var router = new RetroBoxSerialLineRouter();
        var serial = new StringWriter();
        var channel = new RetroBoxSerialNfcCommandChannel(router, serial);

        await channel.SendStatusAsync();

        Assert.False(router.HasPendingCommand);
    }

    // A gate release is a chain of async continuations (SemaphoreSlim -> the write -> the
    // finally block), so it does not necessarily land in the same synchronous step as the
    // event that triggers it. Bound the wait instead of awaiting the task directly, so a lost
    // release fails fast with a readable message instead of hanging for the full CI timeout.
    private static async Task AwaitWithinBound(Task task)
    {
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(task.IsCompleted, "The awaited task did not complete within the bound.");
        await task;
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
