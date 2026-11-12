using System.Collections.Concurrent;
using Omni.App.Common.Statics;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Sign-in lockout counted per account AND address (QA t12-fix-01): five wrong passwords for one account from one address
/// lock that pair for <see cref="AccountRules.LockoutDuration"/>, so someone else's attempts can't keep the owner locked out
/// of their own account from their own address (Identity's lockout was per account). A distributed guess at one account is
/// still stopped: <see cref="AccountRules.MaxFailedSignInsPerAccount"/> failures from any mix of addresses within the same
/// window lock the account everywhere. Per-address request limits (Accounts:AttemptsPerIpPerHour) stay on top.
/// In memory, so per app instance (the app runs as one instance; a restart clears it).
/// </summary>
public sealed class SignInThrottle
{
    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset WindowStart;
        public DateTimeOffset? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public SignInThrottle(TimeProvider time) => _time = time;

    private static string Pair(string email, string address) => $"{email.Trim().ToUpperInvariant()}|{address}";
    private static string Account(string email) => email.Trim().ToUpperInvariant();

    /// <summary>Whether this account may not try a password from this address right now.</summary>
    public bool IsLocked(string email, string address)
    {
        var now = _time.GetUtcNow();
        return Locked(Pair(email, address), now) || Locked(Account(email), now);
    }

    /// <summary>A wrong password: counts for the pair and for the account.</summary>
    public void Failed(string email, string address)
    {
        var now = _time.GetUtcNow();
        Count(Pair(email, address), AccountRules.MaxFailedSignIns, now);
        Count(Account(email), AccountRules.MaxFailedSignInsPerAccount, now);
        if (_counters.Count > 50_000) Sweep(now); // a flood of made-up emails can't grow it for ever
    }

    /// <summary>A right password: the pair starts again (the account-wide count keeps running for the window).</summary>
    public void Succeeded(string email, string address) => _counters.TryRemove(Pair(email, address), out _);

    private bool Locked(string key, DateTimeOffset now) =>
        _counters.TryGetValue(key, out var c) && c.LockedUntil is { } until && until > now;

    private void Count(string key, int max, DateTimeOffset now)
    {
        var c = _counters.GetOrAdd(key, _ => new Counter { WindowStart = now });
        lock (c)
        {
            if (now - c.WindowStart > AccountRules.LockoutDuration || c.LockedUntil is { } until && until <= now)
            {
                c.Failures = 0;
                c.WindowStart = now;
                c.LockedUntil = null;
            }
            if (++c.Failures >= max) c.LockedUntil = now + AccountRules.LockoutDuration;
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, c) in _counters)
        {
            if (now - c.WindowStart > AccountRules.LockoutDuration && (c.LockedUntil is null || c.LockedUntil <= now)) _counters.TryRemove(key, out _);
        }
    }
}
