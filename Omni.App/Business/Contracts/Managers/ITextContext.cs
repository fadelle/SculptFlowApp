namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// The language and business-type words of the current request, worked out once per request by RequestTextMiddleware
/// (<see cref="InitializeAsync"/>). Language: the user's own setting (Milestone A, S7 adds it), else the client's
/// default_language, else the browser's Accept-Language, else English; only languages with a strings file count.
/// Outside a request (background jobs) it stays English with the default words.
/// </summary>
public interface ITextContext
{
    /// <summary>Two-letter code, e.g. "en".</summary>
    string Language { get; }

    /// <summary>"ltr" or "rtl", for the html dir attribute.</summary>
    string Direction { get; }

    /// <summary>Placeholder name → word for this request ("contact" → "contact", "Contacts" → "Contacts"…).</summary>
    IReadOnlyDictionary<string, string> Terms { get; }

    Task InitializeAsync(CancellationToken ct = default);
}
