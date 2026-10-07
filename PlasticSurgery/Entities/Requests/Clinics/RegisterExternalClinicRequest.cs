using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace PlasticSurgery.Entities.Requests.Clinics;

/// <summary>Sign-up through an external identity provider (Google): no password, the provider has already
/// verified the email, and the provider login is linked to the new user in the same transaction.</summary>
public record RegisterExternalClinicRequest(
    string? FullName, string? ClinicName, string? Email, string LoginProvider, string ProviderKey, string? ProviderDisplayName);
