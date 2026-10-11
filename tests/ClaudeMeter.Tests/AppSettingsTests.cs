using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path => System.IO.Path.Combine(_dir, "ClaudeMeter", "settings.json");

    [Fact]
    public void Missing_file_keeps_inactive_accounts_live()
    {
        Assert.True(AppSettings.Load(Path).KeepInactiveLive);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_the_default()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, "not json");

        Assert.True(AppSettings.Load(Path).KeepInactiveLive);
    }

    [Fact]
    public void Setting_is_saved_and_reloaded()
    {
        Assert.True(AppSettings.Load(Path).SetKeepInactiveLive(false));

        Assert.False(AppSettings.Load(Path).KeepInactiveLive);
        Assert.Equal("""{"keepInactiveLive":false,"showResetDate":false}""", File.ReadAllText(Path));
    }

    [Fact]
    public void Missing_file_shows_the_countdown()
    {
        Assert.False(AppSettings.Load(Path).ShowResetDate);
    }

    [Fact]
    public void Reset_view_is_saved_and_reloaded()
    {
        Assert.True(AppSettings.Load(Path).SetShowResetDate(true));

        Assert.True(AppSettings.Load(Path).ShowResetDate);
    }

    [Fact]
    public void Saving_one_setting_keeps_the_other()
    {
        AppSettings.Load(Path).SetKeepInactiveLive(false);
        AppSettings.Load(Path).SetShowResetDate(true);

        var settings = AppSettings.Load(Path);
        Assert.False(settings.KeepInactiveLive);
        Assert.True(settings.ShowResetDate);
    }
}
