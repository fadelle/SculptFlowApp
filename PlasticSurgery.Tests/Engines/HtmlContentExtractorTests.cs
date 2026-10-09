using PlasticSurgery.Business.Engines.WebScraping;

namespace PlasticSurgery.Tests.Engines;

public class HtmlContentExtractorTests
{
    private readonly HtmlContentExtractor _sut = new();
    private static readonly Uri Page = new("https://clinic.example.com/services/rhinoplasty");

    private ExtractedPage Extract(string html, Uri? url = null) => _sut.Extract(html, url ?? Page);

    private static string Long(string text) => string.Join(" ", Enumerable.Repeat(text, 30));

    [Fact]
    public void Title_comes_from_title_tag_then_first_h1_and_is_whitespace_cleaned()
    {
        Assert.Equal("Rhino Surgery", Extract("<html><head><title>  Rhino \n Surgery </title></head><body><p>x</p></body></html>").Title);
        Assert.Equal("Fallback heading", Extract("<html><body><h1> Fallback   heading </h1></body></html>").Title);
        Assert.Equal("", Extract("<html><body><p>x</p></body></html>").Title);
    }

    [Fact]
    public void Robots_meta_sets_noindex_and_nofollow_and_nofollow_stops_link_collection()
    {
        var page = Extract("""<html><head><meta name="robots" content="noindex, nofollow"></head><body><a href="/a">a</a></body></html>""");
        Assert.True(page.NoIndex);
        Assert.True(page.NoFollow);
        Assert.Empty(page.Links);

        var none = Extract("""<html><head><meta name="googlebot" content="none"></head><body></body></html>""");
        Assert.True(none.NoIndex && none.NoFollow);

        var other = Extract("""<html><head><meta name="description" content="noindex"></head><body></body></html>""");
        Assert.False(other.NoIndex);
    }

    [Fact]
    public void Links_are_made_absolute_and_nofollow_anchor_and_fragment_links_are_skipped()
    {
        var page = Extract("""
            <html><body>
              <a href="/about">About</a>
              <a href="contact">Contact</a>
              <a href="https://other.com/x">Other</a>
              <a href="#top">Top</a>
              <a href="/skip" rel="nofollow">Skip</a>
              <a href="  ">Blank</a>
            </body></html>
            """);
        Assert.Equal(["https://clinic.example.com/about", "https://clinic.example.com/services/contact", "https://other.com/x"], page.Links);
    }

    [Fact]
    public void Base_href_changes_how_relative_links_and_canonical_resolve()
    {
        var page = Extract("""
            <html><head><base href="https://cdn.example.com/dir/"><link rel="canonical" href="page.html"></head>
            <body><a href="next">n</a></body></html>
            """);
        Assert.Equal("https://cdn.example.com/dir/next", Assert.Single(page.Links));
        Assert.Equal("https://cdn.example.com/dir/page.html", page.CanonicalHref);
    }

    [Fact]
    public void Block_elements_become_separate_blocks_and_list_items_are_flagged()
    {
        var page = Extract("<html><body><h1>Title</h1><p>First   paragraph</p><ul><li>One</li><li>Two</li></ul><p>Last<br>line</p></body></html>");
        Assert.Equal(
            [("Title", false), ("First paragraph", false), ("One", true), ("Two", true), ("Last", false), ("line", false)],
            page.Blocks.Select(b => (b.Text, b.ListItem)));
    }

    [Fact]
    public void Tables_are_flattened_to_pipe_separated_rows()
    {
        var page = Extract("<html><body><table><tr><th>Procedure</th><th>Price</th></tr><tr><td>Rhino</td><td>$5,000</td></tr></table></body></html>");
        Assert.Equal(["Procedure | Price", "Rhino | $5,000"], page.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Scripts_styles_hidden_and_navigation_content_is_dropped()
    {
        var page = Extract("""
            <html><head><style>.a{}</style></head><body>
              <nav>Home About Contact</nav>
              <script>var secret = 1;</script>
              <div hidden>Hidden text</div>
              <div aria-hidden="true">Aria hidden</div>
              <div style="display:none">Display none</div>
              <div style="visibility: hidden">Visibility hidden</div>
              <div class="cookie-banner">Accept cookies</div>
              <div role="dialog">A dialog</div>
              <p>Visible paragraph</p>
              <noscript>No script</noscript>
            </body></html>
            """);
        Assert.Equal(["Visible paragraph"], page.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Page_level_header_is_dropped_but_a_header_inside_an_article_is_kept()
    {
        var page = Extract("""
            <html><body>
              <header>Site menu</header>
              <article><header><h2>Article heading</h2></header><p>Body copy</p></article>
            </body></html>
            """);
        Assert.Contains("Article heading", page.Blocks.Select(b => b.Text));
        Assert.DoesNotContain("Site menu", page.Blocks.Select(b => b.Text));
    }

    [Fact]
    public void Images_contribute_their_alt_text_only_when_descriptive()
    {
        var page = Extract("""<html><body><p>Intro</p><img alt="Before and after photo"><img alt="logo"></body></html>""");
        Assert.Contains(page.Blocks, b => b.Text.Contains("Before and after photo"));
        Assert.DoesNotContain(page.Blocks, b => b.Text.Contains("logo"));
    }

    [Fact]
    public void The_main_region_is_preferred_and_footer_contact_lines_are_recovered()
    {
        var page = Extract($"""
            <html><body>
              <div>Outside main text</div>
              <main><h1>Procedure</h1><p>{Long("Real content about the procedure.")}</p></main>
              <footer>
                <p>© 2026 Clinic</p>
                <p>Call us: +961 70 123 456</p>
                <p>info@clinic.example.com</p>
                <p>Mon-Fri 9-5</p>
              </footer>
            </body></html>
            """);
        var texts = page.Blocks.Select(b => b.Text).ToList();
        Assert.DoesNotContain("Outside main text", texts);
        Assert.Contains("Procedure", texts);
        Assert.Contains("Call us: +961 70 123 456", texts);
        Assert.Contains("info@clinic.example.com", texts);
        Assert.Contains("Mon-Fri 9-5", texts);
        Assert.DoesNotContain("© 2026 Clinic", texts);
    }

    [Fact]
    public void A_blog_index_with_many_articles_falls_back_to_the_whole_body()
    {
        var page = Extract($"<html><body><article><p>{Long("First post")}</p></article><article><p>{Long("Second post")}</p></article><p>Sidebar-less outro</p></body></html>");
        Assert.Contains(page.Blocks, b => b.Text.Contains("Second post"));
        Assert.Contains(page.Blocks, b => b.Text == "Sidebar-less outro");
    }

    [Fact]
    public void A_noisy_class_on_a_big_wrapper_does_not_remove_the_whole_page()
    {
        var page = Extract($"<html><body class='has-sidebar'><div class='sidebar-layout'><h1>Main title</h1><p>{Long("Core content")}</p></div></body></html>");
        Assert.Contains(page.Blocks, b => b.Text == "Main title");
        // a small noisy block with no heading is still removed
        var withWidget = Extract($"<html><body><div class='newsletter-signup'><p>Subscribe now</p></div><p>{Long("Core content")}</p></body></html>");
        Assert.DoesNotContain(withWidget.Blocks, b => b.Text.Contains("Subscribe now"));
    }

    [Fact]
    public void Empty_document_has_no_blocks_and_clean_collapses_whitespace()
    {
        Assert.Empty(Extract("").Blocks);
        Assert.Equal("a b c", HtmlContentExtractor.Clean("  a \n b\t c "));
        Assert.Equal("", HtmlContentExtractor.Clean(null));
    }
}
