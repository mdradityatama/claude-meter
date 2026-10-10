namespace ClaudeMeter.Core;

internal abstract record AutoSaveResult
{
    private AutoSaveResult() { }

    public sealed record NoLogin : AutoSaveResult;
    public sealed record Unchanged : AutoSaveResult;
    public sealed record Inserted(string Email) : AutoSaveResult;
    public sealed record Updated : AutoSaveResult;
    /// <summary>New identity not verified yet: "Not saved yet: waiting for network to verify".</summary>
    public sealed record Deferred(string Email) : AutoSaveResult;
    /// <summary>A check failed: "Login files are changing; will retry".</summary>
    public sealed record Rejected(string Reason) : AutoSaveResult;
    /// <summary>Claude Code held a lock for the whole timeout; try at the next trigger.</summary>
    public sealed record Busy : AutoSaveResult;
}

/// <summary>Saves the active Claude Code login whenever it is new or its credentials changed, once verified.</summary>
internal sealed class AutoSaver(ClaudePaths paths, ClaudeLocks locks, AccountStore store, IProfileClient profiles, TimeProvider clock)
{
    /// <summary>Set after a switch post-check mismatch: only a profile-verified capture is accepted until one succeeds.</summary>
    public bool RequireProfile { get; set; }

    public async Task<AutoSaveResult> RunAsync(CancellationToken cancellationToken)
    {
        LiveLogin? live;
        try
        {
            live = LiveLogin.From(LiveFiles.Read(paths));
        }
        catch (FormatException)
        {
            return new AutoSaveResult.Rejected("unreadable global config");
        }

        if (live is null)
            return new AutoSaveResult.NoLogin();

        // Byte-match rule: credentials already saved need no verification.
        if (store.FindByCredentials(live.CredentialsJson) is { } owner)
        {
            if (owner.Key != live.Account.Key)
                return new AutoSaveResult.Rejected("credentials of another saved account");
            if (owner.OAuthAccountJson.AsSpan().SequenceEqual(live.Account.Raw))
                return new AutoSaveResult.Unchanged();
            store.Upsert(owner.WithLiveLogin(live));
            store.TrySave();
            return new AutoSaveResult.Updated();
        }

        var profile = await ProfileCheck.RunAsync(profiles, live.Credentials, clock, cancellationToken);
        if (RequireProfile && profile is not ProfileResult.Found)
            return new AutoSaveResult.Rejected("waiting for a verified capture");

        var decision = CaptureVerifier.Evaluate(live, store.Accounts, profile);
        if (decision.Verdict == CaptureVerdict.Defer)
            return new AutoSaveResult.Deferred(live.Account.Email);
        if (decision.Verdict == CaptureVerdict.Reject)
            return new AutoSaveResult.Rejected(decision.Reason!);

        // Check 4: the verified bytes must still be what Claude Code has, read under its locks.
        try
        {
            using var refreshLocks = await locks.AcquireRefreshAsync(cancellationToken);
            using var storageLock = await locks.AcquireStorageWriteAsync(cancellationToken);
            if (LiveLogin.From(LiveFiles.Read(paths)) is not { } again || !again.SameAs(live))
                return new AutoSaveResult.Rejected("files changed during capture");
        }
        catch (LockTimeoutException)
        {
            return new AutoSaveResult.Busy();
        }
        catch (FormatException)
        {
            return new AutoSaveResult.Rejected("unreadable global config");
        }

        if (profile is ProfileResult.Found)
            RequireProfile = false;

        var existing = store.Find(live.Account.Key);
        store.Upsert(existing?.WithLiveLogin(live) ?? StoredAccount.Create(live, clock.GetUtcNow()));
        store.TrySave();   // on failure the runtime retries on every poll
        return existing is null ? new AutoSaveResult.Inserted(live.Account.Email) : new AutoSaveResult.Updated();
    }
}
