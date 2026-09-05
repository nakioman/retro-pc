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
    private readonly TimeProvider timeProvider;
    private TaskCompletionSource<NfcResponse>? pending;

    // The wire protocol carries no request ids, so a reply that arrives after its command was
    // cancelled (e.g. by a timeout) cannot be told apart from a reply to whatever command is
    // pending next. A single expiring slot lets one late reply be absorbed instead of being
    // handed to the wrong caller as that caller's own result — but only briefly: if nothing
    // ever arrives, the slot must close on its own, or a genuinely unrelated later reply would
    // be swallowed forever.
    private long orphanDeadline;

    // A window armed by a timeout must still let an unprompted ERROR (e.g. a background STATUS
    // poll finding the drive empty) fall through as an event rather than being swallowed. A
    // window armed by ExpectOrphanedReply is different: it exists specifically to catch a
    // follow-up command's own answer, which is known to be coming and may itself be ERROR, so
    // that window accepts whatever arrives.
    private bool orphanAbsorbsAnyReply;

    // Signalled whenever the orphan window closes, so WaitForClearSlotAsync can wake up the
    // instant TryRoute absorbs the straggler instead of always riding out the full window.
    private TaskCompletionSource<bool>? orphanCleared;

    public RetroBoxSerialLineRouter(TimeSpan? orphanWindow = null, TimeProvider? timeProvider = null)
    {
        this.orphanWindow = orphanWindow ?? DefaultOrphanWindow;
        this.timeProvider = timeProvider ?? TimeProvider.System;
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
            // A follow-up's designed answer is an INSERT/EJECT event, which parses here rather
            // than as a command reply, so it never reaches the orphan check below. A follow-up-
            // armed window closes on any valid event this way, not just INSERT/EJECT — INIT
            // (a controller reset) counts too, since the follow-up's answer is lost either way.
            // Without this, the window would ride out its full duration on the ordinary happy
            // path, wide open to swallow an unrelated ERROR that arrives during that stretch.
            bool mayCloseOnEvent;
            lock (gate)
            {
                mayCloseOnEvent = orphanDeadline != 0 && orphanAbsorbsAnyReply;
            }

            if (mayCloseOnEvent && IsValidEvent(line))
            {
                lock (gate)
                {
                    if (orphanDeadline != 0 && orphanAbsorbsAnyReply)
                    {
                        ClearOrphanWindow();
                    }
                }
            }

            return false;
        }

        TaskCompletionSource<NfcResponse> completion;

        lock (gate)
        {
            // ERROR is the one reply the firmware also emits unprompted (e.g. answering a
            // background STATUS poll with the drive empty), so it can never be treated as an
            // orphan or swallowed on a timeout-armed window: with no command waiting, it is an
            // event. A follow-up-armed window is exempt from this — see orphanAbsorbsAnyReply.
            var isUnambiguousReply = response is not NfcResponse.Error;

            if (orphanDeadline != 0 && (isUnambiguousReply || orphanAbsorbsAnyReply))
            {
                var expired = timeProvider.GetTimestamp() >= orphanDeadline;
                ClearOrphanWindow();

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
                ArmOrphanWindow(absorbsAnyReply: false);
            }
        }

        completion?.TrySetException(error);
    }

    /// <summary>
    /// Opens the orphan window without cancelling any pending command. Used after writing a
    /// follow-up command whose own answer must be discarded rather than handed to whichever
    /// command begins next: unlike a timeout-armed window, this one absorbs any reply —
    /// including ERROR — because exactly one answer to the follow-up is guaranteed to arrive.
    /// </summary>
    public void ExpectOrphanedReply()
    {
        lock (gate)
        {
            ArmOrphanWindow(absorbsAnyReply: true);
        }
    }

    /// <summary>
    /// Waits until no orphaned reply is still expected. Callers must do this before beginning a
    /// command: absorbing a late reply only works while nothing else is in flight, otherwise the
    /// retry's own timely reply is eaten instead and the failure renews itself.
    /// </summary>
    public async Task WaitForClearSlotAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining;
            Task clearedSignal;

            lock (gate)
            {
                if (orphanDeadline == 0)
                {
                    return;
                }

                var ticks = orphanDeadline - timeProvider.GetTimestamp();
                if (ticks <= 0)
                {
                    ClearOrphanWindow();
                    return;
                }

                remaining = TimeSpan.FromSeconds((double)ticks / timeProvider.TimestampFrequency);

                // orphanDeadline != 0 is supposed to be maintained in lock-step with
                // orphanCleared: both are set together in ArmOrphanWindow and cleared together
                // in ClearOrphanWindow. If that invariant ever broke, falling back to a
                // completed task would degrade into a hot spin instead of failing loudly.
                clearedSignal = orphanCleared?.Task
                    ?? throw new InvalidOperationException(
                        "orphanDeadline is armed but orphanCleared is null.");
            }

            // Race the remaining window against TryRoute absorbing the straggler early,
            // cancelling the delay's own timer as soon as either one wins so a signal hit
            // doesn't leave a real timer registered for the rest of the window. The loop
            // re-checks afterwards rather than trusting either signal blindly, because the
            // deadline may already have moved on by the time either one completes.
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Task.WhenAny(Task.Delay(remaining, timeProvider, delayCts.Token), clearedSignal);
            }
            finally
            {
                delayCts.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Must be called while holding gate.
    private void ArmOrphanWindow(bool absorbsAnyReply)
    {
        // A window can already be open (e.g. BeginCommand/CancelCommand run again while a
        // follow-up hold is still armed): complete the stale signal first, or a waiter on the
        // old one would wake late for no reason once it's overwritten below.
        orphanCleared?.TrySetResult(true);

        orphanDeadline = timeProvider.GetTimestamp()
            + (long)(orphanWindow.TotalSeconds * timeProvider.TimestampFrequency);
        orphanAbsorbsAnyReply = absorbsAnyReply;
        orphanCleared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Must be called while holding gate.
    private void ClearOrphanWindow()
    {
        orphanDeadline = 0;
        orphanAbsorbsAnyReply = false;

        var cleared = orphanCleared;
        orphanCleared = null;
        cleared?.TrySetResult(true);
    }

    private static bool IsValidEvent(string line)
    {
        try
        {
            RetroBoxArduinoSerialProtocol.ParseEvent(line);
            return true;
        }
        catch (RetroBoxArduinoSerialProtocolException)
        {
            return false;
        }
    }
}
