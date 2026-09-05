using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

internal sealed class StubNfcCommandChannel : IRetroBoxNfcCommandChannel
{
    public List<string> Calls { get; } = [];

    public NfcResponse TagIdResponse { get; init; } = new NfcResponse.Error("no-tag-detected");

    public NfcResponse WriteResponse { get; init; } = new NfcResponse.Ok();

    public Exception? ThrowOnCall { get; init; }

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add("TAGID");
        return Task.FromResult(TagIdResponse);
    }

    public Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add($"WRITE:{id}:{mode}");
        return Task.FromResult(WriteResponse);
    }
}

internal static class RetroBoxSerialLineRouterTestHelpers
{
    public static async Task WaitForPendingCommand(RetroBoxSerialLineRouter router)
    {
        for (var attempt = 0; attempt < 100 && !router.HasPendingCommand; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(router.HasPendingCommand, "The command was never registered with the router.");
    }
}

/// <summary>
/// A manually-advanced <see cref="TimeProvider"/> so time-dependent router tests can drive the
/// clock explicitly instead of waiting out real windows. Only <see cref="GetTimestamp"/> and
/// <see cref="CreateTimer"/> need overriding: those are the only members <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>
/// and the router's own deadline arithmetic touch.
/// </summary>
internal sealed class RetroBoxFakeTimeProvider : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<FakeTimer> pendingTimers = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return ticks;
        }
    }

    public void Advance(TimeSpan amount)
    {
        List<FakeTimer> due;

        lock (gate)
        {
            ticks += amount.Ticks;
            due = pendingTimers.Where(timer => timer.DueTicks <= ticks).ToList();
            pendingTimers.RemoveAll(timer => timer.DueTicks <= ticks);
        }

        // Fire outside the lock: the callback re-enters router code that takes its own lock.
        foreach (var timer in due)
        {
            timer.Callback(timer.State);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);

        lock (gate)
        {
            timer.DueTicks = ticks + (dueTime > TimeSpan.Zero ? dueTime.Ticks : 0);
            pendingTimers.Add(timer);
        }

        return timer;
    }

    private sealed class FakeTimer(RetroBoxFakeTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public long DueTicks { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                DueTicks = owner.ticks + (dueTime > TimeSpan.Zero ? dueTime.Ticks : 0);
            }

            return true;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                owner.pendingTimers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal static class FloppyControlTestCatalogs
{
    public static RetroBoxCatalogData CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return new RetroBoxCatalogData(
            new RetroBoxConfig { DefaultVm = "dos" },
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal)
            {
                ["dos"] = new() { Label = "DOS", Path = "/data/vms/dos" },
            },
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal)
            {
                [floppyId] = new()
                {
                    Label = "Disk 1",
                    Image = imagePath,
                    Mode = mode,
                    Nfc = nfc,
                },
            });
    }
}

internal sealed class MutableCatalogSource(RetroBoxCatalogData initial) : IRetroBoxCatalogSource
{
    private RetroBoxCatalogData current = initial;

    public RetroBoxCatalogSnapshot Snapshot => new(current, null);

    public void Publish(RetroBoxCatalogData catalog) => current = catalog;
}

internal sealed class RecordingFloppyControlClient : IRetroBoxFloppyControlClient
{
    public List<string> Calls { get; } = [];

    public RetroBoxFloppyControlException? InsertError { get; init; }

    public Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        if (InsertError is not null)
        {
            throw InsertError;
        }

        Calls.Add($"insert:{drive}:{imagePath}:{readOnly}");
        return Task.FromResult(new RetroBoxFloppyStatus(drive, true, imagePath, readOnly, false, true));
    }

    public Task<RetroBoxFloppyStatus> EjectAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"eject:{drive}");
        return Task.FromResult(new RetroBoxFloppyStatus(drive, false, null, false, false, true));
    }

    public Task<RetroBoxFloppyStatus> StatusAsync(
        int drive,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"status:{drive}");
        return Task.FromResult(new RetroBoxFloppyStatus(drive, false, null, false, false, false));
    }
}

internal sealed class RecordingNfcClient : IRetroBoxNfcClient
{
    public List<string> Calls { get; } = [];

    public NfcResponse? PingResponse { get; init; }

    public NfcResponse? WriteResponse { get; init; }

    public Exception? ThrowOnCall { get; init; }

    public Task<NfcResponse> PingAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add("PING");
        return Task.FromResult(PingResponse ?? new NfcResponse.Pong());
    }

    public Task<NfcResponse> WriteAsync(string id, string mode, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null)
        {
            throw ThrowOnCall;
        }

        Calls.Add($"WRITE:{id}:{mode}");
        return Task.FromResult(WriteResponse ?? new NfcResponse.Ok());
    }
}
