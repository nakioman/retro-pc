using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
public sealed class CliNfcCommandTests
{
    [Fact]
    public void Read_reports_alive_and_exits_zero_when_device_responds_pong()
    {
        var client = new RecordingNfcClient
        {
            PingResponse = new NfcResponse.Pong(),
        };
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse(["nfc", "read", "--port", "/dev/ttyUSB0"]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(0, exit);
        Assert.Contains("alive", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_reports_dead_and_exits_nonzero_when_device_does_not_respond_pong()
    {
        var client = new RecordingNfcClient
        {
            PingResponse = new NfcResponse.Unknown("PONGER"),
        };
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse(["nfc", "read", "--port", "/dev/ttyUSB0"]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(1, exit);
        Assert.Contains("dead", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_reports_actionable_error_when_port_is_unavailable()
    {
        var client = new RecordingNfcClient
        {
            ThrowOnCall = new NfcPortUnavailable("port busy"),
        };
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse(["nfc", "read", "--port", "/dev/ttyUSB0"]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(1, exit);
        Assert.Contains("port busy", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_succeeds_when_device_responds_ok()
    {
        var client = new RecordingNfcClient
        {
            WriteResponse = new NfcResponse.Ok(),
        };
        var root = CreateValidCatalogRoot("ro");
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse([
            "nfc", "write", "monkey1-disk1",
            "--port", "/dev/ttyUSB0",
            "--config-root", root,
        ]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(0, exit);
        Assert.Contains("written", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nfc: true", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_reports_not_cataloged_for_unknown_id()
    {
        var client = new RecordingNfcClient();
        var root = CreateValidCatalogRoot("ro");
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse([
            "nfc", "write", "unknown-disk",
            "--port", "/dev/ttyUSB0",
            "--config-root", root,
        ]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(1, exit);
        Assert.Contains("unknown-disk", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public void Write_reports_firmware_error()
    {
        var client = new RecordingNfcClient
        {
            WriteResponse = new NfcResponse.Error("tag not detected"),
        };
        var root = CreateValidCatalogRoot("ro");
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var parseResult = command.Parse([
            "nfc", "write", "monkey1-disk1",
            "--port", "/dev/ttyUSB0",
            "--config-root", root,
        ]);
        parseResult.InvocationConfiguration.Output = stdout;
        parseResult.InvocationConfiguration.Error = stderr;
        var exit = parseResult.Invoke();

        Assert.Equal(1, exit);
        Assert.Contains("tag not detected", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_missing_port_option_causes_argument_error()
    {
        var client = new RecordingNfcClient();
        var command = CliCommandFactory.CreateRootCommand(
            nfcClientFactory: _ => client);

        var parseResult = command.Parse(["nfc", "write", "monkey1-disk1"]);
        var exit = parseResult.Invoke();

        Assert.NotEqual(0, exit);
    }

    private static string CreateValidCatalogRoot(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var floppyImage = Path.Combine(root, "monkey_island_disk_1.img");
        File.WriteAllBytes(floppyImage, Array.Empty<byte>());

        File.WriteAllText(
            Path.Combine(root, "config.yaml"),
            """
            defaultVm: test-vm
            """);
        File.WriteAllText(
            Path.Combine(root, "vms.yaml"),
            """
            vms:
              test-vm:
                label: "Test VM"
                path: "/data/vms/test"
            """);
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $$"""
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "{{floppyImage}}"
                mode: "{{mode}}"
                size: "720K"
            """);

        return root;
    }
}
