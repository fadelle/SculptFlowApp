using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Omni's own check on an AI tool call (not only the prompt's): the call names its bot in <see cref="BotHeader"/> and, in a
/// bot test conversation, the test session in <see cref="TestSessionHeader"/> (n8n copies both from the AI trigger). The
/// client is the action's <c>clientId</c> argument or a bound body's <c>ClientId</c>. <see cref="IBotRuntimeService.AuthorizeAsync"/>
/// must allow the bot this <see cref="Capability"/> (null = a tool every bot has); the run is then in
/// <c>HttpContext.Items[BotRunContext.ItemKey]</c> for the action (<see cref="RunOf"/>). A refusal answers HTTP 200 with
/// <c>success = false</c> and code <see cref="NotAllowedCode"/>, like the tools' other business outcomes, with an
/// instruction the AI follows (the workflow's result switches route it to a reply). Runs before RequireFeature.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BotToolAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public const string BotHeader = "X-Omni-Bot-Id";
    public const string TestSessionHeader = "X-Omni-Test-Session";
    public const string NotAllowedCode = "TOOL_NOT_ALLOWED";

    public BotToolAttribute(string? capability = null)
    {
        Capability = capability;
    }

    /// <summary>The BotCapability the tool needs; null = every bot may use it.</summary>
    public string? Capability { get; }

    // Before RequireFeature (default order 0): a bot that may not use a tool never learns what the plan includes.
    public int Order => -10;

    public static BotRunContext RunOf(HttpContext context) =>
        context.Items[BotRunContext.ItemKey] as BotRunContext
        ?? throw new InvalidOperationException("The action has no [BotTool] run."); // text-ok: programming error, never reaches a reader

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var check = await http.RequestServices.GetRequiredService<IBotRuntimeService>().AuthorizeAsync(
            ClientIdOf(context), GuidHeader(http, BotHeader), GuidHeader(http, TestSessionHeader), Capability, http.RequestAborted);
        if (check.Run is null)
        {
            context.Result = new OkObjectResult(NotAllowed(check.DeniedReason));
            return;
        }
        http.Items[BotRunContext.ItemKey] = check.Run;
        await next();
    }

    /// <summary>The business outcome of a refused tool call (also used by an action that checks a capability itself).</summary>
    public static object NotAllowed(string? reason) => new
    {
        success = false,
        code = NotAllowedCode,
        reason,
        message = "This bot isn't allowed to use this tool.", // text-ok: read by the AI (n8n), never shown to the contact
        instruction = "Nothing was looked up, booked or changed. Don't offer or attempt this again. Tell the contact you can't " + // text-ok: AI instruction
            "help with that here and that a team member can, following your handoff policy." // text-ok: AI instruction
    };

    private static Guid? GuidHeader(HttpContext context, string name) =>
        Guid.TryParse(context.Request.Headers[name].FirstOrDefault(), out var id) && id != Guid.Empty ? id : null;

    // The clientId argument (query), or a ClientId property of a bound body (search_knowledge sends it in the body).
    private static Guid ClientIdOf(ActionExecutingContext context)
    {
        if (context.ActionArguments.TryGetValue("clientId", out var value) && value is Guid id) return id;
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument?.GetType().GetProperty("ClientId")?.GetValue(argument) is Guid bodyId) return bodyId;
        }
        return Guid.Empty;
    }
}
