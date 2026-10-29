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

/// <summary>Plan-gated pages (item: the sidebar and routes follow the plan): a page whose module the plan lacks sends every
/// handler to /plan/not-included/{feature}; the gated pages carry the attribute.</summary>
public class RequirePlanFeatureTests
{
    private static readonly Guid ClientId = Guid.NewGuid();

    private static async Task<(IActionResult? Result, bool Ran)> RunAsync(RequirePlanFeatureAttribute filter, Guid? clientId,
        IReadOnlyDictionary<string, string> plan, bool billingEnabled = true)
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientId);
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.GetAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(billingEnabled
            ? new ClientEntitlements(true, true, SubscriptionStatus.Active, "p", "P", null, plan)
            : ClientEntitlements.Unrestricted);
        var services = new ServiceCollection().AddSingleton(current.Object).AddSingleton(entitlements.Object).BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        var page = new PageContext(new ActionContext(http, new RouteData(), new CompiledPageActionDescriptor()));
        var filters = new List<IFilterMetadata>();
        var context = new PageHandlerExecutingContext(page, filters, null, new Dictionary<string, object?>(), new object());
        var ran = false;
        await filter.OnPageHandlerExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new PageHandlerExecutedContext(page, filters, null, new object()));
        });
        return (context.Result, ran);
    }

    [Fact]
    public async Task A_plan_without_the_feature_is_sent_to_the_not_included_page()
    {
        var (result, ran) = await RunAsync(new RequirePlanFeatureAttribute(EntitlementKeys.Campaigns), ClientId, new Dictionary<string, string>());
        Assert.False(ran);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(("/NotInPlan", EntitlementKeys.Campaigns), (redirect.PageName, redirect.RouteValues!["feature"]));
    }

    [Fact]
    public async Task Any_listed_feature_opens_the_page_and_billing_off_or_no_client_never_blocks()
    {
        var either = new RequirePlanFeatureAttribute(EntitlementKeys.Bookings, EntitlementKeys.AiAssistant);
        Assert.True((await RunAsync(either, ClientId, new Dictionary<string, string> { [EntitlementKeys.AiAssistant] = "true" })).Ran);
        Assert.True((await RunAsync(either, ClientId, new Dictionary<string, string>(), billingEnabled: false)).Ran);
        Assert.True((await RunAsync(either, null, new Dictionary<string, string>())).Ran);
    }

    [Theory]
    [InlineData(typeof(Omni.App.Pages.Bots.IndexModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.Bots.EditModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.KnowledgeBase.IndexModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.KnowledgeBase.SettingsModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.KnowledgeBase.Websites.DetailsModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.Settings.InboxSettingsModel), EntitlementKeys.AiAssistant)]
    [InlineData(typeof(Omni.App.Pages.Campaigns.IndexModel), EntitlementKeys.Campaigns)]
    [InlineData(typeof(Omni.App.Pages.Campaigns.CreateModel), EntitlementKeys.Campaigns)]
    [InlineData(typeof(Omni.App.Pages.Campaigns.DetailsModel), EntitlementKeys.Campaigns)]
    [InlineData(typeof(Omni.App.Pages.Services.IndexModel), EntitlementKeys.Bookings)]
    public void Plan_gated_pages_carry_the_guard(Type page, string feature)
    {
        var guard = page.GetCustomAttribute<RequirePlanFeatureAttribute>();
        Assert.NotNull(guard);
        Assert.Contains(feature, guard.FeatureKeys);
    }

    [Fact]
    public void The_not_included_page_knows_every_guarded_feature()
    {
        var page = new Omni.App.Pages.NotInPlanModel();
        foreach (var feature in new[] { EntitlementKeys.AiAssistant, EntitlementKeys.Campaigns, EntitlementKeys.Bookings })
        {
            Assert.IsType<PageResult>(page.OnGet(feature));
        }
        Assert.IsType<NotFoundResult>(page.OnGet("nope"));
    }
}
