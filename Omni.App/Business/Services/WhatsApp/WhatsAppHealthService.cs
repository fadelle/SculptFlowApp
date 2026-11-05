using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.WhatsApp;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Services.WhatsApp;

public class WhatsAppHealthService : IWhatsAppHealthService
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IWhatsAppHealthEventRepository _healthEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;
    private readonly INotificationService _notifications;

    public WhatsAppHealthService(IChannelIntegrationRepository integrations, IWhatsAppHealthEventRepository healthEvents, IUnitOfWork unitOfWork, IInboxNotifier notifier, IEventLogger events, INotificationService notifications)
    {
        _integrations = integrations;
        _healthEvents = healthEvents;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _events = events;
        _notifications = notifications;
    }

    /// <summary>The "requires attention" health levels — INTEGRATION_UNHEALTHY fires only on the transition INTO
    /// one of these, never while it stays here (see ApplyHealthEventAsync).</summary>
    private static bool IsUnhealthy(string level) => level is WhatsAppHealthLevel.Problem or WhatsAppHealthLevel.Disconnected;

    public async Task<WhatsAppHealthResponse?> GetHealthAsync(Guid clientId, CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(clientId, ChannelType.WhatsApp, ct);
        return integration is null ? null : ToHealthResponse(integration);
    }

    public async Task<IReadOnlyList<WhatsAppHealthEventResponse>> GetHealthEventsAsync(
        Guid clientId, int skip, int take, CancellationToken ct = default)
    {
        var events = await _healthEvents.ListForClientAsync(clientId, skip, take, ct);

        return events.Select(h => new WhatsAppHealthEventResponse(
            h.Id, h.EventType, h.Severity, h.Status, h.Code, h.Message, h.OccurredAt)).ToList();
    }

    public async Task<WhatsAppHealthResponse> ApplyHealthEventAsync(WhatsAppHealthEventRequest request, CancellationToken ct = default)
    {
        var integration = await _integrations.GetAsync(request.ClientId, ChannelType.WhatsApp, ct);
        if (integration is null)
        {
            throw new InvalidOperationException(
                $"Client {request.ClientId} has no WhatsApp connection on file — connect one under Settings → Messaging Integrations first."); // text-ok: n8n ingest contract
        }

        // Don't trust ClientId blindly — cross-check whichever identifier the event carries
        // against this client's own stored connection.
        if (!string.IsNullOrWhiteSpace(request.WabaId)
            && !string.Equals(integration.WhatsAppBusinessId, request.WabaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"WabaId '{request.WabaId}' does not match client {request.ClientId}'s stored WhatsApp connection."); // text-ok: n8n ingest contract
        }
        if (!string.IsNullOrWhiteSpace(request.PhoneNumberId)
            && !string.Equals(integration.PhoneNumberId, request.PhoneNumberId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PhoneNumberId '{request.PhoneNumberId}' does not match client {request.ClientId}'s stored WhatsApp connection."); // text-ok: n8n ingest contract
        }

        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;

        // Idempotency: a retried webhook delivery for the exact same (connection, event type,
        // occurred_at) is a no-op — the first delivery already applied this state and recorded the
        // history row (also backstopped by the DB unique index in case of a race).
        var alreadyRecorded = await _healthEvents.ExistsAsync(integration.Id, request.EventType, occurredAt, ct);
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
                // Only a change of the account's standing moves it; "verified", "partner added" and the like leave a ban or
                // restriction in place (QA t10-02).
                if (AccountState(request.Status) is { } state) integration.AccountStatus = state;
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

        var previousLevel = integration.HealthLevel ?? WhatsAppHealthLevel.Unknown;
        var (isHealthy, healthLevel, problemCode, problemMessage) = RecomputeHealth(integration);
        integration.IsHealthy = isHealthy;
        integration.HealthLevel = healthLevel;
        // A healthy connection has no "last problem" (QA t10-02: UNFLAGGED left a phone number there).
        integration.LastProblemCode = healthLevel == WhatsAppHealthLevel.Healthy ? null : problemCode ?? request.Code;
        integration.LastProblemMessage = healthLevel == WhatsAppHealthLevel.Healthy ? null : JoinMessage(problemMessage, request.Message);
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

        _healthEvents.Add(new WhatsAppHealthEvent
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
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

        _events.Log(request.ClientId, EventTypes.WhatsAppHealthEventReceived, source: "n8n",
            metadataJson: JsonSerializer.Serialize(new { request.EventType, healthLevel }));

        await _unitOfWork.SaveChangesAsync(ct);

        await _notifier.WhatsAppHealthUpdatedAsync(
            request.ClientId, integration.Id, healthLevel, integration.AccountStatus, integration.AccountReviewStatus,
            integration.PhoneQualityRating, integration.PhoneStatus, integration.LastProblemMessage,
            integration.UpdatedAt, ct);

        // Only on the transition INTO an unhealthy state — staying unhealthy across repeated webhooks must not
        // re-notify every time (see IsUnhealthy's doc comment).
        if (IsUnhealthy(healthLevel) && !IsUnhealthy(previousLevel))
        {
            await _notifications.CreateAsync(request.ClientId, NotificationType.IntegrationUnhealthy,
                NotificationText.Of("notifications.whatsApp.title", "notifications.problem.text",
                    integration.LastProblemMessage ?? (object)TextArg.Of("notifications.whatsApp.problem")),
                channelIntegrationId: integration.Id, link: "/WhatsApp/Health", ct: ct);
        }

        return ToHealthResponse(integration);
    }

    /// <summary>Sets the connection's health from its current state (connected or not, account, phone, name): what
    /// ApplyHealthEventAsync does after an event, for a connect or disconnect (QA t10-11: a reconnected number stayed
    /// "disconnected" until Meta's next health event). Changes only the row; the caller saves it.</summary>
    public static void Refresh(ChannelIntegration integration)
    {
        var (isHealthy, healthLevel, problemCode, problemMessage) = RecomputeHealth(integration);
        integration.IsHealthy = isHealthy;
        integration.HealthLevel = healthLevel;
        integration.LastProblemCode = healthLevel == WhatsAppHealthLevel.Healthy ? null : problemCode;
        integration.LastProblemMessage = healthLevel == WhatsAppHealthLevel.Healthy ? null : problemMessage;
    }

    /// <summary>Derives a simplified, client-facing health state from the raw fields Meta's webhooks
    /// populate. Tolerant by design: an unrecognized status value doesn't count as a problem, it
    /// just doesn't trip any of the known-bad checks below — matches "normalize carefully while
    /// keeping raw metadata" (the raw value is still stored, just not force-mapped to a severity).</summary>
    private static (bool IsHealthy, string HealthLevel, string? ProblemCode, string? ProblemMessage) RecomputeHealth(ChannelIntegration c)
    {
        if (c.Status != ChannelIntegrationStatus.Connected)
        {
            return (false, WhatsAppHealthLevel.Disconnected, null, "WhatsApp is not connected.");
        }

        var account = c.AccountStatus?.ToLowerInvariant();
        if (account == WhatsAppAccountState.Removed)
        {
            return (false, WhatsAppHealthLevel.Disconnected, c.AccountStatus, "Meta removed this WhatsApp account from the app; nothing can be sent or received.");
        }

        if (account is not null && BadAccountStatuses.Contains(account))
        {
            return (false, WhatsAppHealthLevel.Problem, c.AccountStatus, account switch
            {
                WhatsAppAccountState.Restricted => "Meta restricted this WhatsApp account; some messages can't be sent.",
                _ => "Meta disabled this WhatsApp account; messages can't be sent."
            });
        }

        if (string.Equals(c.AccountReviewStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return (false, WhatsAppHealthLevel.Problem, c.AccountReviewStatus, "WhatsApp account review was rejected.");
        }

        // A banned or restricted NUMBER blocks or limits sending like an account ban does (QA t10-02).
        if (c.PhoneStatus?.ToLowerInvariant() is "banned" or "restricted")
        {
            return (false, WhatsAppHealthLevel.Problem, c.PhoneStatus, $"Meta {c.PhoneStatus.ToLowerInvariant()} this phone number; messages may not be sent.");
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

        if (account is WhatsAppAccountState.ScheduledForDisable or WhatsAppAccountState.Violation)
        {
            return (true, WhatsAppHealthLevel.Warning, c.AccountStatus, account == WhatsAppAccountState.Violation
                ? "Meta reported a policy violation on this WhatsApp account."
                : "Meta will disable this WhatsApp account unless the issue is fixed.");
        }

        if (string.Equals(c.NameStatus, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            return (true, WhatsAppHealthLevel.Warning, c.NameStatus, "Meta rejected this number's display name.");
        }

        if (c.PhoneStatus is not null && BadPhoneStatuses.Contains(c.PhoneStatus.ToLowerInvariant()))
        {
            var message = c.PhoneStatus.ToLowerInvariant() switch
            {
                "flagged" => "Meta flagged this phone number because of its message quality; sending may be limited.",
                "downgrade" => "Meta lowered this phone number's daily messaging limit because of its message quality.",
                _ => $"Phone number status is '{c.PhoneStatus}'."
            };
            return (true, WhatsAppHealthLevel.Warning, c.PhoneStatus, message);
        }

        return (true, WhatsAppHealthLevel.Healthy, null, null);
    }

    private static readonly HashSet<string> BadAccountStatuses = new() { WhatsAppAccountState.Disabled, WhatsAppAccountState.Banned, WhatsAppAccountState.Restricted };

    /// <summary>The account state an account_update sets, or null for an event that says nothing about the account's standing
    /// (verified, partner added, country update...). Accepts the parser's states and n8n's older raw values.</summary>
    public static string? AccountState(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "disabled" or "disable" => WhatsAppAccountState.Disabled,
        "banned" => WhatsAppAccountState.Banned,
        "restricted" or "account_restriction" => WhatsAppAccountState.Restricted,
        "scheduled_for_disable" or "schedule_for_disable" => WhatsAppAccountState.ScheduledForDisable,
        "violation" or "account_violation" => WhatsAppAccountState.Violation,
        "removed" or "account_deleted" or "partner_removed" or "account_offboarded" => WhatsAppAccountState.Removed,
        "reinstated" or "reinstate" or "unrestricted" or "active" => WhatsAppAccountState.Active,
        _ => null
    };

    /// <summary>The problem in our words, with Meta's detail after it when it adds something.</summary>
    private static string? JoinMessage(string? ours, string? meta) =>
        ours is null ? meta : string.IsNullOrWhiteSpace(meta) || ours.Contains(meta, StringComparison.OrdinalIgnoreCase) ? ours : $"{ours} ({meta})";
    private static readonly HashSet<string> BadPhoneStatuses = new() { "flagged", "downgrade", "restricted", "banned" };

    private static WhatsAppHealthResponse ToHealthResponse(ChannelIntegration c) => new(
        c.Id, c.ClientId, c.Status == ChannelIntegrationStatus.Connected,
        c.HealthLevel ?? WhatsAppHealthLevel.Unknown,
        c.DisplayName, c.VerifiedName, c.WhatsAppBusinessId,
        c.AccountStatus, c.AccountReviewStatus, c.PhoneQualityRating, c.PhoneStatus, c.NameStatus,
        c.LastProblemCode, c.LastProblemMessage, c.LastWebhookAt, c.LastHealthEventAt, c.UpdatedAt
    );
}
