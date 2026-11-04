using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Orders;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Requests.Orders;
using Omni.App.Entities.Responses.Catalog;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Entities.Responses.Orders;

namespace Omni.App.Pages.Dashboard;

/// <summary>
/// client-facing edit page for the three contact classification fields (stage, qualification, source) — the
/// ones Campaign audiences (ICampaignAudienceService) filter on — and the language the contact writes in (the AI's
/// fallback when a message has no language of its own; a short list plus "Other" with a two-letter code). Posts through the same
/// IContactService.UpdateStageAsync the existing POST /api/contacts/{id}/stage endpoint
/// already uses (UpdateContactStageRequest just gained an optional Source field for this — see its
/// remarks), so there's no second write path and Campaign counts see the change immediately (they
/// read live from the database, no caching). Also the contact's orders (QA t3-inbox-10): listed, added and updated in a
/// popup through IOrderService (the same rules as /api/orders); <c>?order=new</c> opens the add popup (the Inbox's link).
/// </summary>
public class ContactDetailModel : PageModel
{
    private const string SavedKey = "contactDetail.saved";
    private const string OptedOutKey = "contactDetail.optOut.done";
    private const string OptedInKey = "contactDetail.optOut.undone";
    private const string DeletedKey = "contactDetail.data.deleted";
    private const string OrderAddedKey = "contactDetail.orders.added";
    private const string OrderSavedKey = "contactDetail.orders.saved";
    private const string DetailsSavedKey = "contactDetail.edit.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IContactService _contacts;
    private readonly IOptOutService _optOut;
    private readonly IContactDataService _data;
    private readonly IOrderService _orders;
    private readonly ICatalogService _catalog;
    private readonly IStringLocalizer _text;

    public ContactDetailModel(ICurrentClientContext clientContext, IContactService contacts, IOptOutService optOut, IContactDataService data,
        IOrderService orders, ICatalogService catalog, IStringLocalizer text)
    {
        _orders = orders;
        _catalog = catalog;
        _clientContext = clientContext;
        _contacts = contacts;
        _optOut = optOut;
        _data = data;
        _text = text;
    }

    /// <summary>The signed-in user is the account owner: the privacy panel (export, delete) is shown.</summary>
    public bool CanManageData { get; private set; }

    /// <summary>What the owner types to confirm a delete (the contact's name, else phone, else email).</summary>
    public string DeleteConfirmation { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClientConfigured { get; private set; }
    public ContactResponse? Contact { get; private set; }

    /// <summary>The client's pipeline stages in order, labelled (stage badge and dropdown).</summary>
    public IReadOnlyList<PipelineStageOption> Stages { get; private set; } = Array.Empty<PipelineStageOption>();
    public IReadOnlyList<string> QualificationOptions { get; } = ContactQualification.All.OrderBy(s => s).ToList();

    /// <summary>Distinct sources already on file for this client, unioned with a small starter set
    /// (the examples the product spec gave) so a brand-new client with no contact data yet still has
    /// sensible options — not a fixed enum, since Contact.Source has none (see UpdateContactStageRequest).</summary>
    public IReadOnlyList<string> SourceOptions { get; private set; } = Array.Empty<string>();

    private static readonly string[] SuggestedSources = { "facebook", "instagram", "google", "website", "referral", "whatsapp" };

    [BindProperty]
    public string StageKey { get; set; } = string.Empty;

    [BindProperty]
    public string Qualification { get; set; } = string.Empty;

    [BindProperty]
    public string SourceChoice { get; set; } = string.Empty;

    /// <summary>Used only when SourceChoice == "other" — lets members enter a source not already in
    /// SourceOptions instead of being blocked by a fixed list.</summary>
    [BindProperty]
    public string? CustomSource { get; set; }

    /// <summary>The languages offered by name in the language list (the dashboard's three); any other language is "other"
    /// with its two-letter code.</summary>
    public IReadOnlyList<string> LanguageOptions { get; } = [ContactLanguage.English, ContactLanguage.Arabic, ContactLanguage.French];

    public const string OtherLanguage = "other";

    /// <summary>"" (not known), one of <see cref="LanguageOptions"/>, or <see cref="OtherLanguage"/> (then
    /// <see cref="CustomLanguage"/>).</summary>
    [BindProperty]
    public string LanguageChoice { get; set; } = string.Empty;

    [BindProperty]
    public string? CustomLanguage { get; set; }

    /// <summary>The contact's orders, newest first.</summary>
    public IReadOnlyList<OrderResponse> Orders { get; private set; } = Array.Empty<OrderResponse>();

    /// <summary>The client's active services: what a new order can be for.</summary>
    public IReadOnlyList<ServiceResponse> ActiveServices { get; private set; } = Array.Empty<ServiceResponse>();

    public IReadOnlyList<string> OrderStatuses { get; } =
        [OrderStatus.Considering, OrderStatus.Quoted, OrderStatus.DepositPaid, OrderStatus.Confirmed, OrderStatus.Completed, OrderStatus.Canceled, OrderStatus.Lost];

    /// <summary>The order the popup edits; empty when it adds one.</summary>
    [BindProperty]
    public Guid? OrderId { get; set; }

    [BindProperty]
    public Guid? OrderServiceId { get; set; }

    [BindProperty]
    public string? OrderStatusValue { get; set; }

    [BindProperty]
    public decimal? OrderQuoted { get; set; }

    [BindProperty]
    public decimal? OrderDeposit { get; set; }

    [BindProperty]
    public decimal? OrderFinal { get; set; }

    [BindProperty]
    public string? OrderCurrency { get; set; }

    [BindProperty]
    public string? OrderNotes { get; set; }

    /// <summary>The business's timezone: the follow-up day is read and saved in it.</summary>
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    [BindProperty] public string? EditName { get; set; }
    [BindProperty] public string? EditPhone { get; set; }
    [BindProperty] public string? EditEmail { get; set; }
    [BindProperty] public string? EditCity { get; set; }
    [BindProperty] public string? EditNotes { get; set; }
    /// <summary>The next follow-up's day (09:00 in the business's timezone); empty clears it.</summary>
    [BindProperty] public DateOnly? EditFollowup { get; set; }

    /// <summary>The details popup is open on load (a save that failed: then with <see cref="EditError"/>).</summary>
    public bool EditFormOpen { get; private set; }
    public string? EditError { get; private set; }

    /// <summary>The order popup is open on load (?order=new, or a save that failed: then with <see cref="OrderError"/>).</summary>
    public bool OrderFormOpen { get; private set; }

    public string? OrderError { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? order, CancellationToken ct)
    {
        await LoadAsync(ct);
        if (ClientConfigured && Contact is null) return NotFound();
        if (Contact is not null) FillContactFields();
        OrderFormOpen = order == "new" && Contact is not null;
        return Page();
    }

    /// <summary>The classification form's values from the stored contact.</summary>
    private void FillContactFields(bool fillDetails = true)
    {
        var contact = Contact!;
        if (fillDetails)
        {
            EditName = contact.FullName;
            EditPhone = contact.Phone;
            EditEmail = contact.Email;
            EditCity = contact.City;
            EditNotes = contact.Notes;
            EditFollowup = contact.NextFollowupAt is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, _zone).DateTime) : null;
        }
        StageKey = contact.StageKey;
        Qualification = contact.Qualification;
        SourceChoice = !string.IsNullOrWhiteSpace(contact.Source) && SourceOptions.Contains(contact.Source, StringComparer.OrdinalIgnoreCase)
            ? contact.Source : "other";
        CustomSource = SourceChoice == "other" ? contact.Source : null;
        var language = contact.PreferredLanguage;
        LanguageChoice = string.IsNullOrEmpty(language) ? string.Empty : LanguageOptions.Contains(language) ? language : OtherLanguage;
        CustomLanguage = LanguageChoice == OtherLanguage ? language : null;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var resolvedSource = SourceChoice == "other" ? CustomSource?.Trim() : SourceChoice;
        // Blank clears the language; the service turns a name or code into the two-letter code or refuses it.
        var resolvedLanguage = (LanguageChoice == OtherLanguage ? CustomLanguage : LanguageChoice)?.Trim() ?? string.Empty;

        try
        {
            var contact = await _contacts.UpdateStageAsync(client.Id, Id,
                new UpdateContactStageRequest(StageKey, Qualification, resolvedSource, resolvedLanguage), ct);
            if (contact is null) return NotFound();

            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage(new { Id });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    /// <summary>"Edit details" (QA t3-inbox-12): name, phone, email, city, notes and the next follow-up's day.</summary>
    public async Task<IActionResult> OnPostDetailsAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        _zone = ZoneOf(client.Timezone);
        DateTimeOffset? followup = EditFollowup is { } day
            ? new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), _zone.GetUtcOffset(day.ToDateTime(new TimeOnly(9, 0)))).ToUniversalTime()
            : null;
        try
        {
            var contact = await _contacts.UpdateAsync(client.Id, Id, new UpdateContactRequest(null, EditName ?? "", null, null, EditPhone ?? "", EditEmail ?? "",
                null, EditCity ?? "", null, EditNotes ?? "", followup, ClearNextFollowup: followup is null), ct);
            if (contact is null) return NotFound();
            StatusMessage = _text[DetailsSavedKey].Value;
            return RedirectToPage(new { Id });
        }
        catch (ArgumentException ex)
        {
            EditError = _text.Localize(ex);
            EditFormOpen = true;
            await LoadAsync(ct);
            if (Contact is not null) FillContactFields(fillDetails: false);
            return Page();
        }
    }

    /// <summary>Adds an order for this contact (OrderId empty) or updates one of its orders: status, amounts, currency.</summary>
    public async Task<IActionResult> OnPostOrderAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        var currency = string.IsNullOrWhiteSpace(OrderCurrency) ? null : OrderCurrency.Trim().ToUpperInvariant();
        var notes = string.IsNullOrWhiteSpace(OrderNotes) ? null : OrderNotes.Trim();
        try
        {
            if (OrderId is { } id)
            {
                // Only an order of this contact (another contact's id is "not found", like an unknown one).
                if (!(await _orders.ListForContactAsync(client.Id, Id, ct)).Any(o => o.Id == id)) return NotFound();
                var updated = await _orders.UpdateAsync(client.Id, id,
                    new UpdateOrderRequest(OrderStatusValue, OrderQuoted, OrderDeposit, OrderFinal, currency, null, notes), ct);
                if (updated is null) return NotFound();
                StatusMessage = _text[OrderSavedKey].Value;
            }
            else
            {
                if (OrderServiceId is not { } serviceId) throw new ArgumentException("contactDetail.orders.serviceRequired");
                await _orders.CreateAsync(new CreateOrderRequest(client.Id, Id, serviceId, null, OrderQuoted, currency, notes), ct);
                StatusMessage = _text[OrderAddedKey].Value;
            }
            return RedirectToPage(new { Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nothing was saved: the popup opens again with what was typed and why.
            OrderError = _text.Localize(ex);
            OrderFormOpen = true;
            await LoadAsync(ct);
            if (Contact is not null) FillContactFields();
            return Page();
        }
    }

    /// <summary>Any member opts the contact out of business-started messages or back in (IOptOutService).</summary>
    public async Task<IActionResult> OnPostOptOutAsync(bool optedOut, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        var contact = await _optOut.SetAsync(client.Id, Id, optedOut, ct);
        if (contact is null) return NotFound();
        StatusMessage = _text[optedOut ? OptedOutKey : OptedInKey].Value;
        return RedirectToPage(new { Id });
    }

    /// <summary>Account owner only: everything about the contact as a JSON file.</summary>
    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            var export = await _data.ExportAsync(access, Id, ct);
            return export is null ? NotFound() : File(ContactExportFile.Bytes(export), ContactExportFile.ContentType, ContactExportFile.Name(export));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Account owner only: deletes the contact and its data for good, then goes back to the list.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string? confirmation, CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            if (!await _data.DeleteAsync(access, Id, confirmation, ct)) return NotFound();
            StatusMessage = _text[DeletedKey].Value;
            return LocalRedirect("/dashboard/contacts");
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            ErrorMessage = _text.Localize(ex);
            return RedirectToPage(new { Id });
        }
    }

    private static TimeZoneInfo ZoneOf(string? timezone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timezone ?? "UTC", out var zone) ? zone : TimeZoneInfo.Utc;

    private async Task LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        _zone = ZoneOf(client.Timezone);

        Contact = await _contacts.GetByIdAsync(client.Id, Id, ct);
        if (Contact is null) return;
        CanManageData = (await _clientContext.GetAccessAsync(ct))?.Role == UserRole.Client;
        DeleteConfirmation = ContactDataRules.ConfirmationText(Contact.FullName, Contact.Phone, Contact.Email);
        Stages = await _contacts.ListStagesAsync(client.Id, ct);
        Orders = await _orders.ListForContactAsync(client.Id, Id, ct);
        ActiveServices = await _catalog.ListAsync(client.Id, activeOnly: true, ct);

        var distinctSources = await _contacts.GetDistinctSourcesAsync(client.Id, ct);
        var options = new List<string>(distinctSources);
        foreach (var s in SuggestedSources)
        {
            if (!options.Contains(s, StringComparer.OrdinalIgnoreCase)) options.Add(s);
        }
        if (!string.IsNullOrWhiteSpace(Contact.Source) && !options.Contains(Contact.Source, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(Contact.Source);
        }
        options.Sort(StringComparer.OrdinalIgnoreCase);
        SourceOptions = options;
    }
}
