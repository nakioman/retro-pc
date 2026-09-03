using System.Text;
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

    [Fact]
    public void ResolveSerialDeviceOptions_uses_cli_then_config_then_default_baud()
    {
        var cli = RetroBoxDaemon.ResolveSerialDeviceOptions("/dev/ttyUSB0", 9600, "/dev/ttyUSB1", 115200);
        Assert.Equal("/dev/ttyUSB0", cli!.Port);
        Assert.Equal(9600, cli.Baud);

        var config = RetroBoxDaemon.ResolveSerialDeviceOptions(null, null, "/dev/ttyUSB1", 9600);
        Assert.Equal("/dev/ttyUSB1", config!.Port);
        Assert.Equal(9600, config.Baud);

        var defaultBaud = RetroBoxDaemon.ResolveSerialDeviceOptions(null, null, "/dev/ttyUSB1", null);
        Assert.Equal(RetroBoxSerialDeviceOptions.DefaultBaud, defaultBaud!.Baud);

        Assert.Null(RetroBoxDaemon.ResolveSerialDeviceOptions(null, null, null, null));
    }

    [Fact]
    public async Task RunAsync_with_echo_client_prints_socket_payload()
    {
        var output = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            RetroBoxFloppyControlClient.CreateEcho(output),
            new StringReader(
                """
                INSERT disk1,ro
                EJECT

                """),
            output,
            echoEvents: true);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "{\"id\":\"req-1\",\"command\":\"floppy.insert\",\"params\":{\"drive\":0,\"path\":\"/data/floppies/disk1.img\",\"read_only\":true}}\n"
            + "{\"id\":\"req-2\",\"command\":\"floppy.eject\",\"params\":{\"drive\":0}}\n",
            output.ToString());
    }

    [Fact]
    public async Task RunAsync_treats_init_line_as_informational()
    {
        var client = new RecordingFloppyControlClient();
        var output = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader(
                """
                INIT 1
                INSERT disk1,ro

                """),
            output);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains("Floppy controller initialized (version 1).", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task RunAsync_reads_lines_from_serial_device_runner()
    {
        var runner = new RetroBoxSerialDeviceRunner(_ => Task.FromResult<Stream>(
            new MemoryStream(Encoding.UTF8.GetBytes("INSERT disk1,ro\nEJECT\n"))));
        using var reader = await runner.OpenReaderAsync();
        var client = new RecordingFloppyControlClient();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client,
            reader,
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
    public async Task RunAsync_cancellation_stops_reading()
    {
        var client = new RecordingFloppyControlClient();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadWriteMode),
            client,
            new BlockingTextReader(),
            new StringWriter());
        using var cancellation = new CancellationTokenSource();

        var run = daemon.RunAsync(cancellation.Token);
        cancellation.Cancel();

        Assert.Equal(0, await run);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task RunAsync_sends_status_when_socket_ready_and_applies_response()
    {
        var client = new RecordingFloppyControlClient();
        var serialOutput = new StringWriter();
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader("INSERT disk1,ro\n"),
            new StringWriter(),
            serialOutput: serialOutput,
            socketProbe: new ScriptedSocketProbe(true));

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal("STATUS\n", serialOutput.ToString());
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task RunAsync_without_serial_output_does_not_query_socket()
    {
        var client = new RecordingFloppyControlClient();
        var probe = new ScriptedSocketProbe(false);
        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader("INSERT disk1,ro\n"),
            new StringWriter(),
            socketProbe: probe);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, probe.Calls);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    [Fact]
    public async Task WatchSocketAsync_queries_status_once_per_down_to_up_transition()
    {
        var serialOutput = new StringWriter();
        using var cancellation = new CancellationTokenSource();

        var watch = RetroBoxDaemon.WatchSocketAsync(
            new ScriptedSocketProbe(false, true, true),
            serialOutput,
            TimeSpan.FromMilliseconds(5),
            cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();
        await watch;

        Assert.Equal("STATUS\n", serialOutput.ToString());
    }

    [Fact]
    public async Task WatchSocketAsync_queries_status_again_after_socket_goes_down()
    {
        var serialOutput = new StringWriter();
        using var cancellation = new CancellationTokenSource();

        var watch = RetroBoxDaemon.WatchSocketAsync(
            new ScriptedSocketProbe(true, false, true, true),
            serialOutput,
            TimeSpan.FromMilliseconds(5),
            cancellation.Token);
        await Task.Delay(100);
        cancellation.Cancel();
        await watch;

        Assert.Equal("STATUS\nSTATUS\n", serialOutput.ToString());
    }

    [Fact]
    public async Task WatchSocketAsync_does_not_probe_without_serial_output()
    {
        var probe = new ScriptedSocketProbe(true);
        using var cancellation = new CancellationTokenSource();

        var watch = RetroBoxDaemon.WatchSocketAsync(
            probe,
            null,
            TimeSpan.FromMilliseconds(5),
            cancellation.Token);
        await Task.Delay(50);
        cancellation.Cancel();
        await watch;

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task RunAsync_does_not_treat_a_command_reply_as_a_malformed_event()
    {
        var client = new RecordingFloppyControlClient();
        var output = new StringWriter();
        var router = new RetroBoxSerialLineRouter();
        var pending = router.BeginCommand();

        var daemon = new RetroBoxDaemon(
            CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode),
            client,
            new StringReader(
                """
                OK
                INSERT disk1,ro

                """),
            output,
            lineRouter: router);

        var exitCode = await daemon.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.IsType<NfcResponse.Ok>(await pending);
        Assert.Equal("insert:0:/data/floppies/disk1.img:True", Assert.Single(client.Calls));
    }

    private sealed class BlockingTextReader : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class ScriptedSocketProbe(params bool[] results) : IRetroBoxVmSocketProbe
    {
        private readonly bool[] results = results;
        private int index;

        public int Calls { get; private set; }

        public Task<bool> IsSocketReadyAsync(CancellationToken cancellationToken)
        {
            Calls++;
            var value = results.Length == 0 ? false : results[Math.Min(index++, results.Length - 1)];
            return Task.FromResult(value);
        }
    }

    private static RetroBoxCatalogData CreateCatalog(
        string floppyId,
        string imagePath,
        string mode,
        bool nfc = true)
    {
        return FloppyControlTestCatalogs.CreateCatalog(floppyId, imagePath, mode, nfc);
    }
}
