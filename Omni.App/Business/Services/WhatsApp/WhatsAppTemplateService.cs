using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.WhatsApp;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Campaigns;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.WhatsApp;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.WhatsApp;

public class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWhatsAppTemplateProvider _provider;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;
    private readonly INotificationService _notifications;
    private readonly ICampaignRepository _campaigns;

    public WhatsAppTemplateService(
        IWhatsAppTemplateRepository templates, IChannelIntegrationRepository integrations, IUnitOfWork unitOfWork, IWhatsAppTemplateProvider provider,
        IInboxNotifier notifier, IEventLogger events, INotificationService notifications, ICampaignRepository campaigns)
    {
        _campaigns = campaigns;
        _notifications = notifications;
        _templates = templates;
        _integrations = integrations;
        _unitOfWork = unitOfWork;
        _provider = provider;
        _notifier = notifier;
        _events = events;
    }

    /// <summary>The client's WhatsApp row, but only when it's connected and has what template management needs.</summary>
    private async Task<ChannelIntegration?> ReadyIntegrationAsync(Guid clientId, CancellationToken ct)
    {
        var integration = await _integrations.GetAsync(clientId, ChannelType.WhatsApp, ct);
        return integration is not null && integration.Status == ChannelIntegrationStatus.Connected && _provider.IsReady(integration)
            ? integration
            : null;
    }

    public async Task<IReadOnlyList<WhatsAppTemplateResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var templates = await _templates.ListForClientReadOnlyAsync(clientId, ct);
        return templates.Select(ToResponse).ToList();
    }

    public async Task<WhatsAppTemplateResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetReadOnlyAsync(clientId, id, ct);
        return template is null ? null : ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse> CreateAsync(CreateWhatsAppTemplateRequest request, CancellationToken ct = default)
    {
        ValidateContent(request.Category, request.Body, request.HeaderType, request.HeaderContent, request.Footer);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("templates.errors.nameBodyRequired");
        }
        // Meta only accepts lower-case letters, digits and underscores; catch it here rather than at submission. At most 200, the
        // column's length (QA t8-13: Meta's 512 was allowed and a 300-character name was a 500).
        if (request.Name.Length > NameMaxLength || !request.Name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
        {
            throw new ArgumentException("templates.errors.nameInvalid");
        }
        // Everything is checked before the row is saved, so a bad value leaves no draft behind (QA t8-13: bad buttons did).
        if (!string.IsNullOrWhiteSpace(request.Language) && !IsLanguageCode(request.Language))
        {
            throw new ArgumentException(LanguageInvalidKey).WithArgs(request.Language);
        }
        ValidateButtons(request.ButtonsJson);
        RequestChecks.Json(request.VariablesJson, "variablesJson");

        var now = DateTimeOffset.UtcNow;
        var template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Name = request.Name,
            Category = request.Category,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en_US" : request.Language,
            Status = WhatsAppTemplateStatus.Draft,
            HeaderType = string.IsNullOrWhiteSpace(request.HeaderType) ? null : request.HeaderType,
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
        var (_, notSubmitted) = await SubmitForReviewAsync(template, ct);
        return ToResponse(template) with { SubmitError = notSubmitted };
    }

    // Meta's limits for a template (QA t8-13: a 2,000-character body was accepted and then refused by Meta).
    public const int NameMaxLength = 200;
    public const int BodyMaxLength = 1024;
    public const int HeaderTextMaxLength = 60;
    public const int FooterMaxLength = 60;
    public const int MaxButtons = 10;
    public const int ButtonTextMaxLength = 25;

    public const string LanguageInvalidKey = "templates.errors.languageInvalid";
    public const string HeaderTypeInvalidKey = "templates.errors.headerTypeInvalid";
    public const string ButtonsInvalidKey = "templates.errors.buttonsInvalid";

    /// <summary>A language code shaped like Meta's: two or three lower-case letters, optionally "_" and a two-letter region (en, fil,
    /// en_US, pt_BR). Meta itself refuses a well-formed code it doesn't support, with a readable reason.</summary>
    internal static bool IsLanguageCode(string code) =>
        System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2,3}(_[A-Z]{2})?$");

    /// <summary>The fields both create and edit set, checked against Meta's rules and the columns (400 naming what's wrong).</summary>
    private static void ValidateContent(string? category, string? body, string? headerType, string? headerContent, string? footer)
    {
        if (category is null || !WhatsAppTemplateCategory.All.Contains(category))
        {
            throw new ArgumentException("templates.errors.invalidCategory").WithArgs(category ?? string.Empty);
        }
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("templates.errors.nameBodyRequired");
        }
        RequestChecks.MaxLength(body, BodyMaxLength, "body");
        if (!string.IsNullOrWhiteSpace(headerType) && !WhatsAppTemplateHeaderType.All.Contains(headerType))
        {
            throw new ArgumentException(HeaderTypeInvalidKey).WithArgs(headerType);
        }
        if (headerType == WhatsAppTemplateHeaderType.Text) RequestChecks.MaxLength(headerContent, HeaderTextMaxLength, "headerContent");
        RequestChecks.MaxLength(footer, FooterMaxLength, "footer");
    }

    /// <summary>Buttons are Meta's list: a JSON array of at most 10 objects, each with a "type" (and a "text" of at most 25 characters when it has one)
    /// (QA t8-13: "{not json}" was a 500, and {"a":1} was saved and then broke the submission).</summary>
    internal static void ValidateButtons(string? buttonsJson)
    {
        if (string.IsNullOrWhiteSpace(buttonsJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(buttonsJson);
            var root = doc.RootElement;
            var ok = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() is > 0 and <= MaxButtons
                     && root.EnumerateArray().All(b => b.ValueKind == JsonValueKind.Object
                         && b.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(type.GetString())
                         // A copy-code or one-time-password button has no text; the others have a short one.
                         && (!b.TryGetProperty("text", out var text)
                             || (text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())
                                 && text.GetString()!.Length <= ButtonTextMaxLength)));
            if (!ok) throw new ArgumentException(ButtonsInvalidKey).WithArgs(MaxButtons, ButtonTextMaxLength);
        }
        catch (JsonException)
        {
            throw new ArgumentException(ButtonsInvalidKey).WithArgs(MaxButtons, ButtonTextMaxLength);
        }
    }

    /// <summary>Statuses whose content may change (QA t8-17): never at Meta, refused by Meta (or a failed submission), paused.</summary>
    private static readonly HashSet<string> EditableStatuses =
        [WhatsAppTemplateStatus.Draft, WhatsAppTemplateStatus.Rejected, WhatsAppTemplateStatus.Paused];

    public async Task<WhatsAppTemplateResponse?> UpdateAsync(Guid clientId, Guid id, UpdateWhatsAppTemplateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = await _templates.GetAsync(clientId, id, ct);
        if (template is null) return null;
        if (!EditableStatuses.Contains(template.Status))
        {
            throw new InvalidOperationException("templates.errors.cantEdit").WithArgs(TextArg.Value(template.Status));
        }
        ValidateContent(request.Category, request.Body, request.HeaderType, request.HeaderContent, request.Footer);

        var now = DateTimeOffset.UtcNow;
        template.Category = request.Category;
        template.Body = request.Body;
        template.HeaderType = string.IsNullOrWhiteSpace(request.HeaderType) || request.HeaderType == WhatsAppTemplateHeaderType.None ? null : request.HeaderType;
        template.HeaderContent = template.HeaderType == WhatsAppTemplateHeaderType.Text ? request.HeaderContent : null;
        template.Footer = string.IsNullOrWhiteSpace(request.Footer) ? null : request.Footer;
        template.UpdatedAt = now;

        if (string.IsNullOrEmpty(template.MetaTemplateId))
        {
            // Meta never accepted it: submit the new content as a new template (a draft again without WhatsApp).
            template.Status = WhatsAppTemplateStatus.Draft;
            template.RejectionReason = null;
            await _unitOfWork.SaveChangesAsync(ct);
            var (_, notSubmitted) = await SubmitForReviewAsync(template, ct);
            return ToResponse(template) with { SubmitError = notSubmitted };
        }

        var integration = await ReadyIntegrationAsync(clientId, ct);
        if (integration is null)
        {
            _unitOfWork.DiscardChanges();
            throw new InvalidOperationException("templates.errors.connectFirst");
        }
        try
        {
            await _provider.EditAsync(integration, template, ct);
        }
        catch (WhatsAppTemplateProviderException ex)
        {
            _unitOfWork.DiscardChanges(); // Meta kept the old content, so the row does too
            throw new InvalidOperationException("templates.errors.editRefused").WithArgs(ex.Message);
        }
        template.Status = WhatsAppTemplateStatus.Pending;
        template.RejectionReason = null;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(template);
    }

    public async Task<bool> DeleteAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clientId, id, ct);
        if (template is null) return false;
        // A campaign keeps its template (its page shows what was sent): a template in use can't go.
        if (await _campaigns.FindNameUsingTemplateAsync(clientId, id, ct) is { } campaign)
        {
            throw new InvalidOperationException("templates.errors.usedByCampaign").WithArgs(template.Name, campaign);
        }
        if (!string.IsNullOrEmpty(template.MetaTemplateId))
        {
            var integration = await ReadyIntegrationAsync(clientId, ct)
                ?? throw new InvalidOperationException("templates.errors.connectFirst");
            try
            {
                await _provider.DeleteAsync(integration, template, ct);
            }
            catch (WhatsAppTemplateProviderException ex)
            {
                throw new InvalidOperationException("templates.errors.deleteRefused").WithArgs(ex.Message);
            }
        }
        var previousStatus = template.Status;
        _templates.Remove(template);
        _events.Log(clientId, EventTypes.WhatsAppTemplateStatusChanged, source: "dashboard",
            metadataJson: JsonSerializer.Serialize(new { templateId = id, previousStatus, newStatus = WhatsAppTemplateStatus.Deleted }));
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<WhatsAppTemplateResponse?> RetrySubmitAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clientId, id, ct);
        if (template is null) return null;
        if (!string.IsNullOrEmpty(template.MetaTemplateId))
        {
            throw new InvalidOperationException("templates.errors.alreadyAtMeta");
        }

        var (attempted, notSubmitted) = await SubmitForReviewAsync(template, ct);
        if (!attempted)
        {
            throw new InvalidOperationException("templates.errors.connectFirst");
        }
        return ToResponse(template) with { SubmitError = notSubmitted };
    }

    /// <summary>Sends a template that has no MetaTemplateId yet to Meta for review. Attempted is false (and it stays a draft)
    /// when the client has no connected WhatsApp number. Meta refusing the template is kept on the row as Rejected +
    /// RejectionReason (Meta's readable explanation) so the user doesn't lose their work and can fix it. Meta not reachable, or the
    /// WhatsApp connection needing renewal (QA t8-12: an invalid token marked it "Rejected" with raw JSON), keeps it a draft and
    /// returns why as NotSubmitted; "Submit" sends it again once WhatsApp is reconnected.</summary>
    private async Task<(bool Attempted, string? NotSubmitted)> SubmitForReviewAsync(WhatsAppTemplate template, CancellationToken ct)
    {
        var integration = await ReadyIntegrationAsync(template.ClientId, ct);
        if (integration is null)
        {
            return (false, null);
        }

        try
        {
            var result = await _provider.SubmitAsync(integration, template, ct);

            template.MetaTemplateId = result.ProviderTemplateId;
            template.Status = MapMetaStatus(result.Status);
            template.RejectionReason = null;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (WhatsAppTemplateProviderException ex) when (ex.CouldNotReach)
        {
            template.Status = WhatsAppTemplateStatus.Draft;
            template.RejectionReason = null;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            return (true, ex.Message);
        }
        catch (WhatsAppTemplateProviderException ex)
        {
            template.Status = WhatsAppTemplateStatus.Rejected;
            template.RejectionReason = ex.Message;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return (true, null);
    }

    public async Task<WhatsAppTemplateResponse?> SyncStatusAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var template = await _templates.GetAsync(clientId, id, ct);
        if (template is null) return null;
        if (string.IsNullOrEmpty(template.MetaTemplateId))
        {
            // Never successfully submitted — nothing to sync yet.
            return ToResponse(template);
        }

        var integration = await ReadyIntegrationAsync(clientId, ct)
            ?? throw new InvalidOperationException("templates.errors.noNumber");

        var result = await _provider.GetStatusAsync(integration, template, ct);
        template.Status = MapMetaStatus(result.Status);
        // Meta may report a rejection without repeating the reason; don't wipe one we already have.
        template.RejectionReason = result.RejectedReason ?? (template.Status == WhatsAppTemplateStatus.Rejected ? template.RejectionReason : null);
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse?> ApplyMetaEventAsync(WhatsAppTemplateEventRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required (used as the upsert fallback key when MetaTemplateId is absent).", nameof(request)); // text-ok: n8n sync contract
        }
        // Checked against their columns before anything is stored (QA t8-06: a 40-character status was a 500).
        RequestChecks.MaxLength(request.Name, 200, "name");
        RequestChecks.MaxLength(request.MetaTemplateId, 200, "metaTemplateId");
        RequestChecks.MaxLength(request.Language, 10, "language");
        RequestChecks.MaxLength(request.Category, 30, "category");
        RequestChecks.MaxLength(request.Status, 20, "status");
        RequestChecks.MaxLength(request.QualityRating, 30, "qualityRating");
        RequestChecks.MaxLength(request.PreviousCategory, 30, "previousCategory");
        RequestChecks.MaxLength(request.CurrentCategory, 30, "currentCategory");
        RequestChecks.Json(request.ComponentsJson, "componentsJson");

        // Don't trust ClientId blindly — if the event carries a WabaId, it must match this
        // client's own stored WhatsApp connection, or a misrouted/misconfigured n8n workflow could
        // write another client's template data under this client's id.
        ChannelIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(request.WabaId))
        {
            integration = await _integrations.GetAsync(request.ClientId, ChannelType.WhatsApp, ct);
            if (integration is not null && !string.Equals(integration.WhatsAppBusinessId, request.WabaId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"WabaId '{request.WabaId}' does not match client {request.ClientId}'s stored WhatsApp connection."); // text-ok: n8n sync contract
            }
        }
        integration ??= await _integrations.GetAsync(request.ClientId, ChannelType.WhatsApp, ct);

        var template = !string.IsNullOrWhiteSpace(request.MetaTemplateId)
            ? await _templates.FindByProviderTemplateIdAsync(request.ClientId, request.MetaTemplateId, ct)
            : null;

        template ??= await _templates.FindByNameAsync(request.ClientId, request.Name, request.Language ?? "en_US", ct);

        var now = DateTimeOffset.UtcNow;
        var previousStatus = template?.Status;
        var previousCategory = template is null ? null : (string.IsNullOrWhiteSpace(template.CurrentCategory) ? template.Category : template.CurrentCategory);

        // A quality or category event alone says nothing worth a row: an unknown template stays unknown (QA t10-06: an empty
        // "draft" row appeared for a quality update).
        if (template is null && (string.IsNullOrWhiteSpace(request.Status) || IsQualityOrCategoryEvent(request.EventType)))
        {
            return null;
        }

        if (template is null)
        {
            // Meta knows about a template we don't have a local row for yet (created directly in
            // Business Manager, or our own create-template call never completed) — create a
            // minimal row from what the event tells us rather than dropping the event.
            template = new WhatsAppTemplate
            {
                Id = Guid.NewGuid(),
                ClientId = request.ClientId,
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
        // The reason belongs to the status it came with (QA t10-06): an approved template has none, Meta's "NONE" is none, and a
        // new status without a reason clears an old one. A rejection keeps Meta's code and its own explanation.
        var reason = TemplateReasons.Compose(request.Reason, request.ReasonDetail);
        if (template.Status == WhatsAppTemplateStatus.Approved) template.RejectionReason = null;
        else if (reason is not null) template.RejectionReason = reason;
        else if (!string.Equals(previousStatus, template.Status, StringComparison.Ordinal)) template.RejectionReason = null;
        template.ChannelIntegrationId = integration?.Id ?? template.ChannelIntegrationId;
        template.LastMetaEventAt = request.OccurredAt ?? now;
        template.UpdatedAt = now;

        _events.Log(request.ClientId, EventTypes.WhatsAppTemplateStatusChanged, source: "n8n",
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                templateId = template.Id, eventType = request.EventType,
                previousStatus, newStatus = template.Status
            }));

        await _unitOfWork.SaveChangesAsync(ct);

        await _notifier.WhatsAppTemplateUpdatedAsync(
            request.ClientId, template.Id, template.Name, template.Status, template.RejectionReason, now, ct);

        // The owner hears about what changes their sending (QA t8-15): approved, rejected, paused, disabled or flagged
        // (only on the change, not on a repeated event), and a new category (it changes the price).
        if (StatusNotification(previousStatus, template) is { } statusText)
        {
            await _notifications.CreateAsync(request.ClientId, NotificationType.TemplateStatusChanged, statusText,
                channelIntegrationId: template.ChannelIntegrationId, link: TemplatesLink, ct: ct);
        }
        // Meta announces a category change before it happens: the owner hears what will change and when (QA t10-06).
        if (!string.IsNullOrWhiteSpace(request.PendingCategory))
        {
            await _notifications.CreateAsync(request.ClientId, NotificationType.TemplateStatusChanged,
                NotificationText.Of("notifications.template.categorySoon.title", "notifications.template.categorySoon.message",
                    template.Name, TextArg.Value(request.PendingCategory.ToLowerInvariant()),
                    (request.PendingCategoryAt ?? now).UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                channelIntegrationId: template.ChannelIntegrationId, link: TemplatesLink, ct: ct);
        }
        var newCategory = request.CurrentCategory?.ToLowerInvariant();
        if (previousCategory is not null && !string.IsNullOrWhiteSpace(newCategory)
            && !string.Equals(previousCategory, newCategory, StringComparison.OrdinalIgnoreCase))
        {
            await _notifications.CreateAsync(request.ClientId, NotificationType.TemplateStatusChanged,
                NotificationText.Of("notifications.template.category.title", "notifications.template.category.message",
                    template.Name, TextArg.Value(previousCategory.ToLowerInvariant()), TextArg.Value(newCategory)),
                channelIntegrationId: template.ChannelIntegrationId, link: TemplatesLink, ct: ct);
        }

        return ToResponse(template);
    }

    private const string TemplatesLink = "/WhatsApp/Templates";

    private static bool IsQualityOrCategoryEvent(string? eventType) =>
        eventType is not null && (eventType.Contains("quality", StringComparison.OrdinalIgnoreCase) || eventType.Contains("category", StringComparison.OrdinalIgnoreCase));

    /// <summary>The notification for a status change the owner should hear about, or null (no change, or one that
    /// doesn't affect sending such as pending or in appeal).</summary>
    private static NotificationText? StatusNotification(string? previousStatus, WhatsAppTemplate template)
    {
        if (string.Equals(previousStatus, template.Status, StringComparison.Ordinal)) return null;
        return template.Status switch
        {
            WhatsAppTemplateStatus.Approved => NotificationText.Of("notifications.template.approved.title", "notifications.template.approved.message", template.Name),
            WhatsAppTemplateStatus.Rejected => string.IsNullOrWhiteSpace(template.RejectionReason)
                ? NotificationText.Of("notifications.template.rejected.title", "notifications.template.rejected.message", template.Name)
                : NotificationText.Of("notifications.template.rejected.title", "notifications.template.rejected.messageReason", template.Name, TemplateReasons.Arg(template.RejectionReason)),
            WhatsAppTemplateStatus.Paused => NotificationText.Of("notifications.template.paused.title", "notifications.template.paused.message", template.Name),
            WhatsAppTemplateStatus.Disabled => NotificationText.Of("notifications.template.disabled.title", "notifications.template.disabled.message", template.Name),
            WhatsAppTemplateStatus.Flagged => NotificationText.Of("notifications.template.flagged.title", "notifications.template.flagged.message", template.Name),
            _ => null
        };
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
        "DELETED" or "PENDING_DELETION" => WhatsAppTemplateStatus.Deleted,
        // Back in use after a pause or an archive (QA t10-06: "reinstated" showed as a problem and couldn't be sent).
        "REINSTATED" or "UNARCHIVED" => WhatsAppTemplateStatus.Approved,
        _ => metaStatus.ToLowerInvariant()
    };

    private static WhatsAppTemplateResponse ToResponse(WhatsAppTemplate t) => new(
        t.Id, t.ClientId, t.MetaTemplateId, t.Name, t.Category, t.Language, t.Status,
        t.HeaderType, t.HeaderContent, t.Body, t.Footer, t.ButtonsJson, t.VariablesJson,
        t.RejectionReason, t.CreatedAt, t.UpdatedAt,
        t.ChannelIntegrationId, t.QualityRating, t.PreviousCategory, t.CurrentCategory,
        t.ComponentsJson, t.LastMetaEventAt
    );
}
