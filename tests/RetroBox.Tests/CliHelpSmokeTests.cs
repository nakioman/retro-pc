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
    public void Daemon_returns_failure_for_missing_catalog_root()
    {
        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse([
            "daemon",
            "--config-root",
            Path.Combine(Path.GetTempPath(), "retrobox-missing", Guid.NewGuid().ToString("N")),
        ]).Invoke();

        Assert.Equal(1, exitCode);
    }
}
