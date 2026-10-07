using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Persistence.Repositories.Channels;

public class ChannelIntegrationRepository : IChannelIntegrationRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelIntegrationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(ChannelIntegration integration) => _db.ChannelIntegrations.Add(integration);

    public Task<ChannelIntegration?> GetAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Channel == channel, ct);

    public Task<ChannelIntegration?> GetReadOnlyAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Channel == channel, ct);

    public Task<ChannelIntegration?> GetByIdReadOnlyAsync(Guid integrationId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == integrationId, ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClinicAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.Where(c => c.ClinicId == clinicId).ToListAsync(ct);

    public Task<Guid> GetIdAsync(Guid clinicId, string channel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AsNoTracking()
            .Where(c => c.ClinicId == clinicId && c.Channel == channel)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

    public Task<ChannelIntegration?> FindWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.PhoneNumberId == phoneNumberId, ct);

    public Task<ChannelIntegration?> FindWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.Channel == ChannelType.WhatsApp && c.WhatsAppBusinessId == wabaId, ct);

    public Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clinicId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.Telegram && c.TelegramBotId == botId
            && c.Status == ChannelIntegrationStatus.Connected && c.ClinicId != clinicId, ct);

    public Task<bool> IsInfobipSenderConnectedElsewhereAsync(string sender, Guid clinicId, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.WhatsApp && c.Provider == ChannelProvider.Infobip && c.ProviderSenderId == sender
            && c.Status == ChannelIntegrationStatus.Connected && c.ClinicId != clinicId, ct);

    public Task<int> CountConnectedExceptAsync(Guid clinicId, string exceptChannel, CancellationToken ct = default) =>
        _db.ChannelIntegrations.AsNoTracking()
            .CountAsync(c => c.ClinicId == clinicId && c.Channel != exceptChannel && c.Status == ChannelIntegrationStatus.Connected, ct);

    public Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default) =>
        _db.ChannelIntegrations.Where(c => c.Id == integrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastWebhookAt, at), ct);

    public async Task<IReadOnlyList<ChannelIntegration>> ListForClinicReadOnlyAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.ChannelIntegrations.AsNoTracking().Where(c => c.ClinicId == clinicId).OrderBy(c => c.Channel).ToListAsync(ct);
}
