using System.Text.Json;

namespace RetroBox.Core;

/// <summary>
/// Floppy control for QEMU VMs over QMP, on the A: drive's BlockBackend (<c>floppy0</c>, from
/// <c>-drive if=floppy,index=0</c> in the profile). QEMU serves one QMP client at a time and the
/// CD refresh needs the monitor too, so every command opens its own short connection.
/// </summary>
public sealed class RetroBoxQmpFloppyControlClient(RetroBoxQmpClient qmpClient, TimeSpan? commandTimeout = null)
    : IRetroBoxFloppyControlClient
{
    public const string FloppyDeviceId = "floppy0";

    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan commandTimeout = commandTimeout ?? DefaultCommandTimeout;

    public Task<RetroBoxFloppyStatus> InsertAsync(
        int drive,
        string imagePath,
        bool readOnly,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            drive,
            changed: true,
            (connection, token) => connection.ChangeMediumAsync(FloppyDeviceId, imagePath, "raw", readOnly, token),
            cancellationToken);
    }

    public Task<RetroBoxFloppyStatus> EjectAsync(int drive, CancellationToken cancellationToken = default)
    {
        // Forced, like pulling a real disk: the NFC floppy has physically left the drive.
        return RunAsync(
            drive,
            changed: true,
            (connection, token) => connection.EjectAsync(FloppyDeviceId, force: true, token),
            cancellationToken);
    }

    public Task<RetroBoxFloppyStatus> StatusAsync(int drive, CancellationToken cancellationToken = default)
    {
        return RunAsync(drive, changed: false, null, cancellationToken);
    }

    private async Task<RetroBoxFloppyStatus> RunAsync(
        int drive,
        bool changed,
        Func<RetroBoxQmpConnection, CancellationToken, Task>? command,
        CancellationToken cancellationToken)
    {
        if (drive != 0)
        {
            throw new RetroBoxFloppyControlException(
                "invalid_drive",
                $"QEMU VMs have one floppy drive (0); drive {drive} does not exist.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(commandTimeout);
        try
        {
            await using var connection = await qmpClient.ConnectAsync(timeout.Token);
            if (command is not null)
            {
                await command(connection, timeout.Token);
            }

            var blocks = await connection.ExecuteAsync("query-block", cancellationToken: timeout.Token);
            return ReadStatus(blocks, changed);
        }
        catch (RetroBoxQmpUnavailableException ex)
        {
            throw new RetroBoxFloppyControlUnavailableException(ex.Message);
        }
        catch (RetroBoxQmpException ex) when (ex.ErrorClass == "Disconnected")
        {
            throw new RetroBoxFloppyControlUnavailableException($"QEMU QMP socket is unavailable: {ex.Message}");
        }
        catch (RetroBoxQmpException ex)
        {
            throw new RetroBoxFloppyControlException(ErrorCode(ex, command), $"QEMU QMP: {ex.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RetroBoxFloppyControlUnavailableException(
                $"QEMU QMP socket did not answer within {commandTimeout.TotalSeconds:0.#} s.");
        }
    }

    private static string ErrorCode(RetroBoxQmpException ex, Func<RetroBoxQmpConnection, CancellationToken, Task>? command) =>
        ex.ErrorClass switch
        {
            "DeviceNotFound" => "invalid_drive",
            // blockdev-change-medium reports an image it cannot open as a GenericError.
            "GenericError" when command is not null && ex.Message.StartsWith("Could not open", StringComparison.Ordinal) => "missing_image",
            _ => "internal_failure",
        };

    private static RetroBoxFloppyStatus ReadStatus(JsonElement blocks, bool changed)
    {
        if (blocks.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.TryGetProperty("device", out var device) && device.GetString() == FloppyDeviceId)
                {
                    // QMP has no "drive busy" flag; changing the medium is always accepted.
                    return block.TryGetProperty("inserted", out var inserted)
                        ? new RetroBoxFloppyStatus(
                            0,
                            true,
                            inserted.TryGetProperty("file", out var file) ? file.GetString() : null,
                            inserted.TryGetProperty("ro", out var ro) && ro.ValueKind == JsonValueKind.True,
                            false,
                            changed)
                        : new RetroBoxFloppyStatus(0, false, null, false, false, changed);
                }
            }
        }

        throw new RetroBoxFloppyControlException(
            "invalid_drive",
            $"The QEMU VM has no floppy drive ({FloppyDeviceId}).");
    }
}
