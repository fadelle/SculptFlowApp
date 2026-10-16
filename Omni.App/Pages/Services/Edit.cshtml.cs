using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Entities.Requests.Catalog;

namespace Omni.App.Pages.Services;

/// <summary>Add / edit one service (name, code, booking duration, short description). New
/// services start active; activating/deactivating happens from the list. The client always comes from
/// the logged-in user.</summary>
public class EditModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly ICatalogService _services;

    public EditModel(ICurrentClientContext clientContext, ICatalogService services)
    {
        _clientContext = clientContext;
        _services = services;
    }

    /// <summary>Route value — null when adding a new service.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    public bool ClientConfigured { get; private set; }
    public bool IsNew => Id is null;

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string? Code { get; set; }

    [BindProperty]
    public int? DurationMinutes { get; set; }

    [BindProperty]
    public string? Description { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return Page();
        ClientConfigured = true;

        if (Id is null) return Page();

        var service = await _services.GetByIdAsync(client.Id, Id.Value, ct);
        if (service is null) return NotFound();

        Name = service.Name;
        Code = service.Code;
        DurationMinutes = service.BookingDuration;
        Description = service.Description;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/Services/Index");
        ClientConfigured = true;

        try
        {
            if (Id is null)
            {
                await _services.CreateAsync(new CreateServiceRequest(client.Id, Name, Code, Description, DurationMinutes), ct);
            }
            else if (await _services.UpdateAsync(client.Id, Id.Value, new UpdateServiceRequest(Name, Code, Description, DurationMinutes), ct) is null)
            {
                return NotFound();
            }

            TempData["StatusMessage"] = "Saved.";
            return RedirectToPage("/Services/Index");
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
