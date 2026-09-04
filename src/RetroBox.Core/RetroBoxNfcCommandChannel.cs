namespace RetroBox.Core;

/// <summary>Sends commands to the floppy controller and awaits its reply.</summary>
public interface IRetroBoxNfcCommandChannel
{
    Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default);

    Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default);
}

public sealed class RetroBoxNfcCommandTimeoutException : Exception
{
    public RetroBoxNfcCommandTimeoutException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The command could not be carried out because no channel is currently attached to a
/// controller — the same kind of failure as a timeout, just discovered before the write
/// instead of after it.
/// </summary>
public sealed class RetroBoxNfcCommandUnavailableException : Exception
{
    public RetroBoxNfcCommandUnavailableException(string message)
        : base(message)
    {
    }
}
