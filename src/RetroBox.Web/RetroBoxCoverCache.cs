using RetroBox.Core;

namespace RetroBox.Web;

public sealed class RetroBoxCoverCache(
    string coversRoot,
    RetroBoxFloppyLibrary library,
    Func<Uri, CancellationToken, Task<Stream>> download)
{
    public const string HttpClientName = "covers";

    public string CreateStagingPath(string extension)
    {
        Directory.CreateDirectory(coversRoot);
        return Path.Combine(coversRoot, $".upload-{Guid.NewGuid():N}{extension}");
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

        var stagedPath = Path.Combine(coversRoot, $".{gameId}-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var input = await download(source, cancellationToken))
            await using (var output = File.Create(stagedPath))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

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
