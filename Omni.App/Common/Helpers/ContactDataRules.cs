using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Clients;

namespace Omni.App.Common.Helpers;

/// <summary>Rules for a contact's privacy requests (export, permanent delete).</summary>
public static class ContactDataRules
{
    /// <summary>Only the account owner (role <c>client</c>) exports or deletes a contact's data (403 otherwise).</summary>
    public static void EnsureClientRole(UserAccess actor)
    {
        if (actor.Role != UserRole.Client) throw new UnauthorizedAccessException("contactDetail.data.errors.clientOnly");
    }

    /// <summary>What the person types to confirm a delete: the contact's name, else phone, else email.</summary>
    public static string ConfirmationText(string? fullName, string? phone, string? email) =>
        (!string.IsNullOrWhiteSpace(fullName) ? fullName : !string.IsNullOrWhiteSpace(phone) ? phone : email ?? string.Empty).Trim();

    /// <summary>Case and surrounding spaces are ignored.</summary>
    public static bool Confirms(string? typed, string expected) =>
        expected.Length > 0 && string.Equals((typed ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
