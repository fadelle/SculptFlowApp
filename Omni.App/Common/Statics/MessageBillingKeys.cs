using Microsoft.Extensions.Options;

namespace Omni.App.Common.Statics;

public static class MessageBillingKeys
{
    /// <summary>The idempotency key of an outbound message's usage: one message = one billable event.</summary>
    public static string For(string channel, Guid messageId) => $"{channel}:message:{messageId}";

    /// <summary>The usage "channel" of AI assistant replies (ai_reply): no connected account, who pays the AI provider
    /// comes from Billing:ProviderBilling:Defaults:ai (built-in platform_funded).</summary>
    public const string AiChannel = "ai";

    /// <summary>The idempotency key of an AI reply's ai_reply usage: one AI reply message = one billable event.</summary>
    public static string AiReply(Guid messageId) => $"{AiChannel}:reply:{messageId}";
}
