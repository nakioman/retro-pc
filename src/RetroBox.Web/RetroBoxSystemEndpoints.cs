using Microsoft.AspNetCore.Builder;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxSystemEndpoints
{
    // The config root lives on /data on the appliance, so measuring it reports the free space
    // of the filesystem that holds every floppy image, VM disk, and cover.
    public static void Map(WebApplication app, RetroBoxWebOptions options) =>
        app.MapGet("/api/system", () => BuildView(options.ConfigRoot));

    public static RetroBoxSystemView BuildView(string dataPath) =>
        new(RetroBoxDiskSpace.GetAvailableFreeBytes(dataPath));
}
