namespace ClaudeMeter.Core;

/// <summary>What Claude Code holds right now, read fresh from its files.</summary>
internal sealed record ClaudeHolder(AccountKey? ActiveKey, string? LiveRefreshToken)
{
    /// <summary>True if Claude Code uses this account, by identity or because its refresh token is the live one.</summary>
    public bool Holds(StoredAccount account) =>
        account.Key == ActiveKey || (LiveRefreshToken is not null && account.Credentials?.RefreshToken == LiveRefreshToken);

    /// <returns>Null if the files can't be read; then nothing may be refreshed.</returns>
    public static ClaudeHolder? Read(ClaudePaths paths)
    {
        try
        {
            var files = LiveFiles.Read(paths);
            return new ClaudeHolder(
                files.GlobalConfig is { } config ? OAuthAccount.Parse(config)?.Key : null,
                files.Credentials is { } credentials ? CredentialsInfo.Parse(credentials)?.RefreshToken : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Refreshes the saved tokens of inactive accounts so their usage stays live. Never touches the
/// account Claude Code holds. Callers serialize calls through the runtime's gate.
/// </summary>
internal sealed class TokenRefresher(AccountStore store, IOAuthTokenClient client, Func<ClaudeHolder?> readHolder, Func<bool> keepLive, TimeProvider clock) : ITokenRefresher
{
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly Dictionary<AccountKey, RefreshPolicy> _backoff = [];

    public bool GloballyDisabled { get; private set; }

    public bool CanRefresh => keepLive() && !GloballyDisabled;

    /// <summary>Raised once when Anthropic rejects the client id.</summary>
    public event EventHandler? Disabled;

    public bool NeedsRefresh(StoredAccount account) =>
        account.Credentials?.ExpiresAt is not { } expiresAt || expiresAt - clock.GetUtcNow() <= Margin;

    public Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken) => RefreshAsync(key, force: false, cancellationToken);

    /// <summary>After a 401: refresh even if the token looks valid.</summary>
    public Task<RefreshOutcome> ForceRefreshAsync(AccountKey key, CancellationToken cancellationToken) => RefreshAsync(key, force: true, cancellationToken);

    private async Task<RefreshOutcome> RefreshAsync(AccountKey key, bool force, CancellationToken cancellationToken)
    {
        if (store.Find(key) is not { } account)
            return RefreshOutcome.Unavailable;
        if (account.LoginState == LoginState.Expired)
            return RefreshOutcome.LoginExpired;
        if (!force && !NeedsRefresh(account))
            return RefreshOutcome.Fresh;
        if (!CanRefresh || store.LastSaveFailed || account.Credentials?.RefreshToken is not { } refreshToken)
            return RefreshOutcome.Unavailable;
        if (readHolder() is not { } holder || holder.Holds(account))
            return RefreshOutcome.Unavailable;

        var policy = Backoff(key);
        if (!policy.TryBeginRequest(manual: false))
            return RefreshOutcome.Unavailable;

        var result = await client.RefreshAsync(refreshToken, cancellationToken);

        // Re-find: the record may have changed while the request was out.
        if (store.Find(key) is not { } current || !ReferenceEquals(current.CredentialsJson, account.CredentialsJson))
            return RefreshOutcome.Unavailable;

        switch (result)
        {
            case TokenResult.Success success:
                policy.OnSuccess();
                var json = Splice(current.CredentialsJson, "accessToken", JsonSplice.EncodeString(success.AccessToken));
                json = Splice(json, "expiresAt", JsonSplice.EncodeNumber((clock.GetUtcNow() + success.ExpiresIn).ToUnixTimeMilliseconds()));
                if (success.RefreshToken is { } rotated)
                    json = Splice(json, "refreshToken", JsonSplice.EncodeString(rotated));
                if (success.Scopes is { } scopes)
                    json = Splice(json, "scopes", JsonSplice.EncodeStringArray(scopes));
                store.Upsert(current with { CredentialsJson = json });
                store.TrySave();   // if this fails, LastSaveFailed blocks further refreshes until a save succeeds
                return RefreshOutcome.Refreshed;

            case TokenResult.InvalidGrant:
                store.Upsert(current with { LoginState = LoginState.Expired });
                store.TrySave();
                return RefreshOutcome.LoginExpired;

            case TokenResult.InvalidClient:
                if (!GloballyDisabled)
                {
                    GloballyDisabled = true;
                    Disabled?.Invoke(this, EventArgs.Empty);
                }
                return RefreshOutcome.Unavailable;

            case TokenResult.Transient transient:
                policy.OnRateLimited(transient.RetryAfter);
                return RefreshOutcome.Unavailable;

            default:
                throw new InvalidOperationException("Unknown token result.");
        }
    }

    private static byte[] Splice(byte[] json, string field, byte[] value) => JsonSplice.Replace(json, value, "claudeAiOauth", field);

    private RefreshPolicy Backoff(AccountKey key) =>
        _backoff.TryGetValue(key, out var policy) ? policy : _backoff[key] = new RefreshPolicy(clock);
}
