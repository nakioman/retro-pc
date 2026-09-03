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
        return SendAsync(RetroBoxArduinoSerialProtocol.BuildTagIdCommand(), followUpOnOk: null, cancellationToken);
    }

    public Task<NfcResponse> WriteTagAsync(
        string id,
        string mode,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            RetroBoxArduinoSerialProtocol.BuildWriteCommand(id, mode),
            followUpOnOk: null,
            cancellationToken);
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
            await serialOutput.WriteLineAsync(command.AsMemory(), cancellationToken);

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
