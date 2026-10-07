using Microsoft.AspNetCore.Http;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Managers;

/// <summary>
/// Resolves "the current clinic" for every dashboard Razor Page and dashboard-facing API — from
/// the authenticated Identity user via clinic_users, never from a clinicId the browser supplies.
/// MVP assumes one active membership per user (see ClinicUser's doc comment): the first matching
/// row wins if there's ever more than one.
///
/// Used everywhere except the WhatsApp/Telegram webhook paths, which resolve the clinic from the
/// stored channel connection instead (see Business/Engines/WhatsApp/MetaWebhookProcessor.cs and
/// Controllers/Integrations/TelegramWebhookController.cs). New-user registration creates the clinic AND its first
/// clinic_users row itself (see ClinicRegistrationService).
/// </summary>
public interface ICurrentClinicContext
{
    Task<Clinic?> GetClinicAsync(CancellationToken ct = default);

    Task<Guid?> GetClinicIdAsync(CancellationToken ct = default);
}
