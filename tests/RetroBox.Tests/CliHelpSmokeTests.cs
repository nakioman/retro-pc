using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using RetroBox.Cli;

namespace RetroBox.Tests;

public sealed class CliHelpSmokeTests
{
    public static TheoryData<string[]> HelpInvocations =>
        new()
        {
            new[] { "--help" },
            new[] { "boot", "--help" },
            new[] { "daemon", "--help" },
            new[] { "vm", "--help" },
            new[] { "floppy", "--help" },
            new[] { "nfc", "--help" },
            new[] { "nfc", "read", "--help" },
            new[] { "nfc", "write", "--help" },
            new[] { "import", "--help" },
            new[] { "import", "floppy", "--help" },
        };

    [Theory]
    [MemberData(nameof(HelpInvocations))]
    public void Help_invocations_exit_successfully(string[] args)
    {
        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse(args).Invoke();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Daemon_invokes_configured_runner_with_socket_override()
    {
        RetroBoxDaemonCommandRequest? request = null;
        var command = CliCommandFactory.CreateRootCommand(daemonRunner: captured =>
        {
            request = captured;
            return 0;
        });

        var exitCode = command.Parse([
            "daemon",
            "--config-root",
            "/tmp/retrobox-config",
            "--floppy-control-socket",
            "/Users/nacho/Games/86Box/86box.socket",
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.NotNull(request);
        Assert.Equal("/tmp/retrobox-config", request.ConfigRoot);
        Assert.Equal("/Users/nacho/Games/86Box/86box.socket", request.FloppyControlSocketPath);
    }

    [Fact]
    public void Daemon_invokes_configured_runner_with_serial_options_and_echo()
    {
        RetroBoxDaemonCommandRequest? request = null;
        var command = CliCommandFactory.CreateRootCommand(daemonRunner: captured =>
        {
            request = captured;
            return 0;
        });

        var exitCode = command.Parse([
            "daemon",
            "--serial-port",
            "/dev/ttyUSB0",
            "--serial-baud",
            "9600",
            "--echo",
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.NotNull(request);
        Assert.Equal("/dev/ttyUSB0", request.SerialPort);
        Assert.Equal(9600, request.SerialBaud);
        Assert.True(request.Echo);
    }

    [Fact]
    public void Daemon_help_documents_serial_and_echo_options()
    {
        var output = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();
        var parseResult = command.Parse(["daemon", "--help"]);
        parseResult.InvocationConfiguration.Output = output;

        Assert.Equal(0, parseResult.Invoke());

        var help = output.ToString();
        Assert.Contains("--serial-port", help);
        Assert.Contains("--serial-baud", help);
        Assert.Contains("--echo", help);
    }

    [Fact]
    public void Daemon_starts_with_an_empty_catalog_when_the_catalog_root_is_missing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-missing", Guid.NewGuid().ToString("N"));
        var originalIn = Console.In;
        var originalError = Console.Error;
        var stderr = new StringWriter();

        try
        {
            // The real daemon action reads Console.In and writes Console.Error; redirect both so
            // the test never blocks on the host's real stdin and so the diagnostic can be checked.
            Console.SetIn(TextReader.Null);
            Console.SetError(stderr);

            var command = CliCommandFactory.CreateRootCommand();

            // A missing catalog must not cost the owner the daemon (and, with it, the web panel):
            // the command starts with an empty catalog and reports why instead of refusing to run.
            // --web-port 0 keeps this test from binding a real port on the host.
            var exitCode = command.Parse([
                "daemon",
                "--config-root",
                missingRoot,
                "--web-port",
                "0",
            ]).Invoke();

            Assert.Equal(0, exitCode);
            Assert.Contains("starting with an empty catalog", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetError(originalError);

            if (Directory.Exists(missingRoot))
            {
                Directory.Delete(missingRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Daemon_help_documents_the_web_port_option()
    {
        var output = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();
        var parseResult = command.Parse(["daemon", "--help"]);
        parseResult.InvocationConfiguration.Output = output;

        Assert.Equal(0, parseResult.Invoke());
        Assert.Contains("--web-port", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Daemon_rejects_a_web_port_outside_the_valid_range()
    {
        var error = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();
        var parseResult = command.Parse(["daemon", "--web-port", "-1"]);
        parseResult.InvocationConfiguration.Error = error;

        var exitCode = parseResult.Invoke();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--web-port", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IsRecoverableWebHostBindFailure_treats_an_IOException_as_recoverable()
    {
        // The shape Kestrel actually wraps SocketError.AddressAlreadyInUse in - the busy-port
        // path already has a live end-to-end repro; this is the unit-level guard for the type.
        Assert.True(CliCommandFactory.IsRecoverableWebHostBindFailure(
            new IOException("Failed to bind to address http://0.0.0.0:8080: address already in use.")));
    }

    [Fact]
    public void IsRecoverableWebHostBindFailure_treats_a_SocketException_as_recoverable()
    {
        // Every bind failure other than "address already in use" surfaces as a raw
        // SocketException, not an IOException - EACCES on a privileged port (--web-port 80 under
        // the appliance's unprivileged systemd user) is the concrete case that motivated this.
        Assert.True(CliCommandFactory.IsRecoverableWebHostBindFailure(
            new SocketException((int)SocketError.AccessDenied)));
    }

    [Fact]
    public void IsRecoverableWebHostBindFailure_does_not_treat_an_unrelated_exception_as_recoverable()
    {
        // A genuine programming error must still propagate and abort the daemon loudly, not be
        // swallowed as though it were a mere port conflict.
        Assert.False(CliCommandFactory.IsRecoverableWebHostBindFailure(
            new InvalidOperationException("not a bind failure")));
    }

    [Fact]
    public void Daemon_degrades_to_no_panel_when_the_web_port_is_already_in_use()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-portbusy", Guid.NewGuid().ToString("N"));
        var originalIn = Console.In;
        var originalError = Console.Error;
        var stderr = new StringWriter();

        using var occupant = new TcpListener(IPAddress.Any, 0);
        occupant.Start();
        var port = ((IPEndPoint)occupant.LocalEndpoint).Port;

        try
        {
            Console.SetIn(TextReader.Null);
            Console.SetError(stderr);

            var command = CliCommandFactory.CreateRootCommand();

            // The panel is the secondary function; the hardware loop is the primary one. An
            // occupied port must degrade to "no panel", not abort the daemon.
            var exitCode = command.Parse([
                "daemon",
                "--config-root",
                missingRoot,
                "--web-port",
                port.ToString(),
            ]).Invoke();

            Assert.Equal(0, exitCode);
            Assert.Contains("continuing without it", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetError(originalError);
            occupant.Stop();

            if (Directory.Exists(missingRoot))
            {
                Directory.Delete(missingRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Daemon_serves_the_panel_on_the_configured_web_port_and_stops_it_when_the_daemon_exits()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-webport", Guid.NewGuid().ToString("N"));
        var originalIn = Console.In;
        var originalError = Console.Error;
        var stderr = new StringWriter();
        var input = new PipeTextReader();
        var port = ReserveFreeTcpPort();

        try
        {
            Console.SetIn(input);
            Console.SetError(stderr);

            var command = CliCommandFactory.CreateRootCommand();
            var invokeTask = Task.Run(() => command.Parse([
                "daemon",
                "--config-root",
                missingRoot,
                "--web-port",
                port.ToString(),
            ]).Invoke());

            using var client = new HttpClient();
            var body = await WaitForCatalogResponse(client, $"http://127.0.0.1:{port}/api/catalog");
            Assert.Contains("\"floppies\"", body, StringComparison.Ordinal);

            input.Complete();
            var exitCode = await AwaitWithinBound(invokeTask);
            Assert.Equal(0, exitCode);

            // The host must actually be gone, not merely idle: a fresh connection attempt has to
            // fail once the daemon (and, with it, the web panel) has exited.
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetStringAsync($"http://127.0.0.1:{port}/api/catalog"));
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetError(originalError);

            if (Directory.Exists(missingRoot))
            {
                Directory.Delete(missingRoot, recursive: true);
            }
        }
    }

    private static int ReserveFreeTcpPort()
    {
        using var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task<string> WaitForCatalogResponse(HttpClient client, string url)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return await client.GetStringAsync(url);
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                await Task.Delay(20);
            }
        }

        throw new InvalidOperationException("The web panel never came up.", lastError);
    }

    private static async Task<T> AwaitWithinBound<T>(Task<T> task)
    {
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(task.IsCompleted, "The awaited task did not complete within the bound.");
        return await task;
    }

    /// <summary>Feeds lines to Console.In on demand, like a real terminal would, without a fixed sleep.</summary>
    private sealed class PipeTextReader : TextReader
    {
        private readonly Channel<string> channel =
            Channel.CreateUnbounded<string>();

        public void Complete() => channel.Writer.TryComplete();

        // Console.SetIn wraps whatever is assigned in a SyncTextReader, whose ReadLineAsync goes
        // through the synchronous ReadLine, not this type's ReadLineAsync(CancellationToken)
        // override. Both are overridden so the reader blocks correctly however it is reached.
        public override string? ReadLine()
        {
            try
            {
                return channel.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await channel.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }
}
