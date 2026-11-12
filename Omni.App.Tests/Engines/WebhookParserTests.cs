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
    public void Values_longer_than_their_columns_are_cut_and_unreadable_parts_are_counted()
    {
        // QA t10-03: a 257-character template name, a 300-character profile name or a long wa_id failed the save.
        var json = $$$"""
            {"entry":[1,{"id":"W1","changes":[{"field":"messages","value":{"metadata":{"phone_number_id":"P1"},
              "contacts":[{"wa_id":"{{{new string('9', 60)}}}","profile":{"name":"{{{new string('N', 300)}}}"}}],
              "messages":[{"from":"{{{new string('9', 60)}}}","id":"{{{new string('w', 250)}}}","type":"{{{new string('x', 40)}}}"}]}},
              {"field":"message_template_status_update","value":{"message_template_name":"{{{new string('t', 257)}}}","message_template_language":"en_US_very_long","event":"APPROVED"}}]}]}
            """;

        var events = MetaWebhookParser.Parse(JsonDocument.Parse(json).RootElement, out var unreadable);

        Assert.Equal(1, unreadable);
        var message = events.Single(e => e.Kind == ParsedMetaEventKind.CustomerMessage);
        Assert.Equal((50, 200, 200, 30), (message.CustomerWaId!.Length, message.CustomerName!.Length, message.ExternalMessageId!.Length, message.MessageType!.Length));
        var template = events.Single(e => e.Kind == ParsedMetaEventKind.TemplateStatus);
        Assert.Equal((200, 10), (template.TemplateName!.Length, template.TemplateLanguage!.Length));
    }

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

    [Fact]
    public void An_edited_message_is_parsed_like_a_message()
    {
        // QA t3-inbox-17: edits were dropped silently.
        var u = TelegramUpdateParser.Parse(J("{\"update_id\":7,\"edited_message\":{\"message_id\":42,\"date\":1790000000,\"edit_date\":1790000100,\"chat\":{\"id\":5,\"type\":\"private\"},\"from\":{\"id\":5,\"is_bot\":false,\"first_name\":\"Ann\"},\"text\":\"fixed typo\"}}"));
        Assert.Equal(TelegramUpdateKind.EditedMessage, u.Kind);
        Assert.Equal((5L, 42L, "fixed typo"), (u.Message!.ChatId, u.Message.MessageId, u.Message.Content));
    }

    [Theory]
    [InlineData("kicked", TelegramUpdateKind.BotBlocked)]
    [InlineData("member", TelegramUpdateKind.BotUnblocked)]
    public void Blocking_and_restarting_the_bot_are_parsed(string status, TelegramUpdateKind kind)
    {
        var u = TelegramUpdateParser.Parse(J("{\"update_id\":8,\"my_chat_member\":{\"chat\":{\"id\":9,\"type\":\"private\",\"first_name\":\"Ann\"},\"date\":1790000000,\"new_chat_member\":{\"status\":\"" + status + "\"}}}"));
        Assert.Equal(kind, u.Kind);
        Assert.Equal(9L, u.Message!.ChatId);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("{\"file_id\":\"f\"}", true)]
    [InlineData("[{\"file_id\":\"a\"},{\"file_id\":\"b\"}]", false)]
    public void An_edit_marks_object_or_empty_metadata_and_keeps_a_media_array(string? metadata, bool marked)
    {
        var at = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        var result = Omni.App.Business.Engines.Telegram.TelegramWebhookProcessor.WithEditedAt(metadata, at);
        Assert.Equal(marked, result!.Contains("\"editedAt\":\"2026-11-07T10:00:00Z\""));
        if (!marked) Assert.Equal(metadata, result);
        if (metadata?.Contains("file_id") == true && marked) Assert.Contains("\"file_id\":\"f\"", result);
    }

    [Theory]
    [InlineData("[]", "not_an_object")]
    [InlineData("{\"update_id\":1,\"edited_message\":{}}", "no_chat")]
    [InlineData("{\"update_id\":1,\"channel_post\":{}}", "unsupported_update_type")]
    [InlineData("{\"update_id\":1,\"my_chat_member\":{\"chat\":{\"id\":1,\"type\":\"group\"}}}", "non_private_chat")]
    [InlineData("{\"update_id\":1,\"my_chat_member\":{\"chat\":{\"id\":1,\"type\":\"private\"},\"new_chat_member\":{\"status\":\"administrator\"}}}", "member_status_administrator")]
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
        var e = Assert.Single(P(Change("messages", """{ @META, "contacts":[{"wa_id":"961700","profile":{"name":"Sam"}}], "messages":[{"from":"961700","id":"wamid.1","timestamp":"1700000000","type":"text","text":{"body":"hi"}}]}""")));
        Assert.Equal(ParsedMetaEventKind.CustomerMessage, e.Kind);
        Assert.Equal(("PN1", "WABA1", "961700", "Sam"), (e.PhoneNumberId, e.WabaId, e.CustomerWaId, e.CustomerName));
        Assert.Null(e.Referral);
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
        // display_phone_number is not a message (QA t10-02: UNFLAGGED showed the phone number as the last problem).
        Assert.Null(e.HealthMessage);
        Assert.Equal("GREEN", Assert.Single(P(Change(field, """{"phone_number_id":"PN9","event":"CONNECTED","quality_rating":"GREEN"}"""))).HealthQualityRating);
        Assert.Equal("PN2", Assert.Single(P(Change(field, """{"metadata":{"phone_number_id":"PN2"},"decision":"APPROVED"}"""))).PhoneNumberId);
    }

    // ---- QA t10-05: one change carrying messages from two customers ----

    [Fact]
    public void Each_message_belongs_to_its_own_sender()
    {
        var events = P(Change("messages", """
            { @META, "contacts":[{"wa_id":"96171001003","profile":{"name":"X"}},{"wa_id":"96171001004","profile":{"name":"Y"}}],
              "messages":[{"from":"96171001003","id":"w1","type":"text","text":{"body":"what are your hours?"}},
                          {"from":"96171001004","id":"w2","type":"text","text":{"body":"how much is service A?"}}]}
            """));
        Assert.Equal([("96171001003", "X", "what are your hours?"), ("96171001004", "Y", "how much is service A?")],
            events.Select(e => (e.CustomerWaId!, e.CustomerName, e.Content!)));
    }

    [Fact]
    public void A_sender_missing_from_contacts_keeps_its_number_and_an_echo_belongs_to_the_listed_customer()
    {
        var unnamed = Assert.Single(P(Change("messages", """{ @META, "contacts":[{"wa_id":"1","profile":{"name":"One"}}], "messages":[{"from":"2","id":"w","type":"text","text":{"body":"x"}}]}""")));
        Assert.Equal(("2", (string?)null), (unnamed.CustomerWaId, unnamed.CustomerName));
        var echo = Assert.Single(P(Change("smb_message_echoes", """{ @META, "contacts":[{"wa_id":"96170","profile":{"name":"Cust"}}], "messages":[{"from":"15550001","to":"96170","id":"e","type":"text","text":{"body":"hi"}}]}""")));
        Assert.Equal(("96170", "Cust"), (echo.CustomerWaId, echo.CustomerName));
        var echoTo = Assert.Single(P(Change("smb_message_echoes", """{ @META, "messages":[{"from":"15550001","to":"96171","id":"e2","type":"text","text":{"body":"hi"}}]}""")));
        Assert.Equal("96171", echoTo.CustomerWaId);
    }

    // ---- QA t10-11 / t10-12 ----

    [Fact]
    public void Account_events_take_the_entrys_time_so_a_retry_is_the_same_event()
    {
        var json = """{"entry":[{"id":"WABA1","time":1793318400,"changes":[{"field":"account_update","value":{"event":"ACCOUNT_VIOLATION"}},{"field":"message_template_status_update","value":{"event":"APPROVED","message_template_name":"t"}},{"field":"messages","value":{"metadata":{"phone_number_id":"PN1"},"messages":[{"from":"1","id":"w","type":"text","text":{"body":"x"}}]}}]}]}""";
        var events = P(json);
        var at = DateTimeOffset.FromUnixTimeSeconds(1793318400);
        Assert.Equal(at, events.Single(e => e.Kind == ParsedMetaEventKind.WhatsAppHealth).Timestamp);
        Assert.Equal(at, events.Single(e => e.Kind == ParsedMetaEventKind.TemplateStatus).Timestamp);
        Assert.Null(events.Single(e => e.Kind == ParsedMetaEventKind.CustomerMessage).Timestamp); // a message keeps its own (here none)
        Assert.Equal(at, P(json).Single(e => e.Kind == ParsedMetaEventKind.WhatsAppHealth).Timestamp);  // the retry: same time
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("""{"entry":[1]}""")]
    [InlineData("""{"entry":[{"id":"W","changes":[{"field":"messages","value":"a string"}]}]}""")]
    public void Shapes_Meta_doesnt_send_are_skipped_not_thrown(string json) => Assert.Empty(P(json));

    [Theory]
    [InlineData("""{"object":"whatsapp_business_account","entry":[]}""", true)]
    [InlineData("""{"entry":[]}""", true)]
    [InlineData("""{"object":"page","entry":[]}""", false)]
    [InlineData("""{"object":"instagram","entry":[]}""", false)]
    public void Only_whatsapp_business_account_deliveries_are_read(string json, bool expected) =>
        Assert.Equal(expected, MetaWebhookParser.IsWhatsAppDelivery(JsonDocument.Parse(json).RootElement));

    // ---- QA t10-04: echoes in Meta's documented shape ----

    [Fact]
    public void An_echo_in_Metas_shape_is_read_and_its_customer_is_to_never_from()
    {
        var e = Assert.Single(P(Change("smb_message_echoes", """
            { "messaging_product":"whatsapp", "metadata":{"display_phone_number":"15550100999","phone_number_id":"PN1"},
              "message_echoes":[{"from":"15550100999","to":"96170777","id":"wamid.echo","timestamp":"1700000000","type":"text","text":{"body":"from the phone"}}]}
            """)));
        Assert.Equal((ParsedMetaEventKind.BusinessAppEcho, "96170777", "from the phone", "wamid.echo"), (e.Kind, e.CustomerWaId, e.Content, e.ExternalMessageId));

        // Without contacts[] or "to", the business's own number is never taken as the customer.
        var noTo = Assert.Single(P(Change("smb_message_echoes", """{ @META, "messages":[{"from":"15550100999","id":"e","type":"text","text":{"body":"x"}}]}""")));
        Assert.Null(noTo.CustomerWaId);
    }

    // ---- QA t10-08: click-to-WhatsApp ads and changed numbers ----

    [Fact]
    public void A_message_from_an_ad_keeps_the_referral()
    {
        var e = Assert.Single(P(Change("messages", """
            { @META, "contacts":[{"wa_id":"96170","profile":{"name":"Ann"}}],
              "messages":[{"from":"96170","id":"w","type":"text","text":{"body":"Hi, I saw your ad. Is the offer still valid?"},
                "referral":{"source_url":"https://fb.me/x","source_id":"120201","source_type":"ad","headline":"50% off Service A","body":"This week only","media_type":"image","ctwa_clid":"ARAkLk"}}]}
            """)));
        Assert.Equal(new Omni.App.Entities.Dtos.WhatsApp.MetaAdReferral("ad", "120201", "https://fb.me/x", "50% off Service A", "This week only", "ARAkLk"), e.Referral);
        Assert.Equal("50% off Service A — This week only", e.Referral!.Summary);
        Assert.Equal("Hi, I saw your ad. Is the offer still valid?", e.Content);
        var metadata = JsonDocument.Parse(e.MetadataJson!).RootElement.GetProperty("referral");
        Assert.Equal(("ARAkLk", "120201"), (metadata.GetProperty("ctwa_clid").GetString(), metadata.GetProperty("source_id").GetString()));

        // A photo from an ad keeps its media details next to the referral.
        var photo = Assert.Single(P(Change("messages", """{ @META, "messages":[{"from":"96170","id":"p","type":"image","image":{"id":"m1","caption":"this one"},"referral":{"source_id":"9","source_type":"ad"}}]}""")));
        var photoMeta = JsonDocument.Parse(photo.MetadataJson!).RootElement;
        Assert.Equal(("m1", "9"), (photoMeta.GetProperty("id").GetString(), photoMeta.GetProperty("referral").GetProperty("source_id").GetString()));
        Assert.Equal("ad 9", photo.Referral!.Summary);
    }

    [Theory]
    [InlineData("""{"body":"User A changed from 96170 to 96171","wa_id":"96171","type":"user_changed_number"}""", "96171")]
    [InlineData("""{"body":"User A changed number","new_wa_id":"96172","type":"customer_changed_number"}""", "96172")]
    [InlineData("""{"body":"Something else","type":"customer_identity_changed","identity":"x"}""", null)]
    public void A_system_message_has_its_text_and_a_changed_number(string system, string? newWaId)
    {
        var e = Assert.Single(P(Change("messages", """{ @META, "messages":[{"from":"96170","id":"s","type":"system","system":""" + system + "}]}")));
        Assert.Equal(("system", newWaId), (e.MessageType, e.NewWaId));
        Assert.False(string.IsNullOrWhiteSpace(e.Content));
    }

    // ---- QA t10-02: Meta's documented account_update shapes (entry.id = the WABA, no phone_number_id) ----

    [Theory]
    [InlineData("""{"event":"DISABLED_UPDATE","ban_info":{"waba_ban_state":"DISABLE","waba_ban_date":"2026-10-30"}}""", "disabled", "DISABLE 2026-10-30")]
    [InlineData("""{"event":"DISABLED_UPDATE","ban_info":{"waba_ban_state":"SCHEDULE_FOR_DISABLE"}}""", "scheduled_for_disable", null)]
    [InlineData("""{"event":"DISABLED_UPDATE","ban_info":{"waba_ban_state":"REINSTATE"}}""", "reinstated", null)]
    [InlineData("""{"event":"ACCOUNT_RESTRICTION","restriction_info":[{"restriction_type":"RESTRICTED_BIZ_INITIATED_MESSAGING","expiration":1793318400}]}""", "restricted", "RESTRICTED_BIZ_INITIATED_MESSAGING until 2026-10-30")]
    [InlineData("""{"event":"ACCOUNT_RESTRICTION","restriction_info":[]}""", "unrestricted", null)]
    [InlineData("""{"event":"ACCOUNT_VIOLATION","violation_info":{"violation_type":"SCAM"}}""", "violation", "SCAM")]
    [InlineData("""{"event":"ACCOUNT_DELETED"}""", "removed", null)]
    [InlineData("""{"event":"PARTNER_REMOVED"}""", "removed", null)]
    [InlineData("""{"event":"ACCOUNT_OFFBOARDED"}""", "removed", null)]
    [InlineData("""{"event":"VERIFIED_ACCOUNT","phone_number":"15550100999"}""", "VERIFIED_ACCOUNT", null)]
    public void Account_updates_become_an_account_state(string value, string state, string? message)
    {
        var e = Assert.Single(P(Change("account_update", value)));
        Assert.Equal((ParsedMetaEventKind.WhatsAppHealth, "WABA1", (string?)null), (e.Kind, e.WabaId, e.PhoneNumberId));
        Assert.Equal((state, message), (e.HealthStatus, e.HealthMessage));
    }

    [Fact]
    public void A_rejected_display_name_keeps_its_reason()
    {
        var e = Assert.Single(P(Change("phone_number_name_update", """{"display_phone_number":"15550100999","decision":"REJECTED","requested_verified_name":"Glow","rejection_reason":"NAME_FORMAT_UNACCEPTABLE"}""")));
        Assert.Equal(("REJECTED", "NAME_FORMAT_UNACCEPTABLE"), (e.HealthStatus, e.HealthMessage));
    }

    // ---- QA t10-06: template events as Meta sends them ----

    [Fact]
    public void Template_category_updates_reasons_and_explanations()
    {
        var done = Assert.Single(P(Change("template_category_update", """{"message_template_id":7,"message_template_name":"promo","message_template_language":"en_US","previous_category":"UTILITY","new_category":"MARKETING"}""")));
        Assert.Equal((ParsedMetaEventKind.TemplateStatus, "template_category_update", "UTILITY", "MARKETING", (string?)null),
            (done.Kind, done.RawFieldName, done.TemplatePreviousCategory, done.TemplateCurrentCategory, done.TemplatePendingCategory));

        var soon = Assert.Single(P(Change("template_category_update", """{"message_template_id":7,"message_template_name":"promo","correct_category":"MARKETING","category_update_timestamp":1793318400}""")));
        Assert.Equal(("MARKETING", (string?)null, DateTimeOffset.FromUnixTimeSeconds(1793318400)), (soon.TemplatePendingCategory, soon.TemplateCurrentCategory, soon.TemplatePendingCategoryAt!.Value));

        var approved = Assert.Single(P(Change("message_template_status_update", """{"event":"APPROVED","message_template_name":"promo","reason":"NONE"}""")));
        Assert.Null(approved.TemplateReason);

        var rejected = Assert.Single(P(Change("message_template_status_update", """{"event":"REJECTED","message_template_name":"promo","reason":"INVALID_FORMAT","rejection_info":{"reason":"Variables can't be at the start.","recommendation":"Add text before {{1}}."}}""")));
        Assert.Equal(("INVALID_FORMAT", "Variables can't be at the start. Add text before {{1}}."), (rejected.TemplateReason, rejected.TemplateReasonDetail));

        var paused = Assert.Single(P(Change("message_template_status_update", """{"event":"PAUSED","message_template_name":"promo","reason":"NONE","other_info":{"title":"FIRST_PAUSE","description":"Paused for 3 hours because of low quality."}}""")));
        Assert.Equal(((string?)null, "FIRST_PAUSE Paused for 3 hours because of low quality."), (paused.TemplateReason, paused.TemplateReasonDetail));
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

