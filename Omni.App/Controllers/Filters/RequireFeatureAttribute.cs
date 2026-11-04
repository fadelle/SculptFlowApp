using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Refuses every action of a controller (or one action) when the client's plan doesn't include a feature, through
/// <see cref="IEntitlementService.EnsureFeatureAsync"/> (so billing off = always allowed, like every entitlement). The
/// client is the action's <c>clientId</c> argument (n8n/AI endpoints) or the signed-in user's client (dashboard APIs);
/// with neither, the action runs and answers its own 403/400.
/// <list type="bullet">
/// <item>Dashboard APIs: the EntitlementDeniedException reaches <see cref="ApiErrorsAttribute"/> (422, worded for the reader).</item>
/// <item><see cref="AiTool"/> = true (the AI's booking tools): HTTP 200 with <c>success = false</c> and code
/// <c>FEATURE_NOT_INCLUDED</c>, like the tools' other business outcomes, with an instruction the AI follows instead of
/// reading a billing message to the contact.</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireFeatureAttribute : Attribute, IAsyncActionFilter
{
    public const string NotIncludedCode = BotToolOutcomes.FeatureNotIncludedCode;

    /// <summary>The plan must include <paramref name="featureKey"/>, or any one of <paramref name="orFeatureKeys"/> (e.g. services:
    /// bookings or the AI assistant, QA t2-billing-03).</summary>
    public RequireFeatureAttribute(string featureKey, params string[] orFeatureKeys)
    {
        FeatureKey = featureKey;
        FeatureKeys = [featureKey, .. orFeatureKeys];
    }

    /// <summary>The first (or only) feature; the one an AI tool's answer names.</summary>
    public string FeatureKey { get; }

    /// <summary>Every feature that lets the action run (any one is enough).</summary>
    public IReadOnlyList<string> FeatureKeys { get; }

    /// <summary>Answer as an AI tool business outcome (200, success = false) instead of a 422.</summary>
    public bool AiTool { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;
        var clientId = context.ActionArguments.TryGetValue("clientId", out var value) && value is Guid id && id != Guid.Empty
            ? id
            : await services.GetRequiredService<ICurrentClientContext>().GetClientIdAsync(ct);
        if (clientId is null)
        {
            await next();
            return;
        }

        var entitlements = services.GetRequiredService<IEntitlementService>();
        EntitlementDeniedException? denied = null;
        foreach (var key in FeatureKeys)
        {
            try
            {
                await entitlements.EnsureFeatureAsync(clientId.Value, key, ct);
                denied = null;
                break;
            }
            catch (EntitlementDeniedException ex)
            {
                denied ??= ex;   // the first feature's refusal is the one answered
            }
        }
        if (denied is not null)
        {
            if (!AiTool) throw denied;
            context.Result = new OkObjectResult(BotToolOutcomes.FeatureNotIncluded(FeatureKey));
            return;
        }
        await next();
    }
}
