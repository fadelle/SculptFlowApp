using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Records every call the AI agent (n8n) makes to a tool endpoint as a bot.tool_called step in the message trace: which
/// tool (the action's route template), the answer's status, how long it took, and the client/conversation/contact ids
/// found in the arguments. n8n sends the message's traceparent back on these calls, so they land in that message's trace.
/// A 4xx/5xx answer is a failed step (the bot asked for something it couldn't get). Never changes the response.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TraceBotToolAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var started = Stopwatch.GetTimestamp();
        var executed = await next();

        var tracer = context.HttpContext.RequestServices?.GetService<IMessageTracer>();
        if (tracer is null) return;

        var status = executed.Exception is not null && !executed.ExceptionHandled
            ? StatusCodes.Status500InternalServerError
            : executed.Result switch
            {
                IStatusCodeActionResult { StatusCode: { } code } => code,
                _ => context.HttpContext.Response.StatusCode
            };
        var tool = context.ActionDescriptor.AttributeRouteInfo?.Template ?? context.ActionDescriptor.DisplayName ?? "unknown";
        var ids = new TraceIds(
            ClientId: GuidArgument(context, "clientId", "ClientId"),
            ConversationId: GuidArgument(context, "conversationId", "ConversationId") ?? (tool.Contains("conversations/", StringComparison.Ordinal) ? GuidArgument(context, "id") : null));
        tracer.Step(TraceSteps.BotToolCalled, status >= 400 ? TraceOutcomes.Failed : TraceOutcomes.Ok, ids,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds, new { tool, status, contactId = GuidArgument(context, "contactId", "ContactId") });
    }

    // A Guid argument by name, or a Guid property of a bound request object (n8n sends most ids in the body).
    private static Guid? GuidArgument(ActionExecutingContext context, string name, string? property = null)
    {
        foreach (var (key, value) in context.ActionArguments)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is Guid direct && direct != Guid.Empty) return direct;
        }
        if (property is null) return null;
        foreach (var value in context.ActionArguments.Values)
        {
            if (value is null || value is Guid || value is string || value.GetType().IsPrimitive) continue;
            var found = value.GetType().GetProperty(property)?.GetValue(value);
            if (found is Guid guid && guid != Guid.Empty) return guid;
        }
        return null;
    }
}