using System.Net;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Tests.Indexers;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Auth;

public sealed class SignInThrottleTests
{
    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.7");

    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Under_the_limit_the_attempt_goes_on()
    {
        var throttle = Throttle(perUsername: 3, perAddress: 100);

        throttle.Failed("admin", Address);
        throttle.Failed("admin", Address);

        Assert.Null(throttle.RetryAfter("admin", Address));
    }

    [Fact]
    public void At_the_limit_the_wait_shrinks_with_the_clock_and_ends_after_the_window()
    {
        var throttle = Throttle(perUsername: 3, perAddress: 100, windowMinutes: 15);

        throttle.Failed("admin", Address);
        _clock.Advance(TimeSpan.FromMinutes(1));
        throttle.Failed("admin", Address);
        throttle.Failed("admin", Address);

        // The oldest failure is 1 minute old: it leaves the window in 14 minutes.
        Assert.Equal(TimeSpan.FromMinutes(14), throttle.RetryAfter("admin", Address));

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(4), throttle.RetryAfter("admin", Address));

        // Once the oldest has left, two failures are left and the third attempt may go on.
        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Null(throttle.RetryAfter("admin", Address));
    }

    [Fact]
    public void A_success_clears_the_username_but_not_the_address()
    {
        var throttle = Throttle(perUsername: 2, perAddress: 3);

        throttle.Failed("admin", Address);
        throttle.Failed("admin", Address);
        Assert.NotNull(throttle.RetryAfter("admin", null));

        throttle.Succeeded("admin");
        Assert.Null(throttle.RetryAfter("admin", null));

        // The address still holds its two failures: one more from there, under another name, and it waits.
        Assert.Null(throttle.RetryAfter("someone", Address));
        throttle.Failed("someone", Address);
        Assert.NotNull(throttle.RetryAfter("another", Address));
    }

    [Fact]
    public void The_username_is_not_case_sensitive_and_ignores_surrounding_spaces()
    {
        var throttle = Throttle(perUsername: 2, perAddress: 0);

        throttle.Failed("Admin", null);
        throttle.Failed(" ADMIN ", null);

        Assert.NotNull(throttle.RetryAfter("admin", null));
        Assert.Null(throttle.RetryAfter("other", null));
    }

    [Fact]
    public void An_address_over_the_limit_waits_whatever_the_username()
    {
        var throttle = Throttle(perUsername: 100, perAddress: 2);

        throttle.Failed("one", Address);
        throttle.Failed("two", Address);

        Assert.NotNull(throttle.RetryAfter("three", Address));
        Assert.Null(throttle.RetryAfter("three", IPAddress.Parse("203.0.113.8")));
        // The same address written as an IPv4-mapped IPv6 one is the same client.
        Assert.NotNull(throttle.RetryAfter("three", Address.MapToIPv6()));
    }

    [Fact]
    public void A_limit_of_zero_never_blocks_and_each_key_is_switched_off_on_its_own()
    {
        var off = Throttle(perUsername: 0, perAddress: 0);
        for (var i = 0; i < 50; i++)
        {
            off.Failed("admin", Address);
        }

        Assert.Null(off.RetryAfter("admin", Address));
        Assert.Equal(0, off.TrackedKeys);

        var usernameOff = Throttle(perUsername: 0, perAddress: 2);
        usernameOff.Failed("admin", Address);
        usernameOff.Failed("admin", Address);
        Assert.NotNull(usernameOff.RetryAfter("admin", Address));
        Assert.Null(usernameOff.RetryAfter("admin", null));

        var addressOff = Throttle(perUsername: 2, perAddress: 0);
        addressOff.Failed("admin", Address);
        addressOff.Failed("admin", Address);
        Assert.NotNull(addressOff.RetryAfter("admin", Address));
        Assert.Null(addressOff.RetryAfter("other", Address));
    }

    [Fact]
    public void A_missing_address_is_not_counted()
    {
        var throttle = Throttle(perUsername: 0, perAddress: 1);

        throttle.Failed("admin", null);

        Assert.Null(throttle.RetryAfter("admin", null));
        Assert.Equal(0, throttle.TrackedKeys);
    }

    [Fact]
    public void Starts_are_counted_per_address_and_a_refused_start_is_not_counted()
    {
        var throttle = Throttle(perUsername: 10, perAddress: 2);

        Assert.Null(throttle.TryStart(Address));
        Assert.Null(throttle.TryStart(Address));
        Assert.Equal(TimeSpan.FromMinutes(15), throttle.TryStart(Address));
        Assert.Null(throttle.TryStart(IPAddress.Parse("203.0.113.8")));

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(throttle.TryStart(Address));
    }

    [Fact]
    public void Ten_thousand_and_one_distinct_usernames_do_not_grow_the_memory_without_bound()
    {
        var throttle = Throttle(perUsername: 3, perAddress: 0);

        for (var i = 0; i <= SignInThrottle.MaxKeys; i++)
        {
            throttle.Failed("user" + i, null);
        }

        Assert.Equal(SignInThrottle.MaxKeys, throttle.TrackedKeys);

        // Once the window is over the expired keys are swept to make room for a new one.
        _clock.Advance(TimeSpan.FromMinutes(16));
        throttle.Failed("late", null);
        Assert.Equal(1, throttle.TrackedKeys);
    }

    private SignInThrottle Throttle(int perUsername, int perAddress, int windowMinutes = 15) =>
        new(_clock, Options.Create(new SignInOptions
        {
            MaxFailuresPerUsername = perUsername,
            MaxFailuresPerAddress = perAddress,
            WindowMinutes = windowMinutes,
        }));
}
