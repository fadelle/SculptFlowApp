using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>Text for a reader other than the signed-in user (emails, automatic messages to contacts): the first of their
/// preferred languages that is offered (Localization:Languages + a strings file), else English, with a business type's
/// words in that language (the default words when there is no type).</summary>
public interface IReaderTextFactory
{
    Task<ReaderText> ForAsync(IEnumerable<string?> preferredLanguages, string? businessTypeCode, CancellationToken ct = default);
}
