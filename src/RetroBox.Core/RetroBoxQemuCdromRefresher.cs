namespace RetroBox.Core;

public enum RetroBoxCdromRefreshOutcome
{
    NoQemuVm,
    Changed,
    Ejected,
}

/// <summary>
/// QEMU passes the host drive through but keeps the old disc after a swap until the drive is
/// reopened. After a media change this reopens it over QMP (the guest sees eject + insert), or
/// ejects the VM's disc when the drive is empty: the change command fails without media.
/// </summary>
public sealed class RetroBoxQemuCdromRefresher(
    RetroBoxQmpClient qmpClient,
    IRetroBoxOpticalDriveProbe probe,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly TimeSpan SpinUpPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan SpinUpTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    /// <param name="drivePath">The drive the launcher attached (installer-detected), reopened by that path.</param>
    public async Task<RetroBoxCdromRefreshOutcome> RefreshAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        // The drive is probed before connecting: QEMU serves one QMP client at a time, so the
        // socket is not held while a new disc spins up.
        var media = await WaitForSpinUpAsync(drivePath, cancellationToken);

        RetroBoxQmpConnection connection;
        try
        {
            connection = await qmpClient.ConnectAsync(cancellationToken);
        }
        catch (RetroBoxQmpUnavailableException)
        {
            return RetroBoxCdromRefreshOutcome.NoQemuVm;
        }

        await using (connection)
        {
            if (media is RetroBoxOpticalMedia.Absent or RetroBoxOpticalMedia.TrayOpen)
            {
                // Forced: the disc is physically gone even if the guest locked the tray.
                await connection.EjectAsync(RetroBoxQemuLauncher.OpticalDriveId, force: true, cancellationToken);
                return RetroBoxCdromRefreshOutcome.Ejected;
            }

            await connection.ChangeMediumAsync(
                RetroBoxQemuLauncher.OpticalDriveId,
                drivePath,
                "raw",
                readOnly: true,
                cancellationToken);
            return RetroBoxCdromRefreshOutcome.Changed;
        }
    }

    /// <summary>A drive still "not ready" at the timeout may hold an unreadable disc; it is offered anyway.</summary>
    private async Task<RetroBoxOpticalMedia> WaitForSpinUpAsync(string drivePath, CancellationToken cancellationToken)
    {
        var media = probe.Probe(drivePath);
        for (var waited = TimeSpan.Zero; media == RetroBoxOpticalMedia.NotReady && waited < SpinUpTimeout; waited += SpinUpPollInterval)
        {
            await delay(SpinUpPollInterval, cancellationToken);
            media = probe.Probe(drivePath);
        }

        return media;
    }
}
