using System.Text.Json;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.WhatsApp;

namespace Omni.App.Business.Engines.WhatsApp;

/// <summary>
/// Understands Meta's raw WhatsApp Business Platform webhook JSON shape
/// ({ object, entry[].id (the WABA id), entry[].changes[].field/value }) and nothing else about the
/// rest of the application. Every other class in this feature (MetaWebhookProcessor, the Handlers)
/// works only with the normalized ParsedMetaEvent this produces — see that model's doc comment for
/// why that boundary matters (it's the whole point of this folder: raw Meta JSON parsing lives here
/// and nowhere else in the codebase).
///
/// Defensive by design: an unrecognized "changes[].field" becomes a ParsedMetaEventKind.Unknown
/// event (never thrown away, never a crash), and a sub-field this parser doesn't recognize inside a
/// known field just comes back null — RawJson always keeps the original "value" blob verbatim, so
/// nothing is silently lost even where a specific guess about Meta's exact property name misses.
/// Some of the less common health/account sub-field names here are best-effort against Meta's
/// documented shapes rather than verified against live traffic — see the class list below for which.
/// </summary>
public static class MetaWebhookParser
{
    public static IReadOnlyList<ParsedMetaEvent> Parse(JsonElement root) => Parse(root, out _);

    /// <summary>False when the delivery says it is about something other than a WhatsApp Business Account ("object" set to
    /// another value, e.g. "page" or "instagram"); such a delivery is ignored (QA t10-12). A body without "object" is read.</summary>
    public static bool IsWhatsAppDelivery(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object || GetString(root, "object") is not { } kind
        || string.Equals(kind, "whatsapp_business_account", StringComparison.Ordinal);

    /// <summary>Every event of a delivery. A part that isn't the shape Meta documents (a root, entry, change or value that isn't
    /// an object, or anything inside a change that can't be read) is skipped and counted in <paramref name="unreadable"/>, so
    /// it never fails the rest of the delivery (QA t10-03). Values longer than the columns they are stored in are cut.</summary>
    public static IReadOnlyList<ParsedMetaEvent> Parse(JsonElement root, out int unreadable)
    {
        unreadable = 0;
        var events = new List<ParsedMetaEvent>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            if (root.ValueKind != JsonValueKind.Object) unreadable++;
            return events;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                unreadable++;
                continue;
            }
            var wabaId = GetString(entry, "id");
            // Meta's time for the entry: an account-wide event (health, template) has no time of its own, and a retried
            // delivery repeats it, so it is the event's time (QA t10-11: "now" made every retry look new).
            var entryTime = ParseUnixTimestamp(GetString(entry, "time"));
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object || !change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                {
                    unreadable++;
                    continue;
                }
                var field = GetString(change, "field") ?? "";
                try
                {
                    events.AddRange(ParseChange(field, wabaId, value).Select(e => e.Timestamp is null && entryTime is not null && e.Kind is
                        ParsedMetaEventKind.WhatsAppHealth or ParsedMetaEventKind.TemplateStatus ? e with { Timestamp = entryTime } : e).ToList());
                }
                catch (InvalidOperationException)
                {
                    // A nested part of another JSON kind than documented (e.g. a message that is a string).
                    unreadable++;
                }
            }
        }

        return events.Select(Clip).ToList();
    }

    /// <summary>Cuts the values stored in limited columns (crm.messages, crm.contacts, channels.whatsapp_templates,
    /// channels.whatsapp_health_events, channels.channel_integrations) so an over-long value Meta allows can't fail the save.</summary>
    private static ParsedMetaEvent Clip(ParsedMetaEvent e) => e with
    {
        PhoneNumberId = Cut(e.PhoneNumberId, 200),
        WabaId = Cut(e.WabaId, 200),
        CustomerWaId = Cut(e.CustomerWaId, 50),
        CustomerName = Cut(e.CustomerName, 200),
        MessageType = Cut(e.MessageType, 30),
        ExternalMessageId = Cut(e.ExternalMessageId, 200),
        DeliveryStatus = Cut(e.DeliveryStatus, 30),
        FailureCode = Cut(e.FailureCode, 100),
        MetaTemplateId = Cut(e.MetaTemplateId, 200),
        TemplateName = Cut(e.TemplateName, 200),
        TemplateLanguage = Cut(e.TemplateLanguage, 10),
        TemplateQualityRating = Cut(e.TemplateQualityRating, 30),
        TemplatePreviousCategory = Cut(e.TemplatePreviousCategory, 30),
        TemplateCurrentCategory = Cut(e.TemplateCurrentCategory, 30),
        TemplatePendingCategory = Cut(e.TemplatePendingCategory, 30),
        TemplateReasonDetail = Cut(e.TemplateReasonDetail, 2000),
        NewWaId = Cut(e.NewWaId, 50),
        Referral = e.Referral is null ? null : e.Referral with
        {
            SourceId = Cut(e.Referral.SourceId, 100), Headline = Cut(e.Referral.Headline, 200), Body = Cut(e.Referral.Body, 1000)
        },
        HealthEventType = Cut(e.HealthEventType, 100),
        HealthStatus = Cut(e.HealthStatus, 50),
        HealthCode = Cut(e.HealthCode, 100),
    };

    private static string? Cut(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private static IEnumerable<ParsedMetaEvent> ParseChange(string field, string? wabaId, JsonElement value)
    {
        switch (field)
        {
            case "messages":
                // "messages" is overloaded: value.messages[] present means a genuine inbound
                // customer message; value.statuses[] present (no value.messages) means a delivery
                // status for something we sent. Never both are absent without falling through to
                // Unknown below.
                foreach (var e in ParseMessagesField(wabaId, value)) yield return e;
                break;

            case "smb_message_echoes":
                // A member replying from the WhatsApp Business App (Coexistence) — Meta delivers this
                // on its own dedicated field with the same message shape as "messages", not mixed
                // into "messages" itself. Every entry here is unconditionally an echo.
                foreach (var e in ParseMessageEntries(wabaId, value, ParsedMetaEventKind.BusinessAppEcho)) yield return e;
                break;

            case "message_template_status_update":
            case "message_template_quality_update":
            case "template_category_update":          // the field Meta actually sends (QA t10-06)
            case "message_template_category_update":  // kept for n8n's older forwarding
                yield return ParseTemplateEvent(field, wabaId, value);
                break;

            case "phone_number_quality_update":
            case "phone_number_name_update":
            case "account_update":
            case "account_review_update":
            case "account_alerts":
                yield return ParseHealthEvent(field, wabaId, value);
                break;

            case "history":
                // Coexistence history sync — past messages backfilled from the phone, not live
                // traffic. Never processed as a new message and never AI-eligible; see HistoryHandler.
                yield return new ParsedMetaEvent
                {
                    Kind = ParsedMetaEventKind.HistorySync,
                    WabaId = wabaId,
                    PhoneNumberId = value.TryGetProperty("metadata", out var historyMeta) ? GetString(historyMeta, "phone_number_id") : null,
                    RawFieldName = field,
                    RawJson = value.GetRawText()
                };
                break;

            case "smb_app_state_sync":
                // WhatsApp Business App/Coexistence state sync (e.g. linked-device state) — not a
                // message, never AI-eligible; see AppStateSyncHandler.
                yield return new ParsedMetaEvent
                {
                    Kind = ParsedMetaEventKind.AppStateSync,
                    WabaId = wabaId,
                    RawFieldName = field,
                    RawJson = value.GetRawText()
                };
                break;

            default:
                yield return new ParsedMetaEvent
                {
                    Kind = ParsedMetaEventKind.Unknown,
                    WabaId = wabaId,
                    RawFieldName = field,
                    RawJson = value.GetRawText()
                };
                break;
        }
    }

    private static IEnumerable<ParsedMetaEvent> ParseMessagesField(string? wabaId, JsonElement value)
    {
        // Genuine inbound customer messages: everything under "messages" that has value.messages[]
        // is unconditionally CustomerMessage now — business-app echoes arrive on their own
        // "smb_message_echoes" field (see ParseChange) rather than needing to be distinguished here.
        foreach (var e in ParseMessageEntries(wabaId, value, ParsedMetaEventKind.CustomerMessage)) yield return e;

        var phoneNumberId = value.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "phone_number_id") : null;

        // Delivery/read/failed(/deleted) statuses for messages we sent — Meta's documented status
        // values are sent/delivered/read/failed; "deleted" is passed through as-is if Meta ever
        // sends it rather than rejected, matching this app's general "tolerate unknown status
        // strings" convention (see MessageService.HandleStatusUpdateAsync's switch).
        if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in statuses.EnumerateArray())
            {
                string? failureCode = null;
                string? failureReason = null;
                if (s.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    var err = errors[0];
                    failureCode = GetString(err, "code");
                    failureReason = GetString(err, "message") ?? GetString(err, "title");
                }

                yield return new ParsedMetaEvent
                {
                    Kind = ParsedMetaEventKind.MessageStatus,
                    PhoneNumberId = phoneNumberId,
                    WabaId = wabaId,
                    ExternalMessageId = GetString(s, "id"),
                    DeliveryStatus = GetString(s, "status"),
                    FailureCode = failureCode,
                    FailureReason = failureReason,
                    Timestamp = ParseUnixTimestamp(GetString(s, "timestamp")),
                    RawJson = s.GetRawText()
                };
            }
        }
    }

    /// <summary>Shared by "messages" (customer case) and "smb_message_echoes" (member/Coexistence
    /// case) — both carry the same value.metadata/contacts[]/messages[] shape; only which Kind the
    /// caller wants differs. Meta's "contacts[]" identifies the OTHER party in the thread (the
    /// customer) regardless of which field/direction this particular payload represents.</summary>
    private static IEnumerable<ParsedMetaEvent> ParseMessageEntries(string? wabaId, JsonElement value, ParsedMetaEventKind kind)
    {
        // Meta sends echoes as value.message_echoes[] (QA t10-04: they were dropped); value.messages[] is also accepted.
        var listName = kind == ParsedMetaEventKind.BusinessAppEcho && value.TryGetProperty("message_echoes", out _) ? "message_echoes" : "messages";
        if (!value.TryGetProperty(listName, out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var phoneNumberId = value.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "phone_number_id") : null;

        // Every contact Meta lists, by wa_id: one change can carry messages from several customers (QA t10-05).
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? onlyWaId = null;
        if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var contact in contacts.EnumerateArray())
            {
                var waId = GetString(contact, "wa_id");
                if (waId is null) continue;
                names[waId] = contact.TryGetProperty("profile", out var profile) ? GetString(profile, "name") : null;
            }
            if (contacts.GetArrayLength() == 1) onlyWaId = GetString(contacts[0], "wa_id");
        }

        foreach (var m in messages.EnumerateArray())
        {
            var from = GetString(m, "from");
            // A customer message is the sender's ("from"); an echo's "from" is the business itself, so its customer is the
            // message's "to" (or the one listed contact), never "from" (QA t10-04).
            var customerWaId = kind == ParsedMetaEventKind.CustomerMessage
                ? from ?? onlyWaId
                : GetString(m, "to") ?? onlyWaId;
            var customerName = customerWaId is not null && names.TryGetValue(customerWaId, out var name) ? name : null;
            var messageType = GetString(m, "type") ?? "text";
            var (content, metadataJson, selectedValue) = ExtractMessageContent(m, messageType);
            var referral = Referral(m);
            if (referral is not null) metadataJson = WithReferral(metadataJson, m.GetProperty("referral"));
            var newWaId = messageType == "system" && m.TryGetProperty("system", out var system)
                && GetString(system, "type") is "user_changed_number" or "customer_changed_number"
                    ? GetString(system, "wa_id") ?? GetString(system, "new_wa_id")
                    : null;

            yield return new ParsedMetaEvent
            {
                Kind = kind,
                PhoneNumberId = phoneNumberId,
                WabaId = wabaId,
                CustomerWaId = customerWaId,
                CustomerName = customerName,
                Referral = referral,
                NewWaId = newWaId,
                MessageType = messageType,
                Content = content,
                MetadataJson = metadataJson,
                SelectedValue = selectedValue,
                ExternalMessageId = GetString(m, "id"),
                Timestamp = ParseUnixTimestamp(GetString(m, "timestamp")),
                RawJson = m.GetRawText()
            };
        }
    }

    /// <summary>Pulls a human-readable Content string plus a MetadataJson blob from whatever the
    /// message's type-specific nested object holds (text.body, image.{id,mime_type,caption}, a
    /// location's lat/lng, etc.) — see Message.MetadataJson. For interactive/button replies, also
    /// pulls the stable Meta reply id (SelectedValue) separately from the user-visible title
    /// (Content) — Content/MessageType are what becomes n8n's normalized "messageText" (never the
    /// raw interactive JSON, which only ever goes into MetadataJson for our own internal use); a
    /// caller that wants the stable id instead of the display text can use SelectedValue.</summary>
    private static (string? Content, string? MetadataJson, string? SelectedValue) ExtractMessageContent(JsonElement m, string messageType)
    {
        switch (messageType)
        {
            case "text":
                return (m.TryGetProperty("text", out var text) ? GetString(text, "body") : null, null, null);

            case "interactive":
                if (m.TryGetProperty("interactive", out var interactive))
                {
                    var interactiveType = GetString(interactive, "type");
                    string? title = null;
                    string? replyId = null;
                    if (interactiveType is not null && interactive.TryGetProperty(interactiveType, out var reply))
                    {
                        title = GetString(reply, "title");
                        replyId = GetString(reply, "id");
                    }
                    return (title, interactive.GetRawText(), replyId);
                }
                return (null, null, null);

            case "button":
                // A quick-reply button tap on a template we sent — "payload" is the stable id
                // (set when the template's button was created), "text" is the display label.
                if (m.TryGetProperty("button", out var button))
                {
                    return (GetString(button, "text"), button.GetRawText(), GetString(button, "payload"));
                }
                return (null, null, null);

            case "location":
                if (m.TryGetProperty("location", out var location))
                {
                    return (GetString(location, "name") ?? "[location]", location.GetRawText(), null);
                }
                return ("[location]", null, null);

            case "contacts":
                return ("[contact]", m.TryGetProperty("contacts", out var sharedContacts) ? sharedContacts.GetRawText() : null, null);

            case "system":
                // e.g. "User A changed from 15550001 to 15550002" (QA t10-08: it was stored as an empty bubble).
                return m.TryGetProperty("system", out var systemInfo)
                    ? (GetString(systemInfo, "body"), systemInfo.GetRawText(), null)
                    : (null, null, null);

            case "reaction":
                return m.TryGetProperty("reaction", out var reaction)
                    ? (GetString(reaction, "emoji"), reaction.GetRawText(), null)
                    : (null, null, null);

            case "image": case "video": case "audio": case "document": case "sticker":
                if (m.TryGetProperty(messageType, out var media))
                {
                    return (GetString(media, "caption") ?? $"[{messageType}]", media.GetRawText(), null);
                }
                return ($"[{messageType}]", null, null);

            default:
                return (null, m.GetRawText(), null);
        }
    }

    /// <summary>Meta's click-to-WhatsApp referral on a message, or null.</summary>
    private static MetaAdReferral? Referral(JsonElement m)
    {
        if (!m.TryGetProperty("referral", out var r) || r.ValueKind != JsonValueKind.Object) return null;
        return new MetaAdReferral(GetString(r, "source_type"), GetString(r, "source_id"), GetString(r, "source_url"),
            GetString(r, "headline"), GetString(r, "body"), GetString(r, "ctwa_clid"));
    }

    /// <summary>The message's metadata with Meta's referral added under "referral" (a text message has none of its own).</summary>
    private static string WithReferral(string? metadataJson, JsonElement referral)
    {
        System.Text.Json.Nodes.JsonObject obj;
        try { obj = (metadataJson is null ? null : System.Text.Json.Nodes.JsonNode.Parse(metadataJson) as System.Text.Json.Nodes.JsonObject) ?? new(); }
        catch (JsonException) { obj = new() { ["raw"] = metadataJson }; }
        obj["referral"] = System.Text.Json.Nodes.JsonNode.Parse(referral.GetRawText());
        return obj.ToJsonString();
    }

    /// <summary>Meta's reason "NONE" means no reason (QA t10-06: approved templates showed "NONE").</summary>
    private static string? Reason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || string.Equals(reason, "NONE", StringComparison.OrdinalIgnoreCase) ? null : reason;

    /// <summary>Meta's own words for a rejection (rejection_info) or a pause (other_info), joined into one text.</summary>
    private static string? ReasonDetail(JsonElement value)
    {
        foreach (var (name, first, second) in new[] { ("rejection_info", "reason", "recommendation"), ("other_info", "title", "description") })
        {
            if (!value.TryGetProperty(name, out var info) || info.ValueKind != JsonValueKind.Object) continue;
            var parts = new[] { GetString(info, first), GetString(info, second) }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (parts.Count > 0) return string.Join(" ", parts);
        }
        return null;
    }

    private static ParsedMetaEvent ParseTemplateEvent(string field, string? wabaId, JsonElement value) => new()
    {
        Kind = ParsedMetaEventKind.TemplateStatus,
        WabaId = wabaId,
        MetaTemplateId = GetString(value, "message_template_id"),
        TemplateName = GetString(value, "message_template_name"),
        TemplateLanguage = GetString(value, "message_template_language"),
        TemplateStatus = GetString(value, "event") ?? GetString(value, "status"),
        TemplateReason = Reason(GetString(value, "reason")),
        TemplateReasonDetail = ReasonDetail(value),
        // template_category_update before the change happens: the category it will get and from when (QA t10-06).
        TemplatePendingCategory = GetString(value, "new_category") is null ? GetString(value, "correct_category") : null,
        TemplatePendingCategoryAt = ParseUnixTimestamp(GetString(value, "category_update_timestamp")),
        TemplateQualityRating = GetString(value, "new_quality_score") ?? GetString(value, "quality_score"),
        TemplatePreviousCategory = GetString(value, "previous_category"),
        TemplateCurrentCategory = GetString(value, "new_category"),
        TemplateCategory = GetString(value, "new_category") ?? GetString(value, "message_template_category"),
        TemplateComponentsJson = value.TryGetProperty("message_template_component", out var comp) ? comp.GetRawText() : null,
        RawFieldName = field,
        RawJson = value.GetRawText()
    };

    private static ParsedMetaEvent ParseHealthEvent(string field, string? wabaId, JsonElement value) => new()
    {
        Kind = ParsedMetaEventKind.WhatsAppHealth,
        PhoneNumberId = GetString(value, "phone_number_id")
            ?? (value.TryGetProperty("metadata", out var md) ? GetString(md, "phone_number_id") : null),
        WabaId = wabaId,
        HealthEventType = field,
        HealthStatus = field == "account_update" ? AccountState(value) : GetString(value, "event") ?? GetString(value, "status") ?? GetString(value, "decision"),
        // phone_number_quality_update carries the event (FLAGGED, UNFLAGGED, UPGRADE, DOWNGRADE, ONBOARDING) and
        // current_limit (a messaging TIER such as TIER_10K), never a green/yellow/red rating. The tier is not a quality
        // rating, so only an explicit quality_rating is mapped here; the tier stays in the raw payload.
        HealthQualityRating = GetString(value, "quality_rating"),
        HealthCode = GetString(value, "code"),
        // Never the display phone number: it isn't a message (QA t10-02: "15550100999" showed as the last problem).
        HealthMessage = GetString(value, "message") ?? (field == "account_update" ? AccountDetail(value) : null)
            ?? (field == "phone_number_name_update" ? GetString(value, "rejection_reason") : null),
        RawFieldName = field,
        RawJson = value.GetRawText()
    };

    /// <summary>account_update as one account state (QA t10-02; Meta's documented events): DISABLED_UPDATE by its
    /// ban_info.waba_ban_state (DISABLE → disabled, SCHEDULE_FOR_DISABLE → scheduled_for_disable, REINSTATE → reinstated);
    /// ACCOUNT_RESTRICTION → restricted (unrestricted when the list is empty); ACCOUNT_VIOLATION → violation; ACCOUNT_DELETED,
    /// PARTNER_REMOVED and ACCOUNT_OFFBOARDED → removed. Any other event (verified, partner added...) passes through as itself
    /// and doesn't change the account's state (WhatsAppHealthService.AccountState).</summary>
    private static string? AccountState(JsonElement value)
    {
        var evt = GetString(value, "event") ?? GetString(value, "status");
        switch (evt?.ToUpperInvariant())
        {
            case "DISABLED_UPDATE":
                var ban = value.TryGetProperty("ban_info", out var banInfo) ? GetString(banInfo, "waba_ban_state") : null;
                return ban?.ToUpperInvariant() switch
                {
                    "DISABLE" => "disabled",
                    "SCHEDULE_FOR_DISABLE" => "scheduled_for_disable",
                    "REINSTATE" => "reinstated",
                    _ => "disabled"
                };
            case "ACCOUNT_RESTRICTION":
                return value.TryGetProperty("restriction_info", out var restrictions) && restrictions.ValueKind == JsonValueKind.Array
                       && restrictions.GetArrayLength() > 0 ? "restricted" : "unrestricted";
            case "ACCOUNT_VIOLATION":
                return "violation";
            case "ACCOUNT_DELETED" or "PARTNER_REMOVED" or "ACCOUNT_OFFBOARDED":
                return "removed";
            default:
                return evt;
        }
    }

    /// <summary>The detail behind an account_update, for the technical "last problem" line: the restriction types and until
    /// when, the violation type, or the ban date.</summary>
    private static string? AccountDetail(JsonElement value)
    {
        if (value.TryGetProperty("restriction_info", out var restrictions) && restrictions.ValueKind == JsonValueKind.Array)
        {
            var items = restrictions.EnumerateArray()
                .Select(r => (Type: GetString(r, "restriction_type"), Until: ParseUnixTimestamp(GetString(r, "expiration"))))
                .Where(r => r.Type is not null)
                .Select(r => r.Until is { } until ? $"{r.Type} until {until:yyyy-MM-dd}" : r.Type!)
                .ToList();
            if (items.Count > 0) return string.Join(", ", items);
        }
        if (value.TryGetProperty("violation_info", out var violation) && GetString(violation, "violation_type") is { } type) return type;
        if (value.TryGetProperty("ban_info", out var banInfo) && GetString(banInfo, "waba_ban_date") is { } date) return $"{GetString(banInfo, "waba_ban_state")} {date}".Trim();
        return null;
    }

    /// <summary>Reads a property as a string regardless of whether Meta sent it as a JSON string or
    /// a bare number (template/WABA ids are inconsistently one or the other across Meta API
    /// versions) — returns null if absent or any other JSON kind.</summary>
    private static string? GetString(JsonElement el, string propertyName)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }

    private static DateTimeOffset? ParseUnixTimestamp(string? seconds) =>
        long.TryParse(seconds, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : null;
}
