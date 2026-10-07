using System.Net;
using System.Text;

namespace PlasticSurgery.Common.Enums;

/// <summary>Why a fetch didn't produce a page. Drives whether the crawler records a failure, a skip, or a removal.</summary>
public enum FetchErrorKind
{
    None,
    /// <summary>SSRF guard refused the URL (or a redirect hop).</summary>
    Blocked,
    Timeout,
    Network,
    /// <summary>Non-success HTTP status (see StatusCode).</summary>
    HttpError,
    /// <summary>The response wasn't HTML (an image, PDF, JSON, …) — skipped, never parsed.</summary>
    NotHtml,
    TooLarge,
    TooManyRedirects,
    /// <summary>Redirected to another site — the crawler stays on the configured website.</summary>
    ExternalRedirect
}
