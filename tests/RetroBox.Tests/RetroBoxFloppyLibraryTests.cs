using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-library-{Guid.NewGuid():N}");

    public RetroBoxFloppyLibraryTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Delete_removes_the_entry_and_then_the_image()
    {
        WriteCatalog("disk1");
        var image = Path.Combine(root, "disk1.img");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.Delete("disk1");

        Assert.Empty(new RetroBoxConfigStore(root).Load().Floppies);
        Assert.False(File.Exists(image));
    }

    [Fact]
    public void Delete_leaves_a_loadable_catalog_when_the_image_cannot_be_removed()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(
            new RetroBoxConfigStore(root),
            deleteFile: _ => throw new IOException("simulated failure removing the image"));

        Assert.Throws<RetroBoxCatalogException>(() => library.Delete("disk1"));

        // Whatever happened to the file, the catalog must still load: the daemon and
        // `retrobox boot` both call Load() and an orphaned entry would stop the appliance. The
        // delete is injected so this failure is deterministic on every platform: holding the
        // image open with FileShare.None only maps to an advisory flock on Unix, and unlink(2)
        // ignores advisory locks, so a real File.Delete never actually throws there.
        Assert.Empty(new RetroBoxConfigStore(root).Load().Floppies);
    }

    [Fact]
    public void Delete_rejects_an_unknown_id()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxUnknownFloppyException>(() => library.Delete("nope"));
    }

    [Fact]
    public void UpdateLabelAndMode_changes_the_label_without_touching_nfc()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", "Monkey Island", null);

        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.Equal("Monkey Island", floppy.Label);
        Assert.True(floppy.Nfc);
    }

    [Fact]
    public void UpdateLabelAndMode_clears_nfc_when_the_mode_changes()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", null, RetroBoxFloppyCatalogRules.ReadWriteMode);

        var floppy = new RetroBoxConfigStore(root).Load().Floppies["disk1"];
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadWriteMode, floppy.Mode);
        Assert.False(floppy.Nfc);
        Assert.Null(floppy.NfcUid);
    }

    [Fact]
    public void UpdateLabelAndMode_keeps_nfc_when_the_mode_is_unchanged()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        library.UpdateLabelAndMode("disk1", "Renamed", RetroBoxFloppyCatalogRules.ReadOnlyMode);

        Assert.True(new RetroBoxConfigStore(root).Load().Floppies["disk1"].Nfc);
    }

    [Fact]
    public void UpdateLabelAndMode_rejects_an_invalid_mode()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxCatalogException>(() => library.UpdateLabelAndMode("disk1", null, "rx"));
    }

    [Fact]
    public void UpdateLabelAndMode_rejects_an_unknown_id()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxUnknownFloppyException>(() => library.UpdateLabelAndMode("nope", "x", null));
    }

    [Fact]
    public void UpdateLabelAndMode_throws_a_distinct_exception_when_the_catalog_fails_to_load()
    {
        WriteCatalog("disk1");
        File.AppendAllText(
            Path.Combine(root, "floppies.yaml"),
            $"  broken:\n    label: broken\n    image: {Path.Combine(root, "missing.img")}\n    mode: ro\n    size: 1.44M\n");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        // A sibling entry's missing image fails Load()/Validate() for the whole catalog, before
        // "disk1" is even looked up. This must not be confused with either "disk1 doesn't exist"
        // (RetroBoxUnknownFloppyException) or "the mode was invalid" (a plain
        // RetroBoxCatalogException) — both of those are the client's fault; this is not.
        Assert.Throws<RetroBoxCatalogUnavailableException>(() => library.UpdateLabelAndMode("disk1", "Renamed", null));
    }

    [Fact]
    public void AssignTag_records_the_uid_and_takes_it_from_the_previous_owner()
    {
        WriteCatalog("disk1", "disk2");
        var store = new RetroBoxConfigStore(root);
        var library = new RetroBoxFloppyLibrary(store);

        library.AssignTag("disk1", "04A13BFE");
        library.AssignTag("disk2", "04A13BFE");

        var floppies = store.Load().Floppies;

        Assert.True(floppies["disk2"].Nfc);
        Assert.Equal("04A13BFE", floppies["disk2"].NfcUid);

        // The tag is physical: disk1 no longer has one, and the mount guard must refuse it.
        Assert.False(floppies["disk1"].Nfc);
        Assert.Null(floppies["disk1"].NfcUid);
    }

    [Fact]
    public void AssignTag_rejects_an_unknown_floppy()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxUnknownFloppyException>(() => library.AssignTag("nope", "04A13BFE"));
    }

    [Fact]
    public void AssignTag_rejects_a_write_whose_mode_no_longer_matches_the_catalog()
    {
        WriteCatalog("disk1");
        var store = new RetroBoxConfigStore(root);
        var library = new RetroBoxFloppyLibrary(store);

        // Simulates a PATCH that changed the mode while a tag write for the old mode was already
        // in flight. UpdateLabelAndMode already cleared Nfc/NfcUid for exactly this reason: the
        // tag's payload is "<id>,<mode>", so committing the write now would silently resurrect a
        // tag whose payload the catalog no longer matches.
        library.UpdateLabelAndMode("disk1", null, RetroBoxFloppyCatalogRules.ReadWriteMode);

        Assert.Throws<RetroBoxCatalogException>(
            () => library.AssignTag("disk1", "04A13BFE", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var floppy = store.Load().Floppies["disk1"];
        Assert.False(floppy.Nfc);
        Assert.Null(floppy.NfcUid);
    }

    [Fact]
    public void AssignTag_skips_the_mode_check_when_no_expected_mode_is_given()
    {
        WriteCatalog("disk1");
        var store = new RetroBoxConfigStore(root);
        var library = new RetroBoxFloppyLibrary(store);

        library.AssignTag("disk1", "04A13BFE");

        Assert.True(store.Load().Floppies["disk1"].Nfc);
    }

    private void WriteCatalog(params string[] floppyIds)
    {
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");

        var lines = new List<string> { "floppies:" };
        foreach (var id in floppyIds)
        {
            var image = Path.Combine(root, $"{id}.img");
            File.WriteAllBytes(image, new byte[16]);
            lines.Add($"  {id}:");
            lines.Add($"    label: {id}");
            lines.Add($"    image: {image}");
            lines.Add("    mode: ro");
            lines.Add("    size: 1.44M");
            lines.Add("    nfc: true");
        }

        File.WriteAllText(Path.Combine(root, "floppies.yaml"), string.Join('\n', lines) + '\n');
    }
}
