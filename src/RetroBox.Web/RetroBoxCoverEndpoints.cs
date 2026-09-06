using System.Buffers.Binary;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxCoverEndpoints
{
    public static void Map(
        WebApplication app,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        RetroBoxCoverCache coverCache,
        IRetroBoxCatalogSource catalogSource)
    {
        app.MapPost("/api/games/{id}/cover", (string id, RetroBoxCoverRequest request, CancellationToken cancellationToken) =>
            ReplaceAsync(id, request, settingsStore, coverSourceFactory, coverCache, catalogSource, cancellationToken));
        app.MapPost("/api/games/{id}/cover/upload", (string id, HttpRequest request, CancellationToken cancellationToken) =>
            UploadAsync(id, request, coverCache, catalogSource, cancellationToken));
    }

    private static async Task<IResult> ReplaceAsync(
        string id,
        RetroBoxCoverRequest request,
        RetroBoxScraperSettingsStore settingsStore,
        Func<IRetroBoxCoverSource> coverSourceFactory,
        RetroBoxCoverCache coverCache,
        IRetroBoxCatalogSource catalogSource,
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

            catalogSource.TryReload();
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
        catch (InvalidDataException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "cover-download-failed", ex.Message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status502BadGateway, "cover-download-failed", ex.Message);
        }
    }

    private static async Task<IResult> UploadAsync(
        string id,
        HttpRequest request,
        RetroBoxCoverCache coverCache,
        IRetroBoxCatalogSource catalogSource,
        CancellationToken cancellationToken)
    {
        if (!RetroBoxCatalogRules.IsValidId(id))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", "The cover request is invalid.");
        }

        if (!request.HasFormContentType || !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "expected-multipart", "Expected a multipart form upload.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "expected-multipart", "Expected a multipart form upload.");
        }

        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = RetroBoxLibraryEndpoints.MaxUploadBytes };
        var section = await reader.ReadNextSectionAsync(cancellationToken);
        if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(disposition.Name.Value, "file", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(disposition.FileName.Value))
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "missing-file", "No file was uploaded.");
        }

        var fileName = Path.GetFileName(HeaderUtilities.RemoveQuotes(disposition.FileName).Value ?? string.Empty);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
        {
            return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "unsupported-extension", "Only JPG, PNG and WebP covers are supported.");
        }

        var stagedPath = coverCache.CreateStagingPath(extension);
        try
        {
            await using (var output = File.Create(stagedPath))
            {
                await section.Body.CopyToAsync(output, cancellationToken);
            }

            if (await reader.ReadNextSectionAsync(cancellationToken) is not null)
            {
                return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-request", "Expected exactly one image file.");
            }

            if (new FileInfo(stagedPath).Length == 0 || !IsValidImage(stagedPath, extension))
            {
                return RetroBoxWebResults.Error(StatusCodes.Status400BadRequest, "invalid-image", "The uploaded file is not a valid image.");
            }

            var cover = await coverCache.ReplaceUploadAsync(id, extension, stagedPath, cancellationToken);
            if (cover is null)
            {
                return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", $"Unknown game '{id}'.");
            }

            catalogSource.TryReload();
            return Results.Json(cover, RetroBoxWebJsonContext.Default.RetroBoxCoverView);
        }
        catch (InvalidDataException)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The image exceeds the upload limit.");
        }
        catch (RetroBoxUnknownGameException ex)
        {
            return RetroBoxWebResults.Error(StatusCodes.Status404NotFound, "unknown-game", ex.Message);
        }
        finally
        {
            try { File.Delete(stagedPath); } catch (IOException) { }
        }
    }

    internal static bool IsValidImage(string path, string extension)
    {
        var bytes = File.ReadAllBytes(path);
        return extension switch
        {
            ".jpg" or ".jpeg" => IsValidJpeg(bytes),
            ".png" => IsValidPng(bytes),
            ".webp" => IsValidWebp(bytes),
            _ => false,
        };
    }

    internal static string? DetectImageExtension(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (IsValidJpeg(bytes)) return ".jpg";
        if (IsValidPng(bytes)) return ".png";
        if (IsValidWebp(bytes)) return ".webp";
        return null;
    }

    private static bool IsValidJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
        {
            return false;
        }

        var hasDimensions = false;
        var hasScan = false;
        var hasScanData = false;
        for (var offset = 2; offset + 1 < bytes.Length;)
        {
            if (bytes[offset++] != 0xff)
            {
                return false;
            }

            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            var marker = bytes[offset++];
            if (marker == 0xd9)
            {
                return hasDimensions && hasScan && hasScanData && offset == bytes.Length;
            }

            if (marker is 0xd8 or 0x01 or >= 0xd0 and <= 0xd7 || offset + 1 >= bytes.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
            if (length < 2 || offset + length > bytes.Length)
            {
                return false;
            }

            if (marker == 0xda)
            {
                if (!hasDimensions || length < 8)
                {
                    return false;
                }

                hasScan = true;
                offset += length;
                while (offset < bytes.Length)
                {
                    if (bytes[offset++] != 0xff)
                    {
                        hasScanData = true;
                        continue;
                    }

                    while (offset < bytes.Length && bytes[offset] == 0xff)
                    {
                        offset++;
                    }

                    if (offset >= bytes.Length)
                    {
                        return false;
                    }

                    var scanMarker = bytes[offset++];
                    if (scanMarker == 0x00)
                    {
                        hasScanData = true;
                    }
                    else if (scanMarker == 0xd9)
                    {
                        return hasScanData && offset == bytes.Length;
                    }
                    else if (scanMarker is not (>= 0xd0 and <= 0xd7))
                    {
                        offset -= 2;
                        break;
                    }
                }

                continue;
            }

            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                hasDimensions = length >= 8
                    && BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 3)..]) > 0
                    && BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 5)..]) > 0;
            }

            offset += length;
        }

        return false;
    }

    private static bool IsValidPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            return false;
        }

        var sawHeader = false;
        var sawImageData = false;
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > int.MaxValue || offset + 12L + length > bytes.Length)
            {
                return false;
            }

            var type = bytes.Slice(offset + 4, 4);
            if (!sawHeader)
            {
                if (length != 13 || !type.SequenceEqual("IHDR"u8)
                    || BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8)..]) == 0
                    || BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 12)..]) == 0)
                {
                    return false;
                }

                sawHeader = true;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                sawImageData |= length > 0;
            }

            offset += checked((int)length + 12);
            if (type.SequenceEqual("IEND"u8))
            {
                return sawHeader && sawImageData && length == 0 && offset == bytes.Length;
            }
        }

        return false;
    }

    private static bool IsValidWebp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != bytes.Length - 8
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var hasImage = false;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var type = bytes.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            if (length > int.MaxValue || offset + 8L + length + (length & 1) > bytes.Length)
            {
                return false;
            }

            var payload = bytes.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("VP8 "u8))
            {
                hasImage |= IsValidVp8(payload);
            }
            else if (type.SequenceEqual("VP8L"u8))
            {
                hasImage |= IsValidVp8L(payload);
            }
            else if (type.SequenceEqual("VP8X"u8))
            {
                if (offset != 12 || !IsValidVp8X(payload))
                {
                    return false;
                }

            }
            else if (type.SequenceEqual("ANMF"u8))
            {
                hasImage |= IsValidAnimationFrame(payload);
            }

            offset += checked(8 + (int)length + ((int)length & 1));
        }

        return hasImage && offset == bytes.Length;
    }

    private static bool IsValidVp8(ReadOnlySpan<byte> payload) =>
        payload.Length > 10
        && payload.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a })
        && (BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3fff) > 0
        && (BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3fff) > 0;

    private static bool IsValidVp8L(ReadOnlySpan<byte> payload) =>
        payload.Length > 5
        && payload[0] == 0x2f
        && (BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]) & 0x3fff) < 0x3fff
        && ((BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]) >> 14) & 0x3fff) < 0x3fff;

    private static bool IsValidVp8X(ReadOnlySpan<byte> payload) => payload.Length == 10;

    private static bool IsValidAnimationFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 24)
        {
            return false;
        }

        var type = payload.Slice(16, 4);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]);
        return length <= int.MaxValue
            && 24L + length + (length & 1) == payload.Length
            && (type.SequenceEqual("VP8 "u8) && IsValidVp8(payload.Slice(24, (int)length))
                || type.SequenceEqual("VP8L"u8) && IsValidVp8L(payload.Slice(24, (int)length)));
    }
}

internal static class RetroBoxEndpoints
{
    public static bool IsSupportedImageExtension(string extension) =>
        extension is ".jpg" or ".jpeg" or ".png" or ".webp";
}
