using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class LoginExpiryTests
{
    private static readonly DateTimeOffset Now = ClaudeHome.Start;

    private static LoginInfo Describe(DateTimeOffset? expiresAt, LoginState state = LoginState.Valid, DateTimeOffset? lastActive = null, bool active = false) =>
        LoginExpiry.Describe(expiresAt, state, lastActive, active, Now);

    private static StoredAccount Account(DateTimeOffset loginExpiresAt, long? warnedFor = null, LoginState state = LoginState.Valid)
    {
        var credentials = ClaudeJson.Credentials("a", "r", Now.AddHours(1), "org-a", loginExpiresAt);
        var live = LiveLogin.From(new LiveFiles(credentials, ClaudeJson.GlobalConfig("acc-a", "org-a", "a@example.com")))!;
        return StoredAccount.Create(live, Now) with { ExpiryWarnedFor = warnedFor, LoginState = state };
    }

    [Fact]
    public void Valid_login_shows_whole_days_left() =>
        Assert.Equal(new LoginInfo("Login valid 13 days", Tone.Muted, false), Describe(Now.AddDays(13.5)));

    [Fact]
    public void Three_days_or_less_is_caution()
    {
        Assert.Equal(new LoginInfo("Login valid 3 days", Tone.Muted, false), Describe(Now.AddDays(3).AddMinutes(1)));
        Assert.Equal(new LoginInfo("Login expires in 3 days", Tone.Caution, false), Describe(Now.AddDays(3)));
        Assert.Equal("Login expires in 1 day", Describe(Now.AddDays(1.5)).Text);
        Assert.Equal("Login expires today", Describe(Now.AddDays(0.5)).Text);
    }

    [Fact]
    public void Past_date_or_invalid_grant_is_expired()
    {
        Assert.Equal(new LoginInfo("Login expired", Tone.Critical, true), Describe(Now.AddSeconds(-1)));
        Assert.Equal(new LoginInfo("Login expired", Tone.Critical, true), Describe(Now.AddDays(10), LoginState.Expired));
    }

    [Fact]
    public void Missing_date_falls_back_to_last_active()
    {
        Assert.Equal(new LoginInfo("Last active 15 days ago", Tone.Caution, false), Describe(null, lastActive: Now.AddDays(-15)));
        Assert.Equal(new LoginInfo("Last active 14 days ago", Tone.Muted, false), Describe(null, lastActive: Now.AddDays(-14)));
        Assert.Equal("Last active today", Describe(null, lastActive: Now.AddHours(-3)).Text);
        Assert.Equal("", Describe(null, lastActive: Now.AddDays(-30), active: true).Text);
    }

    [Fact]
    public void Warning_is_due_at_two_days_or_less()
    {
        Assert.Equal("in 2 days", LoginExpiry.WarningDue(Account(Now.AddDays(2)), Now));
        Assert.Null(LoginExpiry.WarningDue(Account(Now.AddDays(2).AddMinutes(1)), Now));
        Assert.Null(LoginExpiry.WarningDue(Account(Now.AddSeconds(-1)), Now));
        Assert.Null(LoginExpiry.WarningDue(Account(Now.AddDays(1), state: LoginState.Expired), Now));
    }

    [Fact]
    public void Warning_is_shown_once_per_expiry_value()
    {
        var at = Now.AddDays(1);

        Assert.Null(LoginExpiry.WarningDue(Account(at, warnedFor: at.ToUnixTimeMilliseconds()), Now));
    }

    [Fact]
    public void New_login_expiry_value_re_arms_the_warning()
    {
        var old = Now.AddDays(-20);

        Assert.Equal("in 1 day", LoginExpiry.WarningDue(Account(Now.AddDays(1.2), warnedFor: old.ToUnixTimeMilliseconds()), Now));
    }

    [Fact]
    public void Is_expired_by_state_or_date()
    {
        Assert.True(LoginExpiry.IsExpired(Account(Now.AddDays(5), state: LoginState.Expired), Now));
        Assert.True(LoginExpiry.IsExpired(Account(Now.AddSeconds(-1)), Now));
        Assert.False(LoginExpiry.IsExpired(Account(Now.AddDays(5)), Now));
    }
}
