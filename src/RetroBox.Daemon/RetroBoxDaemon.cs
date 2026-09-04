using RetroBox.Core;

namespace RetroBox.Daemon;

public sealed class RetroBoxDaemon(
    RetroBoxCatalogData catalog,
    IRetroBoxFloppyControlClient floppyControlClient,
    TextReader input,
    TextWriter output,
    bool echoEvents = false,
    TextWriter? serialOutput = null,
    IRetroBoxVmSocketProbe? socketProbe = null,
    RetroBoxSerialLineRouter? lineRouter = null)
{
    public const string DefaultFloppyControlSocketPath = "/run/retrobox/86box-floppy.sock";

    public static readonly TimeSpan DefaultSocketPollInterval = TimeSpan.FromSeconds(1);

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var handler = new RetroBoxFloppyEventHandler(catalog, floppyControlClient);
        var probe = socketProbe ?? new RetroBoxFloppyControlSocketProbe(floppyControlClient);
        var router = lineRouter ?? new RetroBoxSerialLineRouter();
        var exitCode = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var socketWatcher = WatchSocketAsync(probe, serialOutput, DefaultSocketPollInterval, linked.Token);

        try
        {
            while (await input.ReadLineAsync(linked.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (router.TryRoute(line))
                {
                    continue;
                }

                try
                {
                    var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent(line);
                    var result = await handler.HandleAsync(serialEvent, linked.Token);

                    if (!echoEvents
                        || result.Action is not (RetroBoxFloppyEventHandlerAction.Inserted
                            or RetroBoxFloppyEventHandlerAction.Ejected))
                    {
                        await output.WriteLineAsync(result.Message);
                    }

                    if (result.Action == RetroBoxFloppyEventHandlerAction.Failed)
                    {
                        exitCode = 1;
                    }
                }
                catch (RetroBoxArduinoSerialProtocolException ex)
                {
                    await output.WriteLineAsync(ex.Message);
                    exitCode = 1;
                }
                catch (RetroBoxFloppyControlException ex)
                {
                    await output.WriteLineAsync($"{ex.Code}: {ex.Message}");
                    exitCode = 1;
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            linked.Cancel();
            await socketWatcher;
        }

        return exitCode;
    }

    /// <summary>Re-syncs the physical floppy with the VM whenever the 86Box socket becomes available.</summary>
    internal static async Task WatchSocketAsync(
        IRetroBoxVmSocketProbe probe,
        TextWriter? serialOutput,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        if (serialOutput is null)
        {
            return;
        }

        var socketWasReady = false;
        var firstProbe = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            bool ready;
            try
            {
                ready = await probe.IsSocketReadyAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                ready = false;
            }

            if (ready && (firstProbe || !socketWasReady))
            {
                await serialOutput.WriteLineAsync(RetroBoxArduinoSerialProtocol.BuildStatusCommand());
            }

            socketWasReady = ready;
            firstProbe = false;

            try
            {
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public static string ResolveFloppyControlSocketPath(string? cliSocketPath, string? configSocketPath)
    {
        if (!string.IsNullOrWhiteSpace(cliSocketPath))
        {
            return cliSocketPath;
        }

        if (!string.IsNullOrWhiteSpace(configSocketPath))
        {
            return configSocketPath;
        }

        return DefaultFloppyControlSocketPath;
    }

    public static RetroBoxSerialDeviceOptions? ResolveSerialDeviceOptions(
        string? cliSerialPort,
        int? cliSerialBaud,
        string? configSerialPort,
        int? configSerialBaud)
    {
        var port = string.IsNullOrWhiteSpace(cliSerialPort) ? configSerialPort : cliSerialPort;
        if (string.IsNullOrWhiteSpace(port))
        {
            return null;
        }

        return new RetroBoxSerialDeviceOptions(
            port,
            cliSerialBaud ?? configSerialBaud ?? RetroBoxSerialDeviceOptions.DefaultBaud);
    }
}
