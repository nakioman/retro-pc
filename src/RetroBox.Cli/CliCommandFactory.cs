using System.CommandLine;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using RetroBox.Core;
using RetroBox.Daemon;
using RetroBox.Web;

[assembly: InternalsVisibleTo("RetroBox.Tests")]

namespace RetroBox.Cli;

public sealed record RetroBoxDaemonCommandRequest(
    string ConfigRoot,
    string? FloppyControlSocketPath,
    string? SerialPort,
    int? SerialBaud,
    bool Echo,
    int WebPort);

public sealed record RetroBoxBootCommandRequest(
    string ConfigRoot,
    string BinaryPath,
    string RomPath,
    string VmId,
    string VmPath);

public static class CliCommandFactory
{
    public static RootCommand CreateRootCommand(
        Func<RetroBoxDaemonCommandRequest, int>? daemonRunner = null,
        Func<RetroBoxBootCommandRequest, int>? bootRunner = null,
        IRetroBoxBootHotkeyDetector? hotkeyDetector = null,
        IRetroBoxBootSelectorUi? selectorUi = null,
        Func<string, IRetroBoxNfcClient>? nfcClientFactory = null,
        IBootSplash? bootSplash = null,
        Func<RetroBoxSerialDeviceOptions, CancellationToken, Task<RetroBoxSerialDevice>>? serialDeviceOpener = null)
    {
        var rootCommand = new RootCommand("Retro PC appliance control tool.");

        rootCommand.Subcommands.Add(CreateBootCommand(bootRunner, hotkeyDetector, selectorUi, bootSplash));
        rootCommand.Subcommands.Add(CreateDaemonCommand(daemonRunner, serialDeviceOpener));
        rootCommand.Subcommands.Add(CreateVmCommand());
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "floppy",
            "Manage cataloged floppy images."));
        rootCommand.Subcommands.Add(CreateNfcCommand(nfcClientFactory));
        rootCommand.Subcommands.Add(CreateImportCommand());

        rootCommand.SetAction(_ => 0);

        return rootCommand;
    }

    private static readonly TimeSpan SerialReopenInterval = TimeSpan.FromSeconds(5);

    private static Command CreateDaemonCommand(
        Func<RetroBoxDaemonCommandRequest, int>? daemonRunner,
        Func<RetroBoxSerialDeviceOptions, CancellationToken, Task<RetroBoxSerialDevice>>? serialDeviceOpener)
    {
        var configRootOption = ConfigRootOption();
        var socketPathOption = new Option<string?>("--floppy-control-socket")
        {
            Description = "86Box floppy control Unix socket path.",
        };
        var serialPortOption = new Option<string?>("--serial-port")
        {
            Description = "Floppy controller USB serial port device path.",
        };
        var serialBaudOption = new Option<int?>("--serial-baud")
        {
            Description = "Floppy controller USB serial baud rate.",
            DefaultValueFactory = _ => RetroBoxArduinoSerialProtocol.DefaultBaudRate
        };
        var echoOption = new Option<bool>("--echo")
        {
            Description = "Print the 86Box socket request each event would send instead of connecting.",
        };
        // Deliberately not Option<int>: the value reaches ExecStart from a hand-edited
        // /etc/retrobox/daemon.env, so an empty WEB_PORT= or a typo must not become a usage
        // error. Exit 1 under Restart=on-failure is an indefinite crash loop with the hardware
        // loop down over a panel setting, so the raw token is parsed in the action instead and
        // an unusable value degrades to "no panel", like every other web-panel failure.
        var webPortOption = new Option<string?>("--web-port")
        {
            Description = "Port for the web panel. 0 disables it; an unusable value disables it with a warning.",
            DefaultValueFactory = _ => RetroBoxWebOptions.DefaultPort.ToString(CultureInfo.InvariantCulture),
        };

        var command = new Command("daemon", "Run the long-lived Retro PC hardware integration daemon.")
        {
            configRootOption,
            socketPathOption,
            serialPortOption,
            serialBaudOption,
            echoOption,
            webPortOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var request = new RetroBoxDaemonCommandRequest(
                parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath,
                parseResult.GetValue(socketPathOption),
                parseResult.GetValue(serialPortOption),
                parseResult.GetValue(serialBaudOption),
                parseResult.GetValue(echoOption),
                ResolveWebPort(parseResult.GetValue(webPortOption)));

            if (daemonRunner is not null)
            {
                return daemonRunner(request);
            }

            try
            {
                var store = new RetroBoxConfigStore(request.ConfigRoot);
                RetroBoxCatalogData initial;
                string? startupError = null;

                try
                {
                    initial = store.Load();
                }
                catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
                {
                    // A malformed or unreadable catalog must not cost the owner the panel as
                    // well. Without it the only way back into the appliance is the GRUB recovery
                    // entry, so the daemon starts with an empty catalog and reports why.
                    initial = RetroBoxCatalogData.Empty;
                    startupError = ex.Message;
                    Console.Error.WriteLine($"Catalog is invalid; starting with an empty catalog: {ex.Message}");
                }

                using var catalogSource = new RetroBoxWatchingCatalogSource(
                    request.ConfigRoot,
                    initial,
                    message => Console.Error.WriteLine(message),
                    initialError: startupError);

                var socketPath = RetroBoxDaemon.ResolveFloppyControlSocketPath(
                    request.FloppyControlSocketPath,
                    catalogSource.Current.Config.FloppyControlSocketPath);
                var serialOptions = RetroBoxDaemon.ResolveSerialDeviceOptions(
                    request.SerialPort,
                    request.SerialBaud,
                    catalogSource.Current.Config.SerialPort,
                    catalogSource.Current.Config.SerialBaud);

                IRetroBoxFloppyControlClient client = request.Echo
                    ? RetroBoxFloppyControlClient.CreateEcho(Console.Out)
                    : new RetroBoxFloppyControlClient(socketPath);

                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };

                // The tracker is built once and lives for the whole process, outlying any single
                // serial connection; the holder is the stable indirection the panel is handed
                // instead of a channel bound to a device that may be unplugged.
                var driveState = new RetroBoxDriveStateTracker();
                var channelHolder = new RetroBoxNfcChannelHolder();

                // The panel starts before the controller and is torn down after it. Starting
                // first is what keeps a controller-less appliance serving: the installer writes a
                // --serial-port even when it detected no controller, so opening the device first
                // would abort before the panel ever bound its port.
                var webHost = await TryStartWebHost(
                    request.WebPort, request.ConfigRoot, catalogSource, driveState, channelHolder,
                    cancellation.Token);

                try
                {
                    return await SuperviseSerialDeviceAsync(
                        catalogSource, client, request, serialOptions, webHost is not null, driveState,
                        channelHolder, serialDeviceOpener ?? OpenSerialDeviceAsync, cancellation.Token);
                }
                finally
                {
                    if (webHost is not null)
                    {
                        await webHost.DisposeAsync();
                    }
                }
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or ArgumentException or IOException
                or UnauthorizedAccessException or RetroBoxSerialDeviceException)
            {
                return WriteError(parseResult, ex);
            }
        });

        return command;
    }

    // Internal and tested directly: every unusable spelling of WEB_PORT has to reach the same
    // answer, and driving each one through a live daemon invocation would say nothing more.
    internal static int ResolveWebPort(string? value)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture, out var port) && port is >= 0 and <= 65535)
        {
            return port;
        }

        Console.Error.WriteLine(
            $"--web-port must be between 0 and 65535, was '{value}'; continuing without the web panel.");
        return 0;
    }

    private static Task<RetroBoxSerialDevice> OpenSerialDeviceAsync(
        RetroBoxSerialDeviceOptions options, CancellationToken cancellationToken)
    {
        return new RetroBoxSerialDeviceRunner(options.Port, options.Baud).OpenAsync(cancellationToken);
    }

    // Internal and driven directly by a test: the drive tracker and the channel holder are
    // process-lifetime objects this loop is the sole owner of, and a full daemon invocation
    // offers no handle on either.
    internal static async Task<int> SuperviseSerialDeviceAsync(
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxFloppyControlClient client,
        RetroBoxDaemonCommandRequest request,
        RetroBoxSerialDeviceOptions? serialOptions,
        bool panelIsRunning,
        RetroBoxDriveStateTracker driveState,
        RetroBoxNfcChannelHolder channelHolder,
        Func<RetroBoxSerialDeviceOptions, CancellationToken, Task<RetroBoxSerialDevice>> openDevice,
        CancellationToken cancellationToken)
    {
        if (serialOptions is null)
        {
            // No controller configured at all: read events from stdin, which is what --echo and
            // piping events by hand rely on. Under systemd stdin is /dev/null, so this returns at
            // once and the panel, if any, keeps the process alive below.
            var exitCode = await new RetroBoxDaemon(
                catalogSource, client, Console.In, Console.Out, request.Echo, driveState: driveState)
                .RunAsync(cancellationToken);

            if (panelIsRunning)
            {
                await WaitForCancellation(cancellationToken);
            }

            return exitCode;
        }

        // The reader wraps every failure into RetroBoxSerialDeviceException, but the writer does
        // not: a write to a vanished port (the socket watcher's STATUS poll, in particular)
        // surfaces a raw IOException, and so can Dispose() on a device whose port disappeared
        // underneath it. Both must be caught here, or an unplug first observed by a write instead
        // of a read still takes the whole daemon down - exactly what this loop exists to prevent.
        var reportedUnavailable = false;
        var reportedConnected = false;
        var lastExitCode = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            RetroBoxSerialDevice device;

            try
            {
                device = await openDevice(serialOptions, cancellationToken);
            }
            catch (RetroBoxSerialDeviceException ex)
            {
                reportedConnected = false;

                // Reported once per outage, not once per attempt: the installer writes a
                // --serial-port even with no controller detected, so an appliance that never had
                // one would otherwise fill the journal.
                if (!reportedUnavailable)
                {
                    reportedUnavailable = true;
                    Console.Error.WriteLine(
                        $"Floppy controller is unavailable, retrying every {SerialReopenInterval.TotalSeconds:0}s: {ex.Message}");
                }

                if (!await DelayAsync(SerialReopenInterval, cancellationToken))
                {
                    break;
                }

                continue;
            }

            reportedUnavailable = false;

            // Reported once per connection, not once per iteration: a device that opens fine but
            // reads EOF immediately (a half-dead adapter, a stale node during udev churn) would
            // otherwise have this line printed again every retry interval, forever.
            if (!reportedConnected)
            {
                reportedConnected = true;
                Console.Error.WriteLine("Floppy controller connected.");
            }

            // A channel holds the writer of one open device, so router and channel are rebuilt
            // per connection while driveState, the single tracker built before the loop, is not.
            var router = new RetroBoxSerialLineRouter();
            var channel = new RetroBoxSerialNfcCommandChannel(router, device.Writer);
            channelHolder.Set(channel);

            try
            {
                try
                {
                    lastExitCode = await new RetroBoxDaemon(
                        catalogSource,
                        client,
                        device.Reader,
                        Console.Out,
                        request.Echo,
                        device.Writer,
                        socketProbe: null,
                        lineRouter: router,
                        driveState: driveState,
                        nfcChannel: channel).RunAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is RetroBoxSerialDeviceException or IOException)
                {
                    reportedConnected = false;
                    Console.Error.WriteLine($"Floppy controller went away: {ex.Message}");
                }
            }
            finally
            {
                // Both are cleared here, before the device is even disposed, so the drive
                // endpoints report unavailable the moment a controller goes away rather than
                // timing out against a dead writer. The tracker has to be reset alongside the
                // holder, not instead of it: RetroBoxDriveEndpoints answers a Loaded tracker
                // without ever touching the channel, so a disk that was seated at unplug time
                // would otherwise keep /api/drive reporting "loaded" for the whole outage. Only
                // INIT resets the tracker otherwise, and a controller that is gone sends none.
                driveState.Reset();
                channelHolder.Set(null);

                try
                {
                    device.Dispose();
                }
                catch (Exception ex) when (ex is RetroBoxSerialDeviceException or IOException)
                {
                    reportedConnected = false;
                    Console.Error.WriteLine($"Floppy controller went away while closing: {ex.Message}");
                }
            }

            if (!await DelayAsync(SerialReopenInterval, cancellationToken))
            {
                break;
            }
        }

        return lastExitCode;
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitForCancellation(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<RetroBoxWebHost?> TryStartWebHost(
        int port,
        string configRoot,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxDriveState driveState,
        IRetroBoxNfcCommandChannel nfcChannel,
        CancellationToken cancellationToken)
    {
        if (port == 0)
        {
            return null;
        }

        try
        {
            return await RetroBoxWebHost.StartAsync(
                new RetroBoxWebOptions { Port = port, ConfigRoot = configRoot },
                catalogSource,
                cancellationToken,
                driveState,
                nfcChannel);
        }
        catch (Exception ex) when (IsRecoverableWebHostBindFailure(ex))
        {
            // The panel is the secondary function; the hardware loop is the primary one. A busy
            // or forbidden port (EACCES on a privileged port, for instance) must degrade to "no
            // panel", exactly like an unreadable catalog degrades to "empty catalog" a few lines
            // up. A genuine programming error is not one of these two shapes and still propagates.
            Console.Error.WriteLine($"Web panel could not start on port {port}, continuing without it: {ex.Message}");
            return null;
        }
    }

    // Kestrel wraps only SocketError.AddressAlreadyInUse in an IOException; every other bind
    // failure (EACCES on a privileged port, AddressNotAvailable, ...) surfaces as a raw
    // SocketException. Both must be caught for a busy or forbidden --web-port to degrade to "no
    // panel" instead of aborting the daemon; internal and tested directly since a live repro of
    // every bind-failure shape (EACCES in particular) depends on the test process's privileges.
    internal static bool IsRecoverableWebHostBindFailure(Exception ex) => ex is IOException or SocketException;

    private static Command CreatePlaceholderCommand(string name, string description)
    {
        var command = new Command(name, description);
        command.SetAction(_ => 0);
        return command;
    }

    private static Command CreateVmCommand()
    {
        var command = new Command("vm", "Manage Retro PC virtual machine selections.");
        var configRootOption = ConfigRootOption();
        var defaultVmArgument = new Argument<string?>("id")
        {
            Description = "VM ID to make the new default.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var defaultCommand = new Command("default", "Show or change the default VM.")
        {
            defaultVmArgument,
            configRootOption,
        };
        defaultCommand.SetAction(parseResult =>
        {
            try
            {
                var selection = new RetroBoxVmSelection(new RetroBoxConfigStore(
                    parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath));
                var vmId = parseResult.GetValue(defaultVmArgument);
                if (vmId is null)
                {
                    parseResult.InvocationConfiguration.Output.WriteLine(selection.GetDefaultVmId());
                }
                else
                {
                    selection.SetDefaultVm(vmId);
                }

                return 0;
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });
        command.Subcommands.Add(CreateVmListCommand());
        command.Subcommands.Add(defaultCommand);
        command.SetAction(_ => 0);
        return command;
    }

    private static Command CreateVmListCommand()
    {
        var configRootOption = ConfigRootOption();
        var command = new Command("list", "List cataloged virtual machines.") { configRootOption };
        command.SetAction(parseResult =>
        {
            try
            {
                var selection = new RetroBoxVmSelection(new RetroBoxConfigStore(
                    parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath));
                foreach (var (id, vm) in selection.List())
                {
                    parseResult.InvocationConfiguration.Output.WriteLine($"{id}\t{vm.Label}");
                }

                return 0;
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });
        return command;
    }

    private static Command CreateBootCommand(
        Func<RetroBoxBootCommandRequest, int>? bootRunner,
        IRetroBoxBootHotkeyDetector? hotkeyDetector,
        IRetroBoxBootSelectorUi? selectorUi,
        IBootSplash? bootSplash)
    {
        var configRootOption = ConfigRootOption();
        var binaryOption = new Option<string>("--binary")
        {
            Description = "86Box executable path.",
            DefaultValueFactory = _ => RetroBoxBoot.DefaultBinaryPath,
        };
        var romPathOption = new Option<string>("--rompath")
        {
            Description = "86Box ROM directory.",
            DefaultValueFactory = _ => RetroBoxBoot.DefaultRomPath,
        };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Show the VM without starting 86Box." };
        var selectorOption = new Option<bool>("--selector") { Description = "Open the VM selector immediately." };
        var selectOption = new Option<string?>("--select") { Description = "Run this VM without changing the default." };
        var command = new Command("boot", "Start the configured VM, using F12 to open the selector.")
        {
            configRootOption,
            binaryOption,
            romPathOption,
            dryRunOption,
            selectorOption,
            selectOption,
        };
        command.SetAction(parseResult =>
        {
            var splash = bootSplash ?? new PlymouthBootSplash();
            try
            {
                var configRoot = parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath;
                var store = new RetroBoxConfigStore(configRoot);
                var dryRun = parseResult.GetValue(dryRunOption);
                var selectorRequested = parseResult.GetValue(selectorOption);
                var explicitVmId = parseResult.GetValue(selectOption);
                var ui = new SplashQuittingSelectorUi(selectorUi ?? new RetroBoxConsoleSelector(), splash);
                var bootSelector = new RetroBoxBootSelector(store, ui);
                var firstPass = true;

                while (true)
                {
                    if (firstPass && !dryRun && explicitVmId is null && !selectorRequested)
                    {
                        selectorRequested = (hotkeyDetector ?? new RetroBoxBootHotkeyDetector(
                            new RetroBoxConsoleInput(), new RetroBoxBootClock())).IsSelectorRequested();
                    }

                    var catalog = store.Load();
                    var selection = bootSelector.Resolve(
                        catalog,
                        explicitVmId,
                        selectorRequested,
                        persistDefault: !dryRun,
                        quitOnCancel: !firstPass);
                    if (selection.Action == RetroBoxBootSelectionAction.Cancel)
                    {
                        return 0;
                    }

                    var vmId = selection.VmId
                        ?? throw new RetroBoxCatalogException("No VM was selected.");
                    var vm = catalog.Vms[vmId];
                    if (!Directory.Exists(vm.Path))
                    {
                        throw new RetroBoxCatalogException($"VM '{vmId}' profile directory '{vm.Path}' does not exist.");
                    }

                    var configPath = Path.Combine(vm.Path, "86box.cfg");
                    if (!File.Exists(configPath))
                    {
                        throw new RetroBoxCatalogException($"VM '{vmId}' profile is invalid: '{configPath}' does not exist.");
                    }
                    var request = new RetroBoxBootCommandRequest(
                        configRoot,
                        parseResult.GetValue(binaryOption) ?? RetroBoxBoot.DefaultBinaryPath,
                        parseResult.GetValue(romPathOption) ?? RetroBoxBoot.DefaultRomPath,
                        vmId,
                        vm.Path);

                    if (dryRun)
                    {
                        parseResult.InvocationConfiguration.Output.WriteLine($"{request.VmId}\t{vm.Label}\t{request.VmPath}");
                        return 0;
                    }

                    splash.Cover();
                    var exitCode = bootRunner is null
                        ? RetroBoxBoot.Run(new RetroBoxBootRequest(request.BinaryPath, request.VmId, request.VmPath, request.RomPath))
                        : bootRunner(request);

                    if (explicitVmId is not null)
                    {
                        return exitCode;
                    }

                    firstPass = false;
                    explicitVmId = null;
                    selectorRequested = true;
                }
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                splash.Quit();
                return WriteError(parseResult, ex);
            }
        });
        return command;
    }

    private static Option<string> ConfigRootOption()
    {
        return new Option<string>("--config-root")
        {
            Description = "RetroBox YAML catalog root.",
            DefaultValueFactory = _ => RetroBoxConfigStore.DefaultRootPath,
        };
    }

    private static Command CreateImportCommand()
    {
        var command = new Command("import", "Import external assets into Retro PC catalogs.");
        command.Subcommands.Add(CreateImportFloppyCommand());
        command.SetAction(_ => 0);
        return command;
    }

    private static Command CreateImportFloppyCommand()
    {
        var idArgument = new Argument<string>("id")
        {
            Description = "Catalog ID to register for the floppy image.",
        };
        var labelOption = new Option<string>("--label")
        {
            Description = "Human-readable label for the floppy image.",
            Required = true,
        };
        var imageOption = new Option<string>("--image")
        {
            Description = "Source image path under the scratch root.",
            Required = true,
        };
        var modeOption = new Option<string>("--mode")
        {
            Description = "Floppy access mode.",
            DefaultValueFactory = _ => RetroBoxFloppyCatalogRules.ReadOnlyMode,
        };
        modeOption.AcceptOnlyFromAmong(RetroBoxFloppyCatalogRules.ValidModes);

        var sizeOption = new Option<string>("--size")
        {
            Description = "Floppy size.",
            DefaultValueFactory = _ => RetroBoxFloppyCatalogRules.DefaultImportSize,
        };
        sizeOption.AcceptOnlyFromAmong(RetroBoxFloppyCatalogRules.ValidSizes);

        var configRootOption = ConfigRootOption();
        var scratchRootOption = new Option<string>("--scratch-root")
        {
            Description = "Scratch directory that contains source floppy images.",
            DefaultValueFactory = _ => RetroBoxFloppyImporter.DefaultScratchRoot,
        };
        var catalogedRootOption = new Option<string>("--cataloged-root")
        {
            Description = "Cataloged floppy image directory.",
            DefaultValueFactory = _ => RetroBoxFloppyImporter.DefaultCatalogedRoot,
        };

        var command = new Command("floppy", "Import a floppy image from scratch into the catalog.")
        {
            idArgument,
            labelOption,
            imageOption,
            modeOption,
            sizeOption,
            configRootOption,
            scratchRootOption,
            catalogedRootOption,
        };

        command.SetAction(parseResult =>
        {
            try
            {
                var importer = new RetroBoxFloppyImporter();
                importer.Import(new RetroBoxFloppyImportRequest
                {
                    Id = parseResult.GetValue(idArgument) ?? string.Empty,
                    Label = parseResult.GetValue(labelOption) ?? string.Empty,
                    ImagePath = parseResult.GetValue(imageOption) ?? string.Empty,
                    Mode = parseResult.GetValue(modeOption) ?? RetroBoxFloppyCatalogRules.ReadOnlyMode,
                    Size = parseResult.GetValue(sizeOption) ?? RetroBoxFloppyCatalogRules.DefaultImportSize,
                    ConfigRoot = parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath,
                    ScratchRoot = parseResult.GetValue(scratchRootOption) ?? RetroBoxFloppyImporter.DefaultScratchRoot,
                    CatalogedRoot = parseResult.GetValue(catalogedRootOption) ?? RetroBoxFloppyImporter.DefaultCatalogedRoot,
                });

                return 0;
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });

        return command;
    }

    private static Command CreateNfcCommand(Func<string, IRetroBoxNfcClient>? nfcClientFactory)
    {
        var portOption = new Option<string>("--port")
        {
            Description = "Serial port for the NFC reader/writer.",
            Required = true,
        };

        var readCommand = new Command("read", "Check NFC connectivity via PING/PONG.")
        {
            portOption,
        };
        readCommand.SetAction(parseResult =>
        {
            try
            {
                var port = parseResult.GetValue(portOption) ?? string.Empty;
                var client = nfcClientFactory?.Invoke(port)
                    ?? new RetroBoxNfcSerialClient(port);
                var response = client.PingAsync().GetAwaiter().GetResult();

                if (response is NfcResponse.Pong)
                {
                    parseResult.InvocationConfiguration.Output.WriteLine(
                        $"NFC reader on {port} is alive.");
                    return 0;
                }

                parseResult.InvocationConfiguration.Output.WriteLine(
                    $"NFC reader on {port} is dead.");
                return 1;
            }
            catch (Exception ex) when (ex is NfcPortUnavailable or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });

        var writePortOption = new Option<string>("--port")
        {
            Description = "Serial port for the NFC reader/writer.",
            Required = true,
        };
        var configRootOption = ConfigRootOption();
        var idArgument = new Argument<string>("id")
        {
            Description = "Cataloged floppy ID to write to the NFC tag.",
        };

        var writeCommand = new Command("write", "Write a cataloged floppy label to an NFC tag.")
        {
            idArgument,
            writePortOption,
            configRootOption,
        };
        writeCommand.SetAction(parseResult =>
        {
            try
            {
                var id = parseResult.GetValue(idArgument) ?? string.Empty;
                var port = parseResult.GetValue(writePortOption) ?? string.Empty;
                var configRoot = parseResult.GetValue(configRootOption)
                    ?? RetroBoxConfigStore.DefaultRootPath;

                var client = nfcClientFactory?.Invoke(port)
                    ?? new RetroBoxNfcSerialClient(port);
                var store = new RetroBoxConfigStore(configRoot);
                var writer = new RetroBoxNfcWriter(client, store);
                var result = writer.WriteAsync(id).GetAwaiter().GetResult();

                switch (result)
                {
                    case NfcWriteResult.Written:
                        parseResult.InvocationConfiguration.Output.WriteLine(
                            $"{id} written (nfc: true)");
                        return 0;
                    case NfcWriteResult.NotCataloged notCataloged:
                        parseResult.InvocationConfiguration.Error.WriteLine(
                            $"Floppy '{notCataloged.Id}' is not cataloged.");
                        return 1;
                    case NfcWriteResult.WriteFailed writeFailed:
                        parseResult.InvocationConfiguration.Error.WriteLine(
                            $"NFC write failed: {writeFailed.Message}");
                        return 1;
                    default:
                        parseResult.InvocationConfiguration.Error.WriteLine(
                            $"Unexpected NFC write result: {result.GetType().Name}");
                        return 1;
                }
            }
            catch (Exception ex) when (ex is NfcPortUnavailable or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });

        var command = new Command("nfc", "Read or write NFC-backed floppy labels.")
        {
            readCommand,
            writeCommand,
        };
        command.SetAction(_ => 0);

        return command;
    }

    private static int WriteError(ParseResult parseResult, Exception ex)
    {
        parseResult.InvocationConfiguration.Error.WriteLine(ex.Message);
        return 1;
    }
}
