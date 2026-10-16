using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Omni.App.Common.Statics;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Entities.Requests.Clients;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Billing;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Clients;

public partial class ClientRegistrationService : IClientRegistrationService
{
    /// <summary>Identity claim type the user's full name is stored under (identity_user_claims).</summary>
    public const string FullNameClaimType = StaffClaims.FullName;

    private const int MaxNameLength = 200;
    private const int MaxSlugLength = 60;
    private const int MaxSlugAttempts = 3;

    private readonly IClientRepository _clients;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<IdentityUser> _users;
    private readonly IKnowledgeSettingsService _knowledgeSettings;
    private readonly ILogger<ClientRegistrationService> _logger;
    private readonly ISubscriptionService _subscriptions;
    private readonly IConfigManager _config;

    public ClientRegistrationService(
        IClientRepository clients, IBillingAccountRepository billingAccounts, IUnitOfWork unitOfWork, UserManager<IdentityUser> users, IKnowledgeSettingsService knowledgeSettings,
        ILogger<ClientRegistrationService> logger, ISubscriptionService subscriptions,
        IConfigManager config)
    {
        _config = config;
        _clients = clients;
        _billingAccounts = billingAccounts;
        _unitOfWork = unitOfWork;
        _users = users;
        _knowledgeSettings = knowledgeSettings;
        _logger = logger;
        _subscriptions = subscriptions;
    }

    /// <summary>What a refused signup says while Accounts:SignupEnabled is off.</summary>
    public const string SignupClosedMessage = "New client sign-ups are closed right now. Contact Omni to get an account.";

    public bool IsSignupOpen => _config.AccountsSignupEnabled;

    public async Task<ClientRegistrationResult> RegisterAsync(RegisterClientRequest request, CancellationToken ct = default)
    {
        if (!IsSignupOpen) return ClientRegistrationResult.Fail(SignupClosedMessage);

        var fullName = (request.FullName ?? string.Empty).Trim();
        var clientName = (request.ClientName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (fullName.Length == 0) return ClientRegistrationResult.Fail("Full name is required.");
        if (clientName.Length == 0) return ClientRegistrationResult.Fail("Client name is required.");
        if (fullName.Length > MaxNameLength) return ClientRegistrationResult.Fail($"Full name must be {MaxNameLength} characters or fewer.");
        if (clientName.Length > MaxNameLength) return ClientRegistrationResult.Fail($"Client name must be {MaxNameLength} characters or fewer.");
        if (!IsValidEmail(email)) return ClientRegistrationResult.Fail("Enter a valid email address.");
        if (password.Length == 0) return ClientRegistrationResult.Fail("Password is required.");
        if (!string.Equals(password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return ClientRegistrationResult.Fail("The passwords don't match.");
        }

        return await CreateAccountAsync(fullName, clientName, email, user => _users.CreateAsync(user, password), ct);
    }

    public async Task<ClientRegistrationResult> RegisterExternalAsync(RegisterExternalClientRequest request, CancellationToken ct = default)
    {
        if (!IsSignupOpen) return ClientRegistrationResult.Fail(SignupClosedMessage);

        var fullName = (request.FullName ?? string.Empty).Trim();
        var clientName = (request.ClientName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();

        if (fullName.Length == 0) fullName = email; // a provider that sends no name: fall back to the email
        if (clientName.Length == 0) return ClientRegistrationResult.Fail("Client name is required.");
        if (fullName.Length > MaxNameLength) fullName = fullName[..MaxNameLength];
        if (clientName.Length > MaxNameLength) return ClientRegistrationResult.Fail($"Client name must be {MaxNameLength} characters or fewer.");
        if (!IsValidEmail(email)) return ClientRegistrationResult.Fail("Enter a valid email address.");

        return await CreateAccountAsync(fullName, clientName, email, async user =>
        {
            user.EmailConfirmed = true; // the provider verified it; the caller refuses unverified emails
            var created = await _users.CreateAsync(user);
            if (!created.Succeeded) return created;
            return await _users.AddLoginAsync(user, new UserLoginInfo(request.LoginProvider, request.ProviderKey, request.ProviderDisplayName));
        }, ct);
    }

    private async Task<ClientRegistrationResult> CreateAccountAsync(
        string fullName, string clientName, string email, Func<IdentityUser, Task<IdentityResult>> createUser, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = await UniqueSlugAsync(clientName, attempt, ct);

            await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var user = new IdentityUser { UserName = email, Email = email };
                var created = await createUser(user);
                if (!created.Succeeded)
                {
                    await RollbackAsync(tx, ct);
                    return ClientRegistrationResult.Fail(created.Errors.Select(e => e.Description));
                }

                var claim = await _users.AddClaimAsync(user, new Claim(FullNameClaimType, fullName));
                if (!claim.Succeeded)
                {
                    await RollbackAsync(tx, ct);
                    return ClientRegistrationResult.Fail(claim.Errors.Select(e => e.Description));
                }

                var now = DateTimeOffset.UtcNow;
                var client = new Client
                {
                    Id = Guid.NewGuid(),
                    Name = clientName,
                    Slug = slug,
                    Email = email,
                    Timezone = "UTC",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _clients.Add(client);
                _clients.AddMembership(new ClientUser
                {
                    Id = Guid.NewGuid(),
                    ClientId = client.Id,
                    UserId = user.Id,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                // Its (empty) prepaid billing account — see Billing/. Balances only ever change through the ledger.
                _billingAccounts.Add(new BillingAccount
                {
                    Id = Guid.NewGuid(),
                    ClientId = client.Id,
                    Currency = _config.BillingCurrency,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await _unitOfWork.SaveChangesAsync(ct);

                // The one per-client row the app otherwise creates lazily: Knowledge Base retrieval
                // settings. Created now, with the system defaults, so the client starts fully configured.
                await _knowledgeSettings.GetAsync(client.Id, ct);

                await tx.CommitAsync(ct);
                _logger.LogInformation("Registered new client {ClientId} ({Slug}) with its first user.", client.Id, client.Slug);
                await StartSignupPlanAsync(client.Id, ct);
                return new ClientRegistrationResult(true, user, client, Array.Empty<string>());
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
                // account/client exists — tell the user to retry instead of showing an error page.
                await RollbackAsync(tx, ct);
                _logger.LogError(ex, "Client registration failed and was rolled back.");
                return ClientRegistrationResult.Fail("We couldn't finish creating your account. Nothing was saved — please try again.");
            }
        }

        return ClientRegistrationResult.Fail("We couldn't finish creating your account. Please try again.");
    }

    /// <summary>With billing on and Billing:SignupPlanCode set, a new client starts on that plan with its first period
    /// free (a trial). Runs after the registration commit (billing uses its own transaction); if it fails the
    /// account still exists and an admin can assign a plan, so this only logs.</summary>
    private async Task StartSignupPlanAsync(Guid clientId, CancellationToken ct)
    {
        var signupPlan = _config.BillingSignupPlanCode.Trim();
        if (!_config.BillingEnabled || signupPlan.Length == 0) return;
        try
        {
            await _subscriptions.StartAsync(new StartSubscriptionRequest(
                clientId, signupPlan, $"signup:{clientId}", ChargeFirstPeriod: false,
                Source: BillingSource.System, Reason: "Plan at signup"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Billing: couldn't start the signup plan {Plan} for new client {ClientId}; assign one from the admin API.",
                signupPlan, clientId);
        }
    }

    private async Task RollbackAsync(IUnitOfWorkTransaction tx, CancellationToken ct)
    {
        try { await tx.RollbackAsync(ct); }
        finally { _unitOfWork.DiscardChanges(); } // drop the entities from the abandoned attempt
    }

    /// <summary>Slug from the client name — lowercase, hyphen-separated ASCII, like "demo-client" —
    /// made unique with a numeric suffix ("glow-client-2"), and a random one after repeated collisions.</summary>
    private async Task<string> UniqueSlugAsync(string clientName, int attempt, CancellationToken ct)
    {
        var baseSlug = Slugify(clientName);
        if (attempt > 0)
        {
            // A collision just happened concurrently — go straight to a random suffix.
            return $"{Trim(baseSlug, MaxSlugLength - 7)}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        var candidate = baseSlug;
        for (var n = 2; n < 50; n++)
        {
            if (!await _clients.SlugExistsAsync(candidate, ct)) return candidate;
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
        return slug.Length == 0 ? "client" : slug;
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
