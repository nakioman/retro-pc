using System.CommandLine;
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Cli;

public sealed record RetroBoxDaemonCommandRequest(
    string ConfigRoot,
    string? FloppyControlSocketPath);

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
        Func<RetroBoxBootCommandRequest, int>? bootRunner = null)
    {
        var rootCommand = new RootCommand("Retro PC appliance control tool.");

        rootCommand.Subcommands.Add(CreateBootCommand(bootRunner));
        rootCommand.Subcommands.Add(CreateDaemonCommand(daemonRunner));
        rootCommand.Subcommands.Add(CreateVmCommand());
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "floppy",
            "Manage cataloged floppy images."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "nfc",
            "Read or write NFC-backed floppy labels."));
        rootCommand.Subcommands.Add(CreateImportCommand());

        rootCommand.SetAction(_ => 0);

        return rootCommand;
    }

    private static Command CreateDaemonCommand(Func<RetroBoxDaemonCommandRequest, int>? daemonRunner)
    {
        var configRootOption = ConfigRootOption();
        var socketPathOption = new Option<string?>("--floppy-control-socket")
        {
            Description = "86Box floppy control Unix socket path.",
        };

        var command = new Command("daemon", "Run the long-lived Retro PC hardware integration daemon.")
        {
            configRootOption,
            socketPathOption,
        };

        command.SetAction(parseResult =>
        {
            var request = new RetroBoxDaemonCommandRequest(
                parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath,
                parseResult.GetValue(socketPathOption));

            if (daemonRunner is not null)
            {
                return daemonRunner(request);
            }

            try
            {
                var catalog = new RetroBoxConfigStore(request.ConfigRoot).Load();
                var socketPath = RetroBoxDaemon.ResolveFloppyControlSocketPath(
                    request.FloppyControlSocketPath,
                    catalog.Config.FloppyControlSocketPath);
                var daemon = new RetroBoxDaemon(
                    catalog,
                    new RetroBoxFloppyControlClient(socketPath),
                    Console.In,
                    Console.Out);

                return daemon.RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                return WriteError(parseResult, ex);
            }
        });

        return command;
    }

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

    private static Command CreateBootCommand(Func<RetroBoxBootCommandRequest, int>? bootRunner)
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
        var command = new Command("boot", "Start the configured default VM.")
        {
            configRootOption,
            binaryOption,
            romPathOption,
            dryRunOption,
        };
        command.SetAction(parseResult =>
        {
            try
            {
                var configRoot = parseResult.GetValue(configRootOption) ?? RetroBoxConfigStore.DefaultRootPath;
                var catalog = new RetroBoxConfigStore(configRoot).Load();
                var vmId = catalog.Config.DefaultVm;
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

                if (parseResult.GetValue(dryRunOption))
                {
                    parseResult.InvocationConfiguration.Output.WriteLine($"{request.VmId}\t{vm.Label}\t{request.VmPath}");
                    return 0;
                }

                return bootRunner is null
                    ? RetroBoxBoot.Run(new RetroBoxBootRequest(request.BinaryPath, request.VmId, request.VmPath, request.RomPath))
                    : bootRunner(request);
            }
            catch (Exception ex) when (ex is RetroBoxCatalogException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
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

    private static int WriteError(ParseResult parseResult, Exception ex)
    {
        parseResult.InvocationConfiguration.Error.WriteLine(ex.Message);
        return 1;
    }
}
