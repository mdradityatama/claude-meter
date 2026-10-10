using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

/// <summary>Real file system: proves the create is exclusive and Claude Code's owner record is never touched.</summary>
public class LockFileSystemTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Create_fails_when_the_directory_exists()
    {
        var fs = new LockFileSystem();
        var path = Path.Combine(_dir, "x.lock");

        Assert.True(fs.TryCreateDirectory(path));
        Assert.False(fs.TryCreateDirectory(path));
    }

    [Fact]
    public async Task Owner_record_is_never_touched()
    {
        var paths = ClaudePaths.Resolve(name => name == "USERPROFILE" ? _dir : null, File.Exists, p => p, _dir);
        Directory.CreateDirectory(paths.ConfigHome);
        var owner = paths.OAuthRefreshLock + ".owner";
        File.WriteAllText(owner, """{"pid":1}""");
        var ownerTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(owner, ownerTime);
        Directory.CreateDirectory(paths.OAuthRefreshLock);
        Directory.SetLastWriteTimeUtc(paths.OAuthRefreshLock, DateTime.UtcNow.AddMinutes(-2));

        using (await ClaudeLocks.ForSystem(paths).AcquireRefreshAsync(default))
        {
        }

        Assert.Equal("""{"pid":1}""", File.ReadAllText(owner));
        Assert.Equal(ownerTime, File.GetLastWriteTimeUtc(owner));
        Assert.False(Directory.Exists(paths.OAuthRefreshLock));
        Assert.False(Directory.Exists(paths.LegacyLock));
    }
}
