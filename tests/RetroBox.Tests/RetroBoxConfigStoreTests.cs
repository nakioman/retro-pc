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
    public void Load_uses_empty_config_when_config_yaml_is_missing()
    {
        var root = CreateValidRoot();
        File.Delete(Path.Combine(root, "config.yaml"));

        var data = new RetroBoxConfigStore(root).Load();

        Assert.Equal(string.Empty, data.Config.DefaultVm);
        Assert.Null(data.Config.FloppyControlSocketPath);
    }

    [Fact]
    public void Load_uses_empty_floppy_catalog_when_floppies_yaml_is_missing()
    {
        var root = CreateValidRoot();
        File.Delete(Path.Combine(root, "floppies.yaml"));

        var data = new RetroBoxConfigStore(root).Load();

        Assert.Empty(data.Floppies);
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

    [Fact]
    public void Load_rejects_relative_floppy_image_paths()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            """
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "monkey_island_disk_1.img"
                mode: "ro"
                size: "720K"
            """);

        var store = new RetroBoxConfigStore(root);

        var error = Assert.Throws<RetroBoxCatalogException>(() => store.Load());
        Assert.Contains("must be absolute", error.Message);
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

    [Fact]
    public void Save_keeps_only_the_most_recent_backups()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var data = store.Load();

        for (var save = 0; save < 6; save++)
        {
            store.Save(data);
        }

        var backups = Directory.GetFiles(root, "floppies.yaml.*.bak");

        Assert.Equal(RetroBoxConfigStore.BackupsKept, backups.Length);
    }

    [Fact]
    public void Save_keeps_only_the_most_recent_backups_for_every_saved_file()
    {
        // The list of files pruned must be the same list actually written, not a second,
        // hand-maintained copy of it - otherwise a file added to one and not the other silently
        // stops being pruned.
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var data = store.Load();

        for (var save = 0; save < 6; save++)
        {
            store.Save(data);
        }

        Assert.Equal(RetroBoxConfigStore.BackupsKept, Directory.GetFiles(root, "config.yaml.*.bak").Length);
        Assert.Equal(RetroBoxConfigStore.BackupsKept, Directory.GetFiles(root, "vms.yaml.*.bak").Length);
        Assert.Equal(RetroBoxConfigStore.BackupsKept, Directory.GetFiles(root, "floppies.yaml.*.bak").Length);
    }

    [Fact]
    public void Save_persists_nfc_flag_in_yaml()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var original = store.Load();
        var floppies = new Dictionary<string, RetroBoxFloppy>(original.Floppies, StringComparer.Ordinal);
        floppies["monkey1-disk1"] = floppies["monkey1-disk1"] with { Nfc = true };
        var data = original with { Floppies = floppies };

        store.Save(data);

        var yaml = File.ReadAllText(Path.Combine(root, "floppies.yaml"));
        Assert.Contains("nfc: true", yaml);
        var reloaded = new RetroBoxConfigStore(root).Load();
        Assert.True(reloaded.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public void Load_defaults_nfc_to_false_when_key_absent()
    {
        var root = CreateValidRoot();

        var data = new RetroBoxConfigStore(root).Load();

        Assert.False(data.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public void Load_parses_nfc_true_from_yaml()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $$"""
            floppies:
              monkey1-disk1:
                label: "Monkey Island - Disk 1"
                image: "{{Path.Combine(root, "monkey_island_disk_1.img")}}"
                mode: "ro"
                size: "720K"
                nfc: true
            """);

        var data = new RetroBoxConfigStore(root).Load();

        Assert.True(data.Floppies["monkey1-disk1"].Nfc);
    }

    [Fact]
    public void Load_uses_empty_game_catalog_when_games_yaml_is_missing()
    {
        var root = CreateValidRoot();

        var data = new RetroBoxConfigStore(root).Load();

        Assert.Empty(data.Games);
    }

    [Fact]
    public void Save_round_trips_games()
    {
        var root = CreateValidRoot();
        var store = new RetroBoxConfigStore(root);
        var original = store.Load();
        var games = new Dictionary<string, RetroBoxGame>(StringComparer.Ordinal)
        {
            ["monkey-island"] = new() { Label = "The Secret of Monkey Island", FloppyIds = ["monkey1-disk1"] }
        };

        store.Save(original with { Games = games });

        var reloaded = store.Load();
        Assert.Equal("The Secret of Monkey Island", reloaded.Games["monkey-island"].Label);
        Assert.Equal(["monkey1-disk1"], reloaded.Games["monkey-island"].FloppyIds);
    }

    [Theory]
    [InlineData("BAD_ID", "game ID")]
    [InlineData("monkey-island", "Game 'monkey-island' label")]
    public void Load_rejects_invalid_game_values(string id, string expectedMessage)
    {
        var root = CreateValidRoot();
        var label = id == "BAD_ID" ? "Good label" : "";
        File.WriteAllText(Path.Combine(root, "games.yaml"), $"games:\n  {id}:\n    label: {label}\n");

        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxConfigStore(root).Load());

        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public void Load_rejects_unknown_game_membership()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "games.yaml"),
            """
            games:
              first:
                label: First
                floppyIds: [missing]
            """);

        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxConfigStore(root).Load());

        Assert.Contains("unknown floppy 'missing'", error.Message);
    }

    [Fact]
    public void Load_rejects_duplicate_game_membership()
    {
        var root = CreateValidRoot();
        File.WriteAllText(
            Path.Combine(root, "games.yaml"),
            """
            games:
              first:
                label: First
                floppyIds: [monkey1-disk1]
              second:
                label: Second
                floppyIds: [monkey1-disk1]
            """);

        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxConfigStore(root).Load());

        Assert.Contains("belongs to both games", error.Message);
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
