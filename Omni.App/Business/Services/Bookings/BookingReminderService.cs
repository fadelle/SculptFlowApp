using System.Text.Json;
using System.Text.RegularExpressions;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Bookings;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Contracts.WhatsApp;

namespace Omni.App.Business.Services.Bookings;

public partial class BookingReminderService : IBookingReminderService
{
    public const int MaxHours = 168;
    /// <summary>The values a reminder template may use, in order: {{1}} name, {{2}} date and time, {{3}} business.</summary>
    public const int TemplateValues = 3;

    private readonly IBookingReminderRepository _reminders;
    private readonly IContactRepository _contacts;
    private readonly IConversationRepository _conversations;
    private readonly IConversationService _conversationService;
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly IClientRepository _clients;
    private readonly IEntitlementService _entitlements;
    private readonly IMessageService _messages;
    private readonly IReaderTextFactory _readerText;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;
    private readonly ILogger<BookingReminderService> _logger;

    public BookingReminderService(IBookingReminderRepository reminders, IContactRepository contacts, IConversationRepository conversations,
        IConversationService conversationService, IWhatsAppTemplateRepository templates, IClientRepository clients, IEntitlementService entitlements,
        IMessageService messages, IReaderTextFactory readerText, IUnitOfWork unitOfWork, IConfigManager config, TimeProvider time,
        ILogger<BookingReminderService> logger)
    {
        _reminders = reminders;
        _contacts = contacts;
        _conversations = conversations;
        _conversationService = conversationService;
        _templates = templates;
        _clients = clients;
        _entitlements = entitlements;
        _messages = messages;
        _readerText = readerText;
        _unitOfWork = unitOfWork;
        _config = config;
        _time = time;
        _logger = logger;
    }

    public async Task<ReminderSettingsDto> GetSettingsAsync(Guid clientId, CancellationToken ct = default) =>
        ToDto(await _reminders.GetSettingsReadOnlyAsync(clientId, ct));

    public async Task SaveSettingsAsync(Guid clientId, SaveReminderSettingsRequest request, CancellationToken ct = default)
    {
        if (request.FirstHoursBefore is < 0 or > MaxHours || request.SecondHoursBefore is < 0 or > MaxHours)
            throw new ArgumentException("reminders.errors.hours").WithArgs(MaxHours);
        var templates = new Dictionary<string, Guid>();
        foreach (var (language, templateId) in request.Templates)
        {
            if (templateId is not { } id) continue;
            var code = (language ?? string.Empty).Trim().ToLowerInvariant();
            if (!AppLanguage.All.Contains(code) || await _templates.GetReadOnlyAsync(clientId, id, ct) is null)
                throw new ArgumentException("reminders.errors.template");
            templates[code] = id;
        }

        var now = _time.GetUtcNow();
        var settings = await _reminders.GetSettingsAsync(clientId, ct);
        if (settings is null)
        {
            settings = new ReminderSettings { Id = Guid.NewGuid(), ClientId = clientId, CreatedAt = now };
            _reminders.AddSettings(settings);
        }
        settings.IsEnabled = request.Enabled;
        settings.FirstHoursBefore = request.FirstHoursBefore;
        settings.SecondHoursBefore = request.SecondHoursBefore;
        settings.TemplatesJson = JsonSerializer.Serialize(templates);
        settings.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReminderTemplateOption>> ListTemplateOptionsAsync(Guid clientId, CancellationToken ct = default) =>
        (await _templates.ListForClientReadOnlyAsync(clientId, ct))
            .Where(t => t.Status == WhatsAppTemplateStatus.Approved)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new ReminderTemplateOption(t.Id, t.Name, t.Language))
            .ToList();

    public Task<IReadOnlyList<BookingReminderRow>> ListForBookingAsync(Guid clientId, Guid bookingId, CancellationToken ct = default) =>
        _reminders.ListForBookingAsync(clientId, bookingId, ct);

    public async Task<IReadOnlyList<Guid>> ListClientsWithRemindersAsync(CancellationToken ct = default) =>
        _config.RemindersEnabled ? await _reminders.ListEnabledClientIdsAsync(ct) : [];

    public async Task<int> SendDueAsync(Guid clientId, CancellationToken ct = default)
    {
        if (!_config.RemindersEnabled) return 0;
        var settings = ToDto(await _reminders.GetSettingsReadOnlyAsync(clientId, ct));
        if (!settings.Enabled || !(await _entitlements.GetAsync(clientId, ct)).CanUseBookings) return 0;
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        if (client is null || !client.IsActive) return 0;

        var written = 0;
        // The nearer reminder first, so a late earlier reminder is skipped as superseded instead of sent right before it.
        var kinds = new[] { (Kind: ReminderKind.Second, Hours: settings.SecondHoursBefore), (Kind: ReminderKind.First, Hours: settings.FirstHoursBefore) }
            .Where(k => k.Hours > 0).OrderBy(k => k.Hours).ToList();
        foreach (var (kind, hours) in kinds)
        {
            var now = _time.GetUtcNow();
            foreach (var due in await _reminders.ListDueAsync(clientId, kind, hours, now, _config.RemindersBatchSize, ct))
            {
                ct.ThrowIfCancellationRequested();
                var nearer = kinds.FirstOrDefault(k => k.Hours < hours);
                string? skip = due.BookingCreatedAt > due.ScheduledStart.AddHours(-hours) ? ReminderReason.MadeInsideWindow
                    : nearer.Kind is not null && due.ScheduledStart <= now.AddHours(nearer.Hours) ? ReminderReason.Superseded
                    : null;
                if (await HandleAsync(client, settings, due, kind, skip, ct)) written++;
            }
        }
        return written;
    }

    /// <summary>Claims the reminder (one "sending" row; another worker that got there first wins), then sends or records
    /// why not. Returns false when the claim was lost.</summary>
    private async Task<bool> HandleAsync(Client client, ReminderSettingsDto settings, DueReminder due, string kind, string? skipReason, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var row = new BookingReminder
        {
            Id = Guid.NewGuid(), ClientId = client.Id, BookingId = due.BookingId, Kind = kind, ScheduledStart = due.ScheduledStart,
            Status = skipReason is null ? ReminderStatus.Sending : ReminderStatus.Skipped, Reason = skipReason, CreatedAt = now, UpdatedAt = now
        };
        _reminders.AddReminder(row);
        if (!await _unitOfWork.TrySaveChangesAsync(ct)) return false;
        if (skipReason is not null) return true;

        string status, reason;
        string? channel = null;
        Guid? messageId = null;
        try
        {
            (status, reason, channel, messageId) = await SendAsync(client, settings, due, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _unitOfWork.DiscardChanges();
            (status, reason) = (ReminderStatus.Failed, ex is BillingDeniedException or EntitlementDeniedException ? ReminderReason.NoBalance : ReminderReason.SendFailed);
            _logger.LogWarning(ex, "Booking reminder {Kind} for booking {BookingId} failed.", kind, due.BookingId);
        }

        // Set-based, so it works whether or not a failed send left this request's changes discarded.
        await _reminders.CompleteReminderAsync(row.Id, status, status == ReminderStatus.Sent ? null : reason, channel, messageId, _time.GetUtcNow(), ct);
        return true;
    }

    private async Task<(string Status, string Reason, string? Channel, Guid? MessageId)> SendAsync(Client client, ReminderSettingsDto settings,
        DueReminder due, CancellationToken ct)
    {
        var contact = await _contacts.GetByIdAsync(due.ContactId, ct);
        if (contact is null) return (ReminderStatus.Skipped, ReminderReason.NoConversation, null, null);
        if (contact.OptedOutAt is not null || !contact.MarketingOptIn) return (ReminderStatus.Skipped, ReminderReason.OptedOut, null, null);

        var conversation = await LatestConversationAsync(client.Id, contact, ct);
        if (conversation is null) return (ReminderStatus.Skipped, ReminderReason.NoConversation, null, null);
        var local = TimeZoneInfo.ConvertTime(due.ScheduledStart, ViewerTimeZone.Find(client.Timezone) ?? TimeZoneInfo.Utc);
        var name = string.IsNullOrWhiteSpace(contact.FullName) ? string.Empty : contact.FullName.Trim();

        if (conversation.Channel == ConversationChannel.WhatsApp)
        {
            var templateId = PickTemplate(settings.Templates, contact.PreferredLanguage, client.DefaultLanguage);
            if (templateId is null) return (ReminderStatus.Skipped, ReminderReason.NoTemplate, conversation.Channel, null);
            var template = await _templates.GetReadOnlyAsync(client.Id, templateId.Value, ct);
            if (template is null) return (ReminderStatus.Skipped, ReminderReason.NoTemplate, conversation.Channel, null);
            if (template.Status != WhatsAppTemplateStatus.Approved) return (ReminderStatus.Skipped, ReminderReason.TemplateNotApproved, conversation.Channel, null);
            var needed = PlaceholderCount(template.Body);
            if (needed > TemplateValues) return (ReminderStatus.Skipped, ReminderReason.TemplateParameters, conversation.Channel, null);

            var culture = AppCulture.For(TemplateLanguage(template.Language));
            var when = $"{local.ToString(TextCulture.Pattern(culture, DateStyle.WeekdayDate), culture)} {local.ToString("HH:mm", culture)}";
            var values = new[] { name.Length > 0 ? name : "-", when, client.Name }.Take(needed).ToList();
            var sent = await _messages.SendReminderTemplateAsync(client.Id, conversation.Id, template.Id, values, ct);
            return (ReminderStatus.Sent, string.Empty, conversation.Channel, sent?.Id);
        }

        if (conversation.Channel == ConversationChannel.Telegram)
        {
            var text = await _readerText.ForAsync([contact.PreferredLanguage, client.DefaultLanguage], client.BusinessTypeCode, ct);
            var date = local.ToString(TextCulture.Pattern(text.Culture, DateStyle.WeekdayDate), text.Culture);
            var body = text["reminders.text", client.Name, date, local.ToString("HH:mm", text.Culture)];
            var sent = await _messages.SendSystemTextAsync(client.Id, conversation.Id, body, ct);
            return (ReminderStatus.Sent, string.Empty, conversation.Channel, sent?.Id);
        }

        return (ReminderStatus.Skipped, ReminderReason.ChannelUnsupported, conversation.Channel, null);
    }

    /// <summary>The contact's most recently active WhatsApp or Telegram conversation; a new WhatsApp one when there is none
    /// but the contact has a phone number.</summary>
    private async Task<Conversation?> LatestConversationAsync(Guid clientId, Contact contact, CancellationToken ct)
    {
        var whatsApp = await _conversations.FindForContactAsync(clientId, contact.Id, ConversationChannel.WhatsApp, ct);
        var telegram = await _conversations.FindForContactAsync(clientId, contact.Id, ConversationChannel.Telegram, ct);
        var latest = new[] { whatsApp, telegram }.OfType<Conversation>()
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt).FirstOrDefault();
        if (latest is not null || string.IsNullOrWhiteSpace(contact.Phone)) return latest;
        var created = await _conversationService.GetOrCreateForContactAsync(clientId, contact.Id, ConversationChannel.WhatsApp, ct);
        return await _conversations.GetAsync(clientId, created.Id, ct);
    }

    /// <summary>The template for the contact's language, else the client's default language, else any chosen one.</summary>
    public static Guid? PickTemplate(IReadOnlyDictionary<string, Guid> templates, string? contactLanguage, string? clientLanguage)
    {
        if (contactLanguage is not null && templates.TryGetValue(contactLanguage, out var forContact)) return forContact;
        if (clientLanguage is not null && templates.TryGetValue(clientLanguage, out var forClient)) return forClient;
        return templates.Count > 0 ? templates.OrderBy(t => t.Key, StringComparer.Ordinal).First().Value : null;
    }

    /// <summary>How many positional values a template body uses ({{1}}…{{n}}: the highest number).</summary>
    public static int PlaceholderCount(string? body) =>
        string.IsNullOrEmpty(body) ? 0 : Placeholder().Matches(body).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

    /// <summary>"en_US" → "en": the dashboard language a template's dates are written in.</summary>
    private static string TemplateLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? AppLanguage.English : language.Split('_', '-')[0].ToLowerInvariant();

    private static ReminderSettingsDto ToDto(ReminderSettings? s)
    {
        if (s is null) return new ReminderSettingsDto(false, 24, 2, new Dictionary<string, Guid>());
        var templates = JsonSerializer.Deserialize<Dictionary<string, Guid>>(string.IsNullOrWhiteSpace(s.TemplatesJson) ? "{}" : s.TemplatesJson)
            ?? new Dictionary<string, Guid>();
        return new ReminderSettingsDto(s.IsEnabled, s.FirstHoursBefore, s.SecondHoursBefore, templates);
    }

    [GeneratedRegex(@"\{\{\s*(\d{1,2})\s*\}\}")]
    private static partial Regex Placeholder();
}
