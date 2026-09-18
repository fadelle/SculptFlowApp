using System.Text.Json;
using PlasticSurgery.Integrations.WhatsApp.Models;

namespace PlasticSurgery.Integrations.WhatsApp;

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
    public static IReadOnlyList<ParsedMetaEvent> Parse(JsonElement root)
    {
        var events = new List<ParsedMetaEvent>();
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            var wabaId = GetString(entry, "id");
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                var field = GetString(change, "field") ?? "";
                if (!change.TryGetProperty("value", out var value))
                {
                    continue;
                }

                events.AddRange(ParseChange(field, wabaId, value));
            }
        }

        return events;
    }

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
                // Staff replying from the WhatsApp Business App (Coexistence) — Meta delivers this
                // on its own dedicated field with the same message shape as "messages", not mixed
                // into "messages" itself. Every entry here is unconditionally an echo.
                foreach (var e in ParseMessageEntries(wabaId, value, ParsedMetaEventKind.BusinessAppEcho)) yield return e;
                break;

            case "message_template_status_update":
            case "message_template_quality_update":
            case "message_template_category_update":
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

    /// <summary>Shared by "messages" (customer case) and "smb_message_echoes" (staff/Coexistence
    /// case) — both carry the same value.metadata/contacts[]/messages[] shape; only which Kind the
    /// caller wants differs. Meta's "contacts[]" identifies the OTHER party in the thread (the
    /// customer) regardless of which field/direction this particular payload represents.</summary>
    private static IEnumerable<ParsedMetaEvent> ParseMessageEntries(string? wabaId, JsonElement value, ParsedMetaEventKind kind)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        var phoneNumberId = value.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "phone_number_id") : null;

        string? customerWaId = null;
        string? customerName = null;
        if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0)
        {
            var contact = contacts[0];
            customerWaId = GetString(contact, "wa_id");
            if (contact.TryGetProperty("profile", out var profile))
            {
                customerName = GetString(profile, "name");
            }
        }

        foreach (var m in messages.EnumerateArray())
        {
            var from = GetString(m, "from");
            var messageType = GetString(m, "type") ?? "text";
            var (content, metadataJson, selectedValue) = ExtractMessageContent(m, messageType);

            yield return new ParsedMetaEvent
            {
                Kind = kind,
                PhoneNumberId = phoneNumberId,
                WabaId = wabaId,
                CustomerWaId = customerWaId ?? from,
                CustomerName = customerName,
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

    private static ParsedMetaEvent ParseTemplateEvent(string field, string? wabaId, JsonElement value) => new()
    {
        Kind = ParsedMetaEventKind.TemplateStatus,
        WabaId = wabaId,
        MetaTemplateId = GetString(value, "message_template_id"),
        TemplateName = GetString(value, "message_template_name"),
        TemplateLanguage = GetString(value, "message_template_language"),
        TemplateStatus = GetString(value, "event") ?? GetString(value, "status"),
        TemplateReason = GetString(value, "reason"),
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
        HealthStatus = GetString(value, "event") ?? GetString(value, "status") ?? GetString(value, "decision"),
        HealthQualityRating = GetString(value, "current_limit") ?? GetString(value, "quality_rating"),
        HealthCode = GetString(value, "code"),
        HealthMessage = GetString(value, "message") ?? GetString(value, "display_phone_number"),
        RawFieldName = field,
        RawJson = value.GetRawText()
    };

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
