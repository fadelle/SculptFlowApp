using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace Omni.App.Entities.Requests.Clients;

/// <summary>Sign-up through an external identity provider (Google): no password, the provider has already
/// verified the email, and the provider login is linked to the new user in the same transaction. BusinessTypeCode must
/// name an active business type.</summary>
public record RegisterExternalClientRequest(
    string? FullName, string? ClientName, string? Email, string LoginProvider, string ProviderKey, string? ProviderDisplayName,
    string? BusinessTypeCode = null);
