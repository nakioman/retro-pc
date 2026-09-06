using System.Net;
using System.Text;
using System.Text.Json;
using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxScraperEndpointsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-scraper-endpoints-{Guid.NewGuid():N}");

    public RetroBoxScraperEndpointsTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.yaml"), "defaultVm: dos\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"vms:\n  dos:\n    label: DOS\n    path: {root}\n");
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
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
    public async Task Get_settings_redacts_secrets_and_put_preserves_omitted_secrets_but_clears_empty_ones()
    {
        new RetroBoxScraperSettingsStore(root).Save(new RetroBoxScraperSettings
        {
            DevId = "developer-id",
            DevPassword = "developer-password",
            SsId = "user-id",
            SsPassword = "user-password",
        });
        await using var context = await StartAsync();

        var body = await context.Client.GetStringAsync("/api/settings/scraper");

        Assert.DoesNotContain("developer-id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("developer-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("user-id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("user-password", body, StringComparison.Ordinal);
        Assert.Contains("\"configured\":true", body, StringComparison.Ordinal);

        using var response = await context.Client.PutAsync(
            "/api/settings/scraper",
            Json("{\"devPassword\":\"\",\"regionPriority\":[\"us\",\"eu\",\"wor\",\"sp\"],\"languagePriority\":[\"en\",\"es\"]}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = new RetroBoxScraperSettingsStore(root).Load();
        Assert.Equal("developer-id", saved.DevId);
        Assert.Equal(string.Empty, saved.DevPassword);
        Assert.Equal("user-id", saved.SsId);
        Assert.Equal("user-password", saved.SsPassword);
        Assert.Equal(["us", "eu", "wor", "sp"], saved.RegionPriority);
        Assert.Equal(["en", "es"], saved.LanguagePriority);
    }

    [Fact]
    public async Task Put_settings_rejects_priority_lists_outside_the_closed_supported_values()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PutAsync(
            "/api/settings/scraper",
            Json("{\"regionPriority\":[\"sp\",\"wor\",\"eu\",\"jp\"],\"languagePriority\":[\"es\",\"en\"]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid-priorities", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_and_search_require_all_four_credentials()
    {
        await using var context = await StartAsync();

        using var test = await context.Client.PostAsync("/api/settings/scraper/test", null);
        using var search = await context.Client.GetAsync("/api/scraper/search?q=doom");

        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, search.StatusCode);
        Assert.Contains("scraper-not-configured", await test.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("scraper-not-configured", await search.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_returns_only_results_with_a_usable_box_cover()
    {
        ConfigureCredentials();
        var source = new FakeCoverSource
        {
            SearchResults =
            [
                new RetroBoxCoverSearchResult("1", "Doom", [new RetroBoxCoverMedia("box-2D", "https://covers.example/doom.png", "sp", "es")]),
                new RetroBoxCoverSearchResult("2", "No Cover", [new RetroBoxCoverMedia("video", "https://covers.example/nope.mp4", null, null)]),
            ],
        };
        await using var context = await StartAsync(source);

        var body = await context.Client.GetStringAsync("/api/scraper/search?q=doom");

        Assert.Contains("\"screenScraperId\":\"1\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("No Cover", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirmed_cover_replaces_the_cached_file_and_persists_the_screen_scraper_id()
    {
        ConfigureCredentials();
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  doom:\n    label: Doom\n    cover: doom.jpg\n");
        Directory.CreateDirectory(Path.Combine(root, "covers"));
        File.WriteAllBytes(Path.Combine(root, "covers", "doom.jpg"), [1, 2, 3]);
        var source = new FakeCoverSource
        {
            GameMedia = [new RetroBoxCoverMedia("box-2D", "https://covers.example/doom.jpg", "sp", "es")],
        };
        await using var context = await StartAsync(source, (_, _) => Task.FromResult<Stream>(new MemoryStream([4, 5, 6])));

        using var response = await context.Client.PostAsync("/api/games/doom/cover", Json("{\"screenScraperId\":\"42\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([4, 5, 6], File.ReadAllBytes(Path.Combine(root, "covers", "doom.jpg")));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "covers"), "*.backup"));
        var game = new RetroBoxConfigStore(root).Load().Games["doom"];
        Assert.Equal("doom.jpg", game.Cover);
        Assert.Equal(42, game.ScreenScraperId);
    }

    [Fact]
    public async Task Failed_cover_download_leaves_the_previous_cover_and_catalog_unchanged()
    {
        ConfigureCredentials();
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  doom:\n    label: Doom\n    cover: doom.jpg\n    screenScraperId: 17\n");
        Directory.CreateDirectory(Path.Combine(root, "covers"));
        File.WriteAllBytes(Path.Combine(root, "covers", "doom.jpg"), [1, 2, 3]);
        var source = new FakeCoverSource
        {
            GameMedia = [new RetroBoxCoverMedia("box-2D", "https://covers.example/doom.png", "sp", "es")],
        };
        await using var context = await StartAsync(source, (_, _) => throw new HttpRequestException("simulated download failure"));

        using var response = await context.Client.PostAsync("/api/games/doom/cover", Json("{\"screenScraperId\":\"42\"}"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(root, "covers", "doom.jpg")));
        var game = new RetroBoxConfigStore(root).Load().Games["doom"];
        Assert.Equal("doom.jpg", game.Cover);
        Assert.Equal(17, game.ScreenScraperId);
    }

    [Theory]
    [MemberData(nameof(SupportedImages))]
    public async Task Upload_cover_replaces_the_file_and_clears_the_scraper_id_for_supported_images(
        string fileName,
        string contentType,
        byte[] image)
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  doom:\n    label: Doom\n    cover: doom.jpg\n    screenScraperId: 17\n");
        Directory.CreateDirectory(Path.Combine(root, "covers"));
        File.WriteAllBytes(Path.Combine(root, "covers", "doom.jpg"), [1, 2, 3]);
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/games/doom/cover/upload", Upload(fileName, contentType, image));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(image, File.ReadAllBytes(Path.Combine(root, "covers", fileName)));
        var game = new RetroBoxConfigStore(root).Load().Games["doom"];
        Assert.Equal(fileName, game.Cover);
        Assert.Null(game.ScreenScraperId);
    }

    [Theory]
    [InlineData("doom.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData("doom.bmp", "image/bmp", new byte[] { 0x42, 0x4d })]
    public async Task Upload_cover_rejects_unsupported_extensions_without_replacing_existing_state(
        string fileName,
        string contentType,
        byte[] image)
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  doom:\n    label: Doom\n    cover: doom.jpg\n    screenScraperId: 17\n");
        Directory.CreateDirectory(Path.Combine(root, "covers"));
        File.WriteAllBytes(Path.Combine(root, "covers", "doom.jpg"), [1, 2, 3]);
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/games/doom/cover/upload", Upload(fileName, contentType, image));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsupported-extension", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(root, "covers", "doom.jpg")));
        Assert.Equal(17, new RetroBoxConfigStore(root).Load().Games["doom"].ScreenScraperId);
    }

    [Fact]
    public async Task Upload_cover_rejects_malformed_content_without_replacing_existing_state()
    {
        File.WriteAllText(Path.Combine(root, "games.yaml"), "games:\n  doom:\n    label: Doom\n    cover: doom.jpg\n    screenScraperId: 17\n");
        Directory.CreateDirectory(Path.Combine(root, "covers"));
        File.WriteAllBytes(Path.Combine(root, "covers", "doom.jpg"), [1, 2, 3]);
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync("/api/games/doom/cover/upload", Upload("doom.png", "image/png", [1, 2, 3]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid-image", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(root, "covers", "doom.jpg")));
        Assert.Equal(17, new RetroBoxConfigStore(root).Load().Games["doom"].ScreenScraperId);
    }

    [Fact]
    public async Task Upload_cover_rejects_oversized_requests()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync(
            "/api/games/doom/cover/upload",
            Upload("doom.jpg", "image/jpeg", new byte[RetroBoxLibraryEndpoints.MaxUploadBytes + 1]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("file-too-large", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_cover_reports_an_unknown_game_after_validating_the_image()
    {
        await using var context = await StartAsync();

        using var response = await context.Client.PostAsync(
            "/api/games/missing/cover/upload",
            Upload("doom.jpg", "image/jpeg", [0xff, 0xd8, 0xff, 0xc0, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0, 0xff, 0xd9]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("unknown-game", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private void ConfigureCredentials()
    {
        new RetroBoxScraperSettingsStore(root).Save(new RetroBoxScraperSettings
        {
            DevId = "developer",
            DevPassword = "developer-password",
            SsId = "user",
            SsPassword = "user-password",
        });
    }

    private async Task<EndpointContext> StartAsync(
        IRetroBoxCoverSource? coverSource = null,
        Func<Uri, CancellationToken, Task<Stream>>? downloadCover = null)
    {
        var store = new RetroBoxConfigStore(root);
        var source = new RetroBoxWatchingCatalogSource(root, store.Load(), watchFileSystem: false);
        var host = await RetroBoxWebHost.StartAsync(
            new RetroBoxWebOptions { Port = 0, ConfigRoot = root },
            source,
            coverSource: coverSource,
            downloadCover: downloadCover);
        return new EndpointContext(host, source, new HttpClient { BaseAddress = host.BaseAddress });
    }

    private static StringContent Json(string value) => new(value, System.Text.Encoding.UTF8, "application/json");

    private static MultipartFormDataContent Upload(string fileName, string contentType, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    public static IEnumerable<object[]> SupportedImages()
    {
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xc0, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0, 0xff, 0xd9 };
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x49, 0x45, 0x4e, 0x44, 0, 0, 0, 0 };
        var webp = new byte[] { 0x52, 0x49, 0x46, 0x46, 22, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x58, 10, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0 };
        return
        [
            ["doom.jpg", "image/jpeg", jpeg],
            ["doom.jpeg", "image/jpeg", jpeg],
            ["doom.png", "image/png", png],
            ["doom.webp", "image/webp", webp],
        ];
    }

    private sealed record EndpointContext(
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

    private sealed class FakeCoverSource : IRetroBoxCoverSource
    {
        public IReadOnlyList<RetroBoxCoverSearchResult> SearchResults { get; init; } = [];

        public IReadOnlyList<RetroBoxCoverMedia> GameMedia { get; init; } = [];

        public Task<IReadOnlyList<RetroBoxCoverSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(SearchResults);

        public Task<IReadOnlyList<RetroBoxCoverMedia>> GetGameAsync(string screenScraperId, CancellationToken cancellationToken) =>
            Task.FromResult(GameMedia);
    }
}
