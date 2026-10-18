using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Omni.App.Entities.Models;

namespace Omni.App.Entities.Dtos.Clients;

/// <summary>Success carries the new user + client (so the caller can sign the user in); failure carries
/// messages that are safe to show on the Register page: strings-file keys (the page localizes them) or Identity's
/// already localized descriptions.</summary>
public record ClientRegistrationResult(bool Succeeded, AppUser? User, Client? Client, IReadOnlyList<string> Errors)
{
    /// <summary>The email already signs in (Identity's duplicate email/user name), so the page offers to sign in instead.</summary>
    public bool EmailTaken { get; init; }

    public static ClientRegistrationResult Fail(params string[] errors) => new(false, null, null, errors);
    public static ClientRegistrationResult Fail(IEnumerable<string> errors) => new(false, null, null, errors.ToList());
}
