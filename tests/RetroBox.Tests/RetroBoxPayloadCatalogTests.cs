using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxPayloadCatalogTests
{
    [Fact]
    public void Payload_catalog_plans_the_same_disks_as_the_old_hdd_script()
    {
        // retrobox-hdd-creation created each profile's hdd.raw from hdd_01_parameters; the
        // declared disks must reproduce those exact sizes on a fresh install.
        var payload = Path.Combine(FindRepositoryRoot(), "appliance", "installer", "payload");
        var root = Path.Combine(Path.GetTempPath(), "retrobox-payload-tests", Guid.NewGuid().ToString("N"));
        var vms = Path.Combine(root, "vms");
        var configRoot = Path.Combine(root, "retrobox");
        Directory.CreateDirectory(configRoot);
        foreach (var profile in Directory.GetDirectories(Path.Combine(payload, "profiles")))
        {
            var target = Path.Combine(vms, Path.GetFileName(profile));
            Directory.CreateDirectory(target);
            File.Copy(Path.Combine(profile, RetroBox86BoxConfig.FileName), Path.Combine(target, RetroBox86BoxConfig.FileName));
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
    public void Cdrom_refresh_unit_runs_a_valid_cli_command_as_the_qmp_socket_owner()
    {
        var payload = Path.Combine(FindRepositoryRoot(), "appliance", "installer", "payload");
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
