using System.Text.Json;
using System.Text.Json.Serialization;
using RetroBox.Core;

namespace RetroBox.Web;

public sealed class RetroBoxScreenScraperCoverSource(HttpClient client, RetroBoxScraperSettings settings) : IRetroBoxCoverSource
{
    public const string HttpClientName = "screenscraper";

    public static readonly Uri ApiBaseAddress = new("https://api.screenscraper.fr/api2/");

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public TimeSpan ConfiguredTimeout => TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 5, 600));

    public async Task<IReadOnlyList<RetroBoxCoverSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var response = await GetAsync(
            "jeuRecherche.php",
            [("recherche", query), ("systemeid", "135")],
            cancellationToken);
        var games = response.Response?.Games ?? [];

        return games.Select(ToSearchResult)
            .Where(result => result.Media.Any(RetroBoxCoverSelector.IsUsableBox2D))
            .ToArray();
    }

    public async Task<IReadOnlyList<RetroBoxCoverMedia>> GetGameAsync(string screenScraperId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenScraperId);
        var response = await GetAsync(
            "jeuInfos.php",
            [("gameid", screenScraperId)],
            cancellationToken);

        return ToMedia(response.Response?.Game?.Media);
    }

    private async Task<RetroBoxScreenScraperResponse> GetAsync(
        string path,
        (string Name, string Value)[] parameters,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConfiguredTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(path, parameters));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var payload = await JsonSerializer.DeserializeAsync(
            stream,
            RetroBoxScreenScraperJsonContext.Default.RetroBoxScreenScraperResponse,
            timeout.Token) ?? throw new RetroBoxScreenScraperException("ScreenScraper returned an empty response.");

        var error = payload.Header?.Error;
        if (string.Equals(payload.Header?.Success, "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new RetroBoxScreenScraperException(
                string.IsNullOrWhiteSpace(error) ? "ScreenScraper rejected the request." : error);
        }

        return payload;
    }

    private Uri BuildRequestUri(string path, IEnumerable<(string Name, string Value)> parameters)
    {
        var credentials = new[]
        {
            ("devid", settings.DevId!),
            ("devpassword", settings.DevPassword!),
            ("ssid", settings.SsId!),
            ("sspassword", settings.SsPassword!),
            ("output", "json"),
        };
        var query = credentials.Concat(parameters)
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Item1)}={Uri.EscapeDataString(parameter.Item2)}");
        return new Uri(ApiBaseAddress, $"{path}?{string.Join("&", query)}");
    }

    private void EnsureConfigured()
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("ScreenScraper credentials are not configured.");
        }
    }

    private static RetroBoxCoverSearchResult ToSearchResult(RetroBoxScreenScraperGame game)
    {
        var title = game.Names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name.Text))?.Text ?? game.Id;
        return new RetroBoxCoverSearchResult(game.Id, title, ToMedia(game.Media));
    }

    private static IReadOnlyList<RetroBoxCoverMedia> ToMedia(RetroBoxScreenScraperMedia[]? media)
    {
        return media?.Select(item => new RetroBoxCoverMedia(item.Type, item.Url, item.Region, item.Language)).ToArray() ?? [];
    }
}

public sealed class RetroBoxScreenScraperException(string message) : Exception(message);

internal sealed record RetroBoxScreenScraperResponse(
    [property: JsonPropertyName("header")] RetroBoxScreenScraperHeader? Header,
    [property: JsonPropertyName("response")] RetroBoxScreenScraperPayload? Response);

internal sealed record RetroBoxScreenScraperHeader(
    [property: JsonPropertyName("success")] string? Success,
    [property: JsonPropertyName("error")] string? Error);

internal sealed record RetroBoxScreenScraperPayload(
    [property: JsonPropertyName("jeux")] RetroBoxScreenScraperGame[]? Games,
    [property: JsonPropertyName("jeu")] RetroBoxScreenScraperGame? Game);

internal sealed record RetroBoxScreenScraperGame(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("noms")] RetroBoxScreenScraperName[]? Names,
    [property: JsonPropertyName("medias")] RetroBoxScreenScraperMedia[]? Media);

internal sealed record RetroBoxScreenScraperName([property: JsonPropertyName("text")] string? Text);

internal sealed record RetroBoxScreenScraperMedia(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("langue")] string? Language);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RetroBoxScreenScraperResponse))]
internal sealed partial class RetroBoxScreenScraperJsonContext : JsonSerializerContext;
