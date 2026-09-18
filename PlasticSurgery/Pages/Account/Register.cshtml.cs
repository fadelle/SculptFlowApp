using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Account;

/// <summary>
/// This MVP has no clinic-creation/signup wizard, so every newly registered user is linked to the
/// single clinic IClinicContext.GetDefaultClinicAsync() resolves (the "Clinic:DefaultSlug" config
/// key) — the one remaining caller of that method. If this app ever needs real multi-clinic signup,
/// this is the one place that changes; nothing downstream cares how a user got linked to a clinic,
/// only that clinic_users has a row (see ICurrentClinicContext).
/// </summary>
public class RegisterModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IClinicContext _clinicContext;
    private readonly ApplicationDbContext _db;

    public RegisterModel(
        UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager,
        IClinicContext clinicContext, ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _clinicContext = clinicContext;
        _db = db;
    }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Email and password are required.";
            return Page();
        }

        var clinic = await _clinicContext.GetDefaultClinicAsync(ct);
        if (clinic is null)
        {
            ErrorMessage = "No clinic is configured yet — run Database/schema.sql and seed.sql, " +
                            "or set Clinic:DefaultSlug to an existing clinic's slug, before registering.";
            return Page();
        }

        var user = new IdentityUser { UserName = Email, Email = Email };
        var createResult = await _userManager.CreateAsync(user, Password);
        if (!createResult.Succeeded)
        {
            ErrorMessage = string.Join(" ", createResult.Errors.Select(e => e.Description));
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        _db.ClinicUsers.Add(new ClinicUser
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic.Id,
            UserId = user.Id,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync(ct);

        await _signInManager.SignInAsync(user, isPersistent: true);

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/dashboard");
    }
}
