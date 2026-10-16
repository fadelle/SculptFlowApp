using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// The words a business type uses (contact, member, service, booking, order, client; singular and plural) in one
/// language. Each word falls back in this order: the type's word in that language, the default word of that language
/// (the strings file's terms.* keys), the type's English word, the English default. So every term always has a value.
/// </summary>
public interface ITerminology
{
    /// <summary>The default words (no business type): sign-in pages, signed-out visitors.</summary>
    IReadOnlyDictionary<string, TermForms> Defaults(string language);

    Task<IReadOnlyDictionary<string, TermForms>> ForBusinessTypeAsync(string businessTypeCode, string language, CancellationToken ct = default);

    /// <summary>The signed-in user's client's words, or the defaults when there is no client.</summary>
    Task<IReadOnlyDictionary<string, TermForms>> ForCurrentClientAsync(string language, CancellationToken ct = default);
}
