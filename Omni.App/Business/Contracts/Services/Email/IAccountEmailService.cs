using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Services.Email;

/// <summary>Builds and sends the account emails in the reader's language. Password reset: the user's own language, else
/// their client's default, else the language of the page that asked, else English. Invite: see SendInviteAsync. Throws
/// EmailSendException when the provider can't send.</summary>
public interface IAccountEmailService
{
    Task SendPasswordResetAsync(AppUser user, Client? client, string? pageLanguage, string link, int minutes, CancellationToken ct = default);

    /// <summary>A member invite, in the client's default language (else the inviter's), with the client's business-type
    /// words.</summary>
    Task SendInviteAsync(string email, string? fullName, string? inviterName, Client client, string? inviterLanguage, string link, int days,
        CancellationToken ct = default);
}
