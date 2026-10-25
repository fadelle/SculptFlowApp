namespace Omni.App.Entities.Dtos.Ai;

/// <summary>The outcome of IBotRuntimeService.AuthorizeAsync: the run when the tool call is allowed, else why not
/// (BotToolCheck.Reasons).</summary>
public record BotToolCheck(BotRunContext? Run, string? DeniedReason)
{
    public const string UnknownBot = "unknown_bot";
    public const string BotNotActive = "bot_not_active";
    public const string UnknownTestSession = "unknown_test_session";
    public const string NotAllowed = "not_allowed";

    public static BotToolCheck Allowed(BotRunContext run) => new(run, null);

    public static BotToolCheck Denied(string reason) => new(null, reason);
}
