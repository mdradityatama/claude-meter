namespace ClaudeMeter.Core;

internal enum CaptureVerdict
{
    Accept,
    /// <summary>A new identity that could not be verified yet; try again later.</summary>
    Defer,
    Reject,
}

internal sealed record CaptureDecision(CaptureVerdict Verdict, string? Reason = null);

/// <summary>Checks 1–3 of capture verification. Check 4, the locked re-read, belongs to the caller.</summary>
internal static class CaptureVerifier
{
    public static CaptureDecision Evaluate(LiveLogin live, IReadOnlyList<StoredAccount> stored, ProfileResult profile)
    {
        var key = live.Account.Key;
        var credentials = live.Credentials;

        if (credentials.RefreshToken is null)
            return Reject("no refresh token");
        if (credentials.OrganizationUuid is { } org && org != key.OrganizationUuid)
            return Reject("organization mismatch");
        if (stored.Any(a => a.Key != key && a.Credentials?.RefreshToken == credentials.RefreshToken))
            return Reject("token of another saved account");

        return profile switch
        {
            ProfileResult.Found found when found.AccountUuid != key.AccountUuid
                || (found.OrganizationUuid is { } profileOrg && profileOrg != key.OrganizationUuid) => Reject("profile mismatch"),
            ProfileResult.Found => new CaptureDecision(CaptureVerdict.Accept),
            _ => stored.Any(a => a.Key == key)
                ? new CaptureDecision(CaptureVerdict.Accept)
                : new CaptureDecision(CaptureVerdict.Defer, "unverified"),
        };

        static CaptureDecision Reject(string reason) => new(CaptureVerdict.Reject, reason);
    }
}
