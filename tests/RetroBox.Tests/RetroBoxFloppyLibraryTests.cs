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
        var image = WriteCatalog("disk1");
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

    private string WriteCatalog(string id)
    {
        var image = Path.Combine(root, $"{id}.img");
        File.WriteAllBytes(image, new byte[16]);
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(
            Path.Combine(root, "floppies.yaml"),
            $"floppies:\n  {id}:\n    label: {id}\n    image: {image}\n    mode: ro\n    size: 1.44M\n    nfc: true\n");
        return image;
    }
}
