using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Guards a Razor Page whose module the client's plan may not include (the page twin of <see cref="RequireFeatureAttribute"/>):
/// when none of <see cref="FeatureKeys"/> is in the plan (<see cref="Omni.App.Entities.Dtos.Billing.ClientEntitlements.IsEnabled"/>, so billing
/// off = always allowed), every handler (GET and POST) is sent to <c>/plan/not-included/{first key}</c>, which says so.
/// The sidebar hides the same pages (<c>_Sidebar</c>). Without a client the page runs and shows its
/// own "no client" state.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequirePlanFeatureAttribute : Attribute, IAsyncPageFilter
{
    public RequirePlanFeatureAttribute(params string[] featureKeys)
    {
        if (featureKeys.Length == 0) throw new ArgumentException("Name at least one feature.", nameof(featureKeys)); // text-ok: a coding mistake, never shown
        FeatureKeys = featureKeys;
    }

    /// <summary>The page opens when the plan includes any of these.</summary>
    public IReadOnlyList<string> FeatureKeys { get; }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;
        var clientId = await services.GetRequiredService<ICurrentClientContext>().GetClientIdAsync(ct);
        if (clientId is not null)
        {
            var plan = await services.GetRequiredService<IEntitlementService>().GetAsync(clientId.Value, ct);
            if (!FeatureKeys.Any(plan.IsEnabled))
            {
                context.Result = new RedirectToPageResult("/NotInPlan", new { feature = FeatureKeys[0] });
                return;
            }
        }
        await next();
    }
}
