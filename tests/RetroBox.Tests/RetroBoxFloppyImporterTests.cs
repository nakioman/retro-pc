using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxFloppyImporterTests
{
    [Fact]
    public void Import_moves_image_to_cataloged_and_registers_floppy_with_defaults()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "blank.img");
        File.WriteAllBytes(sourceImage, [0x42]);

        var importer = new RetroBoxFloppyImporter();

        var result = importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "blank-disk",
            Label = "Blank Disk",
            ImagePath = sourceImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        });

        Assert.Equal(Path.Combine(layout.CatalogedRoot, "blank.img"), result.ImagePath);
        Assert.False(File.Exists(sourceImage));
        Assert.Equal([0x42], File.ReadAllBytes(result.ImagePath));

        var data = new RetroBoxConfigStore(layout.ConfigRoot).Load();
        Assert.Equal("Blank Disk", data.Floppies["blank-disk"].Label);
        Assert.Equal(result.ImagePath, data.Floppies["blank-disk"].Image);
        Assert.Equal("ro", data.Floppies["blank-disk"].Mode);
        Assert.Equal("1.44M", data.Floppies["blank-disk"].Size);
    }

    [Fact]
    public void Import_rejects_images_outside_scratch_root()
    {
        var layout = CreateLayout();
        var outsideImage = Path.Combine(layout.Root, "outside.img");
        File.WriteAllBytes(outsideImage, []);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "outside",
            Label = "Outside",
            ImagePath = outsideImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("scratch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Import_rejects_existing_catalog_id()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "duplicate.img");
        File.WriteAllBytes(sourceImage, []);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "existing-disk",
            Label = "Duplicate",
            ImagePath = sourceImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("existing-disk", error.Message);
        Assert.True(File.Exists(sourceImage));
    }

    [Fact]
    public void Import_rejects_existing_cataloged_image()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "collision.img");
        var targetImage = Path.Combine(layout.CatalogedRoot, "collision.img");
        File.WriteAllBytes(sourceImage, [0x01]);
        File.WriteAllBytes(targetImage, [0x02]);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "collision",
            Label = "Collision",
            ImagePath = sourceImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([0x01], File.ReadAllBytes(sourceImage));
        Assert.Equal([0x02], File.ReadAllBytes(targetImage));
    }

    [Fact]
    public void Import_restores_source_image_when_saving_catalog_fails()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "rollback.img");
        var targetImage = Path.Combine(layout.CatalogedRoot, "rollback.img");
        File.WriteAllBytes(sourceImage, [0x33]);
        File.SetAttributes(Path.Combine(layout.ConfigRoot, "floppies.yaml"), FileAttributes.ReadOnly);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.ThrowsAny<Exception>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "rollback",
            Label = "Rollback",
            ImagePath = sourceImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.IsNotType<RetroBoxCatalogException>(error);
        Assert.True(File.Exists(sourceImage));
        Assert.False(File.Exists(targetImage));
        Assert.DoesNotContain("rollback", File.ReadAllText(Path.Combine(layout.ConfigRoot, "floppies.yaml")));
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("bad,id")]
    [InlineData("bad/id")]
    [InlineData("bad\\id")]
    [InlineData("bad..id")]
    [InlineData("bad_id")]
    [InlineData("Bad-ID")]
    [InlineData("-bad")]
    [InlineData("bad-")]
    [InlineData("bad\tid")]
    public void Import_rejects_invalid_id_before_moving_image(string id)
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "bad-id.img");
        File.WriteAllBytes(sourceImage, [0x33]);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = id,
            Label = "Bad ID",
            ImagePath = sourceImage,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("lowercase ASCII", error.Message);
        Assert.True(File.Exists(sourceImage));
    }

    [Theory]
    [InlineData("360K")]
    [InlineData("720K")]
    [InlineData("1.2M")]
    [InlineData("1.44M")]
    public void Import_accepts_supported_sizes(string size)
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, $"{Guid.NewGuid():N}.img");
        File.WriteAllBytes(sourceImage, []);

        var importer = new RetroBoxFloppyImporter();
        var id = $"disk-{Guid.NewGuid():N}";

        importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = id,
            Label = "Sized Disk",
            ImagePath = sourceImage,
            Size = size,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        });

        var data = new RetroBoxConfigStore(layout.ConfigRoot).Load();
        Assert.Equal(size, data.Floppies[id].Size);
    }

    [Fact]
    public void Import_rejects_invalid_size()
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, "bad-size.img");
        File.WriteAllBytes(sourceImage, []);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = "bad-size",
            Label = "Bad Size",
            ImagePath = sourceImage,
            Size = "800",
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("Invalid floppy size", error.Message);
    }

    [Theory]
    [InlineData("360")]
    [InlineData("720")]
    [InlineData("1200")]
    [InlineData("1440")]
    [InlineData("1.44MB")]
    public void Import_rejects_size_aliases(string size)
    {
        var layout = CreateLayout();
        var sourceImage = Path.Combine(layout.ScratchRoot, $"{Guid.NewGuid():N}.img");
        File.WriteAllBytes(sourceImage, []);

        var importer = new RetroBoxFloppyImporter();

        var error = Assert.Throws<RetroBoxCatalogException>(() => importer.Import(new RetroBoxFloppyImportRequest
        {
            Id = $"alias-{Guid.NewGuid():N}",
            Label = "Alias Size",
            ImagePath = sourceImage,
            Size = size,
            ConfigRoot = layout.ConfigRoot,
            ScratchRoot = layout.ScratchRoot,
            CatalogedRoot = layout.CatalogedRoot,
        }));
        Assert.Contains("Invalid floppy size", error.Message);
    }

    private static TestRetroBoxLayout CreateLayout()
    {
        return TestRetroBoxLayout.Create("retrobox-import-tests", includeExistingFloppy: true);
    }
}
