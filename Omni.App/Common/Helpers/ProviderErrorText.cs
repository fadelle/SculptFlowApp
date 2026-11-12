using Omni.App.Common.Exceptions;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Known provider failures as strings-file keys, so the business user reads a short explanation in their own language
/// instead of Meta's raw Graph JSON or an English status line (QA t3-inbox-14). Meta's own readable explanation
/// (error_user_msg / message) is kept as the argument where there is one; the full technical text stays in the logs and
/// traces (the exception's Message). Null = no mapping: the exception's own text is shown, as before.
/// </summary>
public static class ProviderErrorText
{
    public const string MetaTokenInvalidKey = "providers.meta.tokenInvalid";
    public const string MetaPermissionMissingKey = "providers.meta.permissionMissing";
    public const string MetaAccountProblemKey = "providers.meta.accountProblem";
    public const string MetaRefusedKey = "providers.meta.refused";

    public static (string Key, object[] Args)? For(Exception exception) => exception switch
    {
        MetaGraphApiException m => Meta(m.ErrorCode, m.HttpStatus, m.UserMessage),
        WhatsAppSendException w when w.ConnectionProblemCode is not null => Meta(w.ErrorCode, w.HttpStatus, w.UserMessage),
        _ => null
    };

    private static (string Key, object[] Args) Meta(int? code, int? httpStatus, string? userMessage) => code switch
    {
        0 or 190 or 102 => (MetaTokenInvalidKey, []),
        3 or 10 or (>= 200 and <= 299) => (MetaPermissionMissingKey, []),
        368 or 131031 or 131042 or 131045 or 133010 => (MetaAccountProblemKey, [userMessage ?? "—"]),
        null when httpStatus == 401 => (MetaTokenInvalidKey, []),
        null when httpStatus == 403 => (MetaPermissionMissingKey, []),
        _ => (MetaRefusedKey, [userMessage ?? "—"])
    };
}
