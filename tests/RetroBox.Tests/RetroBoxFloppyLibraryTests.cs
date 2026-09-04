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
        var image = WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        using (File.Open(image, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                library.Delete("disk1");
            }
            catch (IOException)
            {
            }
        }

        // Whatever happened to the file, the catalog must still load: the daemon and
        // `retrobox boot` both call Load() and an orphaned entry would stop the appliance.
        Assert.Empty(new RetroBoxConfigStore(root).Load().Floppies);
    }

    [Fact]
    public void Delete_rejects_an_unknown_id()
    {
        WriteCatalog("disk1");
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(root));

        Assert.Throws<RetroBoxCatalogException>(() => library.Delete("nope"));
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
