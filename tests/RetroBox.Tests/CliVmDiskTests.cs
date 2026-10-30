using RetroBox.Cli;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
public sealed class CliVmDiskTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "retrobox-cli-vm-disk-tests", Guid.NewGuid().ToString("N"));

    public CliVmDiskTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "386sx16"));
        Directory.CreateDirectory(Path.Combine(root, "pentium100"));
        File.WriteAllText(
            Path.Combine(root, "386sx16", "86box.cfg"),
            "hdd_01_fn = hdd.raw\nhdd_01_parameters = 39, 4, 762, 0, ide\n");
        File.WriteAllText(
            Path.Combine(root, "pentium100", "86box.cfg"),
            "hdd_01_fn = hdd.raw\nhdd_01_parameters = 63, 16, 4092, 0, ide\n");
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
        WriteVms($"""
            vms:
              386sx16:
                label: "386SX-16"
                path: "{Path.Combine(root, "386sx16")}"
                disks:
                  - file: hdd.raw
              pentium100:
                label: "Pentium 100"
                path: "{Path.Combine(root, "pentium100")}"
                disks:
                  - file: hdd.raw
            """);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Create_disks_creates_missing_and_keeps_existing()
    {
        var existing = Path.Combine(root, "pentium100", "hdd.raw");
        File.WriteAllText(existing, "data");

        var (exitCode, output, error) = Invoke("vm", "create-disks", "--config-root", root);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        var created = Path.Combine(root, "386sx16", "hdd.raw");
        Assert.Equal(60_862_464L, new FileInfo(created).Length);
        Assert.Equal("data", File.ReadAllText(existing));
        Assert.Equal(
            $"386sx16\tcreated\t{created}\t60862464\npentium100\tkept\t{existing}\n",
            output);
    }

    [Fact]
    public void Create_disks_limits_run_to_given_vm()
    {
        var (exitCode, _, _) = Invoke("vm", "create-disks", "pentium100", "--config-root", root);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(root, "pentium100", "hdd.raw")));
        Assert.False(File.Exists(Path.Combine(root, "386sx16", "hdd.raw")));
    }

    [Fact]
    public void Create_disks_rejects_unknown_vm()
    {
        var (exitCode, _, error) = Invoke("vm", "create-disks", "missing", "--config-root", root);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown VM 'missing'", error);
    }

    [Fact]
    public void Create_disks_reports_each_failure_and_continues()
    {
        WriteVms($"""
            vms:
              386sx16:
                label: "386SX-16"
                path: "{Path.Combine(root, "386sx16")}"
                disks:
                  - file: hdd.raw
                    size: 64M
              pentium100:
                label: "Pentium 100"
                path: "{Path.Combine(root, "pentium100")}"
                disks:
                  - file: hdd.raw
              winxp:
                label: "Windows XP"
                path: "{Path.Combine(root, "winxp")}"
                engine: qemu
                disks:
                  - file: xp.qcow2
                    size: 20G
                    format: qcow2
            """);

        var (exitCode, output, error) = Invoke(
            "vm", "create-disks", "--config-root", root, "--qemu-img", Path.Combine(root, "no-qemu-img"));

        Assert.Equal(1, exitCode);
        Assert.Contains("386sx16: ", error);
        Assert.Contains("does not match hdd_01_parameters", error);
        Assert.Contains("winxp: ", error);
        Assert.Contains("qemu-img was not found", error);
        Assert.Contains("pentium100\tcreated\t", output);
    }

    private void WriteVms(string yaml) => File.WriteAllText(Path.Combine(root, "vms.yaml"), yaml);

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
