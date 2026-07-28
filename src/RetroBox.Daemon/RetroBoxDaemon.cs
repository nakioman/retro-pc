namespace RetroBox.Daemon;

public sealed class RetroBoxDaemon
{
    public Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}
