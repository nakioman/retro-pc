using RetroBox.Core;

namespace RetroBox.Daemon;

public interface IRetroBoxVmSocketProbe
{
    Task<bool> IsSocketReadyAsync(CancellationToken cancellationToken);
}

/// <summary>Treats a successful floppy.status request as the signal that 86Box is accepting control.</summary>
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
