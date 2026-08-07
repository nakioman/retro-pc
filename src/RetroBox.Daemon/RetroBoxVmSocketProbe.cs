using RetroBox.Core;

namespace RetroBox.Daemon;

public interface IRetroBoxVmSocketProbe
{
    Task<bool> IsSocketReadyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Treats the 86Box floppy-control socket as the VM-running signal: a
/// successful floppy.status request means 86Box is up and accepting control
/// commands, so the daemon can safely ask the firmware for its current state.
/// </summary>
public sealed class RetroBoxFloppyControlSocketProbe(
    IRetroBoxFloppyControlClient floppyControlClient) : IRetroBoxVmSocketProbe
{
    public async Task<bool> IsSocketReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await floppyControlClient.StatusAsync(0, cancellationToken);
            return true;
        }
        catch (RetroBoxFloppyControlException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
