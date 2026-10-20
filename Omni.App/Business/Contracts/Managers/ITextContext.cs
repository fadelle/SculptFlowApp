using System.Globalization;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// The language and business-type words of the current request, worked out once per request by RequestTextMiddleware
/// (<see cref="InitializeAsync"/>). A signed-in client or member: their own language (users.language), else the client's
/// default_language, else the browser's Accept-Language, else English. A visitor (sign-in pages): the language picked on
/// those pages (omni.lang cookie), else the browser's. An /api call without a signed-in user (the admin portal, n8n,
/// provider webhooks) is always English. Only offered languages count (<see cref="Offered"/>). Outside a request
/// (background jobs) it stays English with the default words.
/// </summary>
public interface ITextContext
{
    /// <summary>Two-letter code, e.g. "en".</summary>
    string Language { get; }

    /// <summary>"ltr" or "rtl", for the html dir attribute.</summary>
    string Direction { get; }

    /// <summary>The culture dates and numbers are shown with (AppCulture: Arabic keeps Latin digits).</summary>
    CultureInfo Culture { get; }

    /// <summary>Placeholder name → word for this request ("contact" → "contact", "Contacts" → "Contacts"…).</summary>
    IReadOnlyDictionary<string, string> Terms { get; }

    /// <summary>Languages people may pick: a strings file exists and the Localization:Languages setting lists it.
    /// English first.</summary>
    IReadOnlyList<string> Offered { get; }

    /// <summary>Translation review mode for this request: each text is followed by its key (Localization:ShowTextKeys,
    /// never in Production).</summary>
    bool ShowKeys { get; }

    Task InitializeAsync(CancellationToken ct = default);
}
