namespace Omni.App.Common.Exceptions;

/// <summary>A member's booking breaks a rule they may knowingly override (QA t5-ops-01): outside opening hours, in the past,
/// inside the notice, beyond the horizon, inside another booking's buffer, or no availability set up. 422 with code
/// <c>booking_rules</c>; the dashboard asks "Book anyway?" and sends the request again with <c>overrideRules</c>.
/// Message is a strings-file key (bookings.rules.*).</summary>
public class BookingRulesException : InvalidOperationException, IErrorCode
{
    public const string ErrorCode = "booking_rules";

    public BookingRulesException(string key) : base(key)
    {
    }

    /// <summary>The SlotCheckReason that was broken.</summary>
    public string Reason { get; init; } = "";

    public string Code => ErrorCode;
}
