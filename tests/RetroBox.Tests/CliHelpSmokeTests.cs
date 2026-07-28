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
            new[] { "import", "--help" },
        };

    [Theory]
    [MemberData(nameof(HelpInvocations))]
    public void Help_invocations_exit_successfully(string[] args)
    {
        var command = CliCommandFactory.CreateRootCommand();

        var exitCode = command.Parse(args).Invoke();

        Assert.Equal(0, exitCode);
    }
}
