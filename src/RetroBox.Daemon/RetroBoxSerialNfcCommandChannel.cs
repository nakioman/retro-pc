using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>
/// Serializes controller commands over the single serial line the daemon owns.
/// One command at a time is not a limitation but the physical reality: there is
/// one drive and one reader.
/// </summary>
public sealed class RetroBoxSerialNfcCommandChannel : IRetroBoxNfcCommandChannel
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly RetroBoxSerialLineRouter router;
    private readonly TextWriter serialOutput;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim gate = new(1, 1);

    public RetroBoxSerialNfcCommandChannel(
        RetroBoxSerialLineRouter router,
        TextWriter serialOutput,
        TimeSpan? timeout = null)
    {
        this.router = router;
        this.serialOutput = serialOutput;
        this.timeout = timeout ?? DefaultTimeout;
    }

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildTagIdCommand(), cancellationToken);
    }

    /// <summary>
    /// Writes the tag and nothing else. The firmware answers a WRITE and then stays quiet, so
    /// something has to ask it to re-announce the seated tag -- but that re-announce arrives as
    /// an INSERT the daemon handles against the catalog as it stands at that instant, so it
    /// belongs to whoever commits the assignment, after the commit, not to this method.
    /// </summary>
    public Task<NfcResponse> WriteTagAsync(
        string id,
        string mode,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode), cancellationToken);
    }

    public async Task SendStatusAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            await serialOutput.WriteLineAsync(
                RetroBoxArduinoSerialProtocol.BuildStatusCommand().AsMemory(),
                cancellationToken);

            // STATUS answers INSERT/EJECT with a disk seated, but ERROR with the drive empty --
            // and an unprompted ERROR is handed to whatever command is pending. Without a window
            // here, a TAGID that takes the gate right after this release completes as "empty"
            // and the real Tag ID line is dropped with nothing pending. This is the same
            // quarantine WriteTagAsync's follow-up arms, for the same reason.
            router.ExpectOrphanedReply();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NfcResponse> SendAsync(
        string command,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            // Deliberately inside the gate, not before it. A window is armed by the outgoing
            // gate holder's own timeout or STATUS write, which happens while that holder still
            // holds the gate — so a caller that checked the slot before queuing for the gate
            // could still have a window armed underneath it by the time it gets in. Waiting in
            // here means the window, if any, is already fully armed before this ever looks at
            // it. SendStatusAsync never calls this at all, so it is affected only indirectly,
            // through the shared gate it contends for; now that the window closes on STATUS's
            // own event answer (not just its full timeout), that indirect delay is no longer
            // for the window's full duration.
            await router.WaitForClearSlotAsync(cancellationToken);

            var reply = router.BeginCommand();

            try
            {
                await serialOutput.WriteLineAsync(command.AsMemory(), cancellationToken);
            }
            catch (Exception ex)
            {
                // Nothing reached the wire, so no reply is coming and no orphan may be minted.
                router.CancelCommand(ex, expectLateReply: false);
                throw;
            }

            NfcResponse response;
            try
            {
                response = await reply.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                var error = new RetroBoxNfcCommandTimeoutException(
                    $"The floppy controller did not answer '{command}' within {timeout.TotalSeconds:0.##}s.");
                router.CancelCommand(error);
                throw error;
            }
            catch (OperationCanceledException)
            {
                router.CancelCommand(new OperationCanceledException(cancellationToken));
                throw;
            }

            return response;
        }
        finally
        {
            gate.Release();
        }
    }
}
