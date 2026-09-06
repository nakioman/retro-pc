using RetroBox.Core;

namespace RetroBox.Web;

public sealed class RetroBoxCoverCache(
    string coversRoot,
    RetroBoxFloppyLibrary library,
    Func<Uri, CancellationToken, Task<Stream>> download,
    TimeSpan? copyTimeout = null)
{
    public const string HttpClientName = "covers";

    public const long MaxDownloadBytes = RetroBoxLibraryEndpoints.MaxUploadBytes;

    private readonly TimeSpan copyTimeout = copyTimeout ?? RetroBoxScreenScraperCoverSource.RequestTimeout;
    public long MaximumDownloadBytes { get; set; } = 16 * 1024 * 1024;

    public string CreateStagingPath(string extension)
    {
        Directory.CreateDirectory(coversRoot);
        return Path.Combine(coversRoot, $".upload-{Guid.NewGuid():N}{extension}");
    }

    public bool TryGetCachedCover(string cover, out string path, out string contentType)
    {
        path = string.Empty;
        contentType = "application/octet-stream";
        if (string.IsNullOrWhiteSpace(cover)
            || cover != Path.GetFileName(cover)
            || cover.Contains('\\')
            || Path.IsPathRooted(cover))
        {
            return false;
        }

        contentType = Path.GetExtension(cover).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
        if (contentType == "application/octet-stream")
        {
            return false;
        }

        var root = Path.GetFullPath(coversRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, cover));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    public async Task<RetroBoxCoverView?> ReplaceAsync(
        string gameId,
        int screenScraperId,
        Uri source,
        CancellationToken cancellationToken)
    {
        var extension = ResolveExtension(source);
        var cover = $"{gameId}{extension}";
        Directory.CreateDirectory(coversRoot);

        var stagedPath = Path.Combine(coversRoot, $".{gameId}-{Guid.NewGuid():N}.download");
        try
        {
            extension = await DownloadToStagingAsync(source, stagedPath, extension, cancellationToken);
            cover = $"{gameId}{extension}";

            var updated = false;
            library.RunExclusively(() =>
            {
                ReplaceUnderLock(gameId, cover, screenScraperId, stagedPath);
                updated = true;
            });
            return updated ? new RetroBoxCoverView(cover, screenScraperId) : null;
        }
        finally
        {
            SafeDelete(stagedPath);
        }
    }

    public Task<RetroBoxCoverView?> ReplaceUploadAsync(
        string gameId,
        string extension,
        string stagedPath,
        CancellationToken cancellationToken)
    {
        var cover = $"{gameId}{extension}";
        var updated = false;
        library.RunExclusively(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceUnderLock(gameId, cover, null, stagedPath);
            updated = true;
        });
        return Task.FromResult<RetroBoxCoverView?>(updated ? new RetroBoxCoverView(cover, null) : null);
    }

    private void ReplaceUnderLock(string gameId, string cover, int? screenScraperId, string stagedPath)
    {
        var game = library.GetGame(gameId) ?? throw new RetroBoxUnknownGameException($"Unknown game '{gameId}'.");
        var finalPath = Path.Combine(coversRoot, cover);
        var previousPath = game.Cover is null ? null : Path.Combine(coversRoot, Path.GetFileName(game.Cover));
        var backupPath = previousPath is not null && string.Equals(previousPath, finalPath, StringComparison.Ordinal)
            ? Path.Combine(coversRoot, $".{gameId}-{Guid.NewGuid():N}.backup")
            : null;
        var catalogSaved = false;
        var newFileInstalled = false;

        try
        {
            if (backupPath is not null && File.Exists(finalPath))
            {
                File.Move(finalPath, backupPath);
            }

            File.Move(stagedPath, finalPath);
            newFileInstalled = true;
            if (library.UpdateGameCover(gameId, cover, screenScraperId) is null)
            {
                throw new RetroBoxUnknownGameException($"Unknown game '{gameId}'.");
            }

            catalogSaved = true;
        }
        finally
        {
            if (!catalogSaved)
            {
                if (newFileInstalled)
                {
                    SafeDelete(finalPath);
                }

                if (backupPath is not null && File.Exists(backupPath))
                {
                    File.Move(backupPath, finalPath);
                }
            }
            else
            {
                if (previousPath is not null && !string.Equals(previousPath, finalPath, StringComparison.Ordinal))
                {
                    SafeDelete(previousPath);
                }

                if (backupPath is not null)
                {
                    SafeDelete(backupPath);
                }
            }
        }
    }

    private async Task<string> DownloadToStagingAsync(
        Uri source,
        string stagedPath,
        string extension,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(copyTimeout);
        await using var input = await download(source, timeout.Token);
        await using (var output = File.Create(stagedPath))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                downloaded += read;
                if (downloaded > MaximumDownloadBytes)
                {
                    throw new InvalidDataException("The downloaded cover exceeds the size limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            await output.FlushAsync(timeout.Token);
        }

        extension = RetroBoxCoverEndpoints.DetectImageExtension(stagedPath) ?? extension;
        if (!RetroBoxEndpoints.IsSupportedImageExtension(extension)
            || !RetroBoxCoverEndpoints.IsValidImage(stagedPath, extension))
        {
            throw new InvalidDataException("The downloaded cover is not a valid image.");
        }

        return extension;
    }

    private static string ResolveExtension(Uri source)
    {
        var extension = Path.GetExtension(source.AbsolutePath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" ? extension : ".jpg";
    }

    private static void SafeDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class RetroBoxUnknownGameException(string message) : Exception(message);
