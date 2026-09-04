using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>
/// Splits the controller's serial lines between command replies and floppy
/// events. Events are never diverted: the disk can be pulled while a command
/// is still waiting for its reply.
/// </summary>
public sealed class RetroBoxSerialLineRouter
{
    private readonly Lock gate = new();
    private TaskCompletionSource<NfcResponse>? pending;

    public bool HasPendingCommand
    {
        get
        {
            lock (gate)
            {
                return pending is not null;
            }
        }
    }

    public Task<NfcResponse> BeginCommand()
    {
        lock (gate)
        {
            if (pending is not null)
            {
                throw new InvalidOperationException("A floppy controller command is already in flight.");
            }

            pending = new TaskCompletionSource<NfcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        }
    }

    public bool TryRoute(string line)
    {
        TaskCompletionSource<NfcResponse> completion;
        NfcResponse response;

        lock (gate)
        {
            if (pending is null)
            {
                return false;
            }

            response = RetroBoxArduinoSerialProtocol.ParseResponse(line);
            if (response is NfcResponse.Unknown)
            {
                return false;
            }

            completion = pending;
            pending = null;
        }

        completion.TrySetResult(response);
        return true;
    }

    public void CancelCommand(Exception error)
    {
        TaskCompletionSource<NfcResponse>? completion;

        lock (gate)
        {
            completion = pending;
            pending = null;
        }

        completion?.TrySetException(error);
    }
}
