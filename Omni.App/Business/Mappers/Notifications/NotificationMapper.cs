using Microsoft.Extensions.Localization;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Notifications;

namespace Omni.App.Business.Mappers.Notifications;

public static class NotificationMapper
{
    /// <summary>The notification in the reader's language: Title/Message are keys with TextArgs (a row from before keys
    /// holds plain text, which the localizer returns as written).</summary>
    public static NotificationResponse ToResponse(Notification n, IStringLocalizer text)
    {
        var args = n.TextArgs.Select(TextArg.Decode).ToArray();
        return new(n.Id, n.Type, text[n.Title, args].Value, n.Message is null ? null : text[n.Message, args].Value,
            n.ContactId, n.ConversationId, n.BookingId, n.Link, n.IsRead, n.CreatedAt);
    }
}
