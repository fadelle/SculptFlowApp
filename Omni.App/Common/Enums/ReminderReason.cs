namespace Omni.App.Common.Enums;

/// <summary>Why a booking reminder was skipped or failed (scheduling.booking_reminders.reason). Each has a label
/// reminders.reason.{value in camelCase} in the strings files.</summary>
public static class ReminderReason
{
    /// <summary>The booking was made after this reminder's time had passed.</summary>
    public const string MadeInsideWindow = "made_inside_window";
    /// <summary>The later reminder was already due, so this earlier one wasn't sent late.</summary>
    public const string Superseded = "superseded";
    public const string OptedOut = "opted_out";
    /// <summary>No WhatsApp or Telegram conversation and no phone number to start one.</summary>
    public const string NoConversation = "no_conversation";
    public const string ChannelUnsupported = "channel_unsupported";
    /// <summary>No WhatsApp template is chosen for any language.</summary>
    public const string NoTemplate = "no_template";
    public const string TemplateNotApproved = "template_not_approved";
    /// <summary>The template needs more than the three values reminders fill (name, date and time, business).</summary>
    public const string TemplateParameters = "template_parameters";
    /// <summary>Neither included credit nor wallet covers the message.</summary>
    public const string NoBalance = "no_balance";
    /// <summary>The channel or provider refused the message.</summary>
    public const string SendFailed = "send_failed";

    public static readonly IReadOnlyList<string> All =
        [MadeInsideWindow, Superseded, OptedOut, NoConversation, ChannelUnsupported, NoTemplate, TemplateNotApproved, TemplateParameters, NoBalance, SendFailed];
}
