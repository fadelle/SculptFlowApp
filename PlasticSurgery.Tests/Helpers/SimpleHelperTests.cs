using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Configuration;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Tests.Helpers;

public class BillingMathTests
{
    [Theory]
    [InlineData("0.1234565", "0.123457")]
    [InlineData("0.1234564", "0.123456")]
    [InlineData("-0.0000005", "-0.000001")]
    public void RoundMoney_rounds_half_away_from_zero_at_six_places(string input, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            BillingMath.RoundMoney(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Truncate_handles_null_short_and_long()
    {
        Assert.Null(BillingMath.Truncate(null, 5));
        Assert.Equal("abc", BillingMath.Truncate("abc", 5));
        Assert.Equal("abcde", BillingMath.Truncate("abcdef", 5));
        Assert.Equal("", BillingMath.Truncate("", 0));
    }
}

public class SmallHelperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("+961 (70) 123-456", "96170123456")]
    [InlineData("abc", "")]
    public void InfobipNumbers_keeps_digits_only(string? input, string expected) =>
        Assert.Equal(expected, InfobipNumbers.Normalize(input));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  NULL ", true)]
    [InlineData("None", true)]
    [InlineData("n/a", true)]
    [InlineData("undefined", true)]
    [InlineData("hello", false)]
    public void LenientJson_detects_empty_placeholders(string? input, bool empty) =>
        Assert.Equal(empty, LenientJson.IsEmpty(input));

    [Fact]
    public void VectorLiteral_formats_invariant_round_trip()
    {
        Assert.Equal("[]", VectorLiteral.From([]));
        Assert.Equal("[1,0.5,-2.25]", VectorLiteral.From([1f, 0.5f, -2.25f]));
    }

    [Fact]
    public void TelegramWebhookSecret_generates_unique_and_compares()
    {
        var a = TelegramWebhookSecret.Generate();
        Assert.NotEqual(a, TelegramWebhookSecret.Generate());
        Assert.True(TelegramWebhookSecret.Matches(a, a));
        Assert.False(TelegramWebhookSecret.Matches(a, a + "x"));
        Assert.False(TelegramWebhookSecret.Matches(a, new string('z', a.Length)));
        Assert.False(TelegramWebhookSecret.Matches(null, a));
        Assert.False(TelegramWebhookSecret.Matches(a, ""));
        Assert.False(TelegramWebhookSecret.Matches("", ""));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("https://example.com/a%20b", "example.com/a b")]
    [InlineData("HTTP://example.com", "example.com")]
    [InlineData("ftp://x.y", "ftp://x.y")]
    public void UrlDisplay_strips_scheme_and_decodes(string? url, string expected) =>
        Assert.Equal(expected, UrlDisplayHelper.Readable(url));

    [Fact]
    public void InfobipWebhookUrls_build_escapes_secret_and_trims_base()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"https://x.com/api/integrations/whatsapp/connections/{id}/events?token=a%2Bb%26c",
            InfobipWebhookUrls.Build("https://x.com/", id, "a+b&c"));
    }

    [Fact]
    public void InfobipWebhookUrls_ForConnection_needs_base_url_and_token()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = " https://x.com/ " }).Build();
        var empty = new ConfigurationBuilder().Build();
        var withToken = new ChannelIntegration { Id = Guid.NewGuid(), WebhookVerifyToken = "t" };
        Assert.EndsWith("?token=t", InfobipWebhookUrls.ForConnection(cfg, withToken));
        Assert.Null(InfobipWebhookUrls.ForConnection(empty, withToken));
        Assert.Null(InfobipWebhookUrls.ForConnection(cfg, new ChannelIntegration { Id = Guid.NewGuid() }));
    }
}

public class ViewerTimeZoneTests
{
    [Fact]
    public void Find_rejects_blank_long_and_unknown_ids()
    {
        Assert.Null(ViewerTimeZone.Find(null));
        Assert.Null(ViewerTimeZone.Find("  "));
        Assert.Null(ViewerTimeZone.Find(new string('x', 101)));
        Assert.Null(ViewerTimeZone.Find("Not/AZone"));
        Assert.NotNull(ViewerTimeZone.Find("UTC"));
    }

    [Fact]
    public void Resolve_prefers_cookie_then_fallback_then_utc()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Cookie = $"{ViewerTimeZone.CookieName}=UTC";
        Assert.Equal(TimeZoneInfo.Utc.Id, ViewerTimeZone.Resolve(ctx.Request, "Asia/Beirut").Id);
        Assert.Equal(TimeZoneInfo.Utc, ViewerTimeZone.Resolve(null));
        Assert.Equal(TimeZoneInfo.Utc, ViewerTimeZone.Resolve(new DefaultHttpContext().Request, "bad"));
    }

    [Fact]
    public void ToUtc_applies_zone_offset_including_dst()
    {
        var tz = ViewerTimeZone.Find("Europe/Paris")!;
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero), ViewerTimeZone.ToUtc(new DateTime(2026, 1, 15, 12, 0, 0), tz));
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), ViewerTimeZone.ToUtc(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), tz));
    }
}

public class ConversationModeSyncTests
{
    private static Conversation Make(string mode) => new() { Id = Guid.NewGuid(), ClinicId = Guid.NewGuid(), LeadId = Guid.NewGuid(), Mode = mode, Channel = "whatsapp" };

    [Fact]
    public void Ai_to_human_sets_flags_and_adds_marker_dated_just_before_the_message()
    {
        var c = Make(ConversationMode.Ai);
        var at = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        ConversationModeSync.Apply(c, ConversationMode.Human, at);
        Assert.True(c.HumanTakeover);
        Assert.False(c.AiEnabled);
        var note = Assert.Single(c.Messages);
        Assert.Equal("Handed from AI to staff", note.Content);
        Assert.Equal(at.AddMilliseconds(-1), note.CreatedAt);
        Assert.Equal(c.Id, note.ConversationId);
    }

    [Fact]
    public void Human_to_ai_adds_returned_marker()
    {
        var c = Make(ConversationMode.Human);
        ConversationModeSync.Apply(c, ConversationMode.Ai);
        Assert.True(c.AiEnabled);
        Assert.False(c.HumanTakeover);
        Assert.Equal("Returned to AI", Assert.Single(c.Messages).Content);
    }

    [Theory]
    [InlineData(ConversationMode.Ai, ConversationMode.Approval)]
    [InlineData(ConversationMode.Approval, ConversationMode.Human)]
    [InlineData(ConversationMode.Ai, ConversationMode.Ai)]
    public void Other_transitions_add_no_marker(string from, string to)
    {
        var c = Make(from);
        ConversationModeSync.Apply(c, to);
        Assert.Empty(c.Messages);
        Assert.Equal(to, c.Mode);
    }
}

public class ConfigValuesTests
{
    private static ConfigDefinition Def(ConfigValueType t, decimal? min = null, decimal? max = null, string? pattern = null) =>
        new("S", "K", "0", t, "d", min, max, pattern);

    [Theory]
    [InlineData("5", null)]
    [InlineData("abc", "Enter a whole number.")]
    [InlineData("1.5", "Enter a whole number.")]
    [InlineData("0", "The value must be at least 1.")]
    [InlineData("11", "The value must be at most 10.")]
    public void Int_validation(string value, string? error) =>
        Assert.Equal(error, ConfigValues.Validate(Def(ConfigValueType.Int, 1, 10), value));

    [Theory]
    [InlineData("1.5", null)]
    [InlineData("-1", "The value must be at least 0.")]
    public void Decimal_validation(string value, string? error) =>
        Assert.Equal(error, ConfigValues.Validate(Def(ConfigValueType.Decimal, 0), value));

    [Fact]
    public void Decimal_with_comma_is_rejected() =>
        Assert.Equal("Enter a number (use . for decimals).", ConfigValues.Validate(Def(ConfigValueType.Decimal, 0), "1,5"));

    [Theory]
    [InlineData("true", null)]
    [InlineData("False", null)]
    [InlineData("yes", "Enter true or false.")]
    public void Bool_validation(string value, string? error) =>
        Assert.Equal(error, ConfigValues.Validate(Def(ConfigValueType.Bool), value));

    [Fact]
    public void Text_validation_uses_pattern_only_when_declared()
    {
        Assert.Null(ConfigValues.Validate(Def(ConfigValueType.String), "anything"));
        Assert.Null(ConfigValues.Validate(Def(ConfigValueType.String, pattern: "^a+$"), "aaa"));
        Assert.NotNull(ConfigValues.Validate(Def(ConfigValueType.String, pattern: "^a+$"), "b"));
    }

    [Fact]
    public void Converters_parse_invariantly()
    {
        Assert.Equal(12, ConfigValues.ToInt("12"));
        Assert.Equal(1.25m, ConfigValues.ToDecimal("1.25"));
        Assert.True(ConfigValues.ToBool("true"));
    }
}

public class KnowledgeTextNormalizerTests
{
    [Fact]
    public void Normalize_cleans_whitespace_invisibles_and_blank_runs()
    {
        Assert.Equal("", KnowledgeTextNormalizer.Normalize(null));
        Assert.Equal("a b", KnowledgeTextNormalizer.Normalize("a \t  b"));
        Assert.Equal("ab", KnowledgeTextNormalizer.Normalize("a​b﻿"));
        Assert.Equal("a\n\nb", KnowledgeTextNormalizer.Normalize("a\r\n\r\n\r\n\r\nb"));
        Assert.Equal("a\nb", KnowledgeTextNormalizer.Normalize("a  \n  b"));
        Assert.Equal("a\n\nb", KnowledgeTextNormalizer.Normalize("a\fb"));
        Assert.Equal("ab", KnowledgeTextNormalizer.Normalize("a\u0001b"));
        Assert.Equal("a\nb", KnowledgeTextNormalizer.Normalize("a\rb"));
    }
}

public class LabelAndBadgeTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("consultation_booked", "Consultation booked")]
    [InlineData("NO_SHOW", "No-show")]
    [InlineData("some_new_status", "Some new status")]
    public void FilterLabel(string? v, string expected) => Assert.Equal(expected, FilterLabelHelper.Label(v));

    [Theory]
    [InlineData("hot", "badge-red")]
    [InlineData("READ", "badge-green")]
    [InlineData("zzz", "badge-gray")]
    [InlineData(null, "badge-gray")]
    public void BadgeClass(string? v, string expected) => Assert.Equal(expected, StatusBadgeHelper.CssClass(v));

    [Theory]
    [InlineData("approved", "Approved", "badge-green")]
    [InlineData("in_appeal", "Problem", "badge-red")]
    [InlineData(null, "Unknown", "badge-red")]
    public void TemplateStatus(string? v, string label, string css)
    {
        Assert.Equal(label, StatusBadgeHelper.TemplateStatusLabel(v));
        Assert.Equal(css, StatusBadgeHelper.TemplateStatusCssClass(v));
    }

    [Fact]
    public void TemplateStatus_label_is_case_insensitive() =>
        Assert.Equal("Pending", StatusBadgeHelper.TemplateStatusLabel("PENDING"));

    [Theory]
    [InlineData("whatsapp", "WA", "#25D366")]
    [InlineData("TELEGRAM", "TG", "#229ED9")]
    [InlineData("pigeon", "?", "#9ca3af")]
    [InlineData(null, "?", "#9ca3af")]
    public void ChannelIcons(string? ch, string initials, string hex)
    {
        Assert.Equal(initials, ChannelIconHelper.Initials(ch));
        Assert.Equal(hex, ChannelIconHelper.Hex(ch));
    }

    [Fact]
    public void ChannelSvgs()
    {
        Assert.NotNull(ChannelIconHelper.Svg("WhatsApp"));
        Assert.Null(ChannelIconHelper.Svg("instagram"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("instagram"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("facebook"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("google"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("outlook"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("tiktok"));
        Assert.NotNull(ChannelIconHelper.BrandSvg("telegram"));
        Assert.Null(ChannelIconHelper.BrandSvg("sms"));
        Assert.Null(ChannelIconHelper.BrandSvg(null));
    }
}

public class PhoneCountryTests
{
    [Theory]
    [InlineData("+96170123456", "LB")]
    [InlineData("96170123456", "LB")]
    [InlineData("0096170123456", "LB")]
    [InlineData("+447911123456", "GB")]
    [InlineData("+14155550100", "US")]
    [InlineData("+971501234567", "AE")]
    [InlineData("+33 6 12 34 56 78", "FR")]
    public void Detects_country(string phone, string expected) => Assert.Equal(expected, PhoneCountry.FromPhone(phone));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("0123456789")]
    [InlineData("1234")]
    [InlineData("+0123456")]
    [InlineData("+")]
    [InlineData("+999123456")]
    public void Returns_null_for_unknowable(string? phone) => Assert.Null(PhoneCountry.FromPhone(phone));
}

public class LocalTimeHelperTests
{
    private static readonly DateTimeOffset T = new(2026, 3, 5, 14, 7, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Time_renders_utc_fallback_and_escapes()
    {
        Assert.Equal("&lt;none&gt;", LocalTimeHelper.Time(null, empty: "<none>").ToString());
        var html = LocalTimeHelper.Time(T, "datetime", clinicTimeZone: "Asia/Beirut").ToString()!;
        Assert.Contains("datetime=\"2026-03-05T12:07:00Z\"", html);
        Assert.Contains("data-clinic-tz=\"Asia/Beirut\"", html);
        Assert.Contains(">Mar 5, 12:07 UTC</time>", html);
        Assert.Contains(">Mar 5, 2026</time>", LocalTimeHelper.Time(T, "date").ToString());
        Assert.Contains(">Mar 5, 12:07 UTC<", LocalTimeHelper.Time(T, "unknown-format").ToString());
    }

    [Fact]
    public void Title_and_Iso()
    {
        Assert.Equal("", LocalTimeHelper.Title(null).ToString());
        Assert.Contains("data-local-title=\"2026-03-05T12:07:00Z|datetime-year\"", LocalTimeHelper.Title(T).ToString());
        Assert.Equal("2026-03-05T12:07:00Z", LocalTimeHelper.Iso(T));
    }

    [Fact]
    public void Ago_buckets()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("just now", LocalTimeHelper.Ago(now.AddSeconds(-10)).ToString());
        Assert.Equal("5 min ago", LocalTimeHelper.Ago(now.AddMinutes(-5).AddSeconds(-1)).ToString());
        Assert.Equal("1 hour ago", LocalTimeHelper.Ago(now.AddHours(-1).AddMinutes(-1)).ToString());
        Assert.Equal("3 hours ago", LocalTimeHelper.Ago(now.AddHours(-3).AddMinutes(-1)).ToString());
        Assert.Equal("yesterday", LocalTimeHelper.Ago(now.AddHours(-30)).ToString());
        Assert.Equal("3 days ago", LocalTimeHelper.Ago(now.AddDays(-3).AddMinutes(-1)).ToString());
        Assert.Contains("<time", LocalTimeHelper.Ago(now.AddDays(-30)).ToString());
    }
}
