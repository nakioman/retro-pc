namespace RetroBox.Core;

/// <summary>
/// Sends floppy commands to whichever VM is running: the 86Box floppy control socket or QEMU's
/// QMP socket. Only one VM runs at a time, so at most one engine answers; the one that last
/// answered is asked first, and the other only when it is unavailable. A command the running VM
/// refuses is never retried on the other engine.
/// </summary>
public sealed class RetroBoxLiveVmFloppyControlClient(params IRetroBoxFloppyControlClient[] engines)
    : IRetroBoxFloppyControlClient
{
    private volatile int current;

    public Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(engine => engine.InsertAsync(drive, imagePath, readOnly, cancellationToken));
    }

    public Task<RetroBoxFloppyStatus> EjectAsync(int drive, CancellationToken cancellationToken = default)
    {
        return SendAsync(engine => engine.EjectAsync(drive, cancellationToken));
    }

    public Task<RetroBoxFloppyStatus> StatusAsync(int drive, CancellationToken cancellationToken = default)
    {
        return SendAsync(engine => engine.StatusAsync(drive, cancellationToken));
    }

    private async Task<RetroBoxFloppyStatus> SendAsync(Func<IRetroBoxFloppyControlClient, Task<RetroBoxFloppyStatus>> command)
    {
        var first = current;
        var unavailable = new List<string>(engines.Length);
        for (var offset = 0; offset < engines.Length; offset++)
        {
            var index = (first + offset) % engines.Length;
            try
            {
                var status = await command(engines[index]);
                current = index;
                return status;
            }
            catch (RetroBoxFloppyControlUnavailableException ex)
            {
                unavailable.Add(ex.Message);
            }
            catch (RetroBoxFloppyControlException)
            {
                // The VM answered and refused: it is the running one.
                current = index;
                throw;
            }
        }

        throw new RetroBoxFloppyControlUnavailableException(
            $"No VM is running: {string.Join("; ", unavailable)}");
    }
}
