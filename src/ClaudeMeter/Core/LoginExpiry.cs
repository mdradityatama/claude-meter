namespace ClaudeMeter.Core;

internal enum Tone
{
    Muted,
    Caution,
    Critical,
}

internal sealed record LoginInfo(string Text, Tone Tone, bool Expired);

/// <summary>Every login ends at refreshTokenExpiresAt and needs /login again; refreshing does not extend it.</summary>
internal static class LoginExpiry
{
    private static readonly TimeSpan CautionWindow = TimeSpan.FromDays(3);
    private static readonly TimeSpan ToastWindow = TimeSpan.FromDays(2);
    private static readonly TimeSpan IdleWarning = TimeSpan.FromDays(14);

    public static bool IsExpired(StoredAccount account, DateTimeOffset now) =>
        account.LoginState == LoginState.Expired || account.Credentials?.RefreshTokenExpiresAt <= now;

    public static LoginInfo Describe(DateTimeOffset? loginExpiresAt, LoginState state, DateTimeOffset? lastActiveAt, bool isActive, DateTimeOffset now)
    {
        if (state == LoginState.Expired || loginExpiresAt <= now)
            return new LoginInfo("Login expired", Tone.Critical, Expired: true);

        if (loginExpiresAt is { } at)
        {
            var left = at - now;
            return left > CautionWindow
                ? new LoginInfo($"Login valid {Days((int)left.TotalDays)}", Tone.Muted, false)
                : new LoginInfo($"Login expires {InDays(left)}", Tone.Caution, false);
        }

        if (isActive || lastActiveAt is not { } last)
            return new LoginInfo("", Tone.Muted, false);

        var idle = now - last;
        var ago = (int)idle.TotalDays == 0 ? "today" : $"{Days((int)idle.TotalDays)} ago";
        return new LoginInfo($"Last active {ago}", idle > IdleWarning ? Tone.Caution : Tone.Muted, false);
    }

    /// <returns>"in N days" when a toast is due (2 days or less, once per refreshTokenExpiresAt), else null.</returns>
    public static string? WarningDue(StoredAccount account, DateTimeOffset now) =>
        account.LoginState == LoginState.Valid
        && account.Credentials?.RefreshTokenExpiresAt is { } at
        && at > now && at - now <= ToastWindow
        && account.ExpiryWarnedFor != at.ToUnixTimeMilliseconds()
            ? InDays(at - now)
            : null;

    public static string InDays(TimeSpan left) => (int)left.TotalDays switch
    {
        0 => "today",
        var days => $"in {Days(days)}",
    };

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}
