using RetroBox.Core;

namespace RetroBox.Tests;

internal static class FloppyControlTestCatalogs
{
    public static RetroBoxCatalogData CreateCatalog(string floppyId, string imagePath, string mode)
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
                },
            },
            new Dictionary<string, RetroBoxGame>(StringComparer.Ordinal));
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
