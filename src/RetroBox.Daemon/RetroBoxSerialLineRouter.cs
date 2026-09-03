using System.Diagnostics;
using RetroBox.Core;

namespace RetroBox.Daemon;

/// <summary>
/// Splits the controller's serial lines between command replies and floppy
/// events. Events are never diverted: the disk can be pulled while a command
/// is still waiting for its reply.
/// </summary>
public sealed class RetroBoxSerialLineRouter
{
    public static readonly TimeSpan DefaultOrphanWindow = TimeSpan.FromSeconds(5);

    private readonly Lock gate = new();
    private readonly TimeSpan orphanWindow;
    private TaskCompletionSource<NfcResponse>? pending;

    // The wire protocol carries no request ids, so a reply that arrives after its command was
    // cancelled (e.g. by a timeout) cannot be told apart from a reply to whatever command is
    // pending next. A single expiring slot lets one late reply be absorbed instead of being
    // handed to the wrong caller as that caller's own result — but only briefly: if nothing
    // ever arrives, the slot must close on its own, or a genuinely unrelated later reply would
    // be swallowed forever.
    private long orphanDeadline;

    public RetroBoxSerialLineRouter(TimeSpan? orphanWindow = null)
    {
        this.orphanWindow = orphanWindow ?? DefaultOrphanWindow;
    }

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

        TaskCompletionSource<NfcResponse> completion;

        lock (gate)
        {
            // ERROR is the one reply the firmware also emits unprompted (e.g. answering a
            // background STATUS poll with the drive empty), so it can never be treated as an
            // orphan or swallowed: with no command waiting, it is an event.
            var isUnambiguousReply = response is not NfcResponse.Error;

            if (orphanDeadline != 0 && isUnambiguousReply)
            {
                var expired = Stopwatch.GetTimestamp() >= orphanDeadline;
                orphanDeadline = 0;

                if (!expired)
                {
                    return true;
                }
            }

            if (pending is null)
            {
                return isUnambiguousReply;
            }

            completion = pending;
            pending = null;
        }

        completion.TrySetResult(response);
        return true;
    }

    public void CancelCommand(Exception error, bool expectLateReply = true)
    {
        TaskCompletionSource<NfcResponse>? completion;

        lock (gate)
        {
            completion = pending;
            pending = null;

            if (completion is not null && expectLateReply)
            {
                orphanDeadline = Stopwatch.GetTimestamp()
                    + (long)(orphanWindow.TotalSeconds * Stopwatch.Frequency);
            }
        }

        completion?.TrySetException(error);
    }
}
