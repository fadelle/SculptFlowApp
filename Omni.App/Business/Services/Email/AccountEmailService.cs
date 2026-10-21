using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Email;
using Omni.App.Business.Contracts.Services.Email;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Email;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Services.Email;

public class AccountEmailService : IAccountEmailService
{
    public const string PasswordResetTag = "password_reset";
    public const string InviteTag = "invite";

    private readonly IEmailProvider _email;
    private readonly IReaderTextFactory _readerText;

    public AccountEmailService(IEmailProvider email, IReaderTextFactory readerText)
    {
        _email = email;
        _readerText = readerText;
    }

    public async Task SendPasswordResetAsync(AppUser user, Client? client, string? pageLanguage, string link, int minutes, CancellationToken ct = default)
    {
        var text = await _readerText.ForAsync([user.Language, client?.DefaultLanguage, pageLanguage], client?.BusinessTypeCode, ct);
        var email = user.Email ?? user.UserName ?? string.Empty;
        var name = string.IsNullOrWhiteSpace(user.FullName) ? null : user.FullName.Trim();
        var (html, plain) = EmailLayout.Build(text, text["emails.reset.heading"],
            [name is null ? text["emails.common.greetingNoName"] : text["emails.common.greeting", name], text["emails.reset.body", email]],
            text["emails.reset.button"], link,
            [text["emails.reset.expiry", minutes], text["emails.reset.ignore"]]);
        await _email.SendAsync(new EmailMessage(email, name, text["emails.reset.subject"], html, plain, PasswordResetTag), ct);
    }

    public async Task SendInviteAsync(string email, string? fullName, string? inviterName, Client client, string? inviterLanguage, string link, int days,
        CancellationToken ct = default)
    {
        var text = await _readerText.ForAsync([client.DefaultLanguage, inviterLanguage], client.BusinessTypeCode, ct);
        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        var inviter = string.IsNullOrWhiteSpace(inviterName) ? null : inviterName.Trim();
        var (html, plain) = EmailLayout.Build(text, text["emails.invite.heading", client.Name],
            [name is null ? text["emails.common.greetingNoName"] : text["emails.common.greeting", name],
             inviter is null ? text["emails.invite.bodyNoInviter", client.Name] : text["emails.invite.body", inviter, client.Name]],
            text["emails.invite.button"], link,
            [text["emails.invite.expiry", days], text["emails.invite.ignore"]]);
        await _email.SendAsync(new EmailMessage(email, name, text["emails.invite.subject", client.Name], html, plain, InviteTag), ct);
    }
}
