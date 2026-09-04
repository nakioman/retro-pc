using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxLibraryEndpoints
{
    private static readonly string[] AllowedExtensions = [".img", ".ima", ".dsk"];

    public const long MaxUploadBytes = 4 * 1024 * 1024;

    public static void Map(WebApplication app, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)
    {
        app.MapPost("/api/floppies", (HttpRequest request) => UploadAsync(request, options, catalogSource));
        app.MapDelete("/api/floppies/{id}", (string id) => Delete(id, options, catalogSource));
        app.MapPatch("/api/floppies/{id}", (string id, RetroBoxFloppyPatch patch) => Patch(id, patch, options, catalogSource));
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource)
    {
        if (!request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "expected-multipart", "Expected a multipart form upload.");
        }

        var form = await request.ReadFormAsync();
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "missing-file", "No file was uploaded.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "file-too-large", "The image exceeds the upload limit.");
        }

        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "unsupported-extension",
                "Only .img, .ima and .dsk images can be imported.");
        }

        var slug = RetroBoxCatalogRules.Slugify(Path.GetFileNameWithoutExtension(fileName));
        if (slug.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "unusable-name", "The filename yields no usable catalog ID.");
        }

        var id = ResolveFreeId(slug, catalogSource);

        Directory.CreateDirectory(options.ScratchRoot);
        var scratchPath = Path.Combine(options.ScratchRoot, fileName);

        await using (var scratch = File.Create(scratchPath))
        {
            await file.CopyToAsync(scratch);
        }

        try
        {
            new RetroBoxFloppyImporter().Import(new RetroBoxFloppyImportRequest
            {
                Id = id,
                Label = Path.GetFileNameWithoutExtension(fileName),
                ImagePath = scratchPath,
                ConfigRoot = options.ConfigRoot,
                ScratchRoot = options.ScratchRoot,
                CatalogedRoot = options.CatalogedRoot,
            });
        }
        catch (RetroBoxCatalogException ex)
        {
            SafeDelete(scratchPath);
            return Error(StatusCodes.Status400BadRequest, "import-failed", ex.Message);
        }

        Refresh(catalogSource);
        return Results.Created($"/api/floppies/{id}", null);
    }

    private static IResult Delete(string id, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot)).Delete(id);
        }
        catch (RetroBoxCatalogException ex) when (ex.Message.StartsWith("Unknown floppy", StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogException ex)
        {
            Refresh(catalogSource);
            return Error(StatusCodes.Status500InternalServerError, "delete-incomplete", ex.Message);
        }

        Refresh(catalogSource);
        return Results.NoContent();
    }

    private static IResult Patch(
        string id,
        RetroBoxFloppyPatch patch,
        RetroBoxWebOptions options,
        IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot))
                .UpdateLabelAndMode(id, patch.Label, patch.Mode);
        }
        catch (RetroBoxCatalogException ex) when (ex.Message.StartsWith("Unknown floppy", StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid-patch", ex.Message);
        }

        Refresh(catalogSource);
        return Results.NoContent();
    }

    private static string ResolveFreeId(string slug, IRetroBoxCatalogSource catalogSource)
    {
        var existing = catalogSource.Current.Floppies;
        if (!existing.ContainsKey(slug))
        {
            return slug;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!existing.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    // The catalog file just changed underneath us. The watcher would notice, but only after its
    // debounce — and an immediate GET /api/catalog must not show stale data.
    private static void Refresh(IRetroBoxCatalogSource catalogSource)
    {
        catalogSource.TryReload();
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

    private static IResult Error(int statusCode, string code, string message)
    {
        // Pass the source-generated JsonTypeInfo directly: the JsonSerializerOptions overload of
        // Results.Json is annotated RequiresUnreferencedCode/RequiresDynamicCode because it cannot
        // statically prove the options carry a source-generated resolver, which is exactly the
        // AOT warning the RequestDelegateGenerator is meant to keep this project free of.
        return Results.Json(
            new RetroBoxErrorView(code, message),
            RetroBoxWebJsonContext.Default.RetroBoxErrorView,
            statusCode: statusCode);
    }
}
