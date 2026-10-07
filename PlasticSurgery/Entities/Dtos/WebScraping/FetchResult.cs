using System.Net;
using System.Text;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.WebScraping;

public sealed record FetchResult
{
    public string RequestedUrl { get; init; } = string.Empty;
    /// <summary>The URL the content actually came from after redirects.</summary>
    public string FinalUrl { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public string? ContentType { get; init; }
    public string? Html { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public bool NotModified { get; init; }
    public FetchErrorKind ErrorKind { get; init; }
    public string? Error { get; init; }
    public bool Ok => ErrorKind == FetchErrorKind.None && (Html is not null || NotModified);
}
