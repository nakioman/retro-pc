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
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", "A floppy id is required.");
        }

        // A confirmed request is exempt from the ownership check below -- the user already
        // answered it -- so without a tag identity it is a blind write onto whatever happens to
        // be seated now. The confirmation dialog is unbounded thinking time, so "now" and "when
        // the 409 was raised" are not the same disk.
        if (request.Confirm && string.IsNullOrEmpty(request.TagUid))
        {
            return RetroBoxWebResults.Error(
                StatusCodes.Status400BadRequest,
                "invalid-request",
                "A confirmed reassignment must carry the tag uid the conflict reported.");
        }

        if (nfcChannel is null)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status503ServiceUnavailable, "no-controller", "No floppy controller is connected.");
        }

        var catalog = catalogSource.Snapshot.Catalog;
        if (!catalog.Floppies.TryGetValue(request.FloppyId, out var floppy))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-floppy", $"Unknown floppy '{request.FloppyId}'.");
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
                return RetroBoxWebResults.Error(StatusCodes.Status409Conflict, "no-tag-present", "There is no floppy in the drive.");
            }

            tagUid = tag.Uid;

            // This narrows the window rather than closing it: what is left is the server's own
            // TAGID-to-WRITE round trip, the same gap every unconfirmed first write already has.
            // Closing it fully would need a firmware WRITE-if-uid-matches, which the protocol
            // does not have.
            if (!string.IsNullOrEmpty(request.TagUid)
                && !string.Equals(request.TagUid, tagUid, StringComparison.Ordinal))
            {
                return RetroBoxWebResults.Error(
                    StatusCodes.Status409Conflict,
                    "tag-changed",
                    $"The tag in the drive is now '{tagUid}', not '{request.TagUid}'. Nothing was written.");
            }

            // Re-read rather than reuse the snapshot taken before the round trip: that one can be
            // many seconds old by now (quarantine wait plus the five second command timeout), and
            // an assignment that landed in between would otherwise be invisible here. AssignTag
            // re-reads under its own lock, so only the warning was ever at stake.
            var previousFloppyId = FindOwner(
                catalogSource.Snapshot.Catalog, tagUid, request.FloppyId, driveState);

            if (previousFloppyId is not null && !request.Confirm)
            {
                return Results.Json(
                    new RetroBoxNfcWriteResult(
                        "tag-already-assigned",
                        previousFloppyId,
                        $"This tag is already assigned to '{previousFloppyId}'. Confirm to reassign it.",
                        tagUid),
                    RetroBoxWebJsonContext.Default.RetroBoxNfcWriteResult,
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (await nfcChannel.WriteTagAsync(request.FloppyId, floppy.Mode, cancellationToken) is not NfcResponse.Ok)
            {
                return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "write-failed", "The controller could not write the tag.");
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
            return RetroBoxWebResults.Error(StatusCodes.Status504GatewayTimeout, "write-unconfirmed", ex.Message);
        }
        catch (Exception ex) when (ex is RetroBoxNfcCommandUnavailableException or IOException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            // RetroBoxNfcCommandUnavailableException is exactly what RetroBoxNfcChannelHolder
            // throws once its channel is null (device unplugged) — the production shape of "no
            // controller," distinct from the defensive nfcChannel-is-null branch above, which a
            // real appliance never actually takes.
            return RetroBoxWebResults.Error(StatusCodes.Status503ServiceUnavailable, "no-controller", ex.Message);
        }

        try
        {
            library.AssignTag(request.FloppyId, tagUid, floppy.Mode);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
        }
        catch (RetroBoxFloppyModeChangedException ex)
        {
            // A concurrent PATCH changed the mode while the write was in flight, and
            // UpdateLabelAndMode already cleared this floppy's tag deliberately for exactly this
            // reason (mode is baked into the tag's payload) — committing this write now would
            // silently undo that. Caught by its own type, not the base RetroBoxCatalogException,
            // so an unrelated validation failure from store.Save is never mislabelled as this.
            return RetroBoxWebResults.Error(StatusCodes.Status409Conflict, "mode-changed", ex.Message);
        }

        catalogSource.TryReload();

        // Only now: the firmware answers a WRITE and then stays quiet, so it has to be asked to
        // re-announce the seated tag, and that answer comes back as an INSERT the daemon handles
        // against the catalog as it stands at that instant. Sent from inside WriteTagAsync it
        // raced the YAML save and this reload -- the mount guard would see Nfc: false and refuse
        // the floppy just assigned, logging "has no assigned tag" while the panel showed a green
        // badge, with a manual eject and reinsert as the only recovery.
        try
        {
            await nfcChannel.SendStatusAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is RetroBoxNfcCommandUnavailableException or IOException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            // The tag is written and the catalog committed, so this is not a failed request: all
            // that is lost is the automatic mount, which the next insert performs anyway.
        }

        return Results.Json(
            new RetroBoxNfcWriteResult("written", null, null, tagUid),
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
}
