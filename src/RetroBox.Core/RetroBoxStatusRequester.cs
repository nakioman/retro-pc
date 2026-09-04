namespace RetroBox.Core;

/// <summary>
/// Asks the controller to re-announce what is in the drive. The answer arrives as an ordinary
/// INSERT/EJECT event rather than a routed command reply, so this registers no pending command.
/// </summary>
public interface IRetroBoxStatusRequester
{
    Task SendStatusAsync(CancellationToken cancellationToken = default);
}
