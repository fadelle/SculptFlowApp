using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Common.Statics;

namespace Omni.App.Pages;

/// <summary>Where <see cref="Controllers.Filters.RequirePlanFeatureAttribute"/> sends a page the client's plan doesn't
/// include (AI bots and knowledge base, campaigns, services): says so and points at Billing. An unknown feature is a 404.</summary>
public class NotInPlanModel : PageModel
{
    private const string AiTitleKey = "plan.notIncluded.ai.title";
    private const string AiTextKey = "plan.notIncluded.ai.text";
    private const string CampaignsTitleKey = "plan.notIncluded.campaigns.title";
    private const string CampaignsTextKey = "plan.notIncluded.campaigns.text";
    private const string BookingsTitleKey = "bookings.notIncluded.title";
    private const string BookingsTextKey = "bookings.notIncluded.text";

    /// <summary>The feature's text keys (title, text) and icon.</summary>
    private static readonly Dictionary<string, (string Title, string Text, string Icon)> Features = new()
    {
        [EntitlementKeys.AiAssistant] = (AiTitleKey, AiTextKey, "smart_toy"),
        [EntitlementKeys.Campaigns] = (CampaignsTitleKey, CampaignsTextKey, "campaign"),
        [EntitlementKeys.Bookings] = (BookingsTitleKey, BookingsTextKey, "event_busy"),
    };

    public string TitleKey { get; private set; } = "";
    public string TextKey { get; private set; } = "";
    public string Icon { get; private set; } = "";

    public IActionResult OnGet(string feature)
    {
        if (!Features.TryGetValue(feature, out var f)) return NotFound();
        (TitleKey, TextKey, Icon) = f;
        return Page();
    }
}
