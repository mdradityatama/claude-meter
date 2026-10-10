using System.Text;
using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class AccountStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;

    private readonly ClaudeHome _home = new();

    public void Dispose() => _home.Dispose();

    private static byte[] Credentials(string refresh, string org = "org-a") =>
        ClaudeJson.Credentials($"{refresh}-access", refresh, Start.AddHours(8), org);

    [Fact]
    public void Round_trip_keeps_every_field_and_exact_bytes()
    {
        var a = _home.Save("acc-a", "org-a", "a@example.com", Credentials("a-refresh"), Start.AddDays(-2));
        var usage = new UsageSnapshot(new UsageWindow(42, Start.AddHours(2)), new UsageWindow(18, null));
        var t = _home.Save("acc-t", "org-t", "t@example.com", Credentials("t-refresh", "org-t"), Start.AddDays(-1)) with
        {
            LastKnownUsage = usage,
            LastKnownAt = Start,
            ExpiryWarnedFor = 1792900000000,
            LoginState = LoginState.Expired,
        };
        _home.Store.Upsert(t);
        Assert.True(_home.Store.TrySave());

        var reloaded = new AccountStore(_home.Paths.Store, new FakeProtector());

        Assert.Equal(StoreLoadResult.Loaded, reloaded.Load());
        Assert.Equal(2, reloaded.Accounts.Count);
        var a2 = reloaded.Find(a.Key)!;
        var t2 = reloaded.Find(t.Key)!;
        Assert.Equal(a.CredentialsJson, a2.CredentialsJson);
        Assert.Equal(a.OAuthAccountJson, a2.OAuthAccountJson);
        Assert.Contains("futureField", Encoding.UTF8.GetString(a2.CredentialsJson));
        Assert.Contains("Dimas Ü ✓", Encoding.UTF8.GetString(a2.OAuthAccountJson));
        Assert.Equal(("a@example.com", "pro", Start.AddDays(-2), (DateTimeOffset?)Start.AddDays(-2)), (a2.Email, a2.Plan, a2.AddedAt, a2.LastActiveAt));
        Assert.Equal(usage, t2.LastKnownUsage);
        Assert.Equal(Start, t2.LastKnownAt);
        Assert.Equal(1792900000000, t2.ExpiryWarnedFor);
        Assert.Equal(LoginState.Expired, t2.LoginState);
    }

    [Fact]
    public void Store_file_is_not_plaintext()
    {
        _home.Save("acc-a", "org-a", "a@example.com", Credentials("a-refresh"), Start);

        Assert.DoesNotContain("a-refresh", Encoding.UTF8.GetString(File.ReadAllBytes(_home.Paths.Store)));
    }

    [Fact]
    public void Accounts_are_ordered_by_added_at()
    {
        var day3 = _home.Save("acc-3", "org-a", "3@example.com", Credentials("r3"), Start.AddDays(3));
        var day1 = _home.Save("acc-1", "org-a", "1@example.com", Credentials("r1"), Start.AddDays(1));
        _home.Save("acc-2", "org-a", "2@example.com", Credentials("r2"), Start.AddDays(2));

        Assert.Equal(["1@example.com", "2@example.com", "3@example.com"], _home.Store.Accounts.Select(a => a.Email));

        _home.Store.Upsert(day1 with { Email = "one@example.com" });
        Assert.Equal(["one@example.com", "2@example.com", "3@example.com"], _home.Store.Accounts.Select(a => a.Email));
        Assert.Equal(day3.Key, _home.Store.Accounts[^1].Key);
    }

    [Fact]
    public void Same_account_in_two_organizations_are_two_entries()
    {
        _home.Save("acc-a", "org-pro", "a@example.com", Credentials("r1", "org-pro"), Start);
        _home.Save("acc-a", "org-team", "a@example.com", Credentials("r2", "org-team"), Start.AddMinutes(1));

        Assert.Equal(2, _home.Store.Accounts.Count);
    }

    [Fact]
    public void Missing_file_is_empty()
    {
        Assert.Equal(StoreLoadResult.Empty, _home.Store.Load());
        Assert.Empty(_home.Store.Accounts);
    }

    [Fact]
    public void Unreadable_store_is_set_aside_never_overwritten()
    {
        Directory.CreateDirectory(_home.Paths.AppData);
        File.WriteAllText(_home.Paths.Store, "garbage");

        Assert.Equal(StoreLoadResult.SetAside, _home.Store.Load());

        Assert.Empty(_home.Store.Accounts);
        Assert.False(File.Exists(_home.Paths.Store));
        var aside = Assert.Single(Directory.GetFiles(_home.Paths.AppData, "accounts.dat.unreadable-*"));
        Assert.Equal("garbage", File.ReadAllText(aside));
    }

    [Fact]
    public void Failed_save_keeps_unsaved_flag()
    {
        Directory.CreateDirectory(_home.Paths.Store);
        var live = LiveLogin.From(new LiveFiles(Credentials("r1"), ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com")))!;

        _home.Store.Upsert(StoredAccount.Create(live, Start));

        Assert.False(_home.Store.TrySave());
        Assert.True(_home.Store.HasUnsavedChanges);
        Assert.True(_home.Store.LastSaveFailed);

        Directory.Delete(_home.Paths.Store);
        Assert.True(_home.Store.TrySave());
        Assert.False(_home.Store.LastSaveFailed);
    }

    [Fact]
    public void Remove_deletes_by_key()
    {
        var a = _home.Save("acc-a", "org-a", "a@example.com", Credentials("r1"), Start);

        Assert.True(_home.Store.Remove(a.Key));
        Assert.False(_home.Store.Remove(a.Key));
        Assert.Empty(_home.Store.Accounts);
    }

    [Fact]
    public void Finds_an_account_by_exact_credentials()
    {
        var a = _home.Save("acc-a", "org-a", "a@example.com", Credentials("r1"), Start);

        Assert.Equal(a.Key, _home.Store.FindByCredentials(Credentials("r1"))?.Key);
        Assert.Null(_home.Store.FindByCredentials(Credentials("r2")));
    }

    [Fact]
    public void Dpapi_round_trip()
    {
        var protector = new DpapiProtector();
        var plaintext = "secret"u8.ToArray();

        var protectedData = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, protectedData);
        Assert.Equal(plaintext, protector.Unprotect(protectedData));
    }
}
