using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Omni.App.Common.Statics;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
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
    /// <summary>Longest full or client name; the only argument of the name-length error keys.</summary>
    public const int MaxNameLength = 200;
    private const int MaxSlugLength = 60;
    private const int MaxSlugAttempts = 3;

    private readonly IClientRepository _clients;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _users;
    private readonly IKnowledgeSettingsService _knowledgeSettings;
    private readonly ILogger<ClientRegistrationService> _logger;
    private readonly ISubscriptionService _subscriptions;
    private readonly IConfigManager _config;
    private readonly IBusinessTypeService _businessTypes;
    private readonly ITextContext _text;

    public ClientRegistrationService(
        IClientRepository clients, IBillingAccountRepository billingAccounts, IUnitOfWork unitOfWork, UserManager<AppUser> users, IKnowledgeSettingsService knowledgeSettings,
        ILogger<ClientRegistrationService> logger, ISubscriptionService subscriptions,
        IConfigManager config, IBusinessTypeService businessTypes, ITextContext text)
    {
        _config = config;
        _businessTypes = businessTypes;
        _text = text;
        _clients = clients;
        _billingAccounts = billingAccounts;
        _unitOfWork = unitOfWork;
        _users = users;
        _knowledgeSettings = knowledgeSettings;
        _logger = logger;
        _subscriptions = subscriptions;
    }

    /// <summary>Strings-file key of what a refused signup says while Accounts:SignupEnabled is off.</summary>
    public const string SignupClosedKey = "signup.errors.closed";

    // Strings-file keys of the other refusals (the pages localize them, with MaxNameLength as {0}).
    private const string FullNameRequiredKey = "signup.errors.fullNameRequired";
    private const string ClientNameRequiredKey = "signup.errors.clientNameRequired";
    private const string FullNameTooLongKey = "signup.errors.fullNameTooLong";
    private const string ClientNameTooLongKey = "signup.errors.clientNameTooLong";
    private const string EmailInvalidKey = "signup.errors.emailInvalid";
    private const string PasswordRequiredKey = "signup.errors.passwordRequired";
    private const string PasswordMismatchKey = "signup.errors.passwordMismatch";
    private const string FailedNothingSavedKey = "signup.errors.failedNothingSaved";
    private const string FailedKey = "signup.errors.failed";

    public bool IsSignupOpen => _config.AccountsSignupEnabled;

    /// <summary>Strings-file key of the error for a missing, unknown or inactive business type (the pages localize it).</summary>
    public const string BusinessTypeRequiredKey = "signup.errors.businessTypeRequired";

    public async Task<ClientRegistrationResult> RegisterAsync(RegisterClientRequest request, CancellationToken ct = default)
    {
        if (!IsSignupOpen) return ClientRegistrationResult.Fail(SignupClosedKey);

        var fullName = (request.FullName ?? string.Empty).Trim();
        var clientName = (request.ClientName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (fullName.Length == 0) return ClientRegistrationResult.Fail(FullNameRequiredKey);
        if (clientName.Length == 0) return ClientRegistrationResult.Fail(ClientNameRequiredKey);
        if (fullName.Length > MaxNameLength) return ClientRegistrationResult.Fail(FullNameTooLongKey);
        if (clientName.Length > MaxNameLength) return ClientRegistrationResult.Fail(ClientNameTooLongKey);
        if (!EmailAddress.IsValid(email)) return ClientRegistrationResult.Fail(EmailInvalidKey);
        if (password.Length == 0) return ClientRegistrationResult.Fail(PasswordRequiredKey);
        if (!string.Equals(password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return ClientRegistrationResult.Fail(PasswordMismatchKey);
        }
        if (!await _businessTypes.IsActiveAsync(request.BusinessTypeCode, ct)) return ClientRegistrationResult.Fail(BusinessTypeRequiredKey);

        return await CreateAccountAsync(fullName, clientName, email, request.BusinessTypeCode!.Trim(), user => _users.CreateAsync(user, password), ct);
    }

    public async Task<ClientRegistrationResult> RegisterExternalAsync(RegisterExternalClientRequest request, CancellationToken ct = default)
    {
        if (!IsSignupOpen) return ClientRegistrationResult.Fail(SignupClosedKey);

        var fullName = (request.FullName ?? string.Empty).Trim();
        var clientName = (request.ClientName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();

        if (fullName.Length == 0) fullName = email; // a provider that sends no name: fall back to the email
        if (clientName.Length == 0) return ClientRegistrationResult.Fail(ClientNameRequiredKey);
        if (fullName.Length > MaxNameLength) fullName = fullName[..MaxNameLength];
        if (clientName.Length > MaxNameLength) return ClientRegistrationResult.Fail(ClientNameTooLongKey);
        if (!EmailAddress.IsValid(email)) return ClientRegistrationResult.Fail(EmailInvalidKey);
        if (!await _businessTypes.IsActiveAsync(request.BusinessTypeCode, ct)) return ClientRegistrationResult.Fail(BusinessTypeRequiredKey);

        return await CreateAccountAsync(fullName, clientName, email, request.BusinessTypeCode!.Trim(), async user =>
        {
            user.EmailConfirmed = true; // the provider verified it; the caller refuses unverified emails
            var created = await _users.CreateAsync(user);
            if (!created.Succeeded) return created;
            return await _users.AddLoginAsync(user, new UserLoginInfo(request.LoginProvider, request.ProviderKey, request.ProviderDisplayName));
        }, ct);
    }

    private async Task<ClientRegistrationResult> CreateAccountAsync(
        string fullName, string clientName, string email, string businessTypeCode, Func<AppUser, Task<IdentityResult>> createUser,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            var slug = await UniqueSlugAsync(clientName, attempt, ct);

            await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var now = DateTimeOffset.UtcNow;
                // The person signing up is the client user (role client): the one who manages the client's members.
                var user = new AppUser
                {
                    UserName = email, Email = email, FullName = fullName, Role = UserRole.Client, IsActive = true,
                    CreatedAt = now, UpdatedAt = now
                };
                var created = await createUser(user);
                if (!created.Succeeded)
                {
                    await RollbackAsync(tx, ct);
                    // Identity's descriptions are already in the request's language (LocalizedIdentityErrorDescriber).
                    return ClientRegistrationResult.Fail(created.Errors.Select(e => e.Description)) with
                    {
                        EmailTaken = created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                            or nameof(IdentityErrorDescriber.DuplicateUserName))
                    };
                }

                var client = new Client
                {
                    Id = Guid.NewGuid(),
                    Name = clientName,
                    Slug = slug,
                    Email = email,
                    Timezone = "UTC",
                    BusinessTypeCode = businessTypeCode,
                    // The dashboard opens in the language the owner signed up in.
                    DefaultLanguage = _text.Language,
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
                return ClientRegistrationResult.Fail(FailedNothingSavedKey);
            }
        }

        return ClientRegistrationResult.Fail(FailedKey);
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

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
