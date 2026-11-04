namespace Omni.App.Entities.Models;

public class Service
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public int? BookingDuration { get; set; }
    /// <summary>Optional price with its ISO currency (both or neither, QA t5-ops-18).</summary>
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Client? Client { get; set; }
}
