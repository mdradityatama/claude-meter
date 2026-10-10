using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AccountSwitcherTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly AccountKey A = new("acc-a", "org-a");

    private readonly ClaudeHome _home = new();
    private readonly FakeProfileClient _profiles;
    private readonly FakeTokenRefresher _refresher = new();
    private readonly AccountSwitcher _switcher;

    public AccountSwitcherTests()
    {
        _profiles = new FakeProfileClient { OnCall = () => Assert.Equal(0, _home.Locks.HeldCount) };
        _switcher = new AccountSwitcher(_home.Paths, _home.ClaudeLocks, _home.Store, _profiles, _refresher, _home.Clock);
    }

    public void Dispose() => _home.Dispose();

    private static byte[] CredentialsA(string refresh) => ClaudeJson.Credentials($"{refresh}-access", refresh, Start.AddHours(8), "org-a");

    /// <summary>A and T stored; Claude Code holds A with rotated credentials; the profile confirms A.</summary>
    private (StoredAccount T, byte[] LiveA, byte[] ConfigBefore) Arrange(bool storeA = true, DateTimeOffset? targetLoginExpiresAt = null)
    {
        if (storeA)
            _home.Save("acc-a", "org-a", "a@example.com", CredentialsA("a-refresh-1"), Start.AddDays(-2));
        var t = _home.Save("acc-t", "org-t", "t@example.com",
            ClaudeJson.Credentials("t-access", "t-refresh", Start.AddHours(5), "org-t", targetLoginExpiresAt), Start.AddDays(-1));
        var liveA = CredentialsA("a-refresh-2");
        var config = ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com");
        _home.WriteLive(liveA, config);
        _profiles.Result = new ProfileResult.Found("acc-a", "org-a");
        return (t, liveA, config);
    }

    [Fact]
    public async Task Switch_writes_the_target_and_saves_the_previous_login()
    {
        var (t, liveA, configBefore) = Arrange();

        var result = await _switcher.SwitchAsync(t.Key, null, default);

        Assert.Equal(new SwitchResult.Switched("t@example.com"), result);
        Assert.Equal(t.CredentialsJson, _home.LiveCredentials);
        var (start, length) = JsonSplice.Find(configBefore, "oauthAccount")!.Value;
        Assert.Equal([.. configBefore[..start], .. t.OAuthAccountJson, .. configBefore[(start + length)..]], _home.LiveConfig);
        Assert.Equal(liveA, _home.Store.Find(A)!.CredentialsJson);
        Assert.Equal(Start, _home.Store.Find(t.Key)!.LastActiveAt);
        Assert.Equal([t.Key], _refresher.Calls);
    }

    [Fact]
    public async Task Locks_are_taken_in_claude_codes_order_and_released_in_reverse()
    {
        var (t, _, _) = Arrange();

        await _switcher.SwitchAsync(t.Key, null, default);

        Assert.Equal(
            ["create .oauth_refresh.lock", "create .claude.lock", "create .storage-write.lock", "create .claude.json.lock",
             "delete .claude.json.lock", "delete .storage-write.lock", "delete .claude.lock", "delete .oauth_refresh.lock"],
            _home.Locks.Log);
    }

    [Fact]
    public async Task Busy_config_lock_restores_the_previous_credentials()
    {
        var (t, liveA, configBefore) = Arrange();
        _home.Locks.AddForeign(_home.Paths.GlobalConfigLock, _home.Clock.Now);

        var result = await _switcher.SwitchAsync(t.Key, null, default);

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.Busy), result);
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Equal(configBefore, _home.LiveConfig);
    }

    [Fact]
    public async Task Config_write_failure_restores_the_previous_credentials()
    {
        var (t, liveA, configBefore) = Arrange();
        File.SetAttributes(_home.Paths.GlobalConfig, FileAttributes.ReadOnly);

        var result = await _switcher.SwitchAsync(t.Key, null, default);

        Assert.IsType<SwitchResult.Failed>(result);
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Equal(configBefore, _home.LiveConfig);
    }

    [Fact]
    public async Task Unverified_unknown_active_account_aborts_without_changes()
    {
        var (t, liveA, configBefore) = Arrange(storeA: false);
        _profiles.Result = new ProfileResult.Unavailable("offline");

        var result = await _switcher.SwitchAsync(t.Key, null, default);

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.Unverified), result);
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Equal(configBefore, _home.LiveConfig);
        Assert.Null(_home.Store.Find(A));
    }

    [Fact]
    public async Task Verified_unknown_active_account_is_saved_before_the_switch()
    {
        var (t, liveA, _) = Arrange(storeA: false);
        byte[]? onDiskWhileWritingConfig = null;
        _home.Locks.AfterCreate = path =>
        {
            if (path != _home.Paths.GlobalConfigLock)
                return;
            var disk = new AccountStore(_home.Paths.Store, new FakeProtector());
            disk.Load();
            onDiskWhileWritingConfig = disk.Find(A)?.CredentialsJson;
        };

        Assert.Equal(new SwitchResult.Switched("t@example.com"), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Equal(liveA, onDiskWhileWritingConfig);
    }

    [Fact]
    public async Task Store_save_failure_aborts_before_touching_claude_files()
    {
        var (t, liveA, configBefore) = Arrange(storeA: false);
        File.Delete(_home.Paths.Store);
        Directory.CreateDirectory(_home.Paths.Store);

        var result = await _switcher.SwitchAsync(t.Key, null, default);

        Assert.IsType<SwitchResult.Failed>(result);
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Equal(configBefore, _home.LiveConfig);
    }

    [Fact]
    public async Task Expired_target_is_refused_without_locks()
    {
        var (t, _, _) = Arrange();
        _home.Store.Upsert(t with { LoginState = LoginState.Expired });

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.TargetExpired), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Empty(_refresher.Calls);
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Target_past_its_login_expiry_is_refused()
    {
        var (t, _, _) = Arrange(targetLoginExpiresAt: Start.AddSeconds(-1));

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.TargetExpired), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Refresh_reporting_login_expired_aborts()
    {
        var (t, liveA, _) = Arrange();
        _refresher.Outcome = RefreshOutcome.LoginExpired;

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.TargetExpired), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Empty(_home.Locks.Log);
    }

    [Fact]
    public async Task Files_changing_before_the_lock_abort()
    {
        var (t, _, _) = Arrange();
        var rotated = CredentialsA("a-refresh-3");
        _home.Locks.AfterCreate = path =>
        {
            if (path == _home.Paths.StorageWriteLock)
                File.WriteAllBytes(_home.Paths.Credentials, rotated);
        };

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.Changing), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Equal(rotated, _home.LiveCredentials);
    }

    [Fact]
    public async Task Busy_refresh_lock_fails_cleanly()
    {
        var (t, liveA, configBefore) = Arrange();
        _home.Locks.AddForeign(_home.Paths.OAuthRefreshLock, _home.Clock.Now);

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.Busy), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Equal(liveA, _home.LiveCredentials);
        Assert.Equal(configBefore, _home.LiveConfig);
    }

    [Fact]
    public async Task Post_check_mismatch_is_unconfirmed()
    {
        var (t, _, _) = Arrange();
        _home.Locks.BeforeDelete = path =>
        {
            if (path == _home.Paths.StorageWriteLock)
                File.WriteAllBytes(_home.Paths.Credentials, CredentialsA("someone-else"));
        };

        Assert.Equal(new SwitchResult.Unconfirmed("t@example.com"), await _switcher.SwitchAsync(t.Key, null, default));
    }

    [Fact]
    public async Task Half_finished_switch_is_repaired_by_switching_again()
    {
        var (t, _, _) = Arrange();
        _home.WriteLive(t.CredentialsJson, ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com"));

        Assert.Equal(new SwitchResult.Switched("t@example.com"), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.Equal(t.Key, OAuthAccount.Parse(_home.LiveConfig)!.Key);
        Assert.Empty(_profiles.Calls);
    }

    [Fact]
    public async Task Switch_without_signed_in_claude_code_is_refused()
    {
        var (t, _, configBefore) = Arrange();
        File.Delete(_home.Paths.Credentials);

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.NoLogin), await _switcher.SwitchAsync(t.Key, null, default));
        Assert.False(File.Exists(_home.Paths.Credentials));
        Assert.Equal(configBefore, _home.LiveConfig);
    }

    [Fact]
    public async Task Switch_to_the_active_account_is_refused()
    {
        Arrange();

        Assert.Equal(new SwitchResult.Failed(AccountSwitcher.AlreadyActive), await _switcher.SwitchAsync(A, null, default));
    }

    [Fact]
    public async Task Progress_reports_lock_wait_then_writing()
    {
        var (t, _, _) = Arrange();
        var stages = new List<SwitchStage>();

        await _switcher.SwitchAsync(t.Key, new InlineProgress<SwitchStage>(stages.Add), default);

        Assert.Equal([SwitchStage.WaitingForLock, SwitchStage.Writing], stages);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
