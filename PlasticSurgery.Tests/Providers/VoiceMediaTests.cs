using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Providers.Media;
using PlasticSurgery.Business.Providers.Media;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Controllers.Client;
using PlasticSurgery.Entities.Dtos.Voice;

namespace PlasticSurgery.Tests.Providers;

public class TelegramVoiceMediaSourceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<ITelegramBotClient> _client = new();
    private readonly Mock<IConfigManager> _config = new();

    public TelegramVoiceMediaSourceTests()
    {
        _config.SetupGet(c => c.VoiceMaxBytes).Returns(1000);
        _integrations.Setup(i => i.GetReadOnlyAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "bot-token" });
    }

    private TelegramVoiceMediaSource Sut() => new(_integrations.Object, _client.Object, _config.Object);

    private Message Voice(string? metadata) => new()
    {
        ClinicId = _clinicId, Channel = ConversationChannel.Telegram, MessageType = "audio", MetadataJson = metadata
    };

    [Fact]
    public async Task The_file_id_is_exchanged_for_the_audio_with_the_clinics_own_token()
    {
        _client.Setup(c => c.GetFileAsync("bot-token", "FID", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramFileInfo("voice/file_3.oga", 400));
        _client.Setup(c => c.DownloadFileAsync("bot-token", "voice/file_3.oga", 1000, It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 9, 8 });

        var audio = await Sut().DownloadAsync(Voice("{\"file_id\":\"FID\",\"mime_type\":\"audio/mpeg\",\"duration\":12}"));

        Assert.Equal(ConversationChannel.Telegram, Sut().Channel);
        Assert.Equal(new byte[] { 9, 8 }, audio!.Bytes);
        Assert.Equal(("audio/mpeg", "file_3.oga", (int?)12), (audio.ContentType, audio.FileName, audio.DurationSeconds));
    }

    [Fact]
    public async Task Missing_mime_type_defaults_to_ogg_and_a_missing_file_id_means_nothing_to_download()
    {
        _client.Setup(c => c.GetFileAsync(It.IsAny<string>(), "FID", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramFileInfo("voice/file_3.oga", null));
        _client.Setup(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new byte[] { 1 });
        Assert.Equal("audio/ogg", (await Sut().DownloadAsync(Voice("{\"file_id\":\"FID\"}")))!.ContentType);

        foreach (var metadata in new string?[] { null, "", "not json", "[]", "{\"duration\":3}", "{\"file_id\":\"\"}" })
            Assert.Null(await Sut().DownloadAsync(Voice(metadata)));
        _client.Verify(c => c.GetFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_disconnected_bot_or_an_oversized_file_is_refused_before_any_download()
    {
        var other = new Message { ClinicId = Guid.NewGuid(), Channel = ConversationChannel.Telegram, MessageType = "audio", MetadataJson = "{\"file_id\":\"FID\"}" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().DownloadAsync(other)); // no integration for that clinic

        _client.Setup(c => c.GetFileAsync("bot-token", "FID", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramFileInfo("voice/big.oga", 5000));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().DownloadAsync(Voice("{\"file_id\":\"FID\"}")));
        _client.Verify(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class MessageMediaServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IMessageRepository> _messages = new();
    private readonly Mock<IVoiceMediaSource> _telegram = new();

    public MessageMediaServiceTests() => _telegram.SetupGet(s => s.Channel).Returns(ConversationChannel.Telegram);

    private MessageMediaService Sut() => new(_messages.Object, [_telegram.Object]);

    private Message Stored(string type = "audio", string channel = ConversationChannel.Telegram)
    {
        var message = new Message { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = channel, MessageType = type };
        _messages.Setup(m => m.GetForClinicReadOnlyAsync(_clinicId, message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        return message;
    }

    [Fact]
    public async Task The_channels_source_provides_the_audio()
    {
        var message = Stored();
        var audio = new VoiceAudio([1], "audio/ogg", "v.oga", 3);
        _telegram.Setup(s => s.DownloadAsync(message, It.IsAny<CancellationToken>())).ReturnsAsync(audio);
        Assert.Same(audio, await Sut().GetAudioAsync(_clinicId, message.Id));
    }

    [Fact]
    public async Task Unknown_other_clinic_or_non_audio_messages_and_missing_files_are_reported()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAudioAsync(_clinicId, Guid.NewGuid())); // not this clinic's

        var text = Stored("text");
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetAudioAsync(_clinicId, text.Id));

        var instagram = Stored(channel: "instagram");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetAudioAsync(_clinicId, instagram.Id));

        var gone = Stored();
        _telegram.Setup(s => s.DownloadAsync(gone, It.IsAny<CancellationToken>())).ReturnsAsync((VoiceAudio?)null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAudioAsync(_clinicId, gone.Id));
        _telegram.Verify(s => s.DownloadAsync(text, It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class MessageMediaControllerTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ICurrentClinicContext> _clinic = new();
    private readonly Mock<IMessageMediaService> _media = new();

    private MessageMediaController Sut()
    {
        var controller = new MessageMediaController(_clinic.Object, _media.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task A_user_without_a_clinic_is_forbidden_and_the_service_is_never_asked()
    {
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Assert.IsType<ForbidResult>(await Sut().Get(Guid.NewGuid(), default));
        _media.Verify(m => m.GetAudioAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_audio_is_streamed_with_its_type_range_support_and_a_private_cache()
    {
        var messageId = Guid.NewGuid();
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
        _media.Setup(m => m.GetAudioAsync(_clinicId, messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VoiceAudio([1, 2, 3], "audio/ogg", "v.oga", 2));
        var controller = Sut();

        var result = Assert.IsType<FileContentResult>(await controller.Get(messageId, default));

        Assert.Equal(("audio/ogg", true), (result.ContentType, result.EnableRangeProcessing));
        Assert.Equal(new byte[] { 1, 2, 3 }, result.FileContents);
        Assert.Equal("private, max-age=3600", controller.Response.Headers.CacheControl.ToString());
    }
}
