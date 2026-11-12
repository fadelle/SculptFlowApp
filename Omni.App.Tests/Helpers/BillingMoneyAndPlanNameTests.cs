using System.Globalization;
using Omni.App.Common.Helpers;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t2-billing-16: French read "$25,50", and plan names were English in every language.</summary>
public class BillingMoneyAndPlanNameTests
{
    [Theory]
    [InlineData("en", "USD", "$25.50")]
    [InlineData("fr", "USD", "25,50")]
    [InlineData("fr", "EUR", "EUR")]
    public void Money_follows_the_readers_language(string language, string currency, string expected)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = AppCulture.For(language);
            var text = BillingTextHelper.Money(25.5m, currency);
            Assert.Contains(expected, text);
            if (language == "fr") Assert.False(text.StartsWith('$'), text); // the symbol after the number in French
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void A_seeded_plan_has_its_name_in_the_readers_language_and_others_keep_theirs()
    {
        var fr = TextKit.Localizer("fr");
        Assert.Equal("Essai gratuit", fr.PlanName("trial", "Free Trial"));
        Assert.Equal("+ Réservations", fr.PlanName("inbox-bookings", "+ Bookings"));
        Assert.Equal("[QA] Pro", fr.PlanName("qa-pro", "[QA] Pro"));
    }
}
