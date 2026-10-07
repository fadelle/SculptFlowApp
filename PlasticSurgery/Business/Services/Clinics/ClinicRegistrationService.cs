using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Business.Contracts.Services.Clinics;
using PlasticSurgery.Business.Contracts.Services.Knowledge;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Clinics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Requests.Clinics;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Billing;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Services.Clinics;

public partial class ClinicRegistrationService : IClinicRegistrationService
{
    /// <summary>Identity claim type the user's full name is stored under (identity_user_claims).</summary>
    public const string FullNameClaimType = StaffClaims.FullName;

    private const int MaxNameLength = 200;
    private const int MaxSlugLength = 60;
    private const int MaxSlugAttempts = 3;

    private readonly IClinicRepository _clinics;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<IdentityUser> _users;
    private readonly IKnowledgeSettingsService _knowledgeSettings;
    private readonly ILogger<ClinicRegistrationService> _logger;
    private readonly ISubscriptionService _subscriptions;
    private readonly IConfigManager _config;

    public ClinicRegistrationService(
        IClinicRepository clinics, IBillingAccountRepository billingAccounts, IUnitOfWork unitOfWork, UserManager<IdentityUser> users, IKnowledgeSettingsService knowledgeSettings,
        ILogger<ClinicRegistrationService> logger, ISubscriptionService subscriptions,
        IConfigManager config)
    {
        _config = config;
        _clinics = clinics;
        _billingAccounts = billingAccounts;
        _unitOfWork = unitOfWork;
        _users = users;
        _knowledgeSettings = knowledgeSettings;
        _logger = logger;
        _subscriptions = subscriptions;
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

        return await CreateAccountAsync(fullName, clinicName, email, user => _users.CreateAsync(user, password), ct);
    }

    public async Task<ClinicRegistrationResult> RegisterExternalAsync(RegisterExternalClinicRequest request, CancellationToken ct = default)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var clinicName = (request.ClinicName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();

        if (fullName.Length == 0) fullName = email; // a provider that sends no name: fall back to the email
        if (clinicName.Length == 0) return ClinicRegistrationResult.Fail("Clinic name is required.");
        if (fullName.Length > MaxNameLength) fullName = fullName[..MaxNameLength];
        if (clinicName.Length > MaxNameLength) return ClinicRegistrationResult.Fail($"Clinic name must be {MaxNameLength} characters or fewer.");
        if (!IsValidEmail(email)) return ClinicRegistrationResult.Fail("Enter a valid email address.");

        return await CreateAccountAsync(fullName, clinicName, email, async user =>
        {
            user.EmailConfirmed = true; // the provider verified it; the caller refuses unverified emails
            var created = await _users.CreateAsync(user);
            if (!created.Succeeded) return created;
            return await _users.AddLoginAsync(user, new UserLoginInfo(request.LoginProvider, request.ProviderKey, request.ProviderDisplayName));
        }, ct);
    }

    private async Task<ClinicRegistrationResult> CreateAccountAsync(
        string fullName, string clinicName, string email, Func<IdentityUser, Task<IdentityResult>> createUser, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = await UniqueSlugAsync(clinicName, attempt, ct);

            await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var user = new IdentityUser { UserName = email, Email = email };
                var created = await createUser(user);
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
                _clinics.Add(clinic);
                _clinics.AddMembership(new ClinicUser
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinic.Id,
                    UserId = user.Id,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                // Its (empty) prepaid billing account — see Billing/. Balances only ever change through the ledger.
                _billingAccounts.Add(new BillingAccount
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinic.Id,
                    Currency = _config.BillingCurrency,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await _unitOfWork.SaveChangesAsync(ct);

                // The one per-clinic row the app otherwise creates lazily: Knowledge Base retrieval
                // settings. Created now, with the system defaults, so the clinic starts fully configured.
                await _knowledgeSettings.GetAsync(clinic.Id, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation("Registered new clinic {ClinicId} ({Slug}) with its first user.", clinic.Id, clinic.Slug);
                await StartSignupPlanAsync(clinic.Id, ct);
                return new ClinicRegistrationResult(true, user, clinic, Array.Empty<string>());
            }
            catch (Exception ex) when (_unitOfWork.IsDuplicateRecord(ex))
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

    /// <summary>With billing on and Billing:SignupPlanCode set, a new clinic starts on that plan with its first period
    /// free (a trial). Runs after the registration commit (billing uses its own transaction); if it fails the
    /// account still exists and an admin can assign a plan, so this only logs.</summary>
    private async Task StartSignupPlanAsync(Guid clinicId, CancellationToken ct)
    {
        var signupPlan = _config.BillingSignupPlanCode.Trim();
        if (!_config.BillingEnabled || signupPlan.Length == 0) return;
        try
        {
            await _subscriptions.StartAsync(new StartSubscriptionRequest(
                clinicId, signupPlan, $"signup:{clinicId}", ChargeFirstPeriod: false,
                Source: BillingSource.System, Reason: "Plan at signup"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Billing: couldn't start the signup plan {Plan} for new clinic {ClinicId}; assign one from the admin API.",
                signupPlan, clinicId);
        }
    }

    private async Task RollbackAsync(IUnitOfWorkTransaction tx, CancellationToken ct)
    {
        try { await tx.RollbackAsync(ct); }
        finally { _unitOfWork.DiscardChanges(); } // drop the entities from the abandoned attempt
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
            if (!await _clinics.SlugExistsAsync(candidate, ct)) return candidate;
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
