using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxNfcEndpoints
{
    public static void Map(
        WebApplication app,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel)
    {
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot));

        app.MapPost("/api/nfc/write", (RetroBoxNfcWriteRequest request, CancellationToken cancellationToken) =>
            WriteAsync(request, library, catalogSource, nfcChannel, cancellationToken));
    }

    private static async Task<IResult> WriteAsync(
        RetroBoxNfcWriteRequest request,
        RetroBoxFloppyLibrary library,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel,
        CancellationToken cancellationToken)
    {
        if (nfcChannel is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "no-controller", "No floppy controller is connected.");
        }

        var catalog = catalogSource.Snapshot.Catalog;
        if (!catalog.Floppies.TryGetValue(request.FloppyId, out var floppy))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", $"Unknown floppy '{request.FloppyId}'.");
        }

        // Every serial exchange happens outside RetroBoxFloppyLibrary's lock. Each is bounded at
        // five seconds, and that lock is also taken by upload, delete and rename — holding it
        // across two round trips would freeze the whole library on a wedged controller.
        string tagUid;

        try
        {
            var presence = await nfcChannel.ReadTagIdAsync(cancellationToken);
            if (presence is not NfcResponse.TagId tag)
            {
                return Error(StatusCodes.Status409Conflict, "no-tag-present", "There is no floppy in the drive.");
            }

            tagUid = tag.Uid;

            var currentOwner = catalog.Floppies
                .FirstOrDefault(entry =>
                    !string.Equals(entry.Key, request.FloppyId, StringComparison.Ordinal)
                    && string.Equals(entry.Value.NfcUid, tagUid, StringComparison.Ordinal));

            if (currentOwner.Key is not null && !request.Confirm)
            {
                return Results.Json(
                    new RetroBoxNfcWriteResult("tag-already-assigned", currentOwner.Key),
                    RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult,
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (await nfcChannel.WriteTagAsync(request.FloppyId, floppy.Mode, cancellationToken) is not NfcResponse.Ok)
            {
                return Error(StatusCodes.Status502BadGateway, "write-failed", "The controller could not write the tag.");
            }
        }
        catch (RetroBoxNfcCommandTimeoutException ex)
        {
            // The write may or may not have landed, so this is not reported as a failure. The
            // read-back the spec asks for happens through the drive stream rather than inline:
            // an inline TAGID would first have to wait out the orphan quarantine the timeout just
            // opened, turning one request into roughly fifteen seconds, and the panel is already
            // subscribed to /api/drive/events, which performs exactly that TAGID probe within a
            // couple of seconds and shows what is actually on the tag.
            return Error(StatusCodes.Status504GatewayTimeout, "write-unconfirmed", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "no-controller", ex.Message);
        }

        try
        {
            library.AssignTag(request.FloppyId, tagUid);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
        }

        catalogSource.TryReload();

        return Results.Json(
            new RetroBoxNfcWriteResult("written", null),
            RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult);
    }

    private static IResult Error(int statusCode, string code, string message)
    {
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.RetroBoxErrorView,
            statusCode: statusCode);
    }
}
