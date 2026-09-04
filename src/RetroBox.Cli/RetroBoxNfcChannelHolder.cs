using RetroBox.Core;

namespace RetroBox.Cli;

/// <summary>
/// The panel outlives any one serial connection, so it holds this rather than a channel bound to
/// a device that may be unplugged. Null means "no controller right now", which is exactly what
/// the drive endpoints report as unavailable.
/// </summary>
internal sealed class RetroBoxNfcChannelHolder : IRetroBoxNfcCommandChannel
{
    private volatile IRetroBoxNfcCommandChannel? current;

    public void Set(IRetroBoxNfcCommandChannel? channel) => current = channel;

    public Task<NfcResponse> ReadTagIdAsync(CancellationToken cancellationToken = default) =>
        Require().ReadTagIdAsync(cancellationToken);

    public Task<NfcResponse> WriteTagAsync(string id, string mode, CancellationToken cancellationToken = default) =>
        Require().WriteTagAsync(id, mode, cancellationToken);

    public Task SendStatusAsync(CancellationToken cancellationToken = default) =>
        Require().SendStatusAsync(cancellationToken);

    private IRetroBoxNfcCommandChannel Require() =>
        current ?? throw new RetroBoxNfcCommandUnavailableException("No floppy controller is connected.");
}
