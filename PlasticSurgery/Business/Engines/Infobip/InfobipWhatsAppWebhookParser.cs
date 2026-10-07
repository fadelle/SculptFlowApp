using System.Globalization;
using System.Text.Json;
using PlasticSurgery.Business.Providers.WhatsApp;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.WhatsApp;

namespace PlasticSurgery.Business.Engines.Infobip;

/// <summary>
/// Turns one Infobip WhatsApp webhook body into the same ParsedMetaEvent shape MetaWebhookParser produces, so
/// the existing Handlers (CustomerMessageHandler, MessageStatusHandler, UnknownEventHandler) do all the
/// persistence. Infobip posts three kinds of body, all as { "results": [ ... ] } — the connection's single
/// webhook URL accepts any of them, so each result is classified by its own fields:
///
///   has "message"                 -> inbound customer message        (Receive WhatsApp inbound message)
///   has "seenAt"                  -> MessageStatus "read"            (Receive seen reports)
///   has "status" (+ "messageId")  -> MessageStatus sent/delivered/failed (Receive delivery reports)
///
/// Defensive like MetaWebhookParser: unrecognized shapes become Unknown (logged, acknowledged), never throw.
/// </summary>
public static class InfobipWhatsAppWebhookParser
{
    public static IReadOnlyList<ParsedMetaEvent> Parse(JsonElement root)
    {
        var events = new List<ParsedMetaEvent>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            events.Add(Unknown("unrecognized_body", root));
            return events;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (result.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
            {
                events.Add(ParseInbound(result, message));
            }
            else if (result.TryGetProperty("seenAt", out _))
            {
                events.Add(ParseSeen(result));
            }
            else if (result.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
            {
                events.Add(ParseDeliveryReport(result, status));
            }
            else
            {
                events.Add(Unknown("unrecognized_result", result));
            }
        }

        return events;
    }

    private static ParsedMetaEvent ParseInbound(JsonElement result, JsonElement message)
    {
        // contact.phoneNumber can be missing when the customer uses a Meta username and only their
        // business-scoped user id (BSUID) is shared; "from" then may not be a phone number either. Leads are
        // matched by phone today, so such a message can't be attributed yet — it's logged as Unknown instead of
        // being silently dropped (see the BSUID TODO in PROJECT_HANDOFF.md).
        string? phone = null;
        if (result.TryGetProperty("contact", out var contact) && contact.ValueKind == JsonValueKind.Object)
        {
            phone = GetString(contact, "phoneNumber");
        }
        phone ??= GetString(result, "from");
        phone = InfobipNumbers.Normalize(phone);
        if (phone.Length < 6)
        {
            return Unknown("inbound_without_phone", result);
        }

        var infobipType = GetString(message, "type")?.ToUpperInvariant() ?? "UNKNOWN";
        var (messageType, content, selectedValue) = MapMessage(infobipType, message);

        return new ParsedMetaEvent
        {
            Kind = ParsedMetaEventKind.CustomerMessage,
            ProviderSenderId = InfobipNumbers.Normalize(GetString(result, "to")),
            CustomerWaId = phone,
            CustomerName = contact.ValueKind == JsonValueKind.Object ? GetString(contact, "name") : null,
            MessageType = messageType,
            Content = content,
            // Everything but plain text keeps Infobip's message object (media url, location, reply ids...).
            MetadataJson = messageType == "text" ? null : NeutralMetadata(message),
            SelectedValue = selectedValue,
            ExternalMessageId = GetString(result, "messageId"),
            Timestamp = ParseTimestamp(GetString(result, "receivedAt")),
            RawJson = result.GetRawText()
        };
    }

    /// <summary>Maps Infobip's upper-case message types onto the lower-case types MetaWebhookParser uses, so
    /// MessageService's AI-eligibility rule ("text"/"interactive") and the Inbox treat both providers alike.</summary>
    private static (string MessageType, string? Content, string? SelectedValue) MapMessage(string infobipType, JsonElement message)
    {
        switch (infobipType)
        {
            case "TEXT":
                return ("text", GetString(message, "text"), null);

            case "INTERACTIVE_BUTTON_REPLY":
            case "INTERACTIVE_LIST_REPLY":
                // title = what the customer saw and tapped, id = our stable reply id.
                return ("interactive", GetString(message, "title"), GetString(message, "id"));

            case "BUTTON":
                // Quick-reply tap on a template: text = label, payload = stable id.
                return ("button", GetString(message, "text"), GetString(message, "payload"));

            case "IMAGE": case "DOCUMENT": case "VIDEO": case "STICKER":
            {
                var type = infobipType.ToLowerInvariant();
                return (type, GetString(message, "caption") ?? $"[{type}]", null);
            }

            case "AUDIO": case "VOICE":
                return ("audio", "[audio]", null);

            case "LOCATION":
                return ("location", GetString(message, "name") ?? GetString(message, "address") ?? "[location]", null);

            case "CONTACT":
                return ("contacts", "[contact]", null);

            default:
                // INFECTED_CONTENT, REACTION, ORDER, anything new: kept in the Inbox, never AI-eligible.
                return (infobipType.ToLowerInvariant(), GetString(message, "text") ?? $"[{infobipType.ToLowerInvariant()}]", null);
        }
    }

    private static ParsedMetaEvent ParseSeen(JsonElement result) => new()
    {
        Kind = ParsedMetaEventKind.MessageStatus,
        ProviderSenderId = InfobipNumbers.Normalize(GetString(result, "to")),
        ExternalMessageId = GetString(result, "messageId"),
        DeliveryStatus = "read",
        Timestamp = ParseTimestamp(GetString(result, "seenAt")),
        RawJson = result.GetRawText()
    };

    /// <summary>Infobip status groups -> our Message.DeliveryStatus vocabulary (sent/delivered/read/failed).</summary>
    private static ParsedMetaEvent ParseDeliveryReport(JsonElement result, JsonElement status)
    {
        var group = GetString(status, "groupName")?.ToUpperInvariant();
        var deliveryStatus = group switch
        {
            "DELIVERED" => "delivered",
            "PENDING" or "ACCEPTED" => "sent",
            "UNDELIVERABLE" or "EXPIRED" or "REJECTED" => "failed",
            _ => null
        };
        if (deliveryStatus is null)
        {
            return Unknown("unknown_status_group", result);
        }

        string? failureCode = null, failureReason = null;
        if (deliveryStatus == "failed")
        {
            var hasError = result.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                           && GetString(error, "name") is { } errorName && errorName != "NO_ERROR";
            failureCode = hasError ? GetString(error, "name") : GetString(status, "name");
            failureReason = hasError ? GetString(error, "description") : GetString(status, "description");
            // Failure reasons are shown in the Inbox and on campaign pages — never let the provider's name through.
            if (failureReason is not null && failureReason.Contains("infobip", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "The message couldn't be delivered.";
            }
        }

        return new ParsedMetaEvent
        {
            Kind = ParsedMetaEventKind.MessageStatus,
            ExternalMessageId = GetString(result, "messageId"),
            DeliveryStatus = deliveryStatus,
            FailureCode = failureCode,
            FailureReason = failureReason,
            Timestamp = ParseTimestamp(GetString(result, "doneAt")) ?? ParseTimestamp(GetString(result, "sentAt")),
            RawJson = result.GetRawText()
        };
    }

    /// <summary>
    /// One "Receive WhatsApp template update" webhook body (account-level, not per sender):
    ///   { messageTemplateId, messageTemplateName, messageTemplateLanguage, timestamp,
    ///     change: { type: TEMPLATE_STATUS_UPDATE | TEMPLATE_CATEGORY_UPDATE | TEMPLATE_QUALITY_UPDATE, ... } }
    /// Returns null when the body isn't one.
    /// </summary>
    public static ParsedMetaEvent? ParseTemplateUpdate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || GetString(root, "messageTemplateId") is not { } templateId
            || !root.TryGetProperty("change", out var change) || change.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var changeType = GetString(change, "type")?.ToUpperInvariant();
        string? status = null, reason = null, previousCategory = null, newCategory = null, quality = null;
        switch (changeType)
        {
            case "TEMPLATE_STATUS_UPDATE":
                status = InfobipWhatsAppTemplateProvider.ToWhatsAppStatus(GetString(change, "newStatus"));
                reason = GetString(change, "reason");
                if (string.Equals(reason, "NONE", StringComparison.OrdinalIgnoreCase)) reason = null;
                break;
            case "TEMPLATE_CATEGORY_UPDATE":
                previousCategory = GetString(change, "previousCategory")?.ToLowerInvariant();
                newCategory = GetString(change, "newCategory")?.ToLowerInvariant();
                break;
            case "TEMPLATE_QUALITY_UPDATE":
                quality = (GetString(change, "newQuality") ?? GetString(change, "newQualityScore") ?? GetString(change, "quality"))?.ToLowerInvariant();
                break;
        }

        return new ParsedMetaEvent
        {
            Kind = ParsedMetaEventKind.TemplateStatus,
            MetaTemplateId = templateId,
            TemplateName = GetString(root, "messageTemplateName"),
            TemplateLanguage = GetString(root, "messageTemplateLanguage"),
            TemplateStatus = status,
            TemplateReason = reason,
            TemplateCategory = newCategory,
            TemplatePreviousCategory = previousCategory,
            TemplateCurrentCategory = newCategory,
            TemplateQualityRating = quality,
            Timestamp = ParseTimestamp(GetString(root, "timestamp")),
            RawFieldName = changeType?.ToLowerInvariant() ?? "template_update",
            RawJson = root.GetRawText()
        };
    }

    /// <summary>The message object for Message.MetadataJson, which the Inbox receives. Infobip's media "url" points
    /// at the Infobip API host, so it's replaced by just the media id (the last path segment) — enough to download
    /// the file later through our own server, without the browser ever seeing the provider's domain.</summary>
    private static string NeutralMetadata(JsonElement message)
    {
        var metadata = new Dictionary<string, JsonElement>();
        foreach (var property in message.EnumerateObject())
        {
            if (property.NameEquals("url") && property.Value.ValueKind == JsonValueKind.String
                && Uri.TryCreate(property.Value.GetString(), UriKind.Absolute, out var url)
                && (url.Host.Contains("infobip", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.Contains("/media/")))
            {
                metadata["mediaId"] = JsonSerializer.SerializeToElement(url.Segments[^1].Trim('/'));
                continue;
            }
            metadata[property.Name] = property.Value;
        }
        return JsonSerializer.Serialize(metadata);
    }

    private static ParsedMetaEvent Unknown(string reason, JsonElement raw) => new()
    {
        Kind = ParsedMetaEventKind.Unknown,
        RawFieldName = reason,
        RawJson = raw.GetRawText()
    };

    /// <summary>Infobip timestamps look like "2025-01-01T10:10:00.000+0000" — an offset without a colon, which
    /// DateTimeOffset.Parse doesn't accept, so the colon is put back first.</summary>
    internal static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > 5 && (v[^5] == '+' || v[^5] == '-') && v[^4..].All(char.IsDigit))
        {
            v = v[..^2] + ":" + v[^2..];
        }
        return DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
