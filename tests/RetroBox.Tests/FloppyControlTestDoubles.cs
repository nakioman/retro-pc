using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

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
