using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Common.Enums;

namespace Omni.App.Pages;

/// <summary>Where <see cref="Controllers.Filters.RequireConnectedChannelAttribute"/> sends a channel page (the WhatsApp
/// pages) while that channel isn't connected: says so and links to Messaging Integrations. An unknown channel is a 404.</summary>
public class ChannelNotConnectedModel : PageModel
{
    public string Channel { get; private set; } = "";

    public IActionResult OnGet(string channel)
    {
        if (!ChannelType.All.Contains(channel)) return NotFound();
        Channel = channel;
        return Page();
    }
}
