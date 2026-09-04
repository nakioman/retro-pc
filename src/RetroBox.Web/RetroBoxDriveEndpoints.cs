using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxDriveEndpoints
{
    public const string Unavailable = "unavailable";
    public const string Empty = "empty";
    public const string Loaded = "loaded";
    public const string BlankTag = "blankTag";

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public static void Map(WebApplication app, IRetroBoxDriveState? driveState, IRetroBoxNfcCommandChannel? nfcChannel)
    {
        app.MapGet("/api/drive", () => BuildViewAsync(driveState, nfcChannel));
        app.MapGet("/api/drive/events", (HttpContext context) => StreamAsync(context, driveState, nfcChannel));
    }

    /// <summary>
    /// A blank tag never raises an INSERT — the firmware cannot read a payload from it — so the
    /// event stream alone can never tell "no disk" from "a new disk waiting to be assigned".
    /// TAGID is the only way to ask.
    /// </summary>
    public static async Task<RetroBoxDriveView> BuildViewAsync(
        IRetroBoxDriveState? driveState,
        IRetroBoxNfcCommandChannel? nfcChannel,
        CancellationToken cancellationToken = default)
    {
        if (driveState is null || nfcChannel is null)
        {
            return new RetroBoxDriveView(Unavailable, null, null, null);
        }

        if (driveState.Current is RetroBoxDriveState.Loaded loaded)
        {
            return new RetroBoxDriveView(Loaded, loaded.FloppyId, loaded.Mode, null);
        }

        try
        {
            return await nfcChannel.ReadTagIdAsync(cancellationToken) switch
            {
                NfcResponse.TagId tag => new RetroBoxDriveView(BlankTag, null, null, tag.Uid),
                _ => new RetroBoxDriveView(Empty, null, null, null),
            };
        }
        catch (Exception ex) when (ex is RetroBoxNfcCommandTimeoutException or RetroBoxNfcCommandUnavailableException
            or IOException)
        {
            // A controller that stops answering, or a channel with nothing behind it right now
            // (RetroBoxNfcChannelHolder between connections), is reported as unavailable rather
            // than as an empty drive: "no disk" is a claim, and this code no longer knows.
            return new RetroBoxDriveView(Unavailable, null, null, null);
        }
    }

    private static async Task StreamAsync(
        HttpContext context,
        IRetroBoxDriveState? driveState,
        IRetroBoxNfcCommandChannel? nfcChannel)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        string? lastPayload = null;

        while (!context.RequestAborted.IsCancellationRequested)
        {
            var view = await BuildViewAsync(driveState, nfcChannel, context.RequestAborted);
            var payload = JsonSerializer.Serialize(view, RetroBoxWebJsonContext.Default.RetroBoxDriveView);

            if (payload != lastPayload)
            {
                lastPayload = payload;
                await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }

            try
            {
                await Task.Delay(PollInterval, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
