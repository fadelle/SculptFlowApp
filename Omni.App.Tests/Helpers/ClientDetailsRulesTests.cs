namespace Omni.App.Tests.Helpers;

/// <summary>QA t1-accounts-12: business info accepted any phone, email, website (javascript: included) or a 5,000-character address.</summary>
public class ClientDetailsRulesTests
{
    [Fact]
    public void Good_details_are_cleaned_and_a_website_without_a_scheme_gets_https()
    {
        var d = ClientDetailsRules.Clean(" +961 1 555-123 ", " info@glow.lb ", "glow.lb/about", " Hamra st ", "  ", null);
        Assert.Equal(("+961 1 555-123", "info@glow.lb", "https://glow.lb/about", "Hamra st", (string?)null, (string?)null),
            (d.Phone, d.Email, d.Website, d.Address, d.OperatingHours, d.BookingInfo));
        Assert.Equal("http://glow.lb", ClientDetailsRules.Clean(null, null, "http://glow.lb", null, null, null).Website);
    }

    [Theory]
    [InlineData("not a phone <script>", null, null, "clients.errors.invalidPhone")]
    [InlineData("12345", null, null, "clients.errors.invalidPhone")]
    [InlineData(null, "not-an-email", null, "clients.errors.invalidEmail")]
    [InlineData(null, null, "javascript:alert(1)", "clients.errors.invalidWebsite")]
    [InlineData(null, null, "ftp://glow.lb", "clients.errors.invalidWebsite")]
    [InlineData(null, null, "localhost", "clients.errors.invalidWebsite")]
    [InlineData(null, null, "glow lb", "clients.errors.invalidWebsite")]
    public void Bad_details_are_refused_with_their_reason(string? phone, string? email, string? website, string key) =>
        Assert.Equal(key, Assert.Throws<ArgumentException>(() => ClientDetailsRules.Clean(phone, email, website, null, null, null)).Message);

    [Theory]
    [InlineData(" lb ", "LB")]
    [InlineData("ae", "AE")]
    [InlineData("FR", "FR")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void A_country_is_a_known_two_letter_code_upper_cased(string? input, string? expected) =>
        Assert.Equal(expected, ClientDetailsRules.Country(input));

    [Theory]
    [InlineData("LBN")]
    [InlineData("XX")]
    [InlineData("L1")]
    [InlineData("en-US")]
    [InlineData("Lebanon")]
    public void An_unknown_or_malformed_country_is_refused(string input) =>
        Assert.Equal("clients.errors.invalidCountry", Assert.Throws<ArgumentException>(() => ClientDetailsRules.Country(input)).Message);

    [Fact]
    public void A_name_is_required_trimmed_and_at_most_200_characters()
    {
        Assert.Equal("Glow", ClientDetailsRules.Name("  Glow "));
        Assert.Equal(200, ClientDetailsRules.Name(new string('n', 200)).Length);
        Assert.Equal("clients.errors.nameRequired", Assert.Throws<ArgumentException>(() => ClientDetailsRules.Name("  ")).Message);
        var tooLong = Assert.Throws<ArgumentException>(() => ClientDetailsRules.Name(new string('n', 201)));
        Assert.Equal(("clients.errors.nameTooLong", (object?)200), (tooLong.Message, Assert.Single(TextError.ArgsOf(tooLong))));
    }

    [Fact]
    public void Long_texts_are_refused_with_their_limit()
    {
        var ex = Assert.Throws<ArgumentException>(() => ClientDetailsRules.Clean(null, null, null, new string('a', 5000), null, null));
        Assert.Equal(("clients.errors.addressTooLong", (object?)500), (ex.Message, Assert.Single(TextError.ArgsOf(ex))));
        Assert.Equal("clients.errors.hoursTooLong", Assert.Throws<ArgumentException>(() => ClientDetailsRules.Clean(null, null, null, null, new string('h', 2001), null)).Message);
        Assert.Equal("clients.errors.bookingInfoTooLong", Assert.Throws<ArgumentException>(() => ClientDetailsRules.Clean(null, null, null, null, null, new string('b', 2001))).Message);
    }
}
