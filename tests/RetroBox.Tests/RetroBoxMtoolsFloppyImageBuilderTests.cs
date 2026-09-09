using System.IO.Compression;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxMtoolsFloppyImageBuilderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-zip-{Guid.NewGuid():N}");

    public RetroBoxMtoolsFloppyImageBuilderTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void BuildFromZip_strips_a_single_wrapper_directory_and_preserves_subdirectories()
    {
        var zipPath = CreateZip(("GAME/README.TXT", "read me"), ("GAME/DATA/LEVEL1.DAT", "level"));
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            archive.CreateEntry("GAME/");
        }

        var calls = new List<(string FileName, IReadOnlyList<string> Arguments)>();
        var builder = new RetroBoxMtoolsFloppyImageBuilder((fileName, arguments) => calls.Add((fileName, arguments)));

        builder.BuildFromZip(zipPath, Path.Combine(root, "game.img"));

        Assert.Contains(calls, call => call.FileName == "mmd" && call.Arguments.Last() == "::/DATA");
        Assert.Contains(calls, call => call.FileName == "mcopy" && call.Arguments.Last() == "::/README.TXT");
        Assert.Contains(calls, call => call.FileName == "mcopy" && call.Arguments.Last() == "::/DATA/LEVEL1.DAT");
        Assert.DoesNotContain(calls.SelectMany(call => call.Arguments), argument => argument.Contains("::/GAME", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildFromZip_preserves_multiple_roots()
    {
        var zipPath = CreateZip(("README.TXT", "read me"), ("DATA/LEVEL1.DAT", "level"));
        var calls = new List<(string FileName, IReadOnlyList<string> Arguments)>();
        var builder = new RetroBoxMtoolsFloppyImageBuilder((fileName, arguments) => calls.Add((fileName, arguments)));

        builder.BuildFromZip(zipPath, Path.Combine(root, "game.img"));

        Assert.Contains(calls, call => call.FileName == "mcopy" && call.Arguments.Last() == "::/README.TXT");
        Assert.Contains(calls, call => call.FileName == "mcopy" && call.Arguments.Last() == "::/DATA/LEVEL1.DAT");
    }

    [Theory]
    [InlineData("../README.TXT")]
    [InlineData("DATA/../../README.TXT")]
    public void BuildFromZip_rejects_unsafe_paths(string entryName)
    {
        var zipPath = CreateZip((entryName, "nope"));
        var builder = new RetroBoxMtoolsFloppyImageBuilder((_, _) => throw new InvalidOperationException("mtools must not run"));

        var error = Assert.Throws<RetroBoxFloppyImageBuildException>(() => builder.BuildFromZip(zipPath, Path.Combine(root, "game.img")));

        Assert.Equal("unsafe-archive", error.Code);
    }

    [Fact]
    public void BuildFromZip_rejects_an_empty_archive()
    {
        var zipPath = CreateZip();
        var builder = new RetroBoxMtoolsFloppyImageBuilder((_, _) => throw new InvalidOperationException("mtools must not run"));

        var error = Assert.Throws<RetroBoxFloppyImageBuildException>(() => builder.BuildFromZip(zipPath, Path.Combine(root, "game.img")));

        Assert.Equal("empty-archive", error.Code);
    }

    private string CreateZip(params (string Name, string Contents)[] entries)
    {
        var zipPath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, contents) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(contents);
        }

        return zipPath;
    }
}
