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
        Assert.Equal("""{"keepInactiveLive":false}""", File.ReadAllText(Path));
    }
}
