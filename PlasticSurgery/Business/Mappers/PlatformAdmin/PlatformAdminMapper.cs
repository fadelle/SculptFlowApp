using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Mappers.PlatformAdmin;

/// <summary>Entity → detail rows for the platform-admin API. Nothing secret (tokens, PINs) is ever copied.</summary>
public static class PlatformAdminMapper
{
    public static ClinicDetail ToDetail(Clinic c) => new(
        c.Id, c.Name, c.Slug, c.Phone, c.Email, c.Website, c.CountryCode, c.Timezone, c.Address, c.OperatingHours,
        c.ConsultationInfo, c.IsActive, c.CreatedAt, c.UpdatedAt);

    /// <summary>Expects Clinic to be loaded.</summary>
    public static ChannelDetail ToDetail(ChannelIntegration r, string? infobipWebhookUrl) => new(
        r.Id, r.ClinicId, Ref(r.Clinic), r.Channel, r.Status, r.DisplayName, r.PhoneNumberId, r.WhatsAppBusinessId, r.PageId,
        r.InstagramBusinessId, !string.IsNullOrEmpty(r.AccessToken), r.TelegramBotId, r.TelegramBotUsername, r.WebhookStatus,
        r.WebhookRegisteredAt, r.Provider, r.ProviderSenderId, r.LastVerifiedAt, r.LastError, r.MetaBusinessId, r.VerifiedName,
        r.AccountStatus, r.AccountReviewStatus, r.PhoneQualityRating, r.PhoneStatus, r.NameStatus, r.IsHealthy, r.HealthLevel,
        r.LastProblemCode, r.LastProblemMessage, r.LastWebhookAt, r.LastHealthEventAt, r.CreatedAt, r.UpdatedAt, infobipWebhookUrl);

    public static HealthEventRow ToRow(WhatsAppHealthEvent e) =>
        new(e.Id, e.EventType, e.Severity, e.Status, e.Code, e.Message, e.OccurredAt);

    /// <summary>Expects Clinic and Procedure (when set) to be loaded.</summary>
    public static LeadDetail ToDetail(Lead l) => new(
        l.Id, l.ClinicId, Ref(l.Clinic), l.ProcedureId, l.Procedure is null ? null : new NamedRef(l.Procedure.Id, l.Procedure.Name),
        l.FullName, l.FirstName, l.LastName, l.Phone, l.Email, l.Source, l.SourceDetail, l.CampaignName, l.ExternalLeadId, l.Status,
        l.QualificationStatus, l.PreferredLanguage, l.CountryCode, l.City, l.DesiredTimeline, l.Notes, l.MarketingOptIn, l.OptedOutAt,
        l.LastContactAt, l.NextFollowupAt, l.CreatedAt, l.UpdatedAt);

    public static LeadEventRow ToRow(EventLog e) =>
        new(e.Id, e.LeadId, e.ConversationId, e.AppointmentId, e.EventType, e.Source, e.Metadata, e.CreatedAt);

    /// <summary>Expects Lead to be loaded.</summary>
    public static ConversationDetail ToDetail(Conversation c, DateTimeOffset now) => new(
        c.Id, c.ClinicId, c.LeadId,
        c.Lead is null ? null : new ConversationLead(c.Lead.Id, c.Lead.FullName, c.Lead.FirstName, c.Lead.LastName, c.Lead.Phone),
        c.Channel, c.Status, c.Mode, c.LastMessageAt, c.ServiceWindowExpiresAt, c.IsServiceWindowOpen(now), c.CreatedAt);

    public static MessageDetail ToDetail(Message m) => new(
        m.Id, m.Direction, m.SenderType, m.MessageType, m.Content, m.DeliveryStatus, m.FailedAt, m.FailureCode, m.FailureReason,
        m.Origin, m.CampaignId, m.CreatedAt);

    /// <summary>Expects Clinic to be loaded.</summary>
    public static KnowledgeDocDetail ToDetail(KnowledgeDocument d) => new(
        d.Id, d.ClinicId, Ref(d.Clinic), d.Title, d.Category, d.Content, d.SourceType, d.OriginalFileName, d.MimeType,
        d.FileSizeBytes, d.SourceUrl, d.IsActive, d.UpdatedAt);

    public static SearchSettingsDetail ToDetail(KnowledgeSearchSettings s) => new(
        s.Id, s.ClinicId, s.EmbeddingModel, s.VectorDimension, s.VectorIndexType, s.ChunkSizeTokens, s.ChunkOverlapTokens, s.TopK,
        s.MinimumSimilarity);

    private static NamedRef? Ref(Clinic? c) => c is null ? null : new NamedRef(c.Id, c.Name);
}
