using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxScraperEndpoints
{
    private static readonly HashSet<string> ValidRegions = [.. RetroBoxScraperSettings.DefaultRegionPriority];
    private static readonly HashSet<string> ValidLanguages = [.. RetroBoxScraperSettings.DefaultLanguagePriority];

    public static void Map(
        WebApplication app,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory)
    {
        app.MapGet("/api/settings/scraper", () => GetSettings(settingsStore));
        app.MapPut("/api/settings/scraper", (RetroBoxScraperSettingsPatch patch) => UpdateSettings(patch, settingsStore));
        app.MapPost("/api/settings/scraper/test", (CancellationToken cancellationToken) => TestAsync(settingsStore, coverSourceFactory, cancellationToken));
        app.MapGet("/api/scraper/search", (string? q, CancellationToken cancellationToken) =>
            SearchAsync(q, settingsStore, coverSourceFactory, cancellationToken));
    }

    private static IResult GetSettings(RetroBoxScraperSettingsStore settingsStore)
    {
        return Results.Json(
            RetroBoxScraperSettingsView.From(settingsStore.Load()),
            RetroBoxWebJsonContext.Default.RetroBoxScraperSettingsView);
    }

    private static IResult UpdateSettings(RetroBoxScraperSettingsPatch patch, RetroBoxScraperSettingsStore settingsStore)
    {
        if (patch.RegionPriority is not null && !IsClosedPriority(patch.RegionPriority, ValidRegions)
            || patch.LanguagePriority is not null && !IsClosedPriority(patch.LanguagePriority, ValidLanguages))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-priorities", "Priority lists must contain each supported value exactly once.");
        }

        var settings = settingsStore.Load();
        if (patch.DevId is not null)
        {
            settings.DevId = patch.DevId;
        }

        if (patch.DevPassword is not null)
        {
            settings.DevPassword = patch.DevPassword;
        }

        if (patch.SsId is not null)
        {
            settings.SsId = patch.SsId;
        }

        if (patch.SsPassword is not null)
        {
            settings.SsPassword = patch.SsPassword;
        }

        if (patch.RegionPriority is not null)
        {
            settings.RegionPriority = patch.RegionPriority;
        }

        if (patch.LanguagePriority is not null)
        {
            settings.LanguagePriority = patch.LanguagePriority;
        }

        settingsStore.Save(settings);
        return Results.Json(
            RetroBoxScraperSettingsView.From(settings),
            RetroBoxWebJsonContext.Default.RetroBoxScraperSettingsView);
    }

    private static async Task<IResult> TestAsync(
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        CancellationToken cancellationToken)
    {
        if (!settingsStore.Load().IsConfigured)
        {
            return ScraperNotConfigured();
        }

        try
        {
            await coverSourceFactory().SearchAsync("RetroBox", cancellationToken);
            return Results.NoContent();
        }
        catch (RetroBoxScreenScraperException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "scraper-failed", ex.Message);
        }
    }

    private static async Task<IResult> SearchAsync(
        string? query,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        CancellationToken cancellationToken)
    {
        if (!settingsStore.Load().IsConfigured)
        {
            return ScraperNotConfigured();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-query", "A search query is required.");
        }

        try
        {
            var results = await coverSourceFactory().SearchAsync(query, cancellationToken);
            var response = results.Where(result => result.Media.Any(RetroBoxCoverSelector.IsUsableBox2D))
                .Select(result => new RetroBoxScraperSearchResultView(result.ScreenScraperId, result.Title))
                .ToArray();
            return Results.Json(response, RetroBoxWebJsonContext.Default.RetroBoxScraperSearchResultViewArray);
        }
        catch (RetroBoxScreenScraperException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "scraper-failed", ex.Message);
        }
    }

    private static bool IsClosedPriority(string[] values, HashSet<string> allowed)
    {
        return values.Length == allowed.Count && values.All(allowed.Contains) && values.Distinct(StringComparer.Ordinal).Count() == allowed.Count;
    }

    private static IResult ScraperNotConfigured()
    {
        return RetroBoxWebResults.Error(StatusCodes.Status409Conflict, "scraper-not-configured", "All ScreenScraper credentials are required.");
    }
}
