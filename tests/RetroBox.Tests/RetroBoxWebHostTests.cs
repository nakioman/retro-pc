using System.Net;
using System.Text;
using System.Text.Json;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxWebHostTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-games-api-{Guid.NewGuid():N}");

    public RetroBoxWebHostTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), $$"""
            floppies:
              disk1:
                label: Disk 1
                image: {{Path.Combine(root, "disk1.img")}}
                mode: ro
                size: 1.44M
              disk2:
                label: Disk 2
                image: {{Path.Combine(root, "disk2.img")}}
                mode: ro
                size: 1.44M
            """);
        File.WriteAllBytes(Path.Combine(root, "disk1.img"), []);
        File.WriteAllBytes(Path.Combine(root, "disk2.img"), []);
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
    public async Task Get_catalog_returns_the_current_floppies_as_camel_case_json()
    {
        var source = new RetroBoxStaticCatalogSource(
            FloppyControlTestCatalogs.CreateCatalog("disk1", "/data/floppies/disk1.img", RetroBoxFloppyCatalogRules.ReadOnlyMode));
        await using var host = await RetroBoxWebHost.StartAsync(new RetroBoxWebOptions { Port = 0 }, source);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var body = await client.GetStringAsync("/api/catalog");

        Assert.Contains("\"ungroupedFloppies\"", body, StringComparison.Ordinal);
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

    [Fact]
    public async Task Get_catalog_returns_games_with_their_floppies_and_ungrouped_floppies()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  game:\n    label: Game\n    floppyIds: [disk1]\n");
        await using var context = await StartGamesAsync();

        var body = await context.Client.GetStringAsync("/api/catalog");

        Assert.Contains("\"games\":[{\"id\":\"game\",\"label\":\"Game\",\"floppies\":[{\"id\":\"disk1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ungroupedFloppies\":[{\"id\":\"disk2\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_games_creates_a_game_and_returns_it()
    {
        await using var context = await StartGamesAsync();

        using var response = await context.Client.PostAsync("/api/games", Json("{\"id\":\"monkey-island\",\"label\":\"Monkey Island\"}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"id\":\"monkey-island\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Monkey Island", new RetroBoxConfigStore(root).Load().Games["monkey-island"].Label);
    }

    [Fact]
    public async Task Games_create_assign_and_reload_into_grouped_and_ungrouped_catalog_output()
    {
        await using var context = await StartGamesAsync();

        using (var create = await context.Client.PostAsync(
            "/api/games",
            Json("{\"id\":\"monkey-island\",\"label\":\"Monkey Island\"}")))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        }

        using (var assign = await context.Client.PatchAsync(
            "/api/games/monkey-island",
            Json("{\"floppyIds\":[\"disk1\"]}")))
        {
            Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        }

        Assert.True(context.Source.TryReload());

        using var catalog = JsonDocument.Parse(await context.Client.GetStringAsync("/api/catalog"));
        var game = Assert.Single(catalog.RootElement.GetProperty("games").EnumerateArray());
        Assert.Equal("monkey-island", game.GetProperty("id").GetString());
        Assert.Equal("Monkey Island", game.GetProperty("label").GetString());
        Assert.Equal("disk1", Assert.Single(game.GetProperty("floppies").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("disk2", Assert.Single(catalog.RootElement.GetProperty("ungroupedFloppies").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Patch_games_replaces_membership_atomically()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  game:\n    label: Game\n    floppyIds: [disk1]\n");
        await using var context = await StartGamesAsync();

        using var response = await context.Client.PatchAsync("/api/games/game", Json("{\"label\":\"Renamed\",\"floppyIds\":[\"disk2\"]}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var game = new RetroBoxConfigStore(root).Load().Games["game"];
        Assert.Equal("Renamed", game.Label);
        Assert.Equal(["disk2"], game.FloppyIds);
    }

    [Fact]
    public async Task Delete_games_removes_only_the_group()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  game:\n    label: Game\n    floppyIds: [disk1]\n");
        await using var context = await StartGamesAsync();

        using var response = await context.Client.DeleteAsync("/api/games/game");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var catalog = new RetroBoxConfigStore(root).Load();
        Assert.Empty(catalog.Games);
        Assert.Contains("disk1", catalog.Floppies.Keys);
    }

    [Fact]
    public async Task Patch_games_rejects_a_floppy_already_in_another_game()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  first:\n    label: First\n    floppyIds: [disk1]\n  second:\n    label: Second\n");
        await using var context = await StartGamesAsync();

        using var response = await context.Client.PatchAsync("/api/games/second", Json("{\"floppyIds\":[\"disk1\"]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("duplicate-membership", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_games_rejects_an_unknown_floppy()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  game:\n    label: Game\n");
        await using var context = await StartGamesAsync();

        using var response = await context.Client.PatchAsync("/api/games/game", Json("{\"floppyIds\":[\"missing\"]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unknown-floppy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_games_rejects_an_invalid_request()
    {
        await using var context = await StartGamesAsync();

        using var response = await context.Client.PostAsync("/api/games", Json("{\"id\":\"BAD\",\"label\":\"\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid-request", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_games_reports_an_unavailable_catalog()
    {
        await using var context = await StartGamesAsync();
        File.AppendAllText(
            Path.Combine(root, "floppies.yaml"),
            $"  broken:\n    label: Broken\n    image: {Path.Combine(root, "missing.img")}\n    mode: ro\n    size: 1.44M\n");

        using var response = await context.Client.PostAsync("/api/games", Json("{\"id\":\"game\",\"label\":\"Game\"}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("catalog-unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task<GamesEndpointContext> StartGamesAsync()
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source);
        return new GamesEndpointContext(host, source, new HttpClient { BaseAddress = host.BaseAddress });
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed record GamesEndpointContext(
        RetroBoxWebHost Host,
        RetroBoxWatchingCatalogSource Source,
        HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync();
            Source.Dispose();
        }
    }
}
