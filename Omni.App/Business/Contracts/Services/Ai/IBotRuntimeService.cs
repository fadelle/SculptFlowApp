using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Requests.Contacts;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>
/// The bot while it runs (n8n): what an AI trigger tells the workflow about it, and the check behind every AI tool call. A
/// tool call names its bot (X-Omni-Bot-Id) and, for a test conversation, its test session (X-Omni-Test-Session); Omni then
/// allows only that bot's actions on its own client's data, whatever the prompt said.
/// </summary>
public interface IBotRuntimeService
{
    /// <summary>The bot section of an AI trigger, or null when the client has no such bot.</summary>
    Task<AiBotConfig?> ConfigAsync(Guid clientId, Guid botId, CancellationToken ct = default);

    /// <summary>Checks one tool call. Null <paramref name="capability"/> = a tool every bot has (reply, handoff, the
    /// contact's language). Returns the run, or a refusal in <see cref="BotToolCheck.DeniedReason"/>: the bot isn't the
    /// client's, isn't active (live calls), the test session isn't this bot's, or the capability isn't allowed.</summary>
    Task<BotToolCheck> AuthorizeAsync(Guid clientId, Guid? botId, Guid? testSessionId, string? capability, CancellationToken ct = default);

    /// <summary>update_contact / update_contact_details / update_stage share one endpoint: the language alone is always allowed,
    /// the stage needs the pipeline capability, any other field contact_update. Blank fields count as not sent. Returns the
    /// request without blank fields, and the capability it lacks (null when allowed).</summary>
    (UpdateContactContextRequest Request, string? MissingCapability) CheckContactUpdate(BotRunContext run, UpdateContactContextRequest request);
}
