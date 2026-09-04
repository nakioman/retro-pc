using System.Reflection;

namespace RetroBox.Web;

/// <summary>
/// Serves the panel from embedded resources so the appliance stays a single binary at
/// /opt/retrobox/retrobox with no wwwroot for the installer to copy.
/// </summary>
public static class RetroBoxStaticAssets
{
    private const string ResourcePrefix = "RetroBox.Web.wwwroot.";

    public static bool TryGet(string relativePath, out byte[] content, out string contentType)
    {
        content = [];
        contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var resourceName = ResourcePrefix + relativePath.Replace('/', '.');
        var assembly = typeof(RetroBoxStaticAssets).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return false;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        content = buffer.ToArray();
        contentType = ResolveContentType(relativePath);
        return true;
    }

    private static string ResolveContentType(string relativePath)
    {
        return Path.GetExtension(relativePath) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
    }
}
