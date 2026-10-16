using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

public class Order
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid ContactId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? BookingId { get; set; }

    public string Status { get; set; } = OrderStatus.Considering;
    public decimal? QuotedAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTimeOffset? ServiceDate { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Contact? Contact { get; set; }
    public Service? Service { get; set; }
    public Booking? Booking { get; set; }
}
