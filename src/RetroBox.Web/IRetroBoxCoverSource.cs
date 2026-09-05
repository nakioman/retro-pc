namespace RetroBox.Web;

public interface IRetroBoxCoverSource
{
    Task<IReadOnlyList<RetroBoxCoverSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetroBoxCoverMedia>> GetGameAsync(string screenScraperId, CancellationToken cancellationToken);
}

public sealed record RetroBoxCoverSearchResult(
    string ScreenScraperId,
    string Title,
    IReadOnlyList<RetroBoxCoverMedia> Media);

public sealed record RetroBoxCoverMedia(string Type, string? Url, string? Region, string? Language);
