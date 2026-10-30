using RetroBox.Cli;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
public sealed class CliCdromTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "retrobox-cli-cdrom-tests", Guid.NewGuid().ToString("N"));

    public CliCdromTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Refresh_does_nothing_without_an_installer_detected_drive()
    {
        var env = WriteEnv("CDROM_DEVICE=/dev/sr0\nCDROM_STATUS=NOT_DETECTED\n");

        var (exitCode, output, _) = Invoke("cdrom", "refresh", "--hardware-env", env, "--qmp-socket", Path.Combine(root, "qmp.sock"));

        Assert.Equal(0, exitCode);
        Assert.Contains("No installer-detected optical drive", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_ignores_media_changes_on_other_drives()
    {
        var drive = Device("sr0");
        var other = Device("sr1");
        var env = WriteEnv($"CDROM_DEVICE={drive}\nCDROM_STATUS=DETECTED\n");

        var (exitCode, output, _) = Invoke(
            "cdrom", "refresh", "--device", other, "--hardware-env", env, "--qmp-socket", Path.Combine(root, "qmp.sock"));

        Assert.Equal(0, exitCode);
        Assert.Contains("not the VM's optical drive", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_does_nothing_when_no_qemu_vm_runs()
    {
        var drive = Device("sr0");
        var env = WriteEnv($"CDROM_DEVICE={drive}\nCDROM_STATUS=DETECTED\n");

        var (exitCode, output, _) = Invoke(
            "cdrom", "refresh", "--device", drive, "--hardware-env", env, "--qmp-socket", Path.Combine(root, "qmp.sock"));

        Assert.Equal(0, exitCode);
        Assert.Contains("No QEMU VM is running", output, StringComparison.Ordinal);
    }

    private string Device(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string WriteEnv(string content)
    {
        var path = Path.Combine(root, "hardware.env");
        File.WriteAllText(path, content);
        return path;
    }

    private static (int ExitCode, string Output, string Error) Invoke(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var parseResult = CliCommandFactory.CreateRootCommand().Parse(args);
        parseResult.InvocationConfiguration.Output = output;
        parseResult.InvocationConfiguration.Error = error;
        var exitCode = parseResult.Invoke();
        return (exitCode, output.ToString(), error.ToString());
    }
}
