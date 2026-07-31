using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxConfigStoreTests
{
    [Fact]
    public void Load_reads_all_yaml_catalogs_from_alternate_root()
    {
        var root = CreateValidRoot();

        var store = new RetroBoxConfigStore(root);

        var data = store.Load();

        Assert.Equal("pentium100", data.Config.DefaultVm);
        Assert.Null(data.Config.FloppyControlSocketPath);
        Assert.Equal("Pentium 100", data.Vms["pentium100"].Label);
        Assert.Equal("ro", data.Floppies["monkey1-disk1"].Mode);
        Assert.Equal("720K", data.Floppies["monkey1-disk1"].Size);
    }

    [Fact]
    public void Load_reads_optional_floppy_control_socket_path()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "config.yaml"),
            """
            defaultVm: pentium100
            floppyControlSocketPath: /Users/nacho/Games/86Box/86box.socket
            """);

        var store = new RetroBoxConfigStore(root);

        var data = store.Load();

        Assert.Equal("/Users/nacho/Games/86Box/86box.socket", data.Config.FloppyControlSocketPath);
    }

    [Fact]
    public void Load_rejects_duplicate_ids()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "vms.yaml"),
            """
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
              pentium100:
                label: "Duplicate"
                path: "/data/vms/duplicate"
            """);

        var store = new RetroBoxConfigStore(root);

        var error = Assert.Throws<RetroBoxCatalogException>(() => store.Load());
        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pentium100", error.Message);
    }

    [Fact]
    public void Load_rejects_missing_floppy_image_paths()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            """
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "/missing/monkey_island_disk_1.img"
                mode: "ro"
                size: "720K"
            """);

        var store = new RetroBoxConfigStore(root);

        var error = Assert.Throws<RetroBoxCatalogException>(() => store.Load());
        Assert.Contains("does not exist", error.Message);
    }

    [Theory]
    [InlineData("floppies.yaml", "mode: \"writeable\"", "Invalid floppy mode 'writeable'")]
    [InlineData("floppies.yaml", "size: \"800K\"", "Invalid floppy size '800K'")]
    [InlineData("config.yaml", "defaultVm: missing-vm", "Unknown default VM 'missing-vm'")]
    public void Load_rejects_invalid_catalog_values(string fileName, string replacement, string expectedMessage)
    {
        var root = CreateValidRoot();
        var path = Path.Combine(root, fileName);
        var yaml = File.ReadAllText(path);

        if (fileName == "config.yaml")
        {
            yaml = replacement + Environment.NewLine;
        }
        else if (replacement.StartsWith("mode:", StringComparison.Ordinal))
        {
            yaml = yaml.Replace("mode: \"ro\"", replacement, StringComparison.Ordinal);
        }
        else
        {
            yaml = yaml.Replace("size: \"720K\"", replacement, StringComparison.Ordinal);
        }

        File.WriteAllText(path, yaml);
        var store = new RetroBoxConfigStore(root);

        var error = Assert.Throws<RetroBoxCatalogException>(() => store.Load());
        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public void Save_writes_backup_before_replacing_yaml()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var original = store.Load();
        var data = original with
        {
            Config = original.Config with { DefaultVm = "386sx16" }
        };

        store.Save(data);

        Assert.Contains("defaultVm: 386sx16", File.ReadAllText(Path.Combine(root, "config.yaml")));
        var backups = Directory.GetFiles(root, "config.yaml.*.bak");
        Assert.Single(backups);
        Assert.Contains("defaultVm: pentium100", File.ReadAllText(backups[0]));
    }

    private static string CreateValidRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var floppyImage = Path.Combine(root, "monkey_island_disk_1.img");
        File.WriteAllBytes(floppyImage, Array.Empty<byte>());

        File.WriteAllText(
            Path.Combine(root, "config.yaml"),
            """
            defaultVm: pentium100
            """);
        File.WriteAllText(
            Path.Combine(root, "vms.yaml"),
            """
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
              386sx16:
                label: "386SX-16"
                path: "/data/vms/386sx16"
            """);
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $$"""
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "{{floppyImage}}"
                mode: "ro"
                size: "720K"
            """);        

        return root;
    }
}
