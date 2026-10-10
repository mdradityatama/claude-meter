namespace ClaudeMeter.Core;

internal enum RefreshOutcome
{
    /// <summary>The token is usable for at least five more minutes.</summary>
    Fresh,
    Refreshed,
    /// <summary>invalid_grant: only /login helps.</summary>
    LoginExpired,
    /// <summary>Not refreshed (toggle off, disabled, backoff, offline, or Claude Code holds it).</summary>
    Unavailable,
}

/// <summary>Refreshes the tokens of accounts the app owns, i.e. inactive ones.</summary>
internal interface ITokenRefresher
{
    Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken);
}

internal enum SwitchStage
{
    WaitingForLock,
    Writing,
}

internal abstract record SwitchResult
{
    private SwitchResult() { }

    public sealed record Switched(string Email) : SwitchResult;
    /// <summary>Files were written, but re-reading them did not show the target.</summary>
    public sealed record Unconfirmed(string Email) : SwitchResult;
    public sealed record Failed(string Message) : SwitchResult;
}

/// <summary>
/// Replaces Claude Code's global login with a saved one, under Claude Code's locks. From the
/// credentials write on, Claude Code owns the target and the app owns the previous account.
/// </summary>
internal sealed class AccountSwitcher(ClaudePaths paths, ClaudeLocks locks, AccountStore store, IProfileClient profiles, ITokenRefresher refresher, TimeProvider clock)
{
    public const string Busy = "Claude Code is refreshing its login. Try again in a few seconds.";
    public const string Changing = "Login files are changing. Try again in a few seconds.";
    public const string Unverified = "Current account isn't saved yet (couldn't verify it). Check your connection and try again.";
    public const string NoLogin = "Claude Code has no signed-in account. Run /login in Claude Code first.";
    public const string TargetExpired = "This account's login expired. Run /login in Claude Code with it.";
    public const string AlreadyActive = "This account is already active.";

    public async Task<SwitchResult> SwitchAsync(AccountKey targetKey, IProgress<SwitchStage>? progress, CancellationToken cancellationToken)
    {
        // 1. Outside locks: a usable target, and the network check of the live login.
        if (store.Find(targetKey) is not { } target)
            return Fail("This account is no longer saved.");
        if (LoginExpiry.IsExpired(target, clock.GetUtcNow()))
            return Fail(TargetExpired);
        if (await refresher.EnsureFreshAsync(targetKey, cancellationToken) == RefreshOutcome.LoginExpired)
            return Fail(TargetExpired);

        LiveLogin? before;
        try
        {
            before = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return Fail(Changing);
        }

        if (before is null)
            return Fail(NoLogin);
        if (before.Account.Key == targetKey)
            return Fail(AlreadyActive);

        var profile = store.FindByCredentials(before.CredentialsJson) is null
            ? await ProfileCheck.RunAsync(profiles, before.Credentials, clock, cancellationToken)
            : null;

        progress?.Report(SwitchStage.WaitingForLock);
        try
        {
            // 2.
            using var refreshLocks = await locks.AcquireRefreshAsync(cancellationToken);
            using var storageLock = await locks.AcquireStorageWriteAsync(cancellationToken);
            progress?.Report(SwitchStage.Writing);

            // 3. Attribute the live login to A; A must be on disk before its live copy is replaced.
            var live = LiveLogin.From(LiveFiles.Read(paths));
            if (live is null || !live.SameAs(before))
                return Fail(Changing);
            if (profile is not null)
            {
                var decision = CaptureVerifier.Evaluate(live, store.Accounts, profile);
                if (decision.Verdict != CaptureVerdict.Accept)
                    return Fail(decision.Verdict == CaptureVerdict.Defer ? Unverified : Changing);
                store.Upsert(store.Find(live.Account.Key)?.WithLiveLogin(live) ?? StoredAccount.Create(live, clock.GetUtcNow()));
            }

            // Byte-matched or just captured, the snapshot of the live login must be on disk, not only in memory.
            if ((store.HasUnsavedChanges || store.LastSaveFailed) && !store.TrySave())
                return Fail("Couldn't save the current account, so nothing was changed.");

            // 4–5.
            target = store.Find(targetKey)!;
            var rollback = live.CredentialsJson;
            AtomicFile.Write(paths.Credentials, target.CredentialsJson);

            // 6–7.
            try
            {
                using var configLock = await locks.AcquireGlobalConfigAsync(cancellationToken);
                var config = File.ReadAllBytes(paths.GlobalConfig);
                AtomicFile.Write(paths.GlobalConfig, JsonSplice.Replace(config, target.OAuthAccountJson, "oauthAccount"));
            }
            catch (Exception ex) when (ex is LockTimeoutException or IOException or UnauthorizedAccessException or FormatException)
            {
                try
                {
                    AtomicFile.Write(paths.Credentials, rollback);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    // Credentials now hold the target while oauthAccount still names the previous account;
                    // switching to the target again repairs that (its credentials byte-match the snapshot).
                    return Fail($"Couldn't finish the switch or undo it. Switch to {target.Email} again from the panel.");
                }

                return Fail(ex is LockTimeoutException ? Busy : "Couldn't update Claude Code's settings file. Nothing was changed.");
            }

            // 8.
            var now = clock.GetUtcNow();
            foreach (var key in new[] { targetKey, live.Account.Key })
            {
                if (store.Find(key) is { } account)
                    store.Upsert(account with { LastActiveAt = now });
            }
            store.TrySave();
        }
        catch (LockTimeoutException)
        {
            return Fail(Busy);
        }
        catch (FormatException)
        {
            return Fail(Changing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("Couldn't write Claude Code's login file. Nothing was changed.");
        }

        // 9. Post-check, outside locks.
        LiveLogin? after;
        try
        {
            after = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            after = null;
        }

        return after is not null && after.Account.Key == targetKey && after.CredentialsJson.AsSpan().SequenceEqual(target.CredentialsJson)
            ? new SwitchResult.Switched(target.Email)
            : new SwitchResult.Unconfirmed(target.Email);

        static SwitchResult Fail(string message) => new SwitchResult.Failed(message);
    }
}
