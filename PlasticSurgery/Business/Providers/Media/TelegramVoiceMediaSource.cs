using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.Telegram;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Providers.Media;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Voice;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Providers.Media;

/// <summary>
/// Telegram's implementation of the voice-media seam (see IVoiceMediaSource). The inbound parser keeps Telegram's raw
/// "voice"/"audio" object in Message.MetadataJson; its file_id is exchanged for the file here, with THIS clinic's own bot token.
/// </summary>
public class TelegramVoiceMediaSource : IVoiceMediaSource
{
    private const string DefaultContentType = "audio/ogg"; // Telegram voice notes are Ogg/Opus

    private readonly IChannelIntegrationRepository _integrations;
    private readonly ITelegramBotClient _client;
    private readonly IConfigManager _config;

    public TelegramVoiceMediaSource(IChannelIntegrationRepository integrations, ITelegramBotClient client, IConfigManager config)
    {
        _integrations = integrations;
        _client = client;
        _config = config;
    }

    public string Channel => ConversationChannel.Telegram;

    public async Task<VoiceAudio?> DownloadAsync(Message message, CancellationToken ct = default)
    {
        var (fileId, contentType, duration) = ReadMetadata(message.MetadataJson);
        if (fileId is null) return null;

        // Clinic-scoped lookup: the token used is always the one belonging to the message's own clinic.
        var integration = await _integrations.GetReadOnlyAsync(message.ClinicId, ChannelType.Telegram, ct);
        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || string.IsNullOrEmpty(integration.AccessToken))
        {
            throw new InvalidOperationException(
                "This clinic doesn't have a connected Telegram bot — reconnect it in Settings → Messaging Integrations.");
        }

        var maxBytes = _config.VoiceMaxBytes;
        var file = await _client.GetFileAsync(integration.AccessToken, fileId, ct);
        if (file.FileSize is { } size && size > maxBytes)
        {
            throw new InvalidOperationException("This voice note is larger than the size the Inbox can play.");
        }

        var bytes = await _client.DownloadFileAsync(integration.AccessToken, file.FilePath, maxBytes, ct);
        var fileName = Path.GetFileName(file.FilePath);
        return new VoiceAudio(bytes, contentType, string.IsNullOrWhiteSpace(fileName) ? "voice.ogg" : fileName, duration);
    }

    private static (string? FileId, string ContentType, int? Duration) ReadMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return (null, DefaultContentType, null);
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, DefaultContentType, null);

            var fileId = root.TryGetProperty("file_id", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            var mime = root.TryGetProperty("mime_type", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            int? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null;
            return (string.IsNullOrWhiteSpace(fileId) ? null : fileId, string.IsNullOrWhiteSpace(mime) ? DefaultContentType : mime!, duration);
        }
        catch (JsonException)
        {
            return (null, DefaultContentType, null);
        }
    }
}
