using System.Runtime.InteropServices;

namespace ClaudeMeter.Core;

internal interface ILockFileSystem
{
    /// <returns>False if the directory already exists (or is being deleted).</returns>
    bool TryCreateDirectory(string path);

    /// <returns>Null if the directory does not exist.</returns>
    DateTimeOffset? GetLastWriteTime(string path);

    void SetLastWriteTime(string path, DateTimeOffset time);

    /// <summary>Removes an empty directory; no-op if it is missing.</summary>
    void DeleteDirectory(string path);
}

internal sealed class LockFileSystem : ILockFileSystem
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorAlreadyExists = 183;

    // Directory.CreateDirectory succeeds silently on an existing directory, so it cannot be a mutex.
    public bool TryCreateDirectory(string path)
    {
        if (CreateDirectoryW(path, IntPtr.Zero))
            return true;
        var error = Marshal.GetLastPInvokeError();
        return error is ErrorAlreadyExists or ErrorAccessDenied
            ? false
            : throw new IOException($"Cannot create lock directory {Path.GetFileName(path)} (error {error}).");
    }

    public DateTimeOffset? GetLastWriteTime(string path) =>
        Directory.Exists(path) ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;

    public void SetLastWriteTime(string path, DateTimeOffset time) => Directory.SetLastWriteTimeUtc(path, time.UtcDateTime);

    public void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
}

/// <param name="Stale">A lock whose mtime is older than this may be taken over.</param>
internal sealed record LockSpec(string Path, TimeSpan Stale);

internal sealed class LockTimeoutException(string path)
    : Exception($"Timed out waiting for {System.IO.Path.GetFileName(path)}.");

/// <summary>
/// Locks compatible with npm proper-lockfile, which Claude Code uses: a lock is a directory and creating
/// it is the mutex. Held locks are touched so they never look stale. Never writes an owner record, so
/// Claude Code can only take a lock from us through the stale rule.
/// </summary>
internal sealed class DirectoryLocks(ILockFileSystem fs, TimeProvider clock, Func<TimeSpan, CancellationToken, Task> delay)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Takes all locks in order. If one is busy, the ones already taken are released and the attempt
    /// starts over, so a waiter never sits on a lock (Claude Code does this for its two refresh locks).
    /// </summary>
    /// <exception cref="LockTimeoutException">Not all locks were free within <see cref="Timeout"/>.</exception>
    public async Task<HeldLocks> AcquireAsync(IReadOnlyList<LockSpec> specs, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + Timeout;
        while (true)
        {
            var held = new List<HeldLock>();
            foreach (var spec in specs)
            {
                if (TryAcquire(spec) is not { } one)
                    break;
                held.Add(one);
            }

            if (held.Count == specs.Count)
                return new HeldLocks(held);

            new HeldLocks(held).Dispose();
            if (clock.GetUtcNow() >= deadline)
                throw new LockTimeoutException(specs[held.Count].Path);
            await delay(RetryDelay, cancellationToken);
        }
    }

    /// <summary>One attempt, no waiting. Takes over only a lock older than its stale threshold.</summary>
    internal HeldLock? TryAcquire(LockSpec spec)
    {
        if (!fs.TryCreateDirectory(spec.Path))
        {
            if (fs.GetLastWriteTime(spec.Path) is not { } mtime || clock.GetUtcNow() - mtime <= spec.Stale)
                return null;
            fs.DeleteDirectory(spec.Path);
            if (!fs.TryCreateDirectory(spec.Path))
                return null;
        }

        return new HeldLock(spec.Path, fs, clock);
    }
}

internal sealed class HeldLock : IDisposable
{
    private readonly ILockFileSystem _fs;
    private readonly TimeProvider _clock;
    private readonly ITimer _toucher;
    private readonly Lock _sync = new();
    private bool _released;

    public HeldLock(string path, ILockFileSystem fs, TimeProvider clock)
    {
        Path = path;
        _fs = fs;
        _clock = clock;
        _toucher = clock.CreateTimer(_ => Touch(), null, DirectoryLocks.TouchInterval, DirectoryLocks.TouchInterval);
    }

    public string Path { get; }

    private void Touch()
    {
        lock (_sync)
        {
            if (_released)
                return;
            try
            {
                _fs.SetLastWriteTime(Path, _clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next touch retries; the stale threshold is far longer than the interval.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_released)
                return;
            _released = true;
        }

        _toucher.Dispose();
        try
        {
            _fs.DeleteDirectory(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind, it turns stale and Claude Code takes it over.
        }
    }
}

/// <summary>Releases in reverse order of acquisition.</summary>
internal sealed class HeldLocks(IReadOnlyList<HeldLock> locks) : IDisposable
{
    public void Dispose()
    {
        for (var i = locks.Count - 1; i >= 0; i--)
            locks[i].Dispose();
    }
}

/// <summary>Claude Code's own locks, in its order: OAuth refresh (primary, legacy), storage write, global config.</summary>
internal sealed class ClaudeLocks(ClaudePaths paths, DirectoryLocks locks)
{
    private static readonly TimeSpan RefreshStale = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StorageWriteStale = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan GlobalConfigStale = TimeSpan.FromSeconds(10);

    public static ClaudeLocks ForSystem(ClaudePaths paths) =>
        new(paths, new DirectoryLocks(new LockFileSystem(), TimeProvider.System, (d, ct) => Task.Delay(d, ct)));

    public Task<HeldLocks> AcquireRefreshAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.OAuthRefreshLock, RefreshStale), new(paths.LegacyLock, RefreshStale)], ct);

    public Task<HeldLocks> AcquireStorageWriteAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.StorageWriteLock, StorageWriteStale)], ct);

    public Task<HeldLocks> AcquireGlobalConfigAsync(CancellationToken ct) =>
        locks.AcquireAsync([new(paths.GlobalConfigLock, GlobalConfigStale)], ct);
}
