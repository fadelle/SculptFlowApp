using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Mappers.BusinessTypes;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Persistence.Contracts.BusinessTypes;

namespace Omni.App.Business.Managers;

public class Terminology : ITerminology
{
    private readonly ITextCatalog _catalog;
    private readonly IBusinessTypeRepository _types;
    private readonly ICurrentClientContext _currentClient;

    public Terminology(ITextCatalog catalog, IBusinessTypeRepository types, ICurrentClientContext currentClient)
    {
        _catalog = catalog;
        _types = types;
        _currentClient = currentClient;
    }

    public IReadOnlyDictionary<string, TermForms> Defaults(string language) =>
        TermName.All.ToDictionary(term => term, term =>
            DefaultOf(language, term) ?? DefaultOf(AppLanguage.English, term) ?? new TermForms(term, term + "s"));

    public async Task<IReadOnlyDictionary<string, TermForms>> ForBusinessTypeAsync(string businessTypeCode, string language,
        CancellationToken ct = default)
    {
        var type = await _types.GetReadOnlyAsync(businessTypeCode, ct);
        if (type is null) return Defaults(language);

        var words = BusinessTypeMapper.ParseTerminology(type.TerminologyJson);
        return TermName.All.ToDictionary(term => term, term =>
            Valid(WordOf(words, language, term))
            ?? DefaultOf(language, term)
            ?? Valid(WordOf(words, AppLanguage.English, term))
            ?? DefaultOf(AppLanguage.English, term)
            ?? new TermForms(term, term + "s"));
    }

    public async Task<IReadOnlyDictionary<string, TermForms>> ForCurrentClientAsync(string language, CancellationToken ct = default) =>
        await _currentClient.GetClientAsync(ct) is { } client
            ? await ForBusinessTypeAsync(client.BusinessTypeCode, language, ct)
            : Defaults(language);

    private static TermForms? WordOf(Dictionary<string, Dictionary<string, TermForms>> words, string language, string term) =>
        words.TryGetValue(language, out var terms) && terms.TryGetValue(term, out var forms) ? forms : null;

    private static TermForms? Valid(TermForms? forms) =>
        forms is not null && !string.IsNullOrWhiteSpace(forms.One) && !string.IsNullOrWhiteSpace(forms.Other) ? forms : null;

    private TermForms? DefaultOf(string language, string term) =>
        _catalog.Find(language, $"terms.{term}.one") is { } one && _catalog.Find(language, $"terms.{term}.other") is { } other
            ? new TermForms(one, other)
            : null;
}
