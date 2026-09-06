using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxCoverEndpoints
{
    public static void Map(
        WebApplication app,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        RetroBoxCoverCache coverCache)
    {
        app.MapPost("/api/games/{id}/cover", (string id, RetroBoxCoverRequest request, CancellationToken cancellationToken) =>
            ReplaceAsync(id, request, settingsStore, coverSourceFactory, coverCache, cancellationToken));
    }

    private static async Task<IResult> ReplaceAsync(
        string id,
        RetroBoxCoverRequest request,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        RetroBoxCoverCache coverCache,
        CancellationToken cancellationToken)
    {
        if (!settingsStore.Load().IsConfigured)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status409Conflict, "scraper-not-configured", "All ScreenScraper credentials are required.");
        }

        if (!RetroBoxCatalogRules.IsValidId(id) || !int.TryParse(request.ScreenScraperId, out var screenScraperId) || screenScraperId < 1)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", "The cover request is invalid.");
        }

        try
        {
            var media = await coverSourceFactory().GetGameAsync(request.ScreenScraperId!, cancellationToken);
            var selected = new RetroBoxCoverSelector().Select(
                media,
                settingsStore.Load().RegionPriority,
                settingsStore.Load().LanguagePriority);
            if (selected?.Url is null || !Uri.TryCreate(selected.Url, UriKind.Absolute, out var source))
            {
                return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "cover-not-found", "No usable box cover was found.");
            }

            var cover = await coverCache.ReplaceAsync(id, screenScraperId, source, cancellationToken);
            if (cover is null)
            {
                return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", $"Unknown game '{id}'.");
            }

            return Results.Json(cover, RetroBoxWebJsonContext.Default.RetroBoxCoverView);
        }
        catch (RetroBoxUnknownGameException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", ex.Message);
        }
        catch (RetroBoxScreenScraperException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "scraper-failed", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "cover-download-failed", ex.Message);
        }
    }
}
