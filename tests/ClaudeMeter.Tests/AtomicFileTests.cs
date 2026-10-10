using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private string Target => Path.Combine(_dir, ".credentials.json");

    [Fact]
    public void Creates_a_new_file()
    {
        AtomicFile.Write(Target, "new"u8);

        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(Target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Replaces_an_existing_file()
    {
        File.WriteAllText(Target, "old");

        AtomicFile.Write(Target, "new"u8);

        Assert.Equal("new"u8.ToArray(), File.ReadAllBytes(Target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Failure_leaves_target_and_no_temp_file()
    {
        File.WriteAllText(Target, "old");
        File.SetAttributes(Target, FileAttributes.ReadOnly);

        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.Write(Target, "new"u8));

        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(Target));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
