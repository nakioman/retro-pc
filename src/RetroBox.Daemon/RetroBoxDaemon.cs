using RetroBox.Core;

namespace RetroBox.Daemon;

public sealed class RetroBoxDaemon(
    RetroBoxCatalogData catalog,
    IRetroBoxFloppyControlClient floppyControlClient,
    TextReader input,
    TextWriter output,
    bool echoEvents = false)
{
    public const string DefaultFloppyControlSocketPath = "/run/retrobox/86box-floppy.sock";

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var handler = new RetroBoxFloppyEventHandler(catalog, floppyControlClient);
        var exitCode = 0;

        try
        {
            while (await input.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var serialEvent = RetroBoxArduinoSerialProtocol.ParseEvent(line);
                    var result = await handler.HandleAsync(serialEvent, cancellationToken);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return exitCode;
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
