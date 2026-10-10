namespace ClaudeMeter.Core;

internal sealed record Notification(string Title, string Body);

/// <summary>Toast copy, as in the approved mockup.</summary>
internal static class Notifications
{
    public static Notification Switched(string email) =>
        new($"Switched to {email}", "Reopen Claude Code to use it. Running sessions may move to this account at their next token refresh.");

    public static Notification SwitchUnconfirmed(string email) =>
        new("Switch not confirmed", $"Claude Code's login files don't show {email}. Claude Meter saves no logins until it can verify one.");

    public static Notification SwitchFailed(string reason) => new("Couldn't switch accounts", reason);

    public static Notification Added(string email) =>
        new($"Added {email}", "Claude Meter saved this login. Switch accounts from the tray panel.");

    public static Notification LoginExpiring(string email, string inDays) =>
        new($"{email}: login expires {inDays}", "Switch to it and run /login in Claude Code to renew.");

    public static Notification RefreshStopped { get; } =
        new("Background refresh stopped", "Anthropic rejected the token refresh. Inactive accounts now show last known usage.");

    public static Notification StoreSetAside { get; } =
        new("Saved accounts couldn't be read", "Claude Meter kept the old file and starts fresh. Accounts are saved again as you use them.");
}
