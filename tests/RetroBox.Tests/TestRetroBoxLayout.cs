namespace RetroBox.Tests;

internal sealed record TestRetroBoxLayout(string Root, string ConfigRoot, string ScratchRoot, string CatalogedRoot)
{
    public static TestRetroBoxLayout Create(string prefix, bool includeExistingFloppy = false)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(root, "retrobox");
        var scratchRoot = Path.Combine(root, "floppies", "scratch");
        var catalogedRoot = Path.Combine(root, "floppies", "cataloged");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(scratchRoot);
        Directory.CreateDirectory(catalogedRoot);

        File.WriteAllText(
            Path.Combine(configRoot, "config.yaml"),
            """
            defaultVm: pentium100
            """);
        File.WriteAllText(
            Path.Combine(configRoot, "vms.yaml"),
            """
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
            """);
        File.WriteAllText(
            Path.Combine(configRoot, "floppies.yaml"),
            includeExistingFloppy
                ? CreateExistingFloppyYaml(catalogedRoot)
                : """
                  floppies: {}
                  """);
        File.WriteAllText(
            Path.Combine(configRoot, "games.yaml"),
            """
            games: {}
            """);

        return new TestRetroBoxLayout(root, configRoot, scratchRoot, catalogedRoot);
    }

    private static string CreateExistingFloppyYaml(string catalogedRoot)
    {
        var existingImage = Path.Combine(catalogedRoot, "existing.img");
        File.WriteAllBytes(existingImage, []);

        return $$"""
               floppies:
                 existing-disk:
                   label: "Existing Disk"
                   image: "{{existingImage}}"
                   mode: "ro"
                   size: "720K"
               """;
    }
}
