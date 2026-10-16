namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Which traces a platform-admin list covers: with a step at or after <see cref="From"/>, optionally touching one
/// client or conversation, optionally only traces where something failed (a failed step or a saved error).</summary>
public record MessageTraceFilter(DateTimeOffset From, Guid? ClientId = null, Guid? ConversationId = null, bool FailedOnly = false);