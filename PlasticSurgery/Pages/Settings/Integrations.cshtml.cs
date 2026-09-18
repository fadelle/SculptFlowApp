using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Settings;

/// <summary>Settings → Channels &amp; Integrations: connect WhatsApp / Instagram / Facebook per clinic.</summary>
public class IntegrationsModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IChannelIntegrationService _integrations;
    private readonly IConfiguration _configuration;

    public IntegrationsModel(ICurrentClinicContext clinicContext, IChannelIntegrationService integrations, IConfiguration configuration)
    {
        _clinicContext = clinicContext;
        _integrations = integrations;
        _configuration = configuration;
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

    [TempData]
    public string? StatusMessage { get; set; }

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

        if (!ChannelType.All.Contains(Channel))
        {
            return BadRequest();
        }

        await _integrations.SaveAsync(new SaveChannelIntegrationRequest(
            clinic.Id, Channel, DisplayName, PhoneNumberId, WhatsAppBusinessId,
            PageId, InstagramBusinessId, AccessToken, WebhookVerifyToken), ct);

        StatusMessage = $"{Label(Channel)} connection saved.";
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
        _ => channel
    };
}
