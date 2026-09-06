namespace RetroBox.Web;

public sealed class RetroBoxCoverSelector
{
    public RetroBoxCoverMedia? Select(
        IEnumerable<RetroBoxCoverMedia> candidates,
        IEnumerable<string> regionPriority,
        IEnumerable<string> languagePriority)
    {
        var usable = candidates.Where(IsUsableBox2D).ToArray();
        foreach (var region in regionPriority)
        {
            foreach (var language in languagePriority)
            {
                var match = usable.FirstOrDefault(candidate =>
                    string.Equals(candidate.Region, region, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.Language, language, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return usable.FirstOrDefault();
    }

    public static bool IsUsableBox2D(RetroBoxCoverMedia media)
    {
        return string.Equals(media.Type, "box-2D", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(media.Url);
    }
}
