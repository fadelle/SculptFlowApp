namespace PlasticSurgery.Entities.Dtos.Telegram;

/// <summary>Where Telegram keeps a file (getFile): the path to download it from and its size when Telegram reports one.</summary>
public record TelegramFileInfo(string FilePath, long? FileSize);
