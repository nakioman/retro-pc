using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class CliVmTests
{
    [Fact]
    public void Vm_list_prints_ids_and_labels_in_stable_order()
    {
        var layout = CreateVmLayout();
        var output = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();
        var parseResult = command.Parse(["vm", "list", "--config-root", layout]);
        parseResult.InvocationConfiguration.Output = output;

        Assert.Equal(0, parseResult.Invoke());
        Assert.Equal("386sx16\t386SX-16\npentium100\tPentium 100\n", output.ToString());
    }

    [Fact]
    public void Vm_default_reads_and_updates_only_config_yaml()
    {
        var layout = CreateVmLayout();
        var configPath = Path.Combine(layout, "config.yaml");
        var before = File.ReadAllText(configPath);
        var vmsBefore = File.ReadAllText(Path.Combine(layout, "vms.yaml"));
        var output = new StringWriter();
        var command = CliCommandFactory.CreateRootCommand();

        var read = command.Parse(["vm", "default", "--config-root", layout]);
        read.InvocationConfiguration.Output = output;
        Assert.Equal(0, read.Invoke());
        Assert.Equal("pentium100\n", output.ToString());

        Assert.Equal(0, command.Parse(["vm", "default", "386sx16", "--config-root", layout]).Invoke());
        Assert.Equal("defaultVm: 386sx16\nfloppyControlSocketPath: /run/retrobox.sock\n", File.ReadAllText(configPath));
        Assert.NotEqual(before, File.ReadAllText(configPath));
        Assert.Equal(vmsBefore, File.ReadAllText(Path.Combine(layout, "vms.yaml")));
    }

    [Fact]
    public void Vm_default_rejects_unknown_ids()
    {
        var layout = CreateVmLayout();
        var command = CliCommandFactory.CreateRootCommand();

        Assert.NotEqual(0, command.Parse(["vm", "default", "missing", "--config-root", layout]).Invoke());
    }

    [Fact]
    public void Boot_dry_run_resolves_default_and_runner_receives_paths_and_overrides()
    {
        var layout = CreateVmLayout();
        var profile = Path.Combine(layout, "profiles", "pentium100");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "86box.cfg"), "# profile");
        File.WriteAllText(Path.Combine(layout, "vms.yaml"), $$"""
            vms:
              pentium100:
                label: "Pentium 100"
                path: "{{profile}}"
              386sx16:
                label: "386SX-16"
                path: "{{profile}}"
            """);

        var output = new StringWriter();
        var captured = (RetroBoxBootCommandRequest?)null;
        var command = CliCommandFactory.CreateRootCommand(bootRunner: request =>
        {
            captured = request;
            return 23;
        });
        var dryRun = command.Parse(["boot", "--dry-run", "--config-root", layout]);
        dryRun.InvocationConfiguration.Output = output;

        Assert.Equal(0, dryRun.Invoke());
        Assert.Contains("pentium100\tPentium 100", output.ToString());
        Assert.Null(captured);

        var exitCode = command.Parse([
            "boot", "--config-root", layout, "--binary", "/custom/86box", "--rompath", "/custom/roms",
        ]).Invoke();

        Assert.Equal(23, exitCode);
        Assert.NotNull(captured);
        Assert.Equal("/custom/86box", captured.BinaryPath);
        Assert.Equal("/custom/roms", captured.RomPath);
        Assert.Equal(profile, captured.VmPath);
    }

    [Fact]
    public void Default_config_root_is_data_retrobox()
    {
        var command = CliCommandFactory.CreateRootCommand();

        Assert.Equal(1, command.Parse(["vm", "list"]).Invoke());
    }

    private static string CreateVmLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-cli-vm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.yaml"),
            "defaultVm: pentium100 # keep this comment\nfloppyControlSocketPath: /run/retrobox.sock\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), """
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
              386sx16:
                label: "386SX-16"
                path: "/data/vms/386sx16"
            """);
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games: {}\n");
        return root;
    }
}
