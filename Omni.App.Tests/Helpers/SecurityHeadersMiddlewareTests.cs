using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Omni.App.Middleware;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t1-accounts-03: dashboard pages and APIs could be framed, with no nosniff, referrer policy or CSP.</summary>
public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpContext> RunAsync(string path, Action<HttpContext>? endpoint = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            // What antiforgery does on a page with a form: add SAMEORIGIN only when nothing set the header.
            if (!ctx.Response.Headers.ContainsKey("X-Frame-Options")) ctx.Response.Headers.XFrameOptions = "SAMEORIGIN";
            endpoint?.Invoke(ctx);
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/Members")]
    [InlineData("/api/members")]
    [InlineData("/js/auth.js")]
    public async Task Every_response_refuses_framing_and_sniffing_and_has_a_policy(string path)
    {
        var headers = (await RunAsync(path)).Response.Headers;
        Assert.Equal("DENY", headers.XFrameOptions.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Contains("camera=()", headers["Permissions-Policy"].ToString());
        var csp = headers.ContentSecurityPolicy.ToString();
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public async Task Api_answers_are_not_stored_unless_the_endpoint_says_otherwise()
    {
        Assert.Equal("no-store", (await RunAsync("/api/members")).Response.Headers.CacheControl.ToString());
        Assert.Equal("", (await RunAsync("/dashboard")).Response.Headers.CacheControl.ToString());
        var own = await RunAsync("/api/public/thing", ctx => ctx.Response.Headers.CacheControl = "public, max-age=60");
        Assert.Equal("public, max-age=60", own.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void Every_external_script_the_app_loads_is_allowed_by_the_policy()
    {
        var root = Path.Combine(Omni.App.Tests.Guards.LeftoverNameScanner.FindRepoRoot(), "Omni.App");
        var sources = Directory.EnumerateFiles(Path.Combine(root, "Pages"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "wwwroot", "js"), "*.js"))
            .Select(File.ReadAllText);
        var scriptHosts = sources
            .SelectMany(s => Regex.Matches(s, @"(?:src\s*=\s*['""]|signalRUrl\s*=\s*""|\.src\s*=\s*')(https://[^/'""]+)").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(scriptHosts);
        var scriptSrc = Regex.Match(SecurityHeadersMiddleware.ContentSecurityPolicy, "script-src ([^;]+)").Groups[1].Value;
        Assert.All(scriptHosts, host => Assert.Contains(host, scriptSrc));
    }
}
