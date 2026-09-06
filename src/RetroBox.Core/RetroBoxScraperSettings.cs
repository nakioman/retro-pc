using YamlDotNet.Serialization;

namespace RetroBox.Core;

public sealed record RetroBoxScraperSettings
{
    public static string[] DefaultRegionPriority { get; } = ["sp", "wor", "eu", "us"];

    public static string[] DefaultLanguagePriority { get; } = ["es", "en"];

    public string? DevId { get; set; }

    public string? DevPassword { get; set; }

    public string? SsId { get; set; }

    public string? SsPassword { get; set; }

    public string[] RegionPriority { get; set; } = [.. DefaultRegionPriority];

    public string[] LanguagePriority { get; set; } = [.. DefaultLanguagePriority];

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int MaxDownloadMegabytes { get; set; } = 16;

    [YamlIgnore]
    public bool IsDeveloperConfigured => HasValue(DevId) && HasValue(DevPassword);

    [YamlIgnore]
    public bool IsUserConfigured => HasValue(SsId) && HasValue(SsPassword);

    [YamlIgnore]
    public bool IsConfigured => IsDeveloperConfigured && IsUserConfigured;

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}
