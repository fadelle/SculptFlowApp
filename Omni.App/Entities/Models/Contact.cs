using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

public class Contact
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ServiceId { get; set; }

    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public string? Source { get; set; }
    public string? SourceDetail { get; set; }
    public string? CampaignName { get; set; }
    public string? ExternalContactId { get; set; }

    /// <summary>A stage key of the client's business type (crm.pipeline_stages); new contacts start at the type's first
    /// open stage (IBusinessTypeService.GetPipelineAsync). Code reads the stage's category, never a fixed key.</summary>
    public string StageKey { get; set; } = string.Empty;
    public string Qualification { get; set; } = ContactQualification.Unknown;

    public string? PreferredLanguage { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? DesiredTimeline { get; set; }
    public string? Notes { get; set; }

    public string? AssignedUserId { get; set; }

    public bool MarketingOptIn { get; set; } = true;
    public DateTimeOffset? OptedOutAt { get; set; }

    public DateTimeOffset? LastContactAt { get; set; }
    public DateTimeOffset? NextFollowupAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Client? Client { get; set; }
    public Service? Service { get; set; }
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
