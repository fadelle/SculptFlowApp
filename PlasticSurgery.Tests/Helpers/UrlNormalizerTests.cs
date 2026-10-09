namespace PlasticSurgery.Tests.Helpers;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("HTTPS://Example.COM/Path/", "https://example.com/Path")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a//b///c", "https://example.com/a/b/c")]
    [InlineData("https://example.com/services/index.html", "https://example.com/services")]
    [InlineData("https://example.com/index.php", "https://example.com/")]
    [InlineData("https://example.com/a#frag", "https://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("https://example.com/a%2fb", "https://example.com/a%2Fb")]
    [InlineData("https://example.com/?b=2&a=1&utm_source=x&fbclid=y", "https://example.com/?a=1&b=2")]
    [InlineData("https://example.com/?", "https://example.com/")]
    [InlineData("  https://example.com/x  ", "https://example.com/x")]
    public void Normalizes(string raw, string expected) => Assert.Equal(expected, UrlNormalizer.Normalize(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/only")]
    [InlineData("ftp://example.com/")]
    [InlineData("mailto:a@b.com")]
    [InlineData("https://user:pw@example.com/")]
    public void Rejects(string? raw) => Assert.Null(UrlNormalizer.Normalize(raw));

    [Fact]
    public void Rejects_overlong_urls()
    {
        Assert.Null(UrlNormalizer.Normalize("https://example.com/" + new string('a', UrlNormalizer.MaxUrlLength)));
    }

    [Fact]
    public void Resolves_relative_links_against_the_base()
    {
        var b = new Uri("https://example.com/dir/page");
        Assert.Equal("https://example.com/dir/other", UrlNormalizer.Normalize("other", b));
        Assert.Equal("https://example.com/root", UrlNormalizer.Normalize("/root", b));
        Assert.Equal("https://other.com/x", UrlNormalizer.Normalize("https://other.com/x", b));
        Assert.Null(UrlNormalizer.Normalize("javascript:alert(1)", b));
    }

    [Fact]
    public void Ipv6_hosts_keep_brackets() =>
        Assert.Equal("http://[2001:db8::1]/a", UrlNormalizer.Normalize("http://[2001:DB8::1]/a"));

    [Fact]
    public void Same_site_ignores_www_and_compares_ports()
    {
        Assert.True(UrlNormalizer.IsSameSite("www.Example.com", "example.com"));
        Assert.False(UrlNormalizer.IsSameSite("a.example.com", "example.com"));
        Assert.True(UrlNormalizer.IsSameSite(new Uri("https://www.example.com/"), new Uri("https://example.com/x")));
        Assert.False(UrlNormalizer.IsSameSite(new Uri("https://example.com:8443/"), new Uri("https://example.com/")));
        Assert.Equal("example.com", UrlNormalizer.BareHost("WWW.Example.com"));
    }

    [Fact]
    public void ToOrigin_rewrites_same_site_urls_only()
    {
        var origin = new Uri("https://www.example.com");
        Assert.Equal("https://www.example.com/a?x=1", UrlNormalizer.ToOrigin("https://example.com/a?x=1", origin));
        Assert.Equal("https://other.com/a", UrlNormalizer.ToOrigin("https://other.com/a", origin));
        Assert.Equal("garbage", UrlNormalizer.ToOrigin("garbage", origin));
        Assert.Equal("https://www.example.com:8443/a", UrlNormalizer.ToOrigin("https://example.com:8443/a", new Uri("https://www.example.com:8443")));
    }

    [Theory]
    [InlineData("https://e.com/logo.PNG", true, false)]
    [InlineData("https://e.com/app.js?v=1", true, false)]
    [InlineData("https://e.com/a.pdf", false, true)]
    [InlineData("https://e.com/a.docx", false, true)]
    [InlineData("https://e.com/page", false, false)]
    [InlineData("not a url", false, false)]
    public void Classifies_assets_and_documents(string url, bool asset, bool doc)
    {
        Assert.Equal(asset, UrlNormalizer.IsAssetUrl(url));
        Assert.Equal(doc, UrlNormalizer.IsDocumentUrl(url));
    }

    [Theory]
    [InlineData("https://e.com/blog/category/news", true)]
    [InlineData("https://e.com/tag/x", true)]
    [InlineData("https://e.com/author/bob", true)]
    [InlineData("https://e.com/blog/page/2", true)]
    [InlineData("https://e.com/page/about", false)]
    [InlineData("https://e.com/page", false)]
    [InlineData("https://e.com/services/rhinoplasty", false)]
    [InlineData("nope", false)]
    public void Detects_listing_pages(string url, bool expected) => Assert.Equal(expected, UrlNormalizer.IsListingUrl(url));
}
