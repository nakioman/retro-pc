using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;

namespace RetroBox.Core;

public sealed class RetroBoxMtoolsFloppyImageBuilder : IRetroBoxFloppyImageBuilder
{
    private const string ImageSize = "1440";
    private readonly Action<string, IReadOnlyList<string>> runMtools;

    public RetroBoxMtoolsFloppyImageBuilder(Action<string, IReadOnlyList<string>>? runMtools = null)
    {
        this.runMtools = runMtools ?? RunProcess;
    }

    public void BuildFromZip(string zipPath, string imagePath)
    {
        var workRoot = Path.Combine(Path.GetDirectoryName(imagePath) ?? Path.GetTempPath(), $"zip-{Guid.NewGuid():N}");

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = ReadEntries(archive);
            Directory.CreateDirectory(workRoot);
            WriteEntries(entries, workRoot);
            RunMtools("mformat", "-C", "-f", ImageSize, "-v", "RETROBOX", "-i", imagePath, "::");

            foreach (var directory in entries.Where(entry => entry.IsDirectory).OrderBy(entry => entry.Path.Count(character => character == '/')))
            {
                RunMtools("mmd", "-i", imagePath, $"::/{directory.Path}");
            }

            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                var sourcePath = Path.Combine(workRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                RunMtools("mcopy", "-i", imagePath, sourcePath, $"::/{entry.Path}");
            }
        }
        catch (RetroBoxFloppyImageBuildException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            throw new RetroBoxFloppyImageBuildException("invalid-zip", "The uploaded ZIP file is invalid.", ex);
        }
        catch (IOException ex)
        {
            throw new RetroBoxFloppyImageBuildException("image-build-failed", "Could not create the floppy image.", ex);
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    private static IReadOnlyList<ZipEntry> ReadEntries(ZipArchive archive)
    {
        var rawEntries = archive.Entries.Select(entry => CreateEntry(entry)).ToList();
        var files = rawEntries.Where(entry => !entry.IsDirectory).ToList();
        if (files.Count == 0)
        {
            throw new RetroBoxFloppyImageBuildException("empty-archive", "The ZIP file does not contain any files.");
        }

        // ZIP creators commonly include a directory entry for the wrapper itself (for example
        // "DRIVER/") as well as every file below it. That entry does not represent a root item
        // that should keep the wrapper in the generated floppy.
        var contentEntries = rawEntries.Where(entry => !entry.IsDirectory || entry.Segments.Length > 1).ToList();
        var stripRoot = contentEntries.All(entry => entry.Segments.Length > 1)
            && contentEntries.Select(entry => entry.Segments[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
        var entries = rawEntries
            .Where(entry => !stripRoot || entry.Segments.Length > 1)
            .Select(entry => stripRoot ? entry with
            {
                Path = string.Join('/', entry.Segments.Skip(1)),
            } : entry)
            .ToList();

        if (entries.Any(entry => entry.Path.Length == 0))
        {
            throw new RetroBoxFloppyImageBuildException("empty-archive", "The ZIP file does not contain any files.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!paths.Add(entry.Path))
            {
                throw new RetroBoxFloppyImageBuildException("unsafe-archive", $"The ZIP contains duplicate path '{entry.Path}'.");
            }
        }

        foreach (var file in entries.Where(entry => !entry.IsDirectory).ToList())
        {
            var segments = file.Path.Split('/');
            for (var length = 1; length < segments.Length; length++)
            {
                var parent = string.Join('/', segments.Take(length));
                if (entries.Any(entry => entry.Path.Equals(parent, StringComparison.OrdinalIgnoreCase) && !entry.IsDirectory))
                {
                    throw new RetroBoxFloppyImageBuildException("unsafe-archive", $"The ZIP has a file and directory named '{parent}'.");
                }

                if (paths.Add(parent))
                {
                    entries.Add(new ZipEntry(null, parent, parent.Split('/'), true));
                }
            }
        }

        return entries;
    }

    private static ZipEntry CreateEntry(ZipArchiveEntry entry)
    {
        if (IsSymbolicLink(entry) || entry.FullName.StartsWith("/", StringComparison.Ordinal) || entry.FullName.StartsWith("\\", StringComparison.Ordinal))
        {
            throw new RetroBoxFloppyImageBuildException("unsafe-archive", "The ZIP contains an unsafe path or symbolic link.");
        }

        var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
        var trimmedPath = entry.FullName.TrimEnd('/');
        var segments = trimmedPath.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.Contains('\\')))
        {
            throw new RetroBoxFloppyImageBuildException("unsafe-archive", "The ZIP contains an unsafe path.");
        }

        return new ZipEntry(entry, string.Join('/', segments), segments, isDirectory);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static void WriteEntries(IEnumerable<ZipEntry> entries, string workRoot)
    {
        foreach (var entry in entries)
        {
            var destination = Path.Combine(workRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Source!.Open();
            using var target = File.Create(destination);
            source.CopyTo(target);
        }
    }

    private void RunMtools(string fileName, params string[] arguments) => runMtools(fileName, arguments);

    private static void RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var code = standardError.Contains("No space left", StringComparison.OrdinalIgnoreCase)
                    || standardError.Contains("Disk full", StringComparison.OrdinalIgnoreCase)
                    ? "floppy-capacity-exceeded"
                    : "image-build-failed";
                throw new RetroBoxFloppyImageBuildException(code, standardError.Trim() is { Length: > 0 } message
                    ? message
                    : "Could not create the floppy image.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new RetroBoxFloppyImageBuildException("image-build-failed", $"Could not run {fileName}.", ex);
        }
    }

    private sealed record ZipEntry(ZipArchiveEntry? Source, string Path, string[] Segments, bool IsDirectory);
}
