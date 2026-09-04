using System.Net;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxWebHostTests
{
    [Fact]
    public async Task Get_catalog_returns_the_current_floppies_as_camel_case_json()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/catalog");

        Assert.Contains("\"floppies\"", body, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"disk1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"nfc\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_root_serves_the_embedded_panel()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("RetroBox", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_unknown_asset_returns_not_found()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        using var response = await client.GetAsync("/nope.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_catalog_reflects_a_catalog_change_without_a_restart()
    {
        var source = new MutableCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        Assert.Contains("disk1", await client.GetStringAsync("/api/catalog"), StringComparison.Ordinal);

        source.Publish(
            FloppyControlTestCatalogs.CreateCatalog("disk2", "/data/floppies/disk2.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var body = await client.GetStringAsync("/api/catalog");
        Assert.Contains("disk2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("disk1", body, StringComparison.Ordinal);
    }
}
