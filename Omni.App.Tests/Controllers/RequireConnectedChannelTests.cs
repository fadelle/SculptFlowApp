using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Controllers.Filters;

namespace Omni.App.Tests.Controllers;

/// <summary>Channel pages (the WhatsApp pages) open only once their channel is connected; the sidebar hides them the same way.</summary>
public class RequireConnectedChannelTests
{
    private static readonly Guid ClientId = Guid.NewGuid();

    private static async Task<(IActionResult? Result, bool Ran)> RunAsync(Guid? clientId, params string[] connected)
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientId);
        var channels = new Mock<IChannelIntegrationService>();
        channels.Setup(c => c.ListConnectedChannelsAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(connected.ToHashSet());
        var services = new ServiceCollection().AddSingleton(current.Object).AddSingleton(channels.Object).BuildServiceProvider();

        var page = new PageContext(new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(), new CompiledPageActionDescriptor()));
        var filters = new List<IFilterMetadata>();
        var context = new PageHandlerExecutingContext(page, filters, null, new Dictionary<string, object?>(), new object());
        var ran = false;
        await new RequireConnectedChannelAttribute(ChannelType.WhatsApp).OnPageHandlerExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new PageHandlerExecutedContext(page, filters, null, new object()));
        });
        return (context.Result, ran);
    }

    [Fact]
    public async Task Without_the_channel_the_page_says_it_is_not_connected()
    {
        var (result, ran) = await RunAsync(ClientId, ChannelType.Telegram);
        Assert.False(ran);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(("/ChannelNotConnected", ChannelType.WhatsApp), (redirect.PageName, redirect.RouteValues!["channel"]));
    }

    [Fact]
    public async Task A_connected_channel_or_no_client_lets_the_page_run()
    {
        Assert.True((await RunAsync(ClientId, ChannelType.WhatsApp)).Ran);
        Assert.True((await RunAsync(null)).Ran);
    }

    [Theory]
    [InlineData(typeof(Omni.App.Pages.WhatsApp.Templates.IndexModel))]
    [InlineData(typeof(Omni.App.Pages.WhatsApp.Health.IndexModel))]
    public void The_whatsapp_pages_carry_the_guard(Type page) =>
        Assert.Equal(ChannelType.WhatsApp, page.GetCustomAttribute<RequireConnectedChannelAttribute>()?.Channel);

    [Fact]
    public void The_not_connected_page_knows_real_channels_only()
    {
        var page = new Omni.App.Pages.ChannelNotConnectedModel();
        Assert.IsType<PageResult>(page.OnGet(ChannelType.WhatsApp));
        Assert.Equal(ChannelType.WhatsApp, page.Channel);
        Assert.IsType<NotFoundResult>(page.OnGet("pigeon"));
    }
}
