using PlasticSurgery.Business.Contracts.Services.Calendars;
using PlasticSurgery.Business.Contracts.Services.Channels;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Business.Contracts.Services.TikTok;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

public class ChannelAdminService : IChannelAdminService
{
    private readonly IChannelAdminRepository _repository;
    private readonly IChannelIntegrationService _channels;
    private readonly IInfobipWhatsAppIntegrationService _infobip;
    private readonly ICalendarIntegrationService _calendars;
    private readonly ITikTokIntegrationService _tikTok;

    public ChannelAdminService(IChannelAdminRepository repository, IChannelIntegrationService channels,
        IInfobipWhatsAppIntegrationService infobip, ICalendarIntegrationService calendars, ITikTokIntegrationService tikTok)
    {
        _repository = repository;
        _channels = channels;
        _infobip = infobip;
        _calendars = calendars;
        _tikTok = tikTok;
    }

    public Task<List<ChannelRow>> ListChannelsAsync(Guid? clinicId, string? channel, bool problemsOnly, CancellationToken ct) =>
        _repository.ListChannelsAsync(clinicId, channel, problemsOnly, ct);

    public async Task<ChannelDetail?> GetChannelAsync(Guid id, CancellationToken ct)
    {
        var row = await _repository.GetChannelAsync(id, ct);
        if (row is null) return null;
        var webhookUrl = ChannelProvider.Of(row) == ChannelProvider.Infobip && row.Status == ChannelIntegrationStatus.Connected
            ? await _infobip.GetWebhookUrlAsync(row.ClinicId, ct)
            : null;
        return PlatformAdminMapper.ToDetail(row, webhookUrl);
    }

    public async Task<List<HealthEventRow>> HealthEventsAsync(Guid channelIntegrationId, CancellationToken ct) =>
        (await _repository.HealthEventsAsync(channelIntegrationId, ct)).Select(PlatformAdminMapper.ToRow).ToList();

    public Task<List<CalendarRow>> ListCalendarsAsync(Guid? clinicId, CancellationToken ct) => _repository.ListCalendarsAsync(clinicId, ct);

    public Task<List<TikTokRow>> ListTikTokAsync(Guid? clinicId, CancellationToken ct) => _repository.ListTikTokAsync(clinicId, ct);

    public async Task<PlatformAdminChange> DisconnectAsync(Guid id, CancellationToken ct)
    {
        var row = await _repository.GetChannelAsync(id, ct) ?? throw new KeyNotFoundException("Channel connection not found.");
        await _channels.DisconnectAsync(row.ClinicId, row.Channel, ct);
        return new PlatformAdminChange(row.ClinicId);
    }

    public async Task<ChannelDetail> ConnectInfobipSenderAsync(Guid clinicId, string? senderNumber, CancellationToken ct)
    {
        var connected = await _infobip.ConnectAsync(clinicId, senderNumber, ct);
        return await GetChannelAsync(connected.Id, ct) ?? throw new KeyNotFoundException("Channel connection not found.");
    }

    public async Task<PlatformAdminChange> DisconnectCalendarAsync(Guid id, CancellationToken ct)
    {
        var calendar = await _repository.GetCalendarAsync(id, ct) ?? throw new KeyNotFoundException("Calendar connection not found.");
        await _calendars.DisconnectAsync(calendar.ClinicId, calendar.Provider, ct);
        return new PlatformAdminChange(calendar.ClinicId);
    }

    public async Task<PlatformAdminChange> SetCalendarSyncAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var calendar = await _repository.GetCalendarAsync(id, ct) ?? throw new KeyNotFoundException("Calendar connection not found.");
        await _calendars.SetSyncEnabledAsync(calendar.ClinicId, calendar.Provider, enabled, ct);
        return new PlatformAdminChange(calendar.ClinicId);
    }

    public async Task<PlatformAdminChange> DisconnectTikTokAsync(Guid id, CancellationToken ct)
    {
        var tikTok = await _repository.GetTikTokAsync(id, ct) ?? throw new KeyNotFoundException("TikTok connection not found.");
        await _tikTok.DisconnectAsync(tikTok.ClinicId, ct);
        return new PlatformAdminChange(tikTok.ClinicId);
    }
}
