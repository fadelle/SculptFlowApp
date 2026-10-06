using System.Text.Json;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Providers.WhatsApp;
using PlasticSurgery.Business.Contracts.Services.WhatsApp;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.WhatsApp;
using PlasticSurgery.Entities.Responses.WhatsApp;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Channels;
using PlasticSurgery.Persistence.Contracts.WhatsApp;

namespace PlasticSurgery.Business.Services.WhatsApp;

public class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEnumerable<IWhatsAppTemplateProvider> _providers;
    private readonly IConfiguration _configuration;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;

    public WhatsAppTemplateService(
        IWhatsAppTemplateRepository templates, IChannelIntegrationRepository integrations, IUnitOfWork unitOfWork, IEnumerable<IWhatsAppTemplateProvider> providers, IConfiguration configuration,
        IInboxNotifier notifier, IEventLogger events)
    {
        _templates = templates;
        _integrations = integrations;
        _unitOfWork = unitOfWork;
        _providers = providers;
        _configuration = configuration;
        _notifier = notifier;
        _events = events;
    }

    /// <summary>The template provider for the active WhatsApp:Provider (see IWhatsAppTemplateProvider).</summary>
    private IWhatsAppTemplateProvider ActiveProvider()
    {
        var name = WhatsAppService.ActiveProviderName(_configuration);
        return _providers.FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException("WhatsApp templates aren't available right now.");
    }

    /// <summary>The clinic's WhatsApp row, but only when it's connected through the active provider and has what
    /// that provider needs — a row from the other provider counts as "no connected number".</summary>
    private async Task<ChannelIntegration?> ReadyIntegrationAsync(Guid clinicId, IWhatsAppTemplateProvider provider, CancellationToken ct)
    {
        var integration = await _integrations.GetAsync(clinicId, ChannelType.WhatsApp, ct);
        return integration is not null && integration.Status == ChannelIntegrationStatus.Connected
               && ChannelProvider.Of(integration) == provider.Name && provider.IsReady(integration)
            ? integration
            : null;
    }

    public async Task<IReadOnlyList<WhatsAppTemplateResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var templates = await _templates.ListForClinicAsync(clinicId, ct);
        return templates.Select(ToResponse).ToList();
    }

    public async Task<WhatsAppTemplateResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clinicId, id, ct);
        return template is null ? null : ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse> CreateAsync(CreateWhatsAppTemplateRequest request, CancellationToken ct = default)
    {
        if (!WhatsAppTemplateCategory.All.Contains(request.Category))
        {
            throw new ArgumentException($"Invalid category '{request.Category}'.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ArgumentException("Name and Body are required.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            Name = request.Name,
            Category = request.Category,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en_US" : request.Language,
            Status = WhatsAppTemplateStatus.Draft,
            HeaderType = request.HeaderType,
            HeaderContent = request.HeaderContent,
            Body = request.Body,
            Footer = request.Footer,
            ButtonsJson = request.ButtonsJson,
            VariablesJson = request.VariablesJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        _templates.Add(template);
        await _unitOfWork.SaveChangesAsync(ct);

        // No connected WhatsApp number yet: it stays a local draft, and "Retry submit" sends it later.
        await SubmitForReviewAsync(template, ct);
        return ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse?> RetrySubmitAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clinicId, id, ct);
        if (template is null) return null;
        if (!string.IsNullOrEmpty(template.MetaTemplateId))
        {
            throw new InvalidOperationException("This template already reached Meta. Use Sync to refresh its status, or create a new template to change it.");
        }

        var submitted = await SubmitForReviewAsync(template, ct);
        if (!submitted)
        {
            throw new InvalidOperationException("Connect your WhatsApp number (Settings → Integrations) before sending templates to Meta.");
        }
        return ToResponse(template);
    }

    /// <summary>Sends a template that has no MetaTemplateId yet for WhatsApp review through the active provider.
    /// Returns false (and leaves it a draft) when the clinic has no WhatsApp number connected through that provider.
    /// A provider refusal is kept on the row as Rejected + RejectionReason so the user doesn't lose their work and
    /// can retry.</summary>
    private async Task<bool> SubmitForReviewAsync(WhatsAppTemplate template, CancellationToken ct)
    {
        var provider = ActiveProvider();
        var integration = await ReadyIntegrationAsync(template.ClinicId, provider, ct);
        if (integration is null)
        {
            return false;
        }

        try
        {
            var result = await provider.SubmitAsync(integration, template, ct);

            template.MetaTemplateId = result.ProviderTemplateId;
            template.Status = MapMetaStatus(result.Status);
            template.RejectionReason = null;
            // Null for Meta keeps Meta rows exactly as they were before providers existed.
            template.Provider = provider.Name == ChannelProvider.Meta ? null : provider.Name;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (WhatsAppTemplateProviderException ex)
        {
            template.Status = WhatsAppTemplateStatus.Rejected;
            template.RejectionReason = ex.Message;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<WhatsAppTemplateResponse?> SyncStatusAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clinicId, id, ct);
        if (template is null) return null;
        if (string.IsNullOrEmpty(template.MetaTemplateId))
        {
            // Never successfully submitted — nothing to sync yet.
            return ToResponse(template);
        }

        var provider = ActiveProvider();
        if (TemplateProviderOf(template) != provider.Name)
        {
            throw new InvalidOperationException(
                "This template was approved for your previous WhatsApp setup and can't be used now. Create it again to send it for review.");
        }

        var integration = await ReadyIntegrationAsync(clinicId, provider, ct)
            ?? throw new InvalidOperationException("This clinic doesn't have a connected WhatsApp number to sync against.");

        var result = await provider.GetStatusAsync(integration, template, ct);
        template.Status = MapMetaStatus(result.Status);
        // Some providers only report a reason through their webhook; don't wipe one we already have.
        template.RejectionReason = result.RejectedReason ?? (template.Status == WhatsAppTemplateStatus.Rejected ? template.RejectionReason : null);
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(template);
    }

    /// <summary>The provider a template was submitted through; null (every pre-provider row) means Meta.</summary>
    public static string TemplateProviderOf(WhatsAppTemplate template) =>
        string.IsNullOrWhiteSpace(template.Provider) ? ChannelProvider.Meta : template.Provider;

    public async Task<WhatsAppTemplateResponse> ApplyMetaEventAsync(WhatsAppTemplateEventRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required (used as the upsert fallback key when MetaTemplateId is absent).", nameof(request));
        }

        // Don't trust ClinicId blindly — if the event carries a WabaId, it must match this
        // clinic's own stored WhatsApp connection, or a misrouted/misconfigured n8n workflow could
        // write another clinic's template data under this clinic's id.
        ChannelIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(request.WabaId))
        {
            integration = await _integrations.GetAsync(request.ClinicId, ChannelType.WhatsApp, ct);
            if (integration is not null && !string.Equals(integration.WhatsAppBusinessId, request.WabaId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"WabaId '{request.WabaId}' does not match clinic {request.ClinicId}'s stored WhatsApp connection.");
            }
        }
        integration ??= await _integrations.GetAsync(request.ClinicId, ChannelType.WhatsApp, ct);

        var template = !string.IsNullOrWhiteSpace(request.MetaTemplateId)
            ? await _templates.FindByProviderTemplateIdAsync(request.ClinicId, request.MetaTemplateId, ct)
            : null;

        template ??= await _templates.FindByNameAsync(request.ClinicId, request.Name, request.Language ?? "en_US", ct);

        var now = DateTimeOffset.UtcNow;
        var previousStatus = template?.Status;

        if (template is null)
        {
            // Meta knows about a template we don't have a local row for yet (created directly in
            // Business Manager, or our own create-template call never completed) — create a
            // minimal row from what the event tells us rather than dropping the event.
            template = new WhatsAppTemplate
            {
                Id = Guid.NewGuid(),
                ClinicId = request.ClinicId,
                Name = request.Name,
                Language = request.Language ?? "en_US",
                Category = (request.Category ?? WhatsAppTemplateCategory.Utility).ToLowerInvariant(),
                Body = string.Empty,
                CreatedAt = now
            };
            _templates.Add(template);
        }

        if (!string.IsNullOrWhiteSpace(request.MetaTemplateId)) template.MetaTemplateId = request.MetaTemplateId;
        if (!string.IsNullOrWhiteSpace(request.Status)) template.Status = MapMetaStatus(request.Status);
        if (!string.IsNullOrWhiteSpace(request.Category)) template.Category = request.Category.ToLowerInvariant();
        if (request.PreviousCategory is not null) template.PreviousCategory = request.PreviousCategory;
        if (request.CurrentCategory is not null) template.CurrentCategory = request.CurrentCategory;
        if (request.QualityRating is not null) template.QualityRating = request.QualityRating;
        if (request.ComponentsJson is not null) template.ComponentsJson = request.ComponentsJson;
        if (request.Reason is not null) template.RejectionReason = request.Reason;
        template.ChannelIntegrationId = integration?.Id ?? template.ChannelIntegrationId;
        template.LastMetaEventAt = request.OccurredAt ?? now;
        template.UpdatedAt = now;

        _events.Log(request.ClinicId, EventTypes.WhatsAppTemplateStatusChanged, source: "n8n",
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                templateId = template.Id, eventType = request.EventType,
                previousStatus, newStatus = template.Status
            }));

        await _unitOfWork.SaveChangesAsync(ct);

        await _notifier.WhatsAppTemplateUpdatedAsync(
            request.ClinicId, template.Id, template.Name, template.Status, template.RejectionReason, now, ct);

        return ToResponse(template);
    }

    /// <summary>Normalizes Meta's known statuses to our lowercase constants; anything unrecognized
    /// passes through lowercased rather than being collapsed to "pending" — tolerating a status
    /// Meta introduces later (e.g. "in_appeal") is the explicit requirement here, not a bug.</summary>
    internal static string MapMetaStatus(string metaStatus) => metaStatus.ToUpperInvariant() switch
    {
        "PENDING" => WhatsAppTemplateStatus.Pending,
        "APPROVED" => WhatsAppTemplateStatus.Approved,
        "REJECTED" => WhatsAppTemplateStatus.Rejected,
        "PAUSED" => WhatsAppTemplateStatus.Paused,
        "DISABLED" => WhatsAppTemplateStatus.Disabled,
        "FLAGGED" => WhatsAppTemplateStatus.Flagged,
        "DELETED" => WhatsAppTemplateStatus.Deleted,
        _ => metaStatus.ToLowerInvariant()
    };

    private static WhatsAppTemplateResponse ToResponse(WhatsAppTemplate t) => new(
        t.Id, t.ClinicId, t.MetaTemplateId, t.Name, t.Category, t.Language, t.Status,
        t.HeaderType, t.HeaderContent, t.Body, t.Footer, t.ButtonsJson, t.VariablesJson,
        t.RejectionReason, t.CreatedAt, t.UpdatedAt,
        t.ChannelIntegrationId, t.QualityRating, t.PreviousCategory, t.CurrentCategory,
        t.ComponentsJson, t.LastMetaEventAt, t.Provider
    );
}
