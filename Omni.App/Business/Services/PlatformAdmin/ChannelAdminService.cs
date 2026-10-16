using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Business.Contracts.HttpClients.Meta;
using Omni.App.Business.Contracts.Services.Channels;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Contracts.Services.TikTok;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Channels;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class ChannelAdminService : IChannelAdminService
{
    private readonly IChannelAdminRepository _repository;
    private readonly IChannelIntegrationService _channels;
    private readonly ICalendarIntegrationService _calendars;
    private readonly ITikTokIntegrationService _tikTok;
    private readonly IMetaGraphClient _graph;

    public ChannelAdminService(IChannelAdminRepository repository, IChannelIntegrationService channels,
        ICalendarIntegrationService calendars, ITikTokIntegrationService tikTok, IMetaGraphClient graph)
    {
        _graph = graph;
        _repository = repository;
        _channels = channels;
        _calendars = calendars;
        _tikTok = tikTok;
    }

    public async Task<WhatsAppTokenDebugResult> DebugWhatsAppTokenAsync(DebugTokenRequest request, CancellationToken ct)
    {
        var token = !string.IsNullOrWhiteSpace(request.AccessToken)
            ? request.AccessToken
            : !string.IsNullOrWhiteSpace(request.Code)
                ? await _graph.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri, ct)
                : throw new ArgumentException("Either AccessToken or Code must be provided.");
        var scopes = await _graph.GetGrantedScopesAsync(token, ct);
        var wabas = await _graph.GetClientWhatsAppBusinessAccountsAsync(token, ct);
        return new WhatsAppTokenDebugResult(scopes, wabas);
    }

    public Task<List<ChannelRow>> ListChannelsAsync(Guid? clinicId, string? channel, bool problemsOnly, CancellationToken ct) =>
        _repository.ListChannelsAsync(clinicId, channel, problemsOnly, ct);

    public async Task<ChannelDetail?> GetChannelAsync(Guid id, CancellationToken ct)
    {
        var row = await _repository.GetChannelAsync(id, ct);
        return row is null ? null : PlatformAdminMapper.ToDetail(row);
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
