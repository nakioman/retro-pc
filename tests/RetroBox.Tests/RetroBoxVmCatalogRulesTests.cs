using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxVmCatalogRulesTests
{
    [Fact]
    public void Load_defaults_engine_to_86box_and_disks_to_empty()
    {
        var root = CreateRoot("""
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
            """);

        var vm = new RetroBoxConfigStore(root).Load().Vms["pentium100"];

        Assert.Equal(RetroBoxVmEngines.EightySixBox, vm.Engine);
        Assert.Empty(vm.Disks);
    }

    [Fact]
    public void Load_reads_qemu_engine_and_disks()
    {
        var root = CreateRoot("""
            vms:
              winxp:
                label: "Windows XP"
                path: "/data/vms/winxp"
                engine: qemu
                disks:
                  - file: xp.qcow2
                    size: 20G
                    format: qcow2
            """);

        var vm = new RetroBoxConfigStore(root).Load().Vms["winxp"];

        Assert.Equal(RetroBoxVmEngines.Qemu, vm.Engine);
        var disk = Assert.Single(vm.Disks);
        Assert.Equal("xp.qcow2", disk.File);
        Assert.Equal("20G", disk.Size);
        Assert.Equal(RetroBoxVmDiskFormats.Qcow2, disk.Format);
    }

    [Fact]
    public void Load_defaults_disk_format_to_raw()
    {
        var root = CreateRoot("""
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
                disks:
                  - file: hdd.raw
            """);

        var disk = Assert.Single(new RetroBoxConfigStore(root).Load().Vms["pentium100"].Disks);

        Assert.Equal(RetroBoxVmDiskFormats.Raw, disk.Format);
        Assert.Null(disk.Size);
    }

    [Theory]
    [InlineData("engine: vmware", "", "VM 'vm' engine 'vmware' must be one of: 86box, qemu.")]
    [InlineData("engine: qemu", "- file: /abs/disk.img\n    size: 1G", "must be relative to the VM path")]
    [InlineData("engine: qemu", "- file: ../escape.img\n    size: 1G", "must stay inside the VM path")]
    [InlineData("engine: qemu", "- file: a.img\n    size: 1G\n  - file: a.img\n    size: 1G", "declares disk 'a.img' twice")]
    [InlineData("engine: qemu", "- size: 1G", "VM 'vm' disk file is required.")]
    [InlineData("engine: qemu", "- file: a.img\n    size: 1G\n    format: vdi", "format 'vdi' must be one of: raw, qcow2.")]
    [InlineData("engine: 86box", "- file: a.qcow2\n    format: qcow2", "86Box disk 'a.qcow2' must be raw")]
    [InlineData("engine: qemu", "- file: a.img\n    size: 1.5G", "size '1.5G' is invalid")]
    [InlineData("engine: qemu", "- file: a.img", "QEMU disk 'a.img' needs a size")]
    public void Load_rejects_invalid_vm_engine_or_disks(string engineLine, string disks, string expectedMessage)
    {
        var disksBlock = disks.Length == 0
            ? string.Empty
            : "\n    disks:\n      " + disks.Replace("\n", "\n    ", StringComparison.Ordinal);
        var root = CreateRoot($"""
            vms:
              vm:
                label: "VM"
                path: "/data/vms/vm"
                {engineLine}{disksBlock}
            """);

        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxConfigStore(root).Load());

        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public void Save_round_trips_engine_and_disks()
    {
        var root = CreateRoot("""
            vms:
              winxp:
                label: "Windows XP"
                path: "/data/vms/winxp"
                engine: qemu
                disks:
                  - file: xp.qcow2
                    size: 20G
                    format: qcow2
            """);
        var store = new RetroBoxConfigStore(root);

        store.Save(store.Load());
        var vm = store.Load().Vms["winxp"];

        Assert.Equal(RetroBoxVmEngines.Qemu, vm.Engine);
        var disk = Assert.Single(vm.Disks);
        Assert.Equal(("xp.qcow2", "20G", "qcow2"), (disk.File, disk.Size, disk.Format));
    }

    [Theory]
    [InlineData("1024", 1024L)]
    [InlineData("512K", 512L * 1024)]
    [InlineData("512M", 512L * 1024 * 1024)]
    [InlineData("20G", 20L * 1024 * 1024 * 1024)]
    [InlineData("2T", 2L * 1024 * 1024 * 1024 * 1024)]
    [InlineData("20g", 20L * 1024 * 1024 * 1024)]
    public void TryParseDiskSize_accepts_bytes_and_binary_suffixes(string value, long expected)
    {
        Assert.True(RetroBoxVmCatalogRules.TryParseDiskSize(value, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0G")]
    [InlineData("G")]
    [InlineData("1.5G")]
    [InlineData("-1")]
    [InlineData("20GB")]
    [InlineData("99999999999T")]
    public void TryParseDiskSize_rejects_invalid_values(string value)
    {
        Assert.False(RetroBoxVmCatalogRules.TryParseDiskSize(value, out _));
    }

    private static string CreateRoot(string vmsYaml)
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-vm-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "vms.yaml"), vmsYaml);
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
        return root;
    }
}
