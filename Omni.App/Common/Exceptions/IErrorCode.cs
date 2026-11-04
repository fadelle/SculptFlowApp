namespace Omni.App.Common.Exceptions;

/// <summary>An exception whose API answer carries a machine-readable <c>code</c> next to the message
/// (<c>{ "error": "...", "code": "..." }</c>), so a page can react to it (ApiErrors).</summary>
public interface IErrorCode
{
    string Code { get; }
}
