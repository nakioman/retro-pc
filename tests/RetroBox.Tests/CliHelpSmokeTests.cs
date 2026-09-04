using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using RetroBox.Cli;
using RetroBox.Daemon;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
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
            // --web-port 0 keeps this test from binding a real port on the host, and it is also
            // why exiting at stdin EOF is the right answer here: with the panel explicitly
            // disabled and no controller there is nothing left to serve. The case that must not
            // exit is a panel that is actually up - see
            // Daemon_keeps_serving_the_panel_when_the_serial_device_is_unavailable.
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

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData(null)]
    public void ResolveWebPort_disables_the_panel_for_an_unusable_value(string? value)
    {
        var originalError = Console.Error;
        var stderr = new StringWriter();

        try
        {
            Console.SetError(stderr);

            Assert.Equal(0, CliCommandFactory.ResolveWebPort(value));
            Assert.Contains("--web-port", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("8080", 8080)]
    [InlineData("65535", 65535)]
    public void ResolveWebPort_keeps_a_usable_value_and_says_nothing(string value, int expected)
    {
        var originalError = Console.Error;
        var stderr = new StringWriter();

        try
        {
            Console.SetError(stderr);

            Assert.Equal(expected, CliCommandFactory.ResolveWebPort(value));
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    public void Daemon_degrades_to_no_panel_when_the_web_port_is_unusable(string webPort)
    {
        // An unusable --web-port used to be a usage error (and, empty, an unhandled exception
        // from the option's own validator). Either way exit 1 hands the unit to
        // Restart=on-failure and crash-loops the appliance over a panel setting, with the
        // hardware loop down; WEB_PORT= in a hand-edited daemon.env is enough to trigger it.
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-badport", Guid.NewGuid().ToString("N"));
        var originalIn = Console.In;
        var originalError = Console.Error;
        var stderr = new StringWriter();

        try
        {
            Console.SetIn(TextReader.Null);
            Console.SetError(stderr);

            var command = CliCommandFactory.CreateRootCommand();
            var exitCode = command.Parse([
                "daemon",
                "--config-root",
                missingRoot,
                "--web-port",
                webPort,
            ]).Invoke();

            Assert.Equal(0, exitCode);
            Assert.Contains("continuing without the web panel", stderr.ToString(), StringComparison.Ordinal);
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

        // ReserveFreeTcpPort() has an inherent gap between reading a free port and Kestrel
        // binding it later: another process (or TIME_WAIT from an earlier test) can take the
        // port in between. The CLI now degrades a lost race to "no panel" instead of throwing
        // (see TryStartWebHost), so a lost race here surfaces as WaitForCatalogResponse never
        // seeing the panel come up, not as an exception. Retrying the whole reserve-and-start
        // cycle on that specific signal - and only that signal - keeps the test honest: a
        // genuine host failure still fails loudly on the first attempt.
        const int maxAttempts = 6;

        try
        {
            using var client = new HttpClient();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var stderr = new StringWriter();
                var input = new PipeTextReader();
                Console.SetIn(input);
                Console.SetError(stderr);

                var port = ReserveFreeTcpPort();
                var command = CliCommandFactory.CreateRootCommand();
                using var cancellation = new CancellationTokenSource();
                var invokeTask = Task.Run(() => command.Parse([
                    "daemon",
                    "--config-root",
                    missingRoot,
                    "--web-port",
                    port.ToString(),
                ]).InvokeAsync(cancellationToken: cancellation.Token));

                var poll = await WaitForCatalogResponse(client, $"http://127.0.0.1:{port}/api/catalog", stderr);

                if (poll.LostPortRace)
                {
                    cancellation.Cancel();
                    input.Complete();
                    await AwaitWithinBound(invokeTask);
                    continue;
                }

                Assert.Contains("\"floppies\"", poll.Body, StringComparison.Ordinal);

                // Shutdown comes from cancellation, not from stdin: a daemon that is serving the
                // panel deliberately outlives its input stream (see the serial-less test below).
                cancellation.Cancel();
                input.Complete();
                var exitCode = await AwaitWithinBound(invokeTask);
                Assert.Equal(0, exitCode);

                // The host must actually be gone, not merely idle: a fresh connection attempt has to
                // fail once the daemon (and, with it, the web panel) has exited.
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => client.GetStringAsync($"http://127.0.0.1:{port}/api/catalog"));
                return;
            }

            Assert.Fail($"Lost the --web-port reservation race {maxAttempts} times in a row.");
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
    public async Task Daemon_keeps_serving_the_panel_when_the_serial_device_is_unavailable()
    {
        // The guard the unit change never had. The installer writes SERIAL_DEVICE=/dev/ttyUSB0
        // even when it detected no controller, so on a controller-less appliance ExecStart opens
        // a device that is not there. The supervisor must not exit over that: it keeps retrying
        // the missing device on an interval, and the web panel keeps serving throughout.
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-noserial", Guid.NewGuid().ToString("N"));
        var missingDevice = $"/dev/retrobox-missing-{Guid.NewGuid():N}";
        var originalIn = Console.In;
        var originalError = Console.Error;
        const int maxAttempts = 6;

        try
        {
            using var client = new HttpClient();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var stderr = new StringWriter();
                Console.SetIn(TextReader.Null);
                Console.SetError(stderr);

                var port = ReserveFreeTcpPort();
                var url = $"http://127.0.0.1:{port}/api/catalog";
                var command = CliCommandFactory.CreateRootCommand();
                using var cancellation = new CancellationTokenSource();
                var invokeTask = Task.Run(() => command.Parse([
                    "daemon",
                    "--config-root",
                    missingRoot,
                    "--serial-port",
                    missingDevice,
                    "--web-port",
                    port.ToString(),
                ]).InvokeAsync(cancellationToken: cancellation.Token));

                var poll = await WaitForCatalogResponse(client, url, stderr);

                if (poll.LostPortRace)
                {
                    cancellation.Cancel();
                    await AwaitWithinBound(invokeTask);
                    continue;
                }

                Assert.Contains("\"floppies\"", poll.Body, StringComparison.Ordinal);

                // The panel can answer before the failed open is even attempted (the host starts
                // first, on purpose). Once the "unavailable" diagnostic appears the supervisor is
                // in its retry loop and keeps retrying indefinitely without exiting; a daemon that
                // is still running and still answering here is proof the missing device does not
                // take the panel down with it.
                await WaitForStderr(stderr, "Floppy controller is unavailable", invokeTask);

                Assert.False(invokeTask.IsCompleted, $"The daemon exited while the panel was serving: {stderr}");
                Assert.Contains("\"floppies\"", await client.GetStringAsync(url), StringComparison.Ordinal);

                cancellation.Cancel();
                Assert.Equal(0, await AwaitWithinBound(invokeTask));
                await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync(url));
                return;
            }

            Assert.Fail($"Lost the --web-port reservation race {maxAttempts} times in a row.");
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
    public async Task Daemon_recovers_the_serial_device_after_transient_open_failures()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "retrobox-reopen", Guid.NewGuid().ToString("N"));
        var originalIn = Console.In;
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var deviceReader = new PipeTextReader();
        var deviceWriter = new StringWriter();
        var attempts = 0;

        Console.SetIn(TextReader.Null);
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            Task<RetroBoxSerialDevice> Opener(RetroBoxSerialDeviceOptions options, CancellationToken cancellationToken)
            {
                attempts++;
                if (attempts <= 2)
                {
                    throw new RetroBoxSerialDeviceException($"attempt {attempts} failed");
                }

                deviceReader.WriteLine("INIT 1.0");
                return Task.FromResult(new RetroBoxSerialDevice(new MemoryStream(), deviceReader, deviceWriter));
            }

            var command = CliCommandFactory.CreateRootCommand(serialDeviceOpener: Opener);
            using var cancellation = new CancellationTokenSource();

            var invocation = Task.Run(() => command.Parse([
                "daemon",
                "--config-root", missingRoot,
                "--serial-port", "/dev/retrobox-reopen-test",
                "--web-port", "0",
            ]).InvokeAsync(cancellationToken: cancellation.Token));

            await WaitForStderr(stderr, "Floppy controller connected", invocation);

            // Reported once per outage, not once per failed attempt: two failed opens must leave
            // exactly one "unavailable" diagnostic behind.
            Assert.Equal(1, CountOccurrences(stderr.ToString(), "Floppy controller is unavailable"));

            await WaitForStderr(stdout, "Floppy controller initialized (version 1.0)", invocation);

            cancellation.Cancel();
            await AwaitWithinBound(invocation);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        return (haystack.Length - haystack.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;
    }

    private static int ReserveFreeTcpPort()
    {
        using var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private readonly record struct CatalogPollResult(bool LostPortRace, string Body);

    // A generous ceiling, not a fixed sleep: a healthy run returns in milliseconds and pays
    // nothing extra, but starting the CLI daemon action, bringing up Kestrel, and serving a
    // request can legitimately take much longer than a couple of seconds on a contended
    // machine (thread-pool pressure from the rest of the parallel test run, a busy host). The
    // poll still fails - with a readable message - rather than hanging forever.
    private static readonly TimeSpan CatalogPollBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExitBudget = TimeSpan.FromSeconds(30);

    // Polls for either outcome of a --web-port attempt: the panel answering, or the CLI's own
    // "Web panel could not start" diagnostic (see TryStartWebHost) showing this attempt lost the
    // port race. That exact phrase is the sentinel, not the shared "continuing without it"
    // suffix, which an unavailable serial device also prints. Anything else after the polling
    // budget is a genuine failure and throws, so a lost race is never confused with the host
    // actually failing to serve.
    private static async Task<CatalogPollResult> WaitForCatalogResponse(HttpClient client, string url, StringWriter stderr)
    {
        Exception? lastError = null;
        var deadline = DateTime.UtcNow + CatalogPollBudget;

        while (DateTime.UtcNow < deadline)
        {
            if (stderr.ToString().Contains("Web panel could not start", StringComparison.Ordinal))
            {
                return new CatalogPollResult(LostPortRace: true, Body: string.Empty);
            }

            try
            {
                var body = await client.GetStringAsync(url);
                return new CatalogPollResult(LostPortRace: false, Body: body);
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                await Task.Delay(20);
            }
        }

        throw new InvalidOperationException(
            $"The web panel never came up within {CatalogPollBudget}. stderr so far: {stderr}", lastError);
    }

    // Bounded poll for one of the daemon's stderr diagnostics: a healthy run sees it in
    // milliseconds, and a run that never prints it fails with the whole of stderr rather than
    // hanging. A daemon that has already exited can never print it, so that is reported first.
    private static async Task WaitForStderr(StringWriter stderr, string fragment, Task<int> invokeTask)
    {
        var deadline = DateTime.UtcNow + CatalogPollBudget;

        while (DateTime.UtcNow < deadline)
        {
            if (stderr.ToString().Contains(fragment, StringComparison.Ordinal))
            {
                return;
            }

            Assert.False(invokeTask.IsCompleted, $"The daemon exited before printing '{fragment}': {stderr}");
            await Task.Delay(20);
        }

        Assert.Fail($"The daemon never printed '{fragment}' within {CatalogPollBudget}. stderr so far: {stderr}");
    }

    private static async Task<T> AwaitWithinBound<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(ExitBudget));

        Assert.True(ReferenceEquals(completed, task), $"The awaited task did not complete within {ExitBudget}.");
        return await task;
    }

    /// <summary>Feeds lines to Console.In on demand, like a real terminal would, without a fixed sleep.</summary>
    private sealed class PipeTextReader : TextReader
    {
        private readonly Channel<string> channel =
            Channel.CreateUnbounded<string>();

        public void Complete() => channel.Writer.TryComplete();

        public void WriteLine(string line) => channel.Writer.TryWrite(line);

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
