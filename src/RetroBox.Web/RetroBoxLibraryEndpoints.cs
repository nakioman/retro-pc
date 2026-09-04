using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using RetroBox.Core;

namespace RetroBox.Web;

public static class RetroBoxLibraryEndpoints
{
    private static readonly string[] AllowedExtensions = [".img", ".ima", ".dsk"];

    public const long MaxUploadBytes = 4 * 1024 * 1024;

    // Kestrel's cap has to cover the multipart envelope (boundary markers, part headers) around
    // the file, not just the file itself. A cap equal to MaxUploadBytes made Kestrel abort inside
    // ReadFormAsync with a bodyless 413 before the handler's own file.Length check below could
    // ever run, so the "file-too-large" {code, message} response was dead code.
    public const long MaxRequestBodyBytes = MaxUploadBytes + 64 * 1024;

    public static void Map(WebApplication app, RetroBoxWebOptions options, IRetroBoxCatalogSource catalogSource)
    {
        var library = new RetroBoxFloppyLibrary(new RetroBoxConfigStore(options.ConfigRoot));

        app.MapPost("/api/floppies", (HttpRequest request) => UploadAsync(request, options, library, catalogSource));
        app.MapDelete("/api/floppies/{id}", (string id) => Delete(id, library, catalogSource));
        app.MapPatch("/api/floppies/{id}", (string id, RetroBoxFloppyPatch patch) => Patch(id, patch, library, catalogSource));
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request,
        RetroBoxWebOptions options,
        RetroBoxFloppyLibrary library,
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

        Directory.CreateDirectory(options.ScratchRoot);

        // Staged under a name nothing can collide on: the ID this upload will get is not known
        // until the exclusive section below resolves it against the live catalog.
        var stagingPath = Path.Combine(options.ScratchRoot, $"upload-{Guid.NewGuid():N}{extension}");

        await using (var scratch = File.Create(stagingPath))
        {
            await file.CopyToAsync(scratch);
        }

        string? id = null;
        RetroBoxCatalogException? importError = null;

        library.RunExclusively(() =>
        {
            var resolvedId = ResolveFreeId(slug, catalogSource);

            // Both the scratch and the cataloged filename come from the resolved ID, not the
            // uploaded name: RetroBoxFloppyImporter targets catalogedRoot/Path.GetFileName(source),
            // so two uploads sharing an original filename would otherwise collide on that move
            // even though ResolveFreeId correctly gave them different catalog IDs.
            var finalScratchPath = Path.Combine(options.ScratchRoot, resolvedId + extension);

            try
            {
                File.Move(stagingPath, finalScratchPath, overwrite: false);

                new RetroBoxFloppyImporter().Import(new RetroBoxFloppyImportRequest
                {
                    Id = resolvedId,
                    Label = Path.GetFileNameWithoutExtension(fileName),
                    ImagePath = finalScratchPath,
                    ConfigRoot = options.ConfigRoot,
                    ScratchRoot = options.ScratchRoot,
                    CatalogedRoot = options.CatalogedRoot,
                });

                id = resolvedId;
                catalogSource.TryReload();
            }
            catch (RetroBoxCatalogException ex)
            {
                importError = ex;
            }
            finally
            {
                // Harmless once Import has moved the file: File.Delete on a path that no longer
                // exists is a no-op. This runs for every failure shape, not just
                // RetroBoxCatalogException, so an UnauthorizedAccessException or a disk-full
                // IOException from the importer does not leak the scratch copy either.
                SafeDelete(stagingPath);
                SafeDelete(finalScratchPath);
            }
        });

        if (importError is not null)
        {
            return Error(StatusCodes.Status400BadRequest, "import-failed", importError.Message);
        }

        return Results.Created($"/api/floppies/{id}", null);
    }

    private static IResult Delete(string id, RetroBoxFloppyLibrary library, IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            library.Delete(id);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
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
        RetroBoxFloppyLibrary library,
        IRetroBoxCatalogSource catalogSource)
    {
        try
        {
            library.UpdateLabelAndMode(id, patch.Label, patch.Mode);
        }
        catch (RetroBoxUnknownFloppyException ex)
        {
            return Error(StatusCodes.Status404NotFound, "unknown-floppy", ex.Message);
        }
        catch (RetroBoxCatalogUnavailableException ex)
        {
            return Error(StatusCodes.Status500InternalServerError, "catalog-unavailable", ex.Message);
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
