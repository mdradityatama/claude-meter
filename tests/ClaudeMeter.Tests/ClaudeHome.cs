using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

/// <summary>A temp user profile with Claude Code's files, a fake clock and an account store.</summary>
internal sealed class ClaudeHome : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 11, 8, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("claude-meter-tests").FullName;

    public ClaudeHome()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        Paths = ClaudePaths.Resolve(name => name == "USERPROFILE" ? _root : null, File.Exists, p => p, Path.Combine(_root, "LocalAppData"));
        Store = new AccountStore(Paths.Store, new FakeProtector());
        Locks = new FakeLockFileSystem(Clock);
        ClaudeLocks = new ClaudeLocks(Paths, new DirectoryLocks(Locks, Clock, (d, _) => { Clock.Advance(d); return Task.CompletedTask; }));
    }

    public FakeTimeProvider Clock { get; } = new(Start);
    public ClaudePaths Paths { get; }
    public AccountStore Store { get; }
    public FakeLockFileSystem Locks { get; }
    public ClaudeLocks ClaudeLocks { get; }

    public byte[] LiveCredentials => File.ReadAllBytes(Paths.Credentials);
    public byte[] LiveConfig => File.ReadAllBytes(Paths.GlobalConfig);

    public void WriteLive(byte[] credentials, byte[] globalConfig)
    {
        File.WriteAllBytes(Paths.Credentials, credentials);
        File.WriteAllBytes(Paths.GlobalConfig, globalConfig);
    }

    /// <summary>Stores an account as if auto-save had captured it, and saves the store.</summary>
    public StoredAccount Save(string account, string org, string email, byte[] credentials, DateTimeOffset addedAt)
    {
        var live = LiveLogin.From(new LiveFiles(credentials, ClaudeJson.GlobalConfig(account, org, email)))!;
        var stored = StoredAccount.Create(live, addedAt);
        Store.Upsert(stored);
        Assert.True(Store.TrySave());
        return stored;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}

/// <summary>Claude Code's file shapes from the spike: single-line credentials; LF, 2-space, no trailing newline config.</summary>
internal static class ClaudeJson
{
    public static byte[] Credentials(string access, string refresh, DateTimeOffset expiresAt, string org, DateTimeOffset? loginExpiresAt = null, string plan = "pro") =>
        Encoding.UTF8.GetBytes(
            $$$"""{"claudeAiOauth":{"accessToken":"{{{access}}}","refreshToken":"{{{refresh}}}","expiresAt":{{{expiresAt.ToUnixTimeMilliseconds()}}},"refreshTokenExpiresAt":{{{(loginExpiresAt ?? expiresAt.AddDays(30)).ToUnixTimeMilliseconds()}}},"scopes":["user:inference","user:profile"],"subscriptionType":"{{{plan}}}","rateLimitTier":"default_claude_ai","futureField":{"x":1}},"organizationUuid":"{{{org}}}"}""");

    public static byte[] GlobalConfig(string account, string org, string email, int startups = 12) =>
        Encoding.UTF8.GetBytes(string.Join("\n",
            "{",
            $"  \"numStartups\": {startups},",
            "  \"oauthAccount\": {",
            $"    \"accountUuid\": \"{account}\",",
            $"    \"emailAddress\": \"{email}\",",
            $"    \"organizationUuid\": \"{org}\",",
            "    \"displayName\": \"Dimas Ü ✓\"",
            "  },",
            "  \"projects\": {",
            "    \"D:/repo\": { \"allowedTools\": [] },",
            "    \"d:/repo\": { \"allowedTools\": [\"Bash\"] }",
            "  }",
            "}"));
}
