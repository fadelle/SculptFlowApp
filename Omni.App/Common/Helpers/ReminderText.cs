namespace Omni.App.Common.Helpers;

/// <summary>Label keys of booking reminder statuses and reasons: reminders.status.{status} and
/// reminders.reason.{reason in camelCase} (Guards/UiTextKeysTests checks every ReminderStatus and ReminderReason value has one).</summary>
public static class ReminderText
{
    public const string StatusKeyPrefix = "reminders.status.";
    public const string ReasonKeyPrefix = "reminders.reason.";

    public static string StatusKey(string status) => StatusKeyPrefix + FilterLabelHelper.KeyPart(status);

    public static string ReasonKey(string reason) => ReasonKeyPrefix + FilterLabelHelper.KeyPart(reason);
}
