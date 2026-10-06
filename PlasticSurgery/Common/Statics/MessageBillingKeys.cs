using Microsoft.Extensions.Options;

namespace PlasticSurgery.Common.Statics;

public static class MessageBillingKeys
{
    /// <summary>The idempotency key of an outbound message's usage: one message = one billable event.</summary>
    public static string For(string channel, Guid messageId) => $"{channel}:message:{messageId}";
}
