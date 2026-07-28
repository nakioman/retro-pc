using System.CommandLine;

namespace RetroBox.Cli;

public static class CliCommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Retro PC appliance control tool.");

        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "boot",
            "Start the configured Retro PC boot flow."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "daemon",
            "Run the long-lived Retro PC hardware integration daemon."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "vm",
            "Manage Retro PC virtual machine selections."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "floppy",
            "Manage cataloged floppy images."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "nfc",
            "Read or write NFC-backed floppy labels."));
        rootCommand.Subcommands.Add(CreatePlaceholderCommand(
            "import",
            "Import external assets into Retro PC catalogs."));

        rootCommand.SetAction(_ => 0);

        return rootCommand;
    }

    private static Command CreatePlaceholderCommand(string name, string description)
    {
        var command = new Command(name, description);
        command.SetAction(_ => 0);
        return command;
    }
}
