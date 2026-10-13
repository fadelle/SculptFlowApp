using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PlasticSurgery.Common.Extensions;

namespace PlasticSurgery.Tests.Helpers;

public class SignupRateLimitingTests
{
    private readonly Mock<IConfigManager> _config = new();

    public SignupRateLimitingTests() => _config.SetupGet(c => c.AccountsSignupsPerIpPerHour).Returns(2);

    private HttpContext Request(string method, string path, string ip = "203.0.113.7")
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(_config.Object).BuildServiceProvider()
        };
        http.Request.Method = method;
        http.Request.Path = path;
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return http;
    }

    [Theory]
    [InlineData("POST", "/Account/Register", true)]
    [InlineData("post", "/account/register", true)]
    [InlineData("POST", "/Account/CompleteGoogleSignup", true)]
    [InlineData("GET", "/Account/Register", false)]
    [InlineData("POST", "/Account/Login", false)]
    [InlineData("POST", "/Account/RegisterSomethingElse", false)]
    [InlineData("POST", "/api/leads", false)]
    public void Only_sign_up_posts_count(string method, string path, bool counts) =>
        Assert.Equal(counts, SignupRateLimiting.IsSignupAttempt(Request(method, path).Request));

    [Fact]
    public void One_ip_is_cut_off_after_the_hourly_limit_while_others_and_other_pages_are_not()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(SignupRateLimiting.Partition);

        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/CompleteGoogleSignup")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);

        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register", ip: "198.51.100.9")).IsAcquired);
        for (var i = 0; i < 10; i++)
        {
            Assert.True(limiter.AttemptAcquire(Request("GET", "/Account/Register")).IsAcquired);
            Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Login")).IsAcquired);
        }
    }

    [Fact]
    public void Raising_the_setting_applies_straight_away()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(SignupRateLimiting.Partition);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);

        _config.SetupGet(c => c.AccountsSignupsPerIpPerHour).Returns(3);
        Assert.True(limiter.AttemptAcquire(Request("POST", "/Account/Register")).IsAcquired);
    }

    [Fact]
    public void The_limiter_is_registered()
    {
        var provider = new ServiceCollection().AddSignupRateLimiting().BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;
        Assert.NotNull(options.GlobalLimiter);
        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
        Assert.NotNull(options.OnRejected);
    }
}
