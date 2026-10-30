using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxPayloadCatalogTests
{
    private static readonly string payload = Path.Combine(FindRepositoryRoot(), "appliance", "installer", "payload");

    [Fact]
    public void Payload_catalog_plans_every_declared_disk()
    {
        // retrobox-hdd-creation created each 86Box profile's hdd.raw from hdd_01_parameters; the
        // declared disks must reproduce those exact sizes on a fresh install.
        var root = Path.Combine(Path.GetTempPath(), "retrobox-payload-tests", Guid.NewGuid().ToString("N"));
        var vms = Path.Combine(root, "vms");
        var configRoot = Path.Combine(root, "retrobox");
        Directory.CreateDirectory(configRoot);
        foreach (var profile in Directory.GetDirectories(Path.Combine(payload, "profiles")))
        {
            var target = Path.Combine(vms, Path.GetFileName(profile));
            Directory.CreateDirectory(target);
            foreach (var nativeProfile in new[] { RetroBox86BoxConfig.FileName, RetroBoxQemuProfile.FileName })
            {
                if (File.Exists(Path.Combine(profile, nativeProfile)))
                {
                    File.Copy(Path.Combine(profile, nativeProfile), Path.Combine(target, nativeProfile));
                }
            }
        }

        File.WriteAllText(
            Path.Combine(configRoot, "vms.yaml"),
            File.ReadAllText(Path.Combine(payload, "retrobox", "vms.yaml")).Replace("/data/vms/", vms + "/", StringComparison.Ordinal));

        try
        {
            var planned = new RetroBoxConfigStore(configRoot).Load().Vms
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .SelectMany(entry => RetroBoxVmDiskPlanner.Plan(entry.Key, entry.Value)
                    .Select(plan => (entry.Key, Path.GetRelativePath(entry.Value.Path, plan.Path), plan.SizeBytes, plan.Format)))
                .ToArray();

            Assert.Equal(
                [
                    ("386sx16", "hdd.raw", 60_862_464L, "raw"),
                    ("core2duo", "hdd.qcow2", 128_849_018_880L, "qcow2"),
                    ("pentium100", "hdd.raw", 2_111_864_832L, "raw"),
                    ("pentium2-350", "hdd.raw", 20_020_331_520L, "raw"),
                ],
                planned);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Core2duo_profile_uses_its_declared_disk_and_leaves_host_arguments_to_the_launcher()
    {
        var arguments = RetroBoxQemuProfile.ReadArguments(Path.Combine(payload, "profiles", "core2duo", RetroBoxQemuProfile.FileName));
        var catalogVm = new RetroBoxConfigStore(Path.Combine(payload, "retrobox")).Load().Vms["core2duo"];

        Assert.Equal("Core 2 Duo", catalogVm.Label);
        Assert.Equal("qemu", catalogVm.Engine);
        Assert.Contains(arguments, argument => argument.StartsWith($"file={Assert.Single(catalogVm.Disks).File},", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("index=2", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument is "-qmp" or "-display" or "-monitor" or "-serial" or "-parallel");
    }

    [Fact]
    public void Cdrom_refresh_unit_runs_a_valid_cli_command_as_the_qmp_socket_owner()
    {
        var unit = File.ReadAllLines(Path.Combine(payload, "units", "retrobox-cdrom-refresh@.service"));
        var rule = File.ReadAllText(Path.Combine(payload, "udev", "90-retrobox-cdrom.rules"));

        Assert.Contains("User=retrobox", unit);
        Assert.Contains($"ConditionPathExists={RetroBoxQemuLauncher.DefaultQmpSocketPath}", unit);
        Assert.Contains("restart retrobox-cdrom-refresh@%k.service", rule, StringComparison.Ordinal);

        var execStart = Assert.Single(unit, line => line.StartsWith("ExecStart=", StringComparison.Ordinal));
        var argv = execStart["ExecStart=".Length..].Replace("%I", "sr0", StringComparison.Ordinal).Split(' ');
        Assert.Equal("/opt/retrobox/retrobox", argv[0]);
        var parseResult = CliCommandFactory.CreateRootCommand().Parse(argv[1..]);
        Assert.Empty(parseResult.Errors);
        Assert.Equal("refresh", parseResult.CommandResult.Command.Name);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RetroBox.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("RetroBox.slnx not found above the test output directory.");
    }
}
