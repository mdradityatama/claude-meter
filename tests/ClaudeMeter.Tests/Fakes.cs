using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<FakeTimer> _timers = [];

    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves time forward, firing due timers in time order.</summary>
    public void Advance(TimeSpan by)
    {
        var end = Now + by;
        while (_timers.Where(t => t.DueAt <= end).MinBy(t => t.DueAt) is { } timer)
        {
            Now = timer.DueAt!.Value;
            timer.Fire();
        }
        Now = end;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class FakeTimer(FakeTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
            clock._timers.Remove(this);
            if (DueAt is not null)
                clock._timers.Add(this);
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : DueAt + _period;
            if (DueAt is null)
                clock._timers.Remove(this);
            callback(state);
        }

        public void Dispose()
        {
            DueAt = null;
            clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>In-memory lock directories with an operation log ("create x", "touch x", "delete x" by file name).</summary>
internal sealed class FakeLockFileSystem(FakeTimeProvider clock) : ILockFileSystem
{
    private readonly Dictionary<string, DateTimeOffset> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ours = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Log { get; } = [];

    /// <summary>Runs after each successful create, e.g. to change files while a lock is held.</summary>
    public Action<string>? AfterCreate { get; set; }

    /// <summary>Runs before each delete, i.e. just before a lock is released.</summary>
    public Action<string>? BeforeDelete { get; set; }

    /// <summary>Locks this process holds right now.</summary>
    public int HeldCount => _ours.Count;

    public void AddForeign(string path, DateTimeOffset mtime) => _dirs[path] = mtime;

    public void RemoveForeign(string path) => _dirs.Remove(path);

    public bool Exists(string path) => _dirs.ContainsKey(path);

    public bool TryCreateDirectory(string path)
    {
        if (_dirs.ContainsKey(path))
            return false;
        _dirs[path] = clock.Now;
        _ours.Add(path);
        Log.Add($"create {Path.GetFileName(path)}");
        AfterCreate?.Invoke(path);
        return true;
    }

    public DateTimeOffset? GetLastWriteTime(string path) => _dirs.TryGetValue(path, out var t) ? t : null;

    public void SetLastWriteTime(string path, DateTimeOffset time)
    {
        if (!_dirs.ContainsKey(path))
            return;
        _dirs[path] = time;
        Log.Add($"touch {Path.GetFileName(path)}");
    }

    public void DeleteDirectory(string path)
    {
        BeforeDelete?.Invoke(path);
        if (_dirs.Remove(path))
            Log.Add($"delete {Path.GetFileName(path)}");
        _ours.Remove(path);
    }
}

internal sealed class FakeUsageClient : IUsageClient
{
    public Queue<FetchResult> Results { get; } = new();
    public List<string> TokensSeen { get; } = [];

    /// <summary>When set, answers by token instead of from <see cref="Results"/>.</summary>
    public Func<string, FetchResult>? Responder { get; set; }

    public Task<FetchResult> FetchAsync(string accessToken, CancellationToken cancellationToken)
    {
        TokensSeen.Add(accessToken);
        return Task.FromResult(Responder?.Invoke(accessToken) ?? Results.Dequeue());
    }
}

/// <summary>Reversible and visibly not plaintext, so tests can assert nothing is stored in the clear.</summary>
internal sealed class FakeProtector : IDataProtector
{
    public byte[] Protect(byte[] plaintext) => [.. "P1"u8, .. plaintext.Select(b => (byte)(b ^ 0x5A))];

    public byte[] Unprotect(byte[] data) =>
        data.AsSpan().StartsWith("P1"u8)
            ? [.. data.Skip(2).Select(b => (byte)(b ^ 0x5A))]
            : throw new System.Security.Cryptography.CryptographicException("Not ours.");
}

internal sealed class FakeProfileClient : IProfileClient
{
    public ProfileResult Result { get; set; } = new ProfileResult.Unavailable("offline");
    public List<string> Calls { get; } = [];
    public Action? OnCall { get; set; }

    public Task<ProfileResult> GetAsync(string accessToken, CancellationToken cancellationToken)
    {
        OnCall?.Invoke();
        Calls.Add(accessToken);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeTokenRefresher : ITokenRefresher
{
    public RefreshOutcome Outcome { get; set; } = RefreshOutcome.Fresh;
    public List<AccountKey> Calls { get; } = [];

    public Task<RefreshOutcome> EnsureFreshAsync(AccountKey key, CancellationToken cancellationToken)
    {
        Calls.Add(key);
        return Task.FromResult(Outcome);
    }
}

internal sealed class FakeOAuthTokenClient : IOAuthTokenClient
{
    public Queue<TokenResult> Results { get; } = new();
    public List<string> RefreshTokensSeen { get; } = [];

    public Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshTokensSeen.Add(refreshToken);
        return Task.FromResult(Results.Dequeue());
    }
}