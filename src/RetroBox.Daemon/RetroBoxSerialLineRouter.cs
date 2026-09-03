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

    // The wire protocol carries no request ids, so a reply that arrives after its command was
    // cancelled (e.g. by a timeout) cannot be told apart from a reply to whatever command is
    // pending next. Counting orphans lets a late reply be absorbed instead of being handed to
    // the wrong caller as that caller's own result.
    private int orphanedReplies;

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
        var response = RetroBoxArduinoSerialProtocol.ParseResponse(line);
        if (response is NfcResponse.Unknown)
        {
            return false;
        }

        TaskCompletionSource<NfcResponse>? completion;

        lock (gate)
        {
            if (orphanedReplies > 0)
            {
                orphanedReplies--;
                return true;
            }

            if (pending is null)
            {
                return true;
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

            if (completion is not null)
            {
                orphanedReplies++;
            }
        }

        completion?.TrySetException(error);
    }
}
