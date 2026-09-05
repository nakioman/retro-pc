using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxCatalogEndpointsTests
{
    [Fact]
    public void BuildCatalogView_projects_every_floppy_field()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(source);

        var floppy = Assert.Single(view.UngroupedFloppies);
        Assert.Equal("disk1", floppy.Id);
        Assert.Equal("Disk 1", floppy.Label);
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadOnlyMode, floppy.Mode);
        Assert.Equal(RetroBoxFloppyCatalogRules.DefaultImportSize, floppy.Size);
        Assert.True(floppy.Nfc);
    }

    [Fact]
    public void BuildCatalogView_orders_floppies_by_id()
    {
        var catalog = new RetroBoxCatalogData(
            new RetroBoxConfig { DefaultVm = "dos" },
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal)
            {
                ["dos"] = new() { Label = "DOS", Path = "/data/vms/dos" },
            },
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal)
            {
                ["zdisk"] = new() { Label = "Z", Image = "/z.img", Nfc = true },
                ["adisk"] = new() { Label = "A", Image = "/a.img", Nfc = true },
            });

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(new RetroBoxStaticCatalogSource(catalog));

        Assert.Equal(["adisk", "zdisk"], view.UngroupedFloppies.Select(f => f.Id));
    }

    [Fact]
    public void BuildCatalogView_reports_the_catalog_error_so_the_panel_can_show_it()
    {
        using var source = new RetroBoxWatchingCatalogSource(
            Path.Combine(Path.GetTempPath(), $"retrobox-view-{Guid.NewGuid():N}"),
            RetroBoxCatalogData.Empty,
            watchFileSystem: false,
            initialError: "floppies.yaml is invalid");

        var view = RetroBoxCatalogEndpoints.BuildCatalogView(source);

        Assert.Empty(view.UngroupedFloppies);
        Assert.Equal("floppies.yaml is invalid", view.CatalogError);
    }

    [Fact]
    public void BuildCatalogView_reports_no_error_for_a_healthy_catalog()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        Assert.Null(RetroBoxCatalogEndpoints.BuildCatalogView(source).CatalogError);
    }

    [Fact]
    public void BuildCatalogView_returns_an_empty_array_for_an_empty_catalog()
    {
        var catalog = new RetroBoxCatalogData(
            new RetroBoxConfig(),
            new Dictionary<string, RetroBoxVm>(StringComparer.Ordinal),
            new Dictionary<string, RetroBoxFloppy>(StringComparer.Ordinal));

        Assert.Empty(RetroBoxCatalogEndpoints.BuildCatalogView(new RetroBoxStaticCatalogSource(catalog)).UngroupedFloppies);
    }
}
