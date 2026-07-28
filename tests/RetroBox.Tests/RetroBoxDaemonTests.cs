using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxDaemonTests
{
    [Fact]
    public async Task RunAsync_handles_stdin_lines_until_end()
    {
        var client = new RecordingFloppyControlClient();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client,
            new StringReader(
                """
                INSERT disk1,ro
                EJECT

                """),
            new StringWriter());

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "insert:0:/data/floppies/disk1.img:True",
                "eject:0",
            ],
            client.Calls);
    }

    [Fact]
    public async Task RunAsync_reports_malformed_line_and_continues()
    {
        var client = new RecordingFloppyControlClient();
        var output = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader(
                """
                BROKEN
                INSERT disk1,ro

                """),
            output);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains("Malformed Arduino serial event 'BROKEN'", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task RunAsync_reports_floppy_control_errors_without_crashing()
    {
        var client = new RecordingFloppyControlClient
        {
            InsertError = new RetroBoxFloppyControlException("missing_image", "Image is missing."),
        };
        var output = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/missing/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader("INSERT disk1,ro"),
            output);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains("missing_image", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Image is missing.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_marks_failed_handler_result_and_continues()
    {
        var client = new RecordingFloppyControlClient();
        var output = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader(
                """
                INSERT missing,ro
                EJECT

                """),
            output);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown floppy 'missing'", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("eject:0", Assert.Single(client.Calls));
    }

    [Fact]
    public void ResolveFloppyControlSocketPath_uses_cli_then_config_then_default()
    {
        Assert.Equal(
            "/cli/socket",
            RetroBoxDaemon.ResolveFloppyControlSocketPath("/cli/socket", "/config/socket"));
        Assert.Equal(
            "/config/socket",
            RetroBoxDaemon.ResolveFloppyControlSocketPath(null, "/config/socket"));
        Assert.Equal(
            RetroBoxDaemon.DefaultFloppyControlSocketPath,
            RetroBoxDaemon.ResolveFloppyControlSocketPath(null, null));
    }

    private static RetroBoxCatalogData CreateCatalog(string floppyId, string imagePath, string mode)
    {
        return FloppyControlTestCatalogs.CreateCatalog(floppyId, imagePath, mode);
    }
}
