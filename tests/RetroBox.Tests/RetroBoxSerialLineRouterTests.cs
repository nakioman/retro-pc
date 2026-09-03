using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxSerialLineRouterTests
{
    [Fact]
    public void TryRoute_consumes_an_unsolicited_response_without_completing_anything()
    {
        var router = new RetroBoxSerialLineRouter();

        Assert.True(router.TryRoute("OK"));
        Assert.False(router.HasPendingCommand);
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

        Assert.IsType<NfcResponse.Ok>(await pending);
    }
}
