using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Services.Email;

/// <summary>Builds and sends the account emails (password reset; member invite in Milestone C S2) in the reader's
/// language: the user's own language, else their client's default, else the language of the page that asked, else
/// English. Throws EmailSendException when the provider can't send.</summary>
public interface IAccountEmailService
{
    Task SendPasswordResetAsync(AppUser user, Client? client, string? pageLanguage, string link, int minutes, CancellationToken ct = default);
}
