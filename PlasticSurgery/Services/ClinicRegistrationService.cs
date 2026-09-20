using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

public record RegisterClinicRequest(string? FullName, string? ClinicName, string? Email, string? Password, string? ConfirmPassword);

/// <summary>Success carries the new user + clinic (so the caller can sign the user in); failure carries
/// messages that are safe to show on the Register page.</summary>
public record ClinicRegistrationResult(bool Succeeded, IdentityUser? User, Clinic? Clinic, IReadOnlyList<string> Errors)
{
    public static ClinicRegistrationResult Fail(params string[] errors) => new(false, null, null, errors);
    public static ClinicRegistrationResult Fail(IEnumerable<string> errors) => new(false, null, null, errors.ToList());
}

/// <summary>
/// Normal registration ALWAYS means NEW USER -> NEW CLINIC. It never attaches an account to an existing
/// clinic (joining one will be a separate invitation/staff flow), so a new signup starts with an empty,
/// fully isolated tenant: no leads, conversations, messages, appointments, procedures, Knowledge Base,
/// campaigns or channel connections — every one of those is scoped by clinic_id, and nothing here copies
/// or shares data from another clinic.
///
/// Everything happens in ONE database transaction on the shared DbContext (Identity's UserManager uses
/// the same context), so a failure at any step — weak password, duplicate email, a slug collision,
/// anything — rolls the whole thing back: never a clinic without its user, a membership without its
/// clinic, or a user with no clinic.
/// </summary>
public interface IClinicRegistrationService
{
    Task<ClinicRegistrationResult> RegisterAsync(RegisterClinicRequest request, CancellationToken ct = default);
}

public partial class ClinicRegistrationService : IClinicRegistrationService
{
    /// <summary>Identity claim type the user's full name is stored under (identity_user_claims).</summary>
    public const string FullNameClaimType = "full_name";

    private const int MaxNameLength = 200;
    private const int MaxSlugLength = 60;
    private const int MaxSlugAttempts = 3;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _users;
    private readonly IKnowledgeSettingsService _knowledgeSettings;
    private readonly ILogger<ClinicRegistrationService> _logger;

    public ClinicRegistrationService(
        ApplicationDbContext db, UserManager<IdentityUser> users, IKnowledgeSettingsService knowledgeSettings,
        ILogger<ClinicRegistrationService> logger)
    {
        _db = db;
        _users = users;
        _knowledgeSettings = knowledgeSettings;
        _logger = logger;
    }

    public async Task<ClinicRegistrationResult> RegisterAsync(RegisterClinicRequest request, CancellationToken ct = default)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var clinicName = (request.ClinicName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (fullName.Length == 0) return ClinicRegistrationResult.Fail("Full name is required.");
        if (clinicName.Length == 0) return ClinicRegistrationResult.Fail("Clinic name is required.");
        if (fullName.Length > MaxNameLength) return ClinicRegistrationResult.Fail($"Full name must be {MaxNameLength} characters or fewer.");
        if (clinicName.Length > MaxNameLength) return ClinicRegistrationResult.Fail($"Clinic name must be {MaxNameLength} characters or fewer.");
        if (!IsValidEmail(email)) return ClinicRegistrationResult.Fail("Enter a valid email address.");
        if (password.Length == 0) return ClinicRegistrationResult.Fail("Password is required.");
        if (!string.Equals(password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return ClinicRegistrationResult.Fail("The passwords don't match.");
        }

        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = await UniqueSlugAsync(clinicName, attempt, ct);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var user = new IdentityUser { UserName = email, Email = email };
                var created = await _users.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    await RollbackAsync(tx, ct);
                    return ClinicRegistrationResult.Fail(created.Errors.Select(e => e.Description));
                }

                var claim = await _users.AddClaimAsync(user, new Claim(FullNameClaimType, fullName));
                if (!claim.Succeeded)
                {
                    await RollbackAsync(tx, ct);
                    return ClinicRegistrationResult.Fail(claim.Errors.Select(e => e.Description));
                }

                var now = DateTimeOffset.UtcNow;
                var clinic = new Clinic
                {
                    Id = Guid.NewGuid(),
                    Name = clinicName,
                    Slug = slug,
                    Email = email,
                    Timezone = "UTC",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Clinics.Add(clinic);
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

                // The one per-clinic row the app otherwise creates lazily: Knowledge Base retrieval
                // settings. Created now, with the system defaults, so the clinic starts fully configured.
                await _knowledgeSettings.GetAsync(clinic.Id, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation("Registered new clinic {ClinicId} ({Slug}) with its first user.", clinic.Id, clinic.Slug);
                return new ClinicRegistrationResult(true, user, clinic, Array.Empty<string>());
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                // Most likely two signups picked the same slug at once — nothing was committed, so try
                // again with a different slug. (A same-email race surfaces as a clean Identity error next time.)
                await RollbackAsync(tx, ct);
                _logger.LogInformation("Registration hit a unique-constraint race (attempt {Attempt}); retrying.", attempt + 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything unexpected mid-way: everything so far is rolled back, so no half-created
                // account/clinic exists — tell the user to retry instead of showing an error page.
                await RollbackAsync(tx, ct);
                _logger.LogError(ex, "Clinic registration failed and was rolled back.");
                return ClinicRegistrationResult.Fail("We couldn't finish creating your account. Nothing was saved — please try again.");
            }
        }

        return ClinicRegistrationResult.Fail("We couldn't finish creating your account. Please try again.");
    }

    private async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        try { await tx.RollbackAsync(ct); }
        finally { _db.ChangeTracker.Clear(); } // drop the entities from the abandoned attempt
    }

    /// <summary>Slug from the clinic name — lowercase, hyphen-separated ASCII, like "demo-clinic" —
    /// made unique with a numeric suffix ("glow-clinic-2"), and a random one after repeated collisions.</summary>
    private async Task<string> UniqueSlugAsync(string clinicName, int attempt, CancellationToken ct)
    {
        var baseSlug = Slugify(clinicName);
        if (attempt > 0)
        {
            // A collision just happened concurrently — go straight to a random suffix.
            return $"{Trim(baseSlug, MaxSlugLength - 7)}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        var candidate = baseSlug;
        for (var n = 2; n < 50; n++)
        {
            if (!await _db.Clinics.AnyAsync(c => c.Slug == candidate, ct)) return candidate;
            candidate = $"{Trim(baseSlug, MaxSlugLength - 5)}-{n}";
        }
        return $"{Trim(baseSlug, MaxSlugLength - 7)}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    public static string Slugify(string name)
    {
        // Strip accents (é -> e), keep only a-z0-9, everything else becomes a single hyphen.
        var normalized = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        var slug = NonSlugChars().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        slug = Trim(slug, MaxSlugLength).Trim('-');
        return slug.Length == 0 ? "clinic" : slug;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd('-');

    private static bool IsValidEmail(string email)
    {
        if (email.Length == 0 || email.Length > 256) return false;
        try { return new MailAddress(email).Address == email; }
        catch (FormatException) { return false; }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
