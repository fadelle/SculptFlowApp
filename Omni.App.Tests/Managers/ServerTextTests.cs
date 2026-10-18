using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Managers;
using Omni.App.Business.Services.Billing;

namespace Omni.App.Tests.Managers;

/// <summary>Server messages in the reader's language (Milestone A, S8c): keyed exceptions with TextArg arguments,
/// notification text, billing labels, Identity's errors.</summary>
public class ServerTextTests
{
    private static readonly IReadOnlyDictionary<string, string> ClinicWords = TermPlaceholders.Build(new Dictionary<string, TermForms>(TextKit.DefaultTerms())
    {
        [TermName.Contact] = new("patient", "patients"), [TermName.Booking] = new("consultation", "consultations") // leftover-ok: clinic words
    });

    [Fact]
    public void A_text_argument_is_put_into_the_same_language_and_words()
    {
        var text = TextKit.Localizer(terms: ClinicWords);

        Assert.Equal("This consultation can't be rescheduled because it is Canceled.", // leftover-ok: clinic words
            text.Localize(new ArgumentException("bookings.errors.cantReschedule").WithArgs(TextArg.Value("canceled"))));
        Assert.Equal("Monday: enter a start and end time.",
            text.Localize(new ArgumentException("availability.errors.dayTimesRequired").WithArgs(TextArg.Of("days.monday"))));
        Assert.Equal("Unsupported file type (no extension). Upload a PDF, DOCX or TXT file.",
            text.Localize(new ArgumentException("knowledge.documents.errors.unsupportedType").WithArgs(TextArg.Of("knowledge.documents.errors.noExtension"))));
    }

    [Fact]
    public void A_text_argument_survives_the_trip_through_the_database()
    {
        Assert.Equal("[[key:notifications.anyone.contact]]", TextArg.Encode(TextArg.Of("notifications.anyone.contact")));
        Assert.Equal("[[value:no_show]]", TextArg.Encode(TextArg.Value("no_show")));
        Assert.Equal(("Ann", "3", ""), (TextArg.Encode("Ann"), TextArg.Encode(3), TextArg.Encode(null)));

        Assert.Equal(TextArg.Of("notifications.anyone.contact"), TextArg.Decode("[[key:notifications.anyone.contact]]"));
        Assert.Equal(TextArg.Value("no_show"), TextArg.Decode("[[value:no_show]]"));
        Assert.Equal("[[time:2026-10-18T10:00:00Z]]", TextArg.Decode("[[time:2026-10-18T10:00:00Z]]")); // the bell's own marker stays
        Assert.Equal("Ann", TextArg.Decode("Ann"));
    }

    [Fact]
    public void Notification_text_uses_the_clients_words_and_falls_back_for_missing_names()
    {
        var text = NotificationText.Of("notifications.newContact.title", "notifications.newContact.message", TextArg.Of("notifications.anyone.newContact"));
        Assert.Equal(("New contact", "A new contact sent their first message."), TextKit.English(text));

        var localizer = TextKit.Localizer(terms: ClinicWords);
        Assert.Equal("A new patient sent their first message.", // leftover-ok: clinic words
            localizer[text.MessageKey!, text.Args.Select(TextArg.Encode).Select(TextArg.Decode).ToArray()].Value);
    }

    [Theory]
    [InlineData(ProviderBillingResponsibility.PlatformFunded, ChannelType.WhatsApp, true, "Billed by Omni")]
    [InlineData(ProviderBillingResponsibility.PlatformFunded, ChannelType.WhatsApp, false, "Covered by Omni")]
    [InlineData(ProviderBillingResponsibility.CustomerDirect, ChannelType.WhatsApp, false, "Paid by you directly to Meta")]
    [InlineData(ProviderBillingResponsibility.CustomerDirect, ChannelType.Telegram, true, "Paid by you directly to the provider · plus an Omni usage fee")]
    [InlineData(ProviderBillingResponsibility.ExternalProviderDirect, ChannelType.WhatsApp, false, "Paid by you to your messaging provider")]
    [InlineData(ProviderBillingResponsibility.NoProviderUsageFee, ChannelType.Telegram, false, "No per-message charge")]
    [InlineData("someone_else", ChannelType.Telegram, false, "Someone else")]
    public void Who_pays_matches_the_admin_wording_in_english(string providerBilling, string channel, bool charged, string expected)
    {
        Assert.Equal(expected, TextKit.Localizer().PaidByLabel(providerBilling, channel, charged));
        if (providerBilling != "someone_else")
            Assert.Equal(BillingQueryService.ClientPaidByLabel(providerBilling, channel, charged), TextKit.Localizer().PaidByLabel(providerBilling, channel, charged));
    }

    [Fact]
    public void Billing_labels_come_from_the_strings_files_with_readable_fallbacks()
    {
        var text = TextKit.Localizer();
        Assert.Equal("AI assistant", text.EntitlementLabel(EntitlementKeys.AiAssistant, "fallback"));
        Assert.Equal("fallback", text.EntitlementLabel("brand_new_feature", "fallback"));
        Assert.Equal("WhatsApp marketing message", text.EventTypeLabel(BillableEventTypes.WhatsAppMarketingMessage));
        Assert.Equal("Viber session", text.EventTypeLabel("viber_session"));
        Assert.Equal("Balance top-up", text.LedgerLabel(LedgerEntryType.WalletTopUp));
        Assert.Equal("Brand new entry", text.LedgerLabel("brand_new_entry"));
        Assert.Equal("Campaigns isn't included in your plan. See Settings → Billing.",
            text.Localize(new EntitlementDeniedException(EntitlementKeys.Campaigns, EntitlementDeniedException.NotIncludedKey)
                .WithArgs(BillingTextHelper.EntitlementArg(EntitlementKeys.Campaigns))));
    }

    [Fact]
    public void Identity_errors_are_worded_from_the_strings_files_and_keep_their_codes()
    {
        var describer = new LocalizedIdentityErrorDescriber(TextKit.Localizer());

        var taken = describer.DuplicateUserName("ann@x.com");
        Assert.Equal((nameof(IdentityErrorDescriber.DuplicateUserName), "Someone already signs in with ann@x.com."), (taken.Code, taken.Description));
        Assert.Equal("Use at least 6 characters for the password.", describer.PasswordTooShort(6).Description);
        Assert.Equal(nameof(IdentityErrorDescriber.PasswordRequiresDigit), describer.PasswordRequiresDigit().Code);
        Assert.All(new[]
        {
            describer.DefaultError(), describer.ConcurrencyFailure(), describer.PasswordMismatch(), describer.InvalidToken(),
            describer.LoginAlreadyAssociated(), describer.InvalidUserName("x"), describer.InvalidEmail("x"), describer.DuplicateEmail("x"),
            describer.UserAlreadyHasPassword(), describer.PasswordRequiresUniqueChars(2), describer.PasswordRequiresNonAlphanumeric(),
            describer.PasswordRequiresDigit(), describer.PasswordRequiresLower(), describer.PasswordRequiresUpper()
        }, e => Assert.DoesNotContain("identity.errors.", e.Description));
    }
}
