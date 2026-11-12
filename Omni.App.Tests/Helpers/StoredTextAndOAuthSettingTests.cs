using Microsoft.Extensions.Configuration;
using Omni.App.Common.Helpers;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t5-ops-13 / -14: blank OAuth settings count as missing, and integration problems are stored as keys.</summary>
public class StoredTextAndOAuthSettingTests
{
    [Fact]
    public void A_stored_key_is_read_in_the_readers_language_and_other_text_as_written()
    {
        var text = TextKit.Localizer();
        Assert.Equal(text["oauth.errors.expired"].Value, StoredText.Show(StoredText.Key("oauth.errors.expired"), text));
        Assert.Equal(text["oauth.errors.noCode", "Google Calendar"].Value, StoredText.Show(StoredText.Key("oauth.errors.noCode", "Google Calendar"), text));
        Assert.Equal("Token refresh failed (invalid_grant)", StoredText.Show("Token refresh failed (invalid_grant)", text));
        Assert.Equal("", StoredText.Show(null, text));
    }

    [Fact]
    public void Arguments_cant_break_the_stored_form()
    {
        var stored = StoredText.Key("oauth.errors.connectFailed", "a|b]]c");
        Assert.StartsWith("[[t:oauth.errors.connectFailed|", stored);
        Assert.EndsWith("]]", stored);
        Assert.Contains("a/b] ]c", StoredText.Show(stored, TextKit.Localizer()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_oauth_setting_is_missing(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MicrosoftCalendar:ClientId"] = value, ["MicrosoftCalendar:ClientSecret"] = "s"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => { _ = OAuthSetting.Required(configuration, "MicrosoftCalendar:ClientId"); });
        Assert.False(OAuthSetting.Has(configuration, "MicrosoftCalendar:ClientId", "MicrosoftCalendar:ClientSecret"));
        Assert.False(new Omni.App.Business.HttpClients.Calendars.OutlookCalendarProviderClient(new HttpClient(), configuration).IsConfigured);
        // The connect redirect is never built with client_id= (QA t5-ops-13): the controller shows "not set up" instead.
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new Omni.App.Business.HttpClients.Calendars.OutlookCalendarProviderClient(new HttpClient(), configuration).BuildAuthorizationUrl("https://x/cb", "s");
        });
    }

    [Fact]
    public void A_set_oauth_setting_is_trimmed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleCalendar:ClientId"] = " id ", ["GoogleCalendar:ClientSecret"] = "secret"
        }).Build();
        Assert.Equal("id", OAuthSetting.Required(configuration, "GoogleCalendar:ClientId"));
        Assert.True(new Omni.App.Business.HttpClients.Calendars.GoogleCalendarProviderClient(new HttpClient(), configuration).IsConfigured);
    }

    [Fact]
    public void A_failure_is_stored_as_its_key_and_read_in_each_language()
    {
        // QA t5-ops-12: campaign failures were stored as English sentences and shown so in every language.
        var en = TextKit.Localizer();
        var keyed = new InvalidOperationException("campaigns.errors.nameTooLong").WithArgs(200);
        var stored = StoredText.Of(keyed, en);
        Assert.Equal(StoredText.Key("campaigns.errors.nameTooLong", "200"), stored);
        Assert.Equal(TextKit.Localizer("fr")["campaigns.errors.nameTooLong", "200"].Value, StoredText.Show(stored, TextKit.Localizer("fr")));

        // A provider's own words stay as written; a known Meta failure becomes its key.
        Assert.Equal("Meta exploded", StoredText.Of(new InvalidOperationException("Meta exploded"), en));
        var token = new Omni.App.Common.Exceptions.WhatsAppSendException("raw", 401, 190, "Error validating access token");
        Assert.Equal(StoredText.Key(ProviderErrorText.MetaTokenInvalidKey), StoredText.Of(token, en));
        Assert.True(StoredText.Of(new InvalidOperationException(new string('x', 2000)), en, maxLength: 1000).Length <= 1000);
    }
}
