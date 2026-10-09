using System.Net;
using Microsoft.Extensions.Hosting;
using PlasticSurgery.Business.Engines.WebScraping;

namespace PlasticSurgery.Tests.Engines;

public class RobotsTxtTests
{
    private const string Token = "sculptflowbot/1.0";

    [Fact]
    public void Empty_file_and_allow_all_permit_everything()
    {
        Assert.True(RobotsTxt.Parse("", Token).IsAllowed("/anything"));
        Assert.True(RobotsTxt.AllowAll.IsAllowed(""));
        Assert.Null(RobotsTxt.AllowAll.CrawlDelaySeconds);
    }

    [Fact]
    public void Wildcard_group_disallow_and_allow_longest_match_wins()
    {
        var r = RobotsTxt.Parse("User-agent: *\nDisallow: /private\nAllow: /private/public # comment\nCrawl-delay: 2.5", Token);
        Assert.False(r.IsAllowed("/private/x"));
        Assert.True(r.IsAllowed("/private/public/page"));
        Assert.True(r.IsAllowed("/open"));
        Assert.Equal(2.5, r.CrawlDelaySeconds);
    }

    [Fact]
    public void Tie_goes_to_allow_and_empty_disallow_allows_all()
    {
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow: /a\nAllow: /a", Token).IsAllowed("/a"));
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow:", Token).IsAllowed("/a"));
    }

    [Fact]
    public void Wildcards_and_end_anchors()
    {
        var r = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$\nDisallow: /tmp*/x", Token);
        Assert.False(r.IsAllowed("/files/a.pdf"));
        Assert.True(r.IsAllowed("/files/a.pdf?x=1"));
        Assert.False(r.IsAllowed("/tmp123/x"));
    }

    [Fact]
    public void Specific_group_beats_wildcard_and_other_agents_are_ignored()
    {
        var text = "User-agent: *\nDisallow: /\n\nUser-agent: otherbot\nDisallow: /other\n\nUser-agent: sculptflowbot\nDisallow: /secret\nCrawl-delay: 1";
        var r = RobotsTxt.Parse(text, Token);
        Assert.True(r.IsAllowed("/page"));
        Assert.False(r.IsAllowed("/secret"));
        Assert.Equal(1, r.CrawlDelaySeconds);
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_rules_and_bad_lines_are_skipped()
    {
        var r = RobotsTxt.Parse("User-agent: foo\nUser-agent: *\ngarbage line\nCrawl-delay: abc\nDisallow: /x", Token);
        Assert.False(r.IsAllowed("/x"));
        Assert.Null(r.CrawlDelaySeconds);
        Assert.False(RobotsTxt.Parse("Disallow: /x\nUser-agent: *\nDisallow: /y", Token).IsAllowed("/y"));
        Assert.True(RobotsTxt.Parse("Disallow: /x", Token).IsAllowed("/x"));
    }

    [Fact]
    public void Path_is_empty_treated_as_root_and_CRLF_is_handled()
    {
        var r = RobotsTxt.Parse("User-agent: *\r\nDisallow: /\r\n", Token);
        Assert.False(r.IsAllowed(""));
    }
}

public class SsrfGuardTests
{
    private static SsrfGuard Make(bool dev = false, string allowed = "")
    {
        var cfg = new Mock<IConfigManager>();
        cfg.SetupGet(c => c.WebScrapingDevAllowedHosts).Returns(allowed);
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(dev ? "Development" : "Production");
        return new SsrfGuard(cfg.Object, env.Object);
    }

    [Theory]
    [InlineData("0.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.5")]
    [InlineData("192.0.2.5")]
    [InlineData("192.88.99.1")]
    [InlineData("192.168.1.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("2002:7f00:0001::1")]
    [InlineData("64:ff9b::a00:1")]
    public void Blocks_private_and_special_addresses(string ip) =>
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)), ip);

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.0.1")]
    [InlineData("198.20.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2002:0808:0808::1")]
    [InlineData("64:ff9b::808:808")]
    public void Allows_public_addresses(string ip) =>
        Assert.False(SsrfGuard.IsBlockedAddress(IPAddress.Parse(ip)), ip);

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pw@example.com/")]
    [InlineData("https://example.com:8080/")]
    [InlineData("http://localhost/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://router.lan/")]
    [InlineData("http://intranet/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task Rejects_unsafe_urls(string url) =>
        await Assert.ThrowsAsync<UnsafeUrlException>(() => Make().ValidateUrlAsync(new Uri(url)));

    [Fact]
    public async Task Accepts_a_public_ip_literal() => await Make().ValidateUrlAsync(new Uri("https://8.8.8.8/"));

    [Fact]
    public async Task Dev_allow_list_only_applies_in_development_and_for_listed_host_and_port()
    {
        await Make(dev: true, allowed: "localhost:5099, other:1").ValidateUrlAsync(new Uri("http://localhost:5099/x"));
        await Assert.ThrowsAsync<UnsafeUrlException>(() => Make(dev: false, allowed: "localhost:5099").ValidateUrlAsync(new Uri("http://localhost:5099/")));
        await Assert.ThrowsAsync<UnsafeUrlException>(() => Make(dev: true, allowed: "localhost:5099").ValidateUrlAsync(new Uri("http://localhost:5100/")));
    }

    [Fact]
    public async Task Unresolvable_host_is_reported()
    {
        var ex = await Assert.ThrowsAsync<UnsafeUrlException>(() => Make().ValidateUrlAsync(new Uri("https://no-such-host.invalid/")));
        Assert.Contains("could not be found", ex.Message);
    }
}
