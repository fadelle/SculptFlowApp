using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;
using Omni.App.Common.Extensions;

namespace Omni.App.Tests.Helpers;

public class AccountRateLimitingTests
{
    private const string Edge = "CF-Connecting-IP";
    private readonly Mock<IConfigManager> _config = new();
    private string? _header = Edge;
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["PlatformAdmin:ApiKey"] = "admin-secret" }).Build();

    public AccountRateLimitingTests()
    {
        _config.SetupGet(c => c.AccountsSignupsPerIpPerHour).Returns(2);
        _config.SetupGet(c => c.AccountsAttemptsPerIpPerHour).Returns(3);
    }

    /// <summary>A request as it reaches the app behind Render: the connection is a proxy hop, the edge header the visitor.</summary>
    private HttpContext Request(string method, string path, string ip = "203.0.113.7", string proxy = "10.0.0.1", string? adminKey = null)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_config.Object).AddSingleton(_configuration)
                .AddSingleton(Options.Create(new ProxyOptions { ClientIpHeader = _header })).BuildServiceProvider()
        };
        if (adminKey is not null) http.Request.Headers["X-Platform-Admin-Key"] = adminKey;
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.Headers[Edge] = ip;
        http.Connection.RemoteIpAddress = IPAddress.Parse(proxy);
        return http;
    }

    private static PartitionedRateLimiter<HttpContext> Limiter() => PartitionedRateLimiter.Create<HttpContext, string>(AccountRateLimiting.Partition);

    [Theory]
    [InlineData("POST", "/Account/Register", true)]
    [InlineData("post", "/account/register", true)]
    [InlineData("POST", "/Account/CompleteGoogleSignup", true)]
    [InlineData("GET", "/Account/Register", false)]
    [InlineData("POST", "/Account/Login", false)]
    [InlineData("POST", "/Account/RegisterSomethingElse", false)]
    [InlineData("POST", "/api/contacts", false)]
    public void Only_sign_up_posts_count_as_sign_ups(string method, string path, bool counts) =>
        Assert.Equal(counts, AccountRateLimiting.IsSignupAttempt(Request(method, path).Request));

    [Theory]
    [InlineData("POST", "/Account/Login", true)]
    [InlineData("POST", "/account/login", true)]
    [InlineData("POST", "/Account/ForgotPassword", true)]
    [InlineData("POST", "/Account/ResetPassword", true)]
    [InlineData("POST", "/Account/AcceptInvite", true)]
    [InlineData("GET", "/Account/Login", false)]
    [InlineData("POST", "/Account/Register", false)]
    [InlineData("POST", "/Account/Logout", false)]
    [InlineData("POST", "/api/auth/login", false)]
    public void Sign_in_reset_and_invite_posts_count_as_account_attempts(string method, string path, bool counts) =>
        Assert.Equal(counts, AccountRateLimiting.IsAccountAttempt(Request(method, path).Request));

    [Fact]
    public void One_ip_is_cut_off_after_the_hourly_sign_up_limit_while_others_and_other_pages_are_not()
    {
        using var limiter = Limiter();

        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/CompleteGoogleSignup")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);

        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", ip: "198.51.100.9")).IsAcquired);
        for (var i = 0; i < 10; i++)
        {
            Assert.True(limiter.AttemptAcquire(Request("GET", "/Account/Register")).IsAcquired);
            Assert.True(limiter.AttemptAcquire(Request("GET", "/Account/Login")).IsAcquired);
        }
    }

    [Fact]
    public void Sign_in_reset_and_invite_attempts_share_one_hourly_limit_per_ip_apart_from_sign_up()
    {
        // QA t1-accounts-08: nothing limited password guessing or locking an owner out from one address.
        using var limiter = Limiter();
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Login")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/ForgotPassword")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/AcceptInvite")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Login")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/ResetPassword")).IsAcquired);

        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Login", ip: "198.51.100.9")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired); // sign-up counts separately
    }

    [Fact]
    public void One_visitor_behind_changing_proxy_hops_is_one_counter()
    {
        // QA t11-auto-01: the limiter keyed on the last proxy hop, which changes per request, so it almost never triggered.
        using var limiter = Limiter();
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", proxy: "10.0.0.1")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", proxy: "10.0.0.2")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register", proxy: "10.0.0.3")).IsAcquired);
    }

    [Fact]
    public void Without_a_configured_header_or_with_a_bad_value_the_connection_address_is_used()
    {
        var ctx = Request("POST", "/Account/Login", ip: "not-an-ip", proxy: "192.0.2.5");
        Assert.Equal("192.0.2.5", ClientIp.Resolve(ctx, Edge));
        var list = Request("POST", "/Account/Login", ip: " 203.0.113.9 , 10.1.1.1", proxy: "192.0.2.5");
        Assert.Equal("203.0.113.9", ClientIp.Resolve(list, Edge));
        Assert.Equal("192.0.2.5", ClientIp.Resolve(list, null));

        _header = null;
        using var limiter = Limiter();
        // Not trusting the header: two visitors through one hop share it (the safe default off Render).
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", ip: "203.0.113.1", proxy: "192.0.2.5")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", ip: "203.0.113.2", proxy: "192.0.2.5")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register", ip: "203.0.113.3", proxy: "192.0.2.5")).IsAcquired);
    }

    [Fact]
    public void The_platform_admin_key_skips_the_limits_but_a_wrong_key_does_not()
    {
        // Omni.Automation registers and signs in dozens of test clients per run from one machine.
        using var limiter = Limiter();
        for (var i = 0; i < 10; i++)
        {
            Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", adminKey: "admin-secret")).IsAcquired);
            Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Login", adminKey: "admin-secret")).IsAcquired);
        }
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", adminKey: "guess")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", adminKey: "guess")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register", adminKey: "guess")).IsAcquired);
    }

    [Fact]
    public void Raising_the_setting_applies_straight_away()
    {
        using var limiter = Limiter();
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);

        _config.SetupGet(c => c.AccountsSignupsPerIpPerHour).Returns(3);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
    }

    [Fact]
    public async Task The_limiter_is_registered_and_explains_each_refusal()
    {
        var provider = new ServiceCollection().AddAccountRateLimiting().BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;
        Assert.NotNull(options.GlobalLimiter);
        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);

        foreach (var (path, expected) in new[] { ("/Account/Register", AccountRateLimiting.RejectedMessage), ("/Account/Login", AccountRateLimiting.AttemptsRejectedMessage) })
        {
            var http = Request("POST", path);
            http.Response.Body = new MemoryStream();
            using var lease = RateLimitLease();
            await options.OnRejected!(new Microsoft.AspNetCore.RateLimiting.OnRejectedContext { HttpContext = http, Lease = lease }, default);
            http.Response.Body.Position = 0;
            Assert.Equal(expected, await new StreamReader(http.Response.Body).ReadToEndAsync());
        }
    }

    private static RateLimitLease RateLimitLease()
    {
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        return limiter.AttemptAcquire(0);
    }
}
