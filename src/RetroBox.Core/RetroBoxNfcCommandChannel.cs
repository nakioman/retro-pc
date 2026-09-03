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
