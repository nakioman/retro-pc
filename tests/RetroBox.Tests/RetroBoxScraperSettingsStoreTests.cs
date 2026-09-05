using RetroBox.Core;
using RetroBox.Web;

namespace RetroBox.Tests;

public sealed class RetroBoxScraperSettingsStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"retrobox-scraper-{Guid.NewGuid():N}");

    public RetroBoxScraperSettingsStoreTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Load_returns_empty_settings_with_default_priorities_when_file_is_absent()
    {
        var settings = new RetroBoxScraperSettingsStore(root).Load();

        Assert.Null(settings.DevId);
        Assert.Null(settings.DevPassword);
        Assert.Null(settings.SsId);
        Assert.Null(settings.SsPassword);
        Assert.Equal(["sp", "wor", "eu", "us"], settings.RegionPriority);
        Assert.Equal(["es", "en"], settings.LanguagePriority);
    }

    [Fact]
    public void Save_round_trips_credentials_and_priorities_through_yaml()
    {
        var expected = new RetroBoxScraperSettings
        {
            DevId = "developer",
            DevPassword = "developer-password",
            SsId = "user",
            SsPassword = "user-password",
            RegionPriority = ["us", "eu"],
            LanguagePriority = ["en"],
        };

        var store = new RetroBoxScraperSettingsStore(root);
        store.Save(expected);

        var reloaded = store.Load();
        Assert.Equal(expected.DevId, reloaded.DevId);
        Assert.Equal(expected.DevPassword, reloaded.DevPassword);
        Assert.Equal(expected.SsId, reloaded.SsId);
        Assert.Equal(expected.SsPassword, reloaded.SsPassword);
        Assert.Equal(expected.RegionPriority, reloaded.RegionPriority);
        Assert.Equal(expected.LanguagePriority, reloaded.LanguagePriority);
        Assert.Contains("devId: developer", File.ReadAllText(Path.Combine(root, "scraper.yaml")));
    }

    [Fact]
    public void Settings_response_reports_configuration_only_when_all_credentials_are_present()
    {
        var response = RetroBoxScraperSettingsView.From(new RetroBoxScraperSettings
        {
            DevId = "developer",
            DevPassword = "developer-password",
            SsId = "user",
            SsPassword = "user-password",
        });

        Assert.True(response.Configured);
        Assert.True(response.DeveloperConfigured);
        Assert.True(response.UserConfigured);
    }

    [Fact]
    public void Settings_response_reports_partial_credential_configuration_without_secrets()
    {
        var response = RetroBoxScraperSettingsView.From(new RetroBoxScraperSettings
        {
            DevId = "developer",
            SsId = "user",
            SsPassword = "user-password",
        });

        Assert.False(response.Configured);
        Assert.False(response.DeveloperConfigured);
        Assert.True(response.UserConfigured);
        Assert.Equal(["sp", "wor", "eu", "us"], response.RegionPriority);
        Assert.Equal(["es", "en"], response.LanguagePriority);
        Assert.DoesNotContain("developer", response.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("user-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_preserves_explicitly_cleared_secret_values()
    {
        var store = new RetroBoxScraperSettingsStore(root);
        store.Save(new RetroBoxScraperSettings
        {
            DevId = "developer",
            DevPassword = "developer-password",
            SsId = "user",
            SsPassword = "user-password",
        });

        store.Save(new RetroBoxScraperSettings
        {
            DevId = "developer",
            DevPassword = string.Empty,
            SsId = "user",
            SsPassword = "user-password",
        });

        var reloaded = store.Load();
        Assert.Equal(string.Empty, reloaded.DevPassword);
        Assert.False(RetroBoxScraperSettingsView.From(reloaded).Configured);
    }
}
