namespace RetroBox.Core;

/// <summary>
/// A QEMU profile's qemu.args: one argument per line, no shell quoting; blank lines and lines
/// starting with '#' are ignored. It is the VM's hardware source of truth (ADR 0009).
/// </summary>
public static class RetroBoxQemuProfile
{
    public const string FileName = "qemu.args";

    public static IReadOnlyList<string> ReadArguments(string path)
    {
        if (!File.Exists(path))
        {
            throw new RetroBoxCatalogException($"QEMU arguments file '{path}' does not exist.");
        }

        return File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    public static string Format(IEnumerable<string> arguments) =>
        string.Concat(arguments.Select(argument => argument + "\n"));
}
