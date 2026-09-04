using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
public sealed class CliImportFloppyTests
{
    [Fact]
    public void Import_floppy_command_imports_with_default_mode_and_size()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "cli.img");
        File.WriteAllBytes(sourceImage, [0x24]);

        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse([
            "import",
            "floppy",
            "cli-disk",
            "--label",
            "CLI Disk",
            "--image",
            sourceImage,
            "--config-root",
            layout.ConfigRoot,
            "--scratch-root",
            layout.ScratchRoot,
            "--cataloged-root",
            layout.CatalogedRoot,
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(sourceImage));

        var data = new RetroBoxConfigStore(layout.ConfigRoot).Load();
        Assert.Equal("CLI Disk", data.Floppies["cli-disk"].Label);
        Assert.Equal("ro", data.Floppies["cli-disk"].Mode);
        Assert.Equal("1.44M", data.Floppies["cli-disk"].Size);
    }

    [Fact]
    public void Import_floppy_command_returns_error_for_failed_import()
    {
        var layout = CreateLayout();
        var outsideImage = Path.Combine(layout.Root, "outside.img");
        File.WriteAllBytes(outsideImage, []);

        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse([
            "import",
            "floppy",
            "outside",
            "--label",
            "Outside",
            "--image",
            outsideImage,
            "--config-root",
            layout.ConfigRoot,
            "--scratch-root",
            layout.ScratchRoot,
            "--cataloged-root",
            layout.CatalogedRoot,
        ]).Invoke();

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Import_floppy_command_rejects_size_aliases()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "alias.img");
        File.WriteAllBytes(sourceImage, []);

        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse([
            "import",
            "floppy",
            "alias",
            "--label",
            "Alias",
            "--image",
            sourceImage,
            "--size",
            "1440",
            "--config-root",
            layout.ConfigRoot,
            "--scratch-root",
            layout.ScratchRoot,
            "--cataloged-root",
            layout.CatalogedRoot,
        ]).Invoke();

        Assert.NotEqual(0, exitCode);
        Assert.True(File.Exists(sourceImage));
    }

    private static TestRetroBoxLayout CreateLayout()
    {
        return TestRetroBoxLayout.Create("retrobox-cli-import-tests");
    }
}
