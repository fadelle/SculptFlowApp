using Omni.App.Common.Helpers;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t12-fix-01: five wrong passwords from anywhere locked the owner out for 15 minutes.</summary>
public class SignInThrottleTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Wrong_passwords_from_one_address_lock_only_that_address()
    {
        var throttle = new SignInThrottle(_time);
        for (var i = 0; i < AccountRules.MaxFailedSignIns; i++) throttle.Failed("owner@x.com", "203.0.113.9");

        Assert.True(throttle.IsLocked("OWNER@x.com ", "203.0.113.9"));     // the attacker's address
        Assert.False(throttle.IsLocked("owner@x.com", "198.51.100.7"));    // the owner's own address still works
        _time.Advance(AccountRules.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.False(throttle.IsLocked("owner@x.com", "203.0.113.9"));
    }

    [Fact]
    public void A_guess_spread_over_many_addresses_still_locks_the_account()
    {
        var throttle = new SignInThrottle(_time);
        for (var i = 0; i < AccountRules.MaxFailedSignInsPerAccount; i++) throttle.Failed("owner@x.com", $"10.0.{i / 250}.{i % 250}");
        Assert.True(throttle.IsLocked("owner@x.com", "198.51.100.7"));
    }

    [Fact]
    public void A_right_password_starts_the_pair_again()
    {
        var throttle = new SignInThrottle(_time);
        for (var i = 0; i < AccountRules.MaxFailedSignIns - 1; i++) throttle.Failed("a@x.com", "1.2.3.4");
        throttle.Succeeded("a@x.com", "1.2.3.4");
        throttle.Failed("a@x.com", "1.2.3.4");
        Assert.False(throttle.IsLocked("a@x.com", "1.2.3.4"));
    }

    [Fact]
    public void Old_failures_fall_out_of_the_window()
    {
        var throttle = new SignInThrottle(_time);
        for (var i = 0; i < AccountRules.MaxFailedSignIns - 1; i++) throttle.Failed("a@x.com", "1.2.3.4");
        _time.Advance(AccountRules.LockoutDuration + TimeSpan.FromMinutes(1));
        throttle.Failed("a@x.com", "1.2.3.4");
        Assert.False(throttle.IsLocked("a@x.com", "1.2.3.4"));
    }
}
