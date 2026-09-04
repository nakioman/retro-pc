using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>Requests a STATUS re-announcement without registering a routed reply.</summary>
public interface IRetroBoxStatusRequester
{
    Task SendStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes controller commands over the single serial line the daemon owns.
/// One command at a time is not a limitation but the physical reality: there is
/// one drive and one reader.
/// </summary>
public sealed class RetroBoxSerialNfcCommandChannel : IRetroBoxNfcCommandChannel, IRetroBoxStatusRequester
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
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildTagIdCommand(), followUpOnOk: null, cancellationToken);
    }

    public Task<NfcResponse> WriteTagAsync(
        string id,
        string mode,
        CancellationToken cancellationToken = default)
    {
        // The firmware answers a WRITE and then stays quiet, so the daemon would never learn
        // the tag changed. STATUS makes it re-announce the seated tag as an INSERT event, which
        // mounts the newly assigned image through the normal event path.
        return SendAsync(
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode),
            followUpOnOk: RetroBoxArduinoSerialProtocol.BuildStatusCommand(),
            cancellationToken);
    }

    public async Task SendStatusAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            await serialOutput.WriteLineAsync(
                RetroBoxArduinoSerialProtocol.BuildStatusCommand().AsMemory(),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<NfcResponse> SendAsync(
        string command,
        string? followUpOnOk,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
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

            if (response is NfcResponse.Ok && followUpOnOk is not null)
            {
                await serialOutput.WriteLineAsync(followUpOnOk.AsMemory(), cancellationToken);
            }

            return response;
        }
        finally
        {
            gate.Release();
        }
    }
}
