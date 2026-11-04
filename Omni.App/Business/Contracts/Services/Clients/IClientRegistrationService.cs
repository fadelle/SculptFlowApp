using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Requests.Clients;

namespace Omni.App.Business.Contracts.Services.Clients;

/// <summary>
/// Normal registration ALWAYS means NEW USER -> NEW CLIENT. It never attaches an account to an existing
/// client (joining one will be a separate invitation/members flow), so a new signup starts with an empty,
/// fully isolated tenant: no contacts, conversations, messages, bookings, services, Knowledge Base,
/// campaigns or channel connections — every one of those is scoped by client_id, and nothing here copies
/// or shares data from another client.
///
/// Everything happens in ONE database transaction on the shared DbContext (Identity's UserManager uses
/// the same context), so a failure at any step — weak password, duplicate email, a slug collision,
/// anything — rolls the whole thing back: never a client without its user, a membership without its
/// client, or a user with no client.
/// </summary>
public interface IClientRegistrationService
{
    /// <summary>Whether self-signup is allowed right now (setting Accounts:SignupEnabled). When it isn't, both Register
    /// methods refuse with <c>SignupClosedKey</c> before creating anything.</summary>
    bool IsSignupOpen { get; }

    Task<ClientRegistrationResult> RegisterAsync(RegisterClientRequest request, CancellationToken ct = default);

    /// <summary>Same NEW USER -> NEW CLIENT rule and the same single transaction as <see cref="RegisterAsync"/>,
    /// but the user has no password and is created with a provider-verified email.</summary>
    Task<ClientRegistrationResult> RegisterExternalAsync(RegisterExternalClientRequest request, CancellationToken ct = default);

    /// <summary>The admin portal opens a client for a sale (QA t6-admin-20): the same client rows as a sign-up (client, billing
    /// account, Knowledge Base settings) in one transaction, but no user yet (the owner accepts an emailed invite), open
    /// whatever Accounts:SignupEnabled says. Then the plan: <paramref name="planCode"/> (first period free) or the sign-up plan;
    /// a plan that fails to start only logs. ArgumentException for a missing or too long name, a bad or already used owner
    /// email, an unknown business type, language or plan, before anything is saved.</summary>
    Task<OpenedClient> OpenForOwnerAsync(string? clientName, string? businessTypeCode, string? language, string? ownerEmail, string? planCode,
        CancellationToken ct = default);
}
