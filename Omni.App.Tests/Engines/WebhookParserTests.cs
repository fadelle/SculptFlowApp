using System.Text.Json;
using Omni.App.Business.Engines.Telegram;
using Omni.App.Business.Engines.WhatsApp;

namespace Omni.App.Tests.Engines;

public class TelegramUpdateParserTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string DefaultFrom = ",\"from\":{\"id\":7,\"is_bot\":false,\"first_name\":\"Ann\",\"last_name\":\"Lee\",\"username\":\"ann\"}";

    private static string Msg(string extra, string chatType = "private", string from = DefaultFrom) =>
        "{\"update_id\":9,\"message\":{\"message_id\":5,\"date\":1700000000,\"chat\":{\"id\":42,\"type\":\"" + chatType
        + "\",\"first_name\":\"ChatFirst\"}" + from + "," + extra + "}}";

    [Fact]
    public void Text_message_is_parsed_with_sender_details()
    {
        var u = TelegramUpdateParser.Parse(J(Msg("\"text\":\"hello\"")));
        Assert.Equal(TelegramUpdateKind.CustomerMessage, u.Kind);
        Assert.Equal(9, u.UpdateId);
        var m = u.Message!;
        Assert.Equal((42L, 7L, 5L), (m.ChatId, m.FromUserId, m.MessageId));
        Assert.Equal(("Ann", "Lee", "ann"), (m.FirstName, m.LastName, m.Username));
        Assert.Equal(("text", "hello", null), (m.MessageType, m.Content, m.MetadataJson));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), m.Timestamp);
    }

    [Theory]
    [InlineData("/start", "command")]
    [InlineData("/start abc123", "command")]
    [InlineData("/help@MyBot", "command")]
    [InlineData("hello /start", "text")]
    [InlineData("/ not a command", "text")]
    public void Commands_are_recognised(string text, string type) =>
        Assert.Equal(type, TelegramUpdateParser.Parse(J(Msg($"\"text\":\"{text}\""))).Message!.MessageType);

    [Theory]
    [InlineData("\"photo\":[{\"file_id\":\"x\"}]", "image", "[photo]")]
    [InlineData("\"voice\":{\"file_id\":\"x\"}", "audio", "[voice message]")]
    [InlineData("\"audio\":{\"file_id\":\"x\"}", "audio", "[audio]")]
    [InlineData("\"video\":{\"file_id\":\"x\"}", "video", "[video]")]
    [InlineData("\"video_note\":{\"file_id\":\"x\"}", "video", "[video message]")]
    [InlineData("\"document\":{\"file_id\":\"x\"}", "document", "[document]")]
    [InlineData("\"sticker\":{\"file_id\":\"x\"}", "sticker", "[sticker]")]
    [InlineData("\"location\":{\"latitude\":1}", "location", "[location]")]
    [InlineData("\"contact\":{\"phone_number\":\"1\"}", "contacts", "[contact]")]
    public void Media_gets_a_placeholder_and_raw_metadata(string extra, string type, string placeholder)
    {
        var m = TelegramUpdateParser.Parse(J(Msg(extra))).Message!;
        Assert.Equal((type, placeholder), (m.MessageType, m.Content));
        Assert.NotNull(m.MetadataJson);
    }

    [Fact]
    public void Caption_replaces_placeholder_and_unknown_content_is_unsupported()
    {
        Assert.Equal("nice", TelegramUpdateParser.Parse(J(Msg("\"photo\":[{}],\"caption\":\"nice\""))).Message!.Content);
        Assert.Equal("[photo]", TelegramUpdateParser.Parse(J(Msg("\"photo\":[{}],\"caption\":\"  \""))).Message!.Content);
        var u = TelegramUpdateParser.Parse(J(Msg("\"poll\":{}"))).Message!;
        Assert.Equal(("unsupported", "[unsupported message]"), (u.MessageType, u.Content));
    }

    [Fact]
    public void Missing_from_falls_back_to_chat_names_and_missing_date_uses_now()
    {
        var json = """{"update_id":1,"message":{"message_id":5,"chat":{"id":42,"type":"private","first_name":"Chat"},"text":"x"}}""";
        var m = TelegramUpdateParser.Parse(J(json)).Message!;
        Assert.Null(m.FromUserId);
        Assert.Equal("Chat", m.FirstName);
        Assert.True((DateTimeOffset.UtcNow - m.Timestamp).Duration() < TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("[]", "not_an_object")]
    [InlineData("{\"update_id\":1,\"edited_message\":{}}", "unsupported_update_type")]
    [InlineData("{\"update_id\":1,\"message\":5}", "unsupported_update_type")]
    [InlineData("{\"update_id\":1,\"message\":{\"message_id\":1}}", "no_chat")]
    [InlineData("{\"update_id\":1,\"message\":{\"message_id\":1,\"chat\":{\"id\":\"x\"}}}", "no_chat")]
    [InlineData("{\"update_id\":1,\"message\":{\"message_id\":1,\"chat\":{\"id\":1,\"type\":\"group\"}}}", "non_private_chat")]
    [InlineData("{\"update_id\":1,\"message\":{\"chat\":{\"id\":1,\"type\":\"private\"}}}", "no_message_id")]
    public void Ignored_updates_carry_a_reason(string json, string reason)
    {
        var u = TelegramUpdateParser.Parse(J(json));
        Assert.Equal(TelegramUpdateKind.Ignored, u.Kind);
        Assert.Equal(reason, u.IgnoredReason);
        Assert.Null(u.Message);
    }

    [Fact]
    public void Bot_senders_are_ignored() =>
        Assert.Equal("bot_sender", TelegramUpdateParser.Parse(J(Msg("\"text\":\"x\"", from: ",\"from\":{\"id\":1,\"is_bot\":true}"))).IgnoredReason);
}

public class MetaWebhookParserTests
{
    private static IReadOnlyList<Omni.App.Entities.Dtos.WhatsApp.ParsedMetaEvent> P(string json) =>
        MetaWebhookParser.Parse(JsonDocument.Parse(json).RootElement);

    private const string Meta = "\"metadata\":{\"phone_number_id\":\"PN1\"}";

    private static string Change(string field, string value) =>
        "{\"entry\":[{\"id\":\"WABA1\",\"changes\":[{\"field\":\"" + field + "\",\"value\":" + value.Replace("@META", Meta) + "}]}]}";

    [Fact]
    public void Missing_entry_gives_no_events()
    {
        Assert.Empty(P("{}"));
        Assert.Empty(P("""{"entry":5}"""));
        Assert.Empty(P("""{"entry":[{"id":"x"},{"id":"y","changes":[{"field":"messages"}]}]}"""));
    }

    [Fact]
    public void Text_message_with_contact_profile()
    {
        var e = Assert.Single(P(Change("messages", """{ @META, "contacts":[{"wa_id":"961700","profile":{"name":"Sam"}}], "messages":[{"from":"961999","id":"wamid.1","timestamp":"1700000000","type":"text","text":{"body":"hi"}}]}""")));
        Assert.Equal(ParsedMetaEventKind.CustomerMessage, e.Kind);
        Assert.Equal(("PN1", "WABA1", "961700", "Sam"), (e.PhoneNumberId, e.WabaId, e.CustomerWaId, e.CustomerName));
        Assert.Equal(("text", "hi", "wamid.1"), (e.MessageType, e.Content, e.ExternalMessageId));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), e.Timestamp);
    }

    [Fact]
    public void Sender_falls_back_to_from_and_type_defaults_to_text()
    {
        var e = Assert.Single(P(Change("messages", """{"messages":[{"from":"96170","id":"1","text":{"body":"x"}}]}""")));
        Assert.Equal("96170", e.CustomerWaId);
        Assert.Equal("text", e.MessageType);
        Assert.Null(e.Timestamp);
    }

    [Theory]
    [InlineData("""{"type":"interactive","interactive":{"type":"button_reply","button_reply":{"id":"b1","title":"Yes"}}}""", "interactive", "Yes", "b1")]
    [InlineData("""{"type":"interactive","interactive":{"type":"list_reply","list_reply":{"id":"l1","title":"Opt"}}}""", "interactive", "Opt", "l1")]
    [InlineData("""{"type":"button","button":{"text":"Book","payload":"BOOK"}}""", "button", "Book", "BOOK")]
    [InlineData("""{"type":"location","location":{"name":"Client"}}""", "location", "Client", null)]
    [InlineData("""{"type":"location","location":{}}""", "location", "[location]", null)]
    [InlineData("""{"type":"contacts","contacts":[{}]}""", "contacts", "[contact]", null)]
    [InlineData("{\"type\":\"reaction\",\"reaction\":{\"emoji\":\"\\ud83d\\udc4d\"}}", "reaction", "\ud83d\udc4d", null)]
    [InlineData("""{"type":"image","image":{"caption":"look"}}""", "image", "look", null)]
    [InlineData("""{"type":"document","document":{}}""", "document", "[document]", null)]
    [InlineData("""{"type":"sticker"}""", "sticker", "[sticker]", null)]
    [InlineData("""{"type":"order","order":{}}""", "order", null, null)]
    public void Message_content_by_type(string message, string type, string? content, string? selected)
    {
        var e = Assert.Single(P(Change("messages", "{\"messages\":[" + message.Insert(1, "\"from\":\"1\",\"id\":\"m\",") + "]}")));
        Assert.Equal((type, content, selected), (e.MessageType, e.Content, e.SelectedValue));
    }

    [Fact]
    public void Statuses_including_failures()
    {
        var evs = P(Change("messages", """{ @META, "statuses":[{"id":"w1","status":"delivered","timestamp":"1700000000"},{"id":"w2","status":"failed","errors":[{"code":131026,"title":"Undeliverable","message":"Bad number"}]},{"id":"w3","status":"failed","errors":[{"code":1,"title":"T"}]}]}"""));
        Assert.Equal(3, evs.Count);
        Assert.All(evs, e => Assert.Equal(ParsedMetaEventKind.MessageStatus, e.Kind));
        Assert.Equal(("w1", "delivered", "PN1"), (evs[0].ExternalMessageId, evs[0].DeliveryStatus, evs[0].PhoneNumberId));
        Assert.Equal(("131026", "Bad number"), (evs[1].FailureCode, evs[1].FailureReason));
        Assert.Equal("T", evs[2].FailureReason);
    }

    [Fact]
    public void Business_app_echoes_have_their_own_kind() =>
        Assert.Equal(ParsedMetaEventKind.BusinessAppEcho,
            Assert.Single(P(Change("smb_message_echoes", """{"messages":[{"from":"1","id":"e","type":"text","text":{"body":"x"}}]}"""))).Kind);

    [Fact]
    public void Template_events()
    {
        var e = Assert.Single(P(Change("message_template_status_update",
            """{"message_template_id":123,"message_template_name":"hello","message_template_language":"en","event":"REJECTED","reason":"ABUSIVE","new_category":"MARKETING","previous_category":"UTILITY","message_template_component":{"a":1}}""")));
        Assert.Equal(ParsedMetaEventKind.TemplateStatus, e.Kind);
        Assert.Equal(("123", "hello", "en", "REJECTED", "ABUSIVE"), (e.MetaTemplateId, e.TemplateName, e.TemplateLanguage, e.TemplateStatus, e.TemplateReason));
        Assert.Equal(("MARKETING", "UTILITY", "MARKETING"), (e.TemplateCategory, e.TemplatePreviousCategory, e.TemplateCurrentCategory));
        Assert.Equal("{\"a\":1}", e.TemplateComponentsJson);
        var q = Assert.Single(P(Change("message_template_quality_update", """{"new_quality_score":"HIGH","status":"x"}""")));
        Assert.Equal(("HIGH", "x"), (q.TemplateQualityRating, q.TemplateStatus));
    }

    [Theory]
    [InlineData("phone_number_quality_update")]
    [InlineData("phone_number_name_update")]
    [InlineData("account_update")]
    [InlineData("account_review_update")]
    [InlineData("account_alerts")]
    public void Health_events(string field)
    {
        var e = Assert.Single(P(Change(field, """{"phone_number_id":"PN9","event":"FLAGGED","current_limit":"TIER_1K","code":"7","display_phone_number":"+1"}""")));
        Assert.Equal(ParsedMetaEventKind.WhatsAppHealth, e.Kind);
        // current_limit is a messaging tier, not a quality rating: it must not end up in the rating field.
        Assert.Equal((field, "PN9", "FLAGGED", null, "7"), (e.HealthEventType, e.PhoneNumberId, e.HealthStatus, e.HealthQualityRating, e.HealthCode));
        Assert.Equal("+1", e.HealthMessage);
        Assert.Equal("GREEN", Assert.Single(P(Change(field, """{"phone_number_id":"PN9","event":"CONNECTED","quality_rating":"GREEN"}"""))).HealthQualityRating);
        Assert.Equal("PN2", Assert.Single(P(Change(field, """{"metadata":{"phone_number_id":"PN2"},"decision":"APPROVED"}"""))).PhoneNumberId);
    }

    [Fact]
    public void History_sync_state_sync_and_unknown()
    {
        var h = Assert.Single(P(Change("history", """{ @META }""")));
        Assert.Equal((ParsedMetaEventKind.HistorySync, "PN1"), (h.Kind, h.PhoneNumberId));
        Assert.Equal(ParsedMetaEventKind.AppStateSync, Assert.Single(P(Change("smb_app_state_sync", "{}"))).Kind);
        var u = Assert.Single(P(Change("something_new", "{}")));
        Assert.Equal((ParsedMetaEventKind.Unknown, "something_new"), (u.Kind, u.RawFieldName));
    }
}

