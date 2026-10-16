namespace Omni.App.Common.Enums;

public static class OrderStatus
{
    public const string Considering = "considering";
    public const string Quoted = "quoted";
    public const string DepositPaid = "deposit_paid";
    public const string Confirmed = "confirmed";
    public const string Completed = "completed";
    public const string Canceled = "canceled";
    public const string Lost = "lost";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Considering, Quoted, DepositPaid, Confirmed, Completed, Canceled, Lost
    };
}
