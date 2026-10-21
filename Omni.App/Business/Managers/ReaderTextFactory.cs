using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Business.Managers;

public class ReaderTextFactory : IReaderTextFactory
{
    private readonly ITextCatalog _catalog;
    private readonly ITerminology _terminology;
    private readonly IConfigManager _config;

    public ReaderTextFactory(ITextCatalog catalog, ITerminology terminology, IConfigManager config)
    {
        _catalog = catalog;
        _terminology = terminology;
        _config = config;
    }

    public async Task<ReaderText> ForAsync(IEnumerable<string?> preferredLanguages, string? businessTypeCode, CancellationToken ct = default)
    {
        var language = LanguagePicker.Pick(preferredLanguages, null, TextContext.OfferedLanguages(_catalog, _config));
        var words = string.IsNullOrWhiteSpace(businessTypeCode)
            ? _terminology.Defaults(language)
            : await _terminology.ForBusinessTypeAsync(businessTypeCode, language, ct);
        return new ReaderText(_catalog, language, TermPlaceholders.Build(words));
    }
}
