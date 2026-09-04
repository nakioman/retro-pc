using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxNfcEndpoints
{
    // library is the same RetroBoxFloppyLibrary instance RetroBoxWebHost.StartAsync gives to
    // RetroBoxLibraryEndpoints.Map: a private instance here would carry its own private lock,
    // and this endpoint's catalog commit would then race an in-flight upload/delete/rename
    // instead of serialising behind it.
    public static void Map(
        WebApplication app,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel,
        RetroBoxFloppyLibrary library,
        IRetroBoxDriveState? driveState)
    {
        app.MapPost("/api/nfc/write", (RetroBoxNfcWriteRequest request, CancellationToken cancellationToken) =>
            WriteAsync(request, library, catalogSource, nfcChannel, driveState, cancellationToken));
    }

    private static async Task<IResult> WriteAsync(
        RetroBoxNfcWriteRequest request,
        RetroBoxFloppyLibrary library,
        IRetroBoxCatalogSource catalogSource,
        IRetroBoxNfcCommandChannel? nfcChannel,
        IRetroBoxDriveState? driveState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.FloppyId))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid-request", "A floppy id is required.");
        }

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

            var previousFloppyId = FindOwner(catalog, tagUid, request.FloppyId, driveState);

            if (previousFloppyId is not null && !request.Confirm)
            {
                return Results.Json(
                    new RetroBoxNfcWriteResult(
                        "tag-already-assigned",
                        previousFloppyId,
                        $"This tag is already assigned to '{previousFloppyId}'. Confirm to reassign it."),
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
        catch (Exception ex) when (ex is RetroBoxNfcCommandUnavailableException or IOException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            // RetroBoxNfcCommandUnavailableException is exactly what RetroBoxNfcChannelHolder
            // throws once its channel is null (device unplugged) — the production shape of "no
            // controller," distinct from the defensive nfcChannel-is-null branch above, which a
            // real appliance never actually takes.
            return Error(StatusCodes.Status503ServiceUnavailable, "no-controller", ex.Message);
        }

        try
        {
            library.AssignTag(request.FloppyId, tagUid, floppy.Mode);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
        }
        catch (RetroBoxFloppyModeChangedException ex)
        {
            // A concurrent PATCH changed the mode while the write was in flight, and
            // UpdateLabelAndMode already cleared this floppy's tag deliberately for exactly this
            // reason (mode is baked into the tag's payload) — committing this write now would
            // silently undo that. Caught by its own type, not the base RetroBoxCatalogException,
            // so an unrelated validation failure from store.Save is never mislabelled as this.
            return Error(StatusCodes.Status409Conflict, "mode-changed", ex.Message);
        }

        catalogSource.TryReload();

        return Results.Json(
            new RetroBoxNfcWriteResult("written", null, null),
            RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult);
    }

    /// <summary>
    /// Names the floppy that already owns <paramref name="tagUid"/>, or null when nothing does.
    /// </summary>
    private static string? FindOwner(
        RetroBoxCatalogData catalog,
        string tagUid,
        string requestedFloppyId,
        IRetroBoxDriveState? driveState)
    {
        var recorded = catalog.Floppies
            .FirstOrDefault(entry =>
                !string.Equals(entry.Key, requestedFloppyId, StringComparison.Ordinal)
                && string.Equals(entry.Value.NfcUid, tagUid, StringComparison.Ordinal));

        if (recorded.Key is not null)
        {
            return recorded.Key;
        }

        // AssignTag is the only writer of NfcUid, so on an appliance that predates this phase
        // every tagged floppy carries Nfc: true with NfcUid: null and the recorded check above
        // can never match — the first panel assignment would silently steal the tag. The
        // firmware knows better: a non-blank tag names its owner in the INSERT the tracker
        // observed, whatever the catalog happens to have written down. This branch only ever
        // adds a warning the recorded check missed, so an Unknown tracker (no controller has
        // reported yet) degrades to exactly the recorded-only behaviour.
        return driveState?.Current is RetroBoxDriveState.Loaded loaded
            && !string.Equals(loaded.FloppyId, requestedFloppyId, StringComparison.Ordinal)
            ? loaded.FloppyId
            : null;
    }

    private static IResult Error(int statusCode, string code, string message)
    {
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.RetroBoxErrorView,
            statusCode: statusCode);
    }
}
