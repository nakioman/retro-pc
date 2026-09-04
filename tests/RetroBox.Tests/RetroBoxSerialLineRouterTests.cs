using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialLineRouterTests
{
    [Fact]
    public async Task TryRoute_consumes_an_unsolicited_response_without_completing_anything()
    {
        var router = new RetroBoxSerialLineRouter();

        Assert.True(router.TryRoute("OK"));
        Assert.False(router.HasPendingCommand);

        var pending = router.BeginCommand();
        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        Assert.True(pending.IsCompleted);
        var tagId = Assert.IsType<NfcResponse.TagId>(await pending);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public void TryRoute_falls_through_to_the_event_path_for_an_unprompted_error()
    {
        var router = new RetroBoxSerialLineRouter();

        Assert.False(router.TryRoute("ERROR no-tag-detected"));
    }

    [Fact]
    public async Task TryRoute_does_not_drain_the_orphan_slot_on_an_unprompted_error()
    {
        var router = new RetroBoxSerialLineRouter();
        var timedOut = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        Assert.False(router.TryRoute("ERROR no-tag-detected"));

        var next = router.BeginCommand();
        Assert.True(router.TryRoute("OK"));

        Assert.False(next.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(async () => await timedOut);
    }

    [Fact]
    public async Task TryRoute_does_not_absorb_a_reply_once_the_orphan_window_has_expired()
    {
        var router = new RetroBoxSerialLineRouter(TimeSpan.Zero);
        var timedOut = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var next = router.BeginCommand();
        Assert.True(router.TryRoute("OK"));

        Assert.True(next.IsCompleted);
        Assert.IsType<NfcResponse.Ok>(await next);
        await Assert.ThrowsAsync<TimeoutException>(async () => await timedOut);
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_ok()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("OK"));

        Assert.IsType<NfcResponse.Ok>(await pending);
        Assert.False(router.HasPendingCommand);
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_a_tag_id()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("Tag ID: 04A13BFE"));

        var tagId = Assert.IsType<NfcResponse.TagId>(await pending);
        Assert.Equal("04A13BFE", tagId.Uid);
    }

    [Fact]
    public async Task TryRoute_completes_the_pending_command_with_an_error()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.True(router.TryRoute("ERROR not written"));

        var error = Assert.IsType<NfcResponse.Error>(await pending);
        Assert.Equal("not written", error.Message);
    }

    [Theory]
    [InlineData("INSERT disk1,ro")]
    [InlineData("EJECT")]
    [InlineData("INIT 1.0")]
    public void TryRoute_never_diverts_events_even_mid_command(string line)
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        Assert.False(router.TryRoute(line));

        Assert.False(pending.IsCompleted);
        Assert.True(router.HasPendingCommand);
    }

    [Fact]
    public void BeginCommand_rejects_a_second_command_in_flight()
    {
        var router = new RetroBoxSerialLineRouter();
        router.BeginCommand();

        // BeginCommand throws synchronously, before any Task is created; xUnit2014 assumes any
        // Task-returning call means an async throw, which is not the case here.
#pragma warning disable xUnit2014
        Assert.Throws<InvalidOperationException>(() => { router.BeginCommand(); });
#pragma warning restore xUnit2014
    }

    [Fact]
    public async Task CancelCommand_fails_the_pending_command_and_frees_the_slot()
    {
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        router.CancelCommand(new TimeoutException("no reply"));

        await Assert.ThrowsAsync<TimeoutException>(async () => await pending);
        Assert.False(router.HasPendingCommand);
    }

    [Fact]
    public async Task TryRoute_absorbs_a_late_reply_after_cancel_instead_of_completing_a_newly_begun_command()
    {
        var router = new RetroBoxSerialLineRouter();
        var timedOut = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var next = router.BeginCommand();

        Assert.True(router.TryRoute("OK"));

        Assert.False(next.IsCompleted);
        await Assert.ThrowsAsync<TimeoutException>(async () => await timedOut);
    }

    [Fact]
    public async Task CancelCommand_on_an_empty_slot_does_not_create_a_phantom_orphan()
    {
        var router = new RetroBoxSerialLineRouter();

        router.CancelCommand(new TimeoutException("nothing was pending"));

        var pending = router.BeginCommand();
        Assert.True(router.TryRoute("OK"));

        Assert.True(pending.IsCompleted);
        Assert.IsType<NfcResponse.Ok>(await pending);
    }

    [Theory]
    [InlineData("INSERT disk1,ro")]
    [InlineData("EJECT")]
    public void TryRoute_closes_a_follow_up_armed_window_on_its_designed_event_answer(string eventLine)
    {
        var router = new RetroBoxSerialLineRouter();
        router.ExpectOrphanedReply();

        // The follow-up's designed answer is an event, not a reply, so it never reaches the
        // orphan-absorb check by the usual route — it must still close the window.
        Assert.False(router.TryRoute(eventLine));

        // With the window closed, an unrelated ERROR arriving afterward is not swallowed as
        // the straggler: it falls through as its own unprompted event, same as always.
        Assert.False(router.TryRoute("ERROR no-tag-detected"));
    }

    [Fact]
    public void TryRoute_does_not_close_a_timeout_armed_window_on_an_event()
    {
        var router = new RetroBoxSerialLineRouter();
        var timedOut = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        // A timeout-armed window has nothing to do with events; it must stay open so it can
        // still absorb the late OK.
        Assert.False(router.TryRoute("INSERT disk1,ro"));

        var next = router.BeginCommand();
        Assert.True(router.TryRoute("OK"));
        Assert.False(next.IsCompleted);
    }

    [Fact]
    public async Task WaitForClearSlotAsync_returns_at_once_when_no_orphan_is_outstanding()
    {
        var router = new RetroBoxSerialLineRouter();

        await router.WaitForClearSlotAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForClearSlotAsync_waits_out_an_orphan_window()
    {
        var time = new RetroBoxFakeTimeProvider();
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromSeconds(1), timeProvider: time);
        _ = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var wait = router.WaitForClearSlotAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        // Drive the window's expiry explicitly rather than waiting it out in real time.
        time.Advance(TimeSpan.FromSeconds(1));

        await wait;
    }

    [Fact]
    public async Task WaitForClearSlotAsync_returns_once_the_late_reply_is_absorbed()
    {
        var router = new RetroBoxSerialLineRouter(orphanWindow: TimeSpan.FromSeconds(30));
        _ = router.BeginCommand();
        router.CancelCommand(new TimeoutException("no reply"));

        var wait = router.WaitForClearSlotAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        // The straggler arrives: the slot is clear immediately, without waiting out the window.
        Assert.True(router.TryRoute("OK"));

        await AwaitWithinBound(wait);
    }

    // A cleared orphan slot is signalled rather than merely timed out, so a caller waiting on
    // it wakes up promptly instead of riding out the whole window. Bound the wait instead of
    // awaiting the task directly, so a lost signal fails fast with a readable message instead
    // of hanging for the full CI timeout.
    private static async Task AwaitWithinBound(Task task)
    {
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(task.IsCompleted, "The awaited task did not complete within the bound.");
        await task;
    }
}
