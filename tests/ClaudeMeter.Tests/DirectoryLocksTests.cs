using ClaudeMeter.Core;

namespace ClaudeMeter.Tests;

public class DirectoryLocksTests
{
    private static readonly DateTimeOffset Start = ClaudeHome.Start;
    private static readonly LockSpec A = new(@"C:\h\.claude\.oauth_refresh.lock", TimeSpan.FromSeconds(60));
    private static readonly LockSpec B = new(@"C:\h\.claude.lock", TimeSpan.FromSeconds(60));

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeLockFileSystem _fs;
    private readonly DirectoryLocks _locks;

    public DirectoryLocksTests()
    {
        _fs = new FakeLockFileSystem(_clock);
        _locks = new DirectoryLocks(_fs, _clock, (d, _) =>
        {
            _clock.Advance(d);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Acquire_creates_and_release_deletes()
    {
        using (await _locks.AcquireAsync([A], default))
            Assert.True(_fs.Exists(A.Path));

        Assert.False(_fs.Exists(A.Path));
    }

    [Fact]
    public async Task Young_foreign_lock_times_out_after_9_seconds_and_is_left_alone()
    {
        _fs.AddForeign(A.Path, Start);

        await Assert.ThrowsAsync<LockTimeoutException>(() => _locks.AcquireAsync([A], default));

        Assert.True(_fs.Exists(A.Path));
        Assert.Empty(_fs.Log);
        Assert.InRange(_clock.Now - Start, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(9.2));
    }

    [Fact]
    public void Lock_older_than_stale_is_taken_over()
    {
        _fs.AddForeign(A.Path, Start - TimeSpan.FromSeconds(61));

        using var held = _locks.TryAcquire(A);

        Assert.NotNull(held);
        Assert.Equal(["delete .oauth_refresh.lock", "create .oauth_refresh.lock"], _fs.Log);
    }

    [Fact]
    public void Lock_exactly_at_the_threshold_is_not_taken_over()
    {
        _fs.AddForeign(A.Path, Start - TimeSpan.FromSeconds(60));

        Assert.Null(_locks.TryAcquire(A));
        Assert.Empty(_fs.Log);
    }

    [Fact]
    public async Task Lock_is_released_when_the_holder_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var held = await _locks.AcquireAsync([A], default);
            throw new InvalidOperationException();
        });

        Assert.False(_fs.Exists(A.Path));
    }

    [Fact]
    public async Task Held_lock_is_touched_every_3_seconds_and_not_after_release()
    {
        var held = await _locks.AcquireAsync([A], default);
        _clock.Advance(TimeSpan.FromSeconds(7));
        held.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(
            ["create .oauth_refresh.lock", "touch .oauth_refresh.lock", "touch .oauth_refresh.lock", "delete .oauth_refresh.lock"],
            _fs.Log);
    }

    [Fact]
    public async Task Failure_on_a_later_lock_releases_the_earlier_ones()
    {
        _fs.FailCreate = path => path == B.Path;

        await Assert.ThrowsAsync<IOException>(() => _locks.AcquireAsync([A, B], default));

        Assert.False(_fs.Exists(A.Path));
        Assert.Equal(0, _fs.HeldCount);
    }

    [Fact]
    public async Task Busy_second_lock_releases_the_first_and_retries()
    {
        _fs.AddForeign(B.Path, Start);
        var waits = 0;
        var locks = new DirectoryLocks(_fs, _clock, (d, _) =>
        {
            if (++waits == 2)
                _fs.RemoveForeign(B.Path);
            _clock.Advance(d);
            return Task.CompletedTask;
        });

        using (await locks.AcquireAsync([A, B], default))
        {
        }

        Assert.Equal(
            ["create .oauth_refresh.lock", "delete .oauth_refresh.lock", "create .oauth_refresh.lock", "delete .oauth_refresh.lock",
             "create .oauth_refresh.lock", "create .claude.lock", "delete .claude.lock", "delete .oauth_refresh.lock"],
            _fs.Log);
    }
}
