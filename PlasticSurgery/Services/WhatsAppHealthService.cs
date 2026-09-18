using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class WhatsAppHealthService : IWhatsAppHealthService
{
    private readonly ApplicationDbContext _db;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;

    public WhatsAppHealthService(ApplicationDbContext db, IInboxNotifier notifier, IEventLogger events)
    {
        _db = db;
        _notifier = notifier;
        _events = events;
    }

    public async Task<WhatsAppHealthResponse?> GetHealthAsync(Guid clinicId, CancellationToken ct = default)
    {
        var integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.WhatsApp, ct);
        return integration is null ? null : ToHealthResponse(integration);
    }

    public async Task<IReadOnlyList<WhatsAppHealthEventResponse>> GetHealthEventsAsync(
        Guid clinicId, int skip, int take, CancellationToken ct = default)
    {
        var events = await _db.WhatsAppHealthEvents
            .Where(h => h.ClinicId == clinicId)
            .OrderByDescending(h => h.OccurredAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        return events.Select(h => new WhatsAppHealthEventResponse(
            h.Id, h.EventType, h.Severity, h.Status, h.Code, h.Message, h.OccurredAt)).ToList();
    }

    public async Task<WhatsAppHealthResponse> ApplyHealthEventAsync(WhatsAppHealthEventRequest request, CancellationToken ct = default)
    {
        var integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == request.ClinicId && c.Channel == ChannelType.WhatsApp, ct);
        if (integration is null)
        {
            throw new InvalidOperationException(
                $"Clinic {request.ClinicId} has no WhatsApp connection on file — connect one under Settings → Channels & Integrations first.");
        }

        // Don't trust ClinicId blindly — cross-check whichever identifier the event carries
        // against this clinic's own stored connection.
        if (!string.IsNullOrWhiteSpace(request.WabaId)
            && !string.Equals(integration.WhatsAppBusinessId, request.WabaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"WabaId '{request.WabaId}' does not match clinic {request.ClinicId}'s stored WhatsApp connection.");
        }
        if (!string.IsNullOrWhiteSpace(request.PhoneNumberId)
            && !string.Equals(integration.PhoneNumberId, request.PhoneNumberId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PhoneNumberId '{request.PhoneNumberId}' does not match clinic {request.ClinicId}'s stored WhatsApp connection.");
        }

        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;

        // Idempotency: a retried webhook delivery for the exact same (connection, event type,
        // occurred_at) is a no-op — the first delivery already applied this state and recorded the
        // history row (also backstopped by the DB unique index in case of a race).
        var alreadyRecorded = await _db.WhatsAppHealthEvents.AnyAsync(h =>
            h.ChannelIntegrationId == integration.Id && h.EventType == request.EventType && h.OccurredAt == occurredAt, ct);
        if (alreadyRecorded)
        {
            return ToHealthResponse(integration);
        }

        switch (request.EventType)
        {
            case WhatsAppHealthEventType.PhoneNumberQualityUpdate:
                if (request.QualityRating is not null) integration.PhoneQualityRating = request.QualityRating;
                if (request.Status is not null) integration.PhoneStatus = request.Status;
                break;
            case WhatsAppHealthEventType.PhoneNumberNameUpdate:
                if (request.Status is not null) integration.NameStatus = request.Status;
                break;
            case WhatsAppHealthEventType.AccountUpdate:
                if (request.Status is not null) integration.AccountStatus = request.Status;
                break;
            case WhatsAppHealthEventType.AccountReviewUpdate:
                if (request.Status is not null) integration.AccountReviewStatus = request.Status;
                break;
            case WhatsAppHealthEventType.ConnectionUpdate:
                if (request.Message is not null) integration.LastError = request.Message;
                break;
            default:
                // template_problem, webhook_problem, unknown_meta_health_event — recorded in
                // history below; nothing on the connection itself maps cleanly to these.
                break;
        }

        var (isHealthy, healthLevel, problemCode, problemMessage) = RecomputeHealth(integration);
        integration.IsHealthy = isHealthy;
        integration.HealthLevel = healthLevel;
        integration.LastProblemCode = problemCode ?? request.Code;
        integration.LastProblemMessage = problemMessage ?? request.Message;
        integration.LastWebhookAt = DateTimeOffset.UtcNow;
        integration.LastHealthEventAt = occurredAt;
        integration.UpdatedAt = DateTimeOffset.UtcNow;

        var severity = healthLevel switch
        {
            WhatsAppHealthLevel.Problem => WhatsAppHealthEventSeverity.Error,
            WhatsAppHealthLevel.Disconnected => WhatsAppHealthEventSeverity.Critical,
            WhatsAppHealthLevel.Warning => WhatsAppHealthEventSeverity.Warning,
            _ => WhatsAppHealthEventSeverity.Info
        };

        _db.WhatsAppHealthEvents.Add(new WhatsAppHealthEvent
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            ChannelIntegrationId = integration.Id,
            EventType = request.EventType,
            Severity = severity,
            Status = request.Status,
            Code = request.Code,
            Message = request.Message,
            RawMetadataJson = request.RawMetadataJson,
            OccurredAt = occurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        });

        _events.Log(request.ClinicId, EventTypes.WhatsAppHealthEventReceived, source: "n8n",
            metadataJson: JsonSerializer.Serialize(new { request.EventType, healthLevel }));

        await _db.SaveChangesAsync(ct);

        await _notifier.WhatsAppHealthUpdatedAsync(
            request.ClinicId, integration.Id, healthLevel, integration.AccountStatus, integration.AccountReviewStatus,
            integration.PhoneQualityRating, integration.PhoneStatus, integration.LastProblemMessage,
            integration.UpdatedAt, ct);

        return ToHealthResponse(integration);
    }

    /// <summary>Derives a simplified, staff-facing health state from the raw fields Meta's webhooks
    /// populate. Tolerant by design: an unrecognized status value doesn't count as a problem, it
    /// just doesn't trip any of the known-bad checks below — matches "normalize carefully while
    /// keeping raw metadata" (the raw value is still stored, just not force-mapped to a severity).</summary>
    private static (bool IsHealthy, string HealthLevel, string? ProblemCode, string? ProblemMessage) RecomputeHealth(ChannelIntegration c)
    {
        if (c.Status != ChannelIntegrationStatus.Connected)
        {
            return (false, WhatsAppHealthLevel.Disconnected, null, "WhatsApp is not connected.");
        }

        if (c.AccountStatus is not null && BadAccountStatuses.Contains(c.AccountStatus.ToLowerInvariant()))
        {
            return (false, WhatsAppHealthLevel.Problem, c.AccountStatus, $"WhatsApp account status is '{c.AccountStatus}'.");
        }

        if (string.Equals(c.AccountReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return (false, WhatsAppHealthLevel.Problem, c.AccountReviewStatus, "WhatsApp account review was rejected.");
        }

        if (string.Equals(c.PhoneQualityRating, "red", StringComparison.OrdinalIgnoreCase))
        {
            return (false, WhatsAppHealthLevel.Problem, c.PhoneQualityRating,
                "Phone number quality rating is red — Meta may restrict sending.");
        }

        if (string.Equals(c.PhoneQualityRating, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            return (true, WhatsAppHealthLevel.Warning, c.PhoneQualityRating, "Phone number quality rating dropped to yellow.");
        }

        if (c.PhoneStatus is not null && BadPhoneStatuses.Contains(c.PhoneStatus.ToLowerInvariant()))
        {
            return (true, WhatsAppHealthLevel.Warning, c.PhoneStatus, $"Phone number status is '{c.PhoneStatus}'.");
        }

        return (true, WhatsAppHealthLevel.Healthy, null, null);
    }

    private static readonly HashSet<string> BadAccountStatuses = new() { "disabled", "banned", "restricted" };
    private static readonly HashSet<string> BadPhoneStatuses = new() { "flagged", "restricted", "banned" };

    private static WhatsAppHealthResponse ToHealthResponse(ChannelIntegration c) => new(
        c.Id, c.ClinicId, c.Status == ChannelIntegrationStatus.Connected,
        c.HealthLevel ?? WhatsAppHealthLevel.Unknown,
        c.DisplayName, c.VerifiedName, c.WhatsAppBusinessId,
        c.AccountStatus, c.AccountReviewStatus, c.PhoneQualityRating, c.PhoneStatus, c.NameStatus,
        c.LastProblemCode, c.LastProblemMessage, c.LastWebhookAt, c.LastHealthEventAt, c.UpdatedAt
    );
}
