using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Channels;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Guards a Razor Page that only makes sense once a channel is connected (the WhatsApp pages): while the client hasn't
/// connected <see cref="Channel"/>, every handler is sent to <c>/channel/not-connected/{channel}</c>, which says so and
/// links to Messaging Integrations. The sidebar hides the same pages (<c>_Sidebar</c>). Without a client the page runs and
/// shows its own "no client" state.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireConnectedChannelAttribute : Attribute, IAsyncPageFilter
{
    public RequireConnectedChannelAttribute(string channel)
    {
        Channel = channel;
    }

    /// <summary>A ChannelType value.</summary>
    public string Channel { get; }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;
        var clientId = await services.GetRequiredService<ICurrentClientContext>().GetClientIdAsync(ct);
        if (clientId is not null
            && !(await services.GetRequiredService<IChannelIntegrationService>().ListConnectedChannelsAsync(clientId.Value, ct)).Contains(Channel))
        {
            context.Result = new RedirectToPageResult("/ChannelNotConnected", new { channel = Channel });
            return;
        }
        await next();
    }
}
