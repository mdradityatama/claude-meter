using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AutoSaverTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly AccountKey A = new("acc-a", "org-a");

    private readonly ClaudeHome _home = new();
    private readonly FakeProfileClient _profiles;
    private readonly AutoSaver _saver;

    public AutoSaverTests()
    {
        _profiles = new FakeProfileClient { OnCall = () => Assert.Equal(0, _home.Locks.HeldCount) };
        _saver = new AutoSaver(_home.Paths, _home.ClaudeLocks, _home.Store, _profiles, _home.Clock);
    }

    public void Dispose() => _home.Dispose();

    private static byte[] CredentialsA(string refresh) => ClaudeJson.Credentials($"{refresh}-access", refresh, Start.AddHours(8), "org-a");

    private static byte[] ConfigA(int startups = 12) => ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com", startups);

    [Fact]
    public async Task New_verified_login_is_inserted()
    {
        var credentials = CredentialsA("a-refresh");
        _home.WriteLive(credentials, ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");

        var result = await _saver.RunAsync(default);

        Assert.Equal(new AutoSaveResult.Inserted("a@example.com"), result);
        var saved = Assert.Single(_home.Store.Accounts);
        Assert.Equal(credentials, saved.CredentialsJson);
        Assert.False(_home.Store.HasUnsavedChanges);
        Assert.Equal(
            ["create .oauth_refresh.lock", "create .claude.lock", "create .storage-write.lock",
             "delete .storage-write.lock", "delete .claude.lock", "delete .oauth_refresh.lock"],
            _home.Locks.Log);
    }

    [Fact]
    public async Task Known_login_with_changed_credentials_is_updated_silently()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.WriteLive(CredentialsA("r-2"), ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");

        Assert.Equal(new AutoSaveResult.Updated(), await _saver.RunAsync(default));
        Assert.Equal(CredentialsA("r-2"), _home.Store.Find(A)!.CredentialsJson);
    }

    [Fact]
    public async Task Known_login_update_clears_expired_state()
    {
        var a = _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.Store.Upsert(a with { LoginState = LoginState.Expired });
        _home.WriteLive(CredentialsA("r-2"), ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");

        Assert.Equal(new AutoSaveResult.Updated(), await _saver.RunAsync(default));
        Assert.Equal(LoginState.Valid, _home.Store.Find(A)!.LoginState);
    }

    [Fact]
    public async Task Unchanged_login_makes_no_request_and_takes_no_lock()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.WriteLive(CredentialsA("r-1"), ConfigA());

        Assert.Equal(new AutoSaveResult.Unchanged(), await _saver.RunAsync(default));
        Assert.Empty(_profiles.Calls);
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Config_rewrite_elsewhere_does_not_count_as_a_change()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.WriteLive(CredentialsA("r-1"), ConfigA(startups: 99));

        Assert.Equal(new AutoSaveResult.Unchanged(), await _saver.RunAsync(default));
    }

    [Fact]
    public async Task Changed_oauth_account_with_same_credentials_updates_without_profile_call()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        var config = Encoding.UTF8.GetString(ConfigA()).Replace("Dimas Ü ✓", "Dimas R");
        _home.WriteLive(CredentialsA("r-1"), Encoding.UTF8.GetBytes(config));

        Assert.Equal(new AutoSaveResult.Updated(), await _saver.RunAsync(default));
        Assert.Contains("Dimas R", Encoding.UTF8.GetString(_home.Store.Find(A)!.OAuthAccountJson));
        Assert.Empty(_profiles.Calls);
    }

    [Fact]
    public async Task Profile_mismatch_writes_nothing()
    {
        _home.WriteLive(CredentialsA("r-1"), ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-x", "org-a");

        Assert.IsType<AutoSaveResult.Rejected>(await _saver.RunAsync(default));
        Assert.Empty(_home.Store.Accounts);
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Files_changing_under_the_lock_write_nothing()
    {
        _home.WriteLive(CredentialsA("r-1"), ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");
        _home.Locks.AfterCreate = path =>
        {
            if (path == _home.Paths.StorageWriteLock)
                File.WriteAllBytes(_home.Paths.Credentials, CredentialsA("r-2"));
        };

        Assert.IsType<AutoSaveResult.Rejected>(await _saver.RunAsync(default));
        Assert.Empty(_home.Store.Accounts);
    }

    [Fact]
    public async Task Unverified_new_login_is_deferred()
    {
        _home.WriteLive(CredentialsA("r-1"), ConfigA());

        Assert.Equal(new AutoSaveResult.Deferred("a@example.com"), await _saver.RunAsync(default));
        Assert.Empty(_home.Store.Accounts);
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Unverified_known_login_is_updated()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.WriteLive(CredentialsA("r-2"), ConfigA());

        Assert.Equal(new AutoSaveResult.Updated(), await _saver.RunAsync(default));
        Assert.Equal(CredentialsA("r-2"), _home.Store.Find(A)!.CredentialsJson);
    }

    [Fact]
    public async Task Require_profile_rejects_unverified_known_login()
    {
        _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("r-1"), Start);
        _home.WriteLive(CredentialsA("r-2"), ConfigA());
        _saver.RequireProfile = true;

        Assert.IsType<AutoSaveResult.Rejected>(await _saver.RunAsync(default));
        Assert.Equal(CredentialsA("r-1"), _home.Store.Find(A)!.CredentialsJson);

        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");
        Assert.Equal(new AutoSaveResult.Updated(), await _saver.RunAsync(default));
        Assert.False(_saver.RequireProfile);
    }

    [Fact]
    public async Task Credentials_of_another_saved_account_are_rejected()
    {
        var t = _home.Save("acc-t", "org-t", "t@example.com", ClaudeJson.Credentials("t", "t-refresh", Start.AddHours(8), "org-t"), Start);
        _home.WriteLive(t.CredentialsJson, ConfigA());

        Assert.IsType<AutoSaveResult.Rejected>(await _saver.RunAsync(default));
        Assert.Empty(_profiles.Calls);
        Assert.Single(_home.Store.Accounts);
    }

    [Fact]
    public async Task Busy_lock_is_reported_and_writes_nothing()
    {
        _home.WriteLive(CredentialsA("r-1"), ConfigA());
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");
        _home.Locks.AddForeign(_home.Paths.StorageWriteLock, _home.Clock.Now);

        Assert.Equal(new AutoSaveResult.Busy(), await _saver.RunAsync(default));
        Assert.Empty(_home.Store.Accounts);
    }

    [Fact]
    public async Task No_login_writes_nothing()
    {
        Assert.Equal(new AutoSaveResult.NoLogin(), await _saver.RunAsync(default));

        _home.WriteLive(CredentialsA("r-1"), Encoding.UTF8.GetBytes("""{"numStartups": 1}"""));
        Assert.Equal(new AutoSaveResult.NoLogin(), await _saver.RunAsync(default));
        Assert.Empty(_home.Store.Accounts);
    }

    [Fact]
    public async Task Duplicate_oauth_account_is_rejected()
    {
        _home.WriteLive(CredentialsA("r-1"), Encoding.UTF8.GetBytes("""{"oauthAccount":{},"oauthAccount":{}}"""));

        Assert.IsType<AutoSaveResult.Rejected>(await _saver.RunAsync(default));
    }
}
