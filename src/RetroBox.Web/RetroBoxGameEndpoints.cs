using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxGameEndpoints
{
    public static void Map(WebApplication app, IRetroBoxCatalogSource catalogSource, RetroBoxFloppyLibrary library)
    {
        app.MapPost("/api/games", (RetroBoxGameCreate request) => Create(request, library, catalogSource));
        app.MapPatch("/api/games/{id}", (string id, RetroBoxGamePatch request) => Update(id, request, library, catalogSource));
        app.MapDelete("/api/games/{id}", (string id) => Delete(id, library, catalogSource));
    }

    private static IResult Create(RetroBoxGameCreate request, RetroBoxFloppyLibrary library, IRetroBoxCatalogSource catalogSource)
    {
        if (!IsValidRequest(request.Id, request.Label))
        {
            return InvalidRequest();
        }

        try
        {
            var game = library.CreateGame(request.Id!, request.Label!);
            Refresh(catalogSource);
            return Results.Json(
                new RetroBoxGameView(request.Id!, game.Label, []),
                RetroBoxWebJsonContext.Default.RetroBoxGameView,
                statusCode: StatusCodes.Status201Created);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return CatalogUnavailable(ex);
        }
        catch (RetroBoxCatalogException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", ex.Message);
        }
    }

    private static IResult Update(string id, RetroBoxGamePatch request, RetroBoxFloppyLibrary library, IRetroBoxCatalogSource catalogSource)
    {
        if (!RetroBoxCatalogRules.IsValidId(id) || string.IsNullOrWhiteSpace(request.Label) && request.Label is not null || request.FloppyIds is null)
        {
            return InvalidRequest();
        }

        try
        {
            var game = library.UpdateGame(id, request.Label, request.FloppyIds);
            if (game is null)
            {
                return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", $"Unknown game '{id}'.");
            }

            Refresh(catalogSource);
            return Results.NoContent();
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return CatalogUnavailable(ex);
        }
        catch (RetroBoxCatalogException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "duplicate-membership", ex.Message);
        }
    }

    private static IResult Delete(string id, RetroBoxFloppyLibrary library, IRetroBoxCatalogSource catalogSource)
    {
        if (!RetroBoxCatalogRules.IsValidId(id))
        {
            return InvalidRequest();
        }

        try
        {
            if (!library.DeleteGame(id))
            {
                return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", $"Unknown game '{id}'.");
            }

            Refresh(catalogSource);
            return Results.NoContent();
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return CatalogUnavailable(ex);
        }
    }

    private static bool IsValidRequest(string? id, string? label)
    {
        return RetroBoxCatalogRules.IsValidId(id) && !string.IsNullOrWhiteSpace(label);
    }

    private static IResult InvalidRequest()
    {
        return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", "The game request is invalid.");
    }

    private static IResult CatalogUnavailable(RetroBoxCatalogUnavailableException ex)
    {
        return RetroBoxWebResults.Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
    }

    private static void Refresh(IRetroBoxCatalogSource catalogSource)
    {
        catalogSource.TryReload();
    }
}
