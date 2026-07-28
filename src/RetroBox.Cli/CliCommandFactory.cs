using System.CommandLine;
using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Cli;

public sealed record RetroBoxDaemonCommandRequest(
    string ConfigRoot,
    string? FloppyControlSocketPath);

public static class CliCommandFactory
{
    public static RootCommand CreateRootCommand(Func<RetroBoxDaemonCommandRequest, int>? daemonRunner = null)
    {
        var rootCommand = new RootCommand("Retro PC appliance control tool.");

        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "boot",
            "Start the configured Retro PC boot flow."));
        rootCommand.Subcommands.Add(CreateDaemonCommand(daemonRunner));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "vm",
            "Manage Retro PC virtual machine selections."));
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
        var configRootOption = new Option<string>("--config-root")
        {
            Description = "RetroBox YAML catalog root.",
            DefaultValueFactory = _ => RetroBoxConfigStore.DefaultRootPath,
        };
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

        var configRootOption = new Option<string>("--config-root")
        {
            Description = "RetroBox YAML catalog root.",
            DefaultValueFactory = _ => RetroBoxConfigStore.DefaultRootPath,
        };
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
