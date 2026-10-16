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
/// client (joining one will be a separate invitation/staff flow), so a new signup starts with an empty,
/// fully isolated tenant: no leads, conversations, messages, appointments, services, Knowledge Base,
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
    /// methods refuse with <c>SignupClosedMessage</c> before creating anything.</summary>
    bool IsSignupOpen { get; }

    Task<ClientRegistrationResult> RegisterAsync(RegisterClientRequest request, CancellationToken ct = default);

    /// <summary>Same NEW USER -> NEW CLIENT rule and the same single transaction as <see cref="RegisterAsync"/>,
    /// but the user has no password and is created with a provider-verified email.</summary>
    Task<ClientRegistrationResult> RegisterExternalAsync(RegisterExternalClientRequest request, CancellationToken ct = default);
}
