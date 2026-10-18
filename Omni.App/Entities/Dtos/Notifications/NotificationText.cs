namespace Omni.App.Entities.Dtos.Notifications;

/// <summary>
/// What a notification says, as strings-file keys and their arguments (one argument list for both keys, so the title
/// and the message can share {0}…). Stored as keys, so each viewer reads it in their own language and business-type
/// words when the bell lists it. An argument may be a <see cref="Ui.TextArg"/> (a fallback word such as "A contact")
/// or a <c>[[time:…]]</c> marker that notifications.js shows in the viewer's timezone.
/// </summary>
public sealed record NotificationText(string TitleKey, string? MessageKey, IReadOnlyList<object?> Args)
{
    public static NotificationText Of(string titleKey, string? messageKey, params object?[] args) => new(titleKey, messageKey, args);
}
