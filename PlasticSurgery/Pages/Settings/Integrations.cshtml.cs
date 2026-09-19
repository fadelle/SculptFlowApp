using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Telegram;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Channels &amp; Integrations: connect WhatsApp / Instagram / Facebook per clinic.</summary>
public class IntegrationsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IChannelIntegrationService _integrations;
    private readonly IConfiguration _configuration;
    private readonly ITelegramIntegrationService _telegram;

    public IntegrationsModel(
        ICurrentClinicContext clinicContext, IChannelIntegrationService integrations, IConfiguration configuration,
        ITelegramIntegrationService telegram)
    {
        _clinicContext = clinicContext;
        _integrations = integrations;
        _configuration = configuration;
        _telegram = telegram;
    }

    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public IReadOnlyDictionary<string, ChannelIntegrationResponse> Channels { get; private set; } =
        new Dictionary<string, ChannelIntegrationResponse>();

    // Meta app config the browser needs to run FB.login() — App ID and Login Configuration IDs
    // are not secret (they're designed to ship in client-side JS); the App Secret never leaves
    // the server (see Services/MetaGraphClient.cs).
    public string MetaAppId => _configuration["Meta:AppId"] ?? "";
    public string MetaGraphApiVersion => _configuration["Meta:GraphApiVersion"] ?? "v21.0";
    public string MetaWhatsAppLoginConfigId => _configuration["Meta:WhatsAppLoginConfigId"] ?? "";
    public string MetaFacebookLoginConfigId => _configuration["Meta:FacebookLoginConfigId"] ?? "";

    [BindProperty]
    public string Channel { get; set; } = string.Empty;

    [BindProperty]
    public string? DisplayName { get; set; }

    [BindProperty]
    public string? PhoneNumberId { get; set; }

    [BindProperty]
    public string? WhatsAppBusinessId { get; set; }

    [BindProperty]
    public string? PageId { get; set; }

    [BindProperty]
    public string? InstagramBusinessId { get; set; }

    [BindProperty]
    public string? AccessToken { get; set; }

    [BindProperty]
    public string? WebhookVerifyToken { get; set; }

    /// <summary>Telegram bot token from the connect form. Write-only: every POST that reads it ends in a
    /// redirect, so it is never rendered back into a page.</summary>
    [BindProperty]
    public string? BotToken { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            return RedirectToPage();
        }

        // Telegram has its own Connect flow (token -> getMe -> setWebhook); it can't be "saved" as fields.
        if (!ChannelType.All.Contains(Channel) || Channel == ChannelType.Telegram)
        {
            return BadRequest();
        }

        await _integrations.SaveAsync(new SaveChannelIntegrationRequest(
            clinic.Id, Channel, DisplayName, PhoneNumberId, WhatsAppBusinessId,
            PageId, InstagramBusinessId, AccessToken, WebhookVerifyToken), ct);

        StatusMessage = $"{Label(Channel)} connection saved.";
        return RedirectToPage();
    }

    /// <summary>Connect / reconnect Telegram: clinic comes from the logged-in user, the token from the form.</summary>
    public async Task<IActionResult> OnPostConnectTelegramAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            return RedirectToPage();
        }

        try
        {
            var result = await _telegram.ConnectAsync(clinic.Id, BotToken, ct);
            StatusMessage = $"Telegram connected — bot {result.DisplayName ?? "(unnamed)"}. The webhook was registered automatically.";
        }
        catch (Exception ex) when (ex is ArgumentException or TelegramApiException or InvalidOperationException)
        {
            ErrorMessage = "Could not connect Telegram: " + ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshTelegramAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            return RedirectToPage();
        }

        var result = await _telegram.RefreshStatusAsync(clinic.Id, ct);
        StatusMessage = result.WebhookStatus == WebhookStatus.Active
            ? "Telegram webhook is active."
            : "Telegram webhook check finished — see the status below.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            return RedirectToPage();
        }

        await _integrations.DisconnectAsync(clinic.Id, Channel, ct);
        StatusMessage = $"{Label(Channel)} disconnected.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        ClinicId = clinic.Id;
        var items = await _integrations.ListAsync(clinic.Id, ct);
        Channels = items.ToDictionary(i => i.Channel);
    }

    private static string Label(string channel) => channel switch
    {
        ChannelType.WhatsApp => "WhatsApp",
        ChannelType.Instagram => "Instagram",
        ChannelType.Facebook => "Facebook",
        ChannelType.Telegram => "Telegram",
        _ => channel
    };
}
