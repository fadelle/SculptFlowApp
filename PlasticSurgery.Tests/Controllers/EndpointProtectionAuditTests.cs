using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PlasticSurgery.Controllers.Filters;

namespace PlasticSurgery.Tests.Controllers;

/// <summary>
/// Security regression net: every HTTP action must be protected by sign-in, the n8n ingest key or the platform-admin key,
/// unless it is on the explicit allow-list below (webhooks with their own check, OAuth callbacks, public sign-in pages).
/// Adding a new open endpoint fails this test until someone consciously adds it here with a reason.
/// </summary>
public class EndpointProtectionAuditTests
{
    private static readonly Dictionary<string, string> AllowedAnonymous = new()
    {
        ["WhatsAppWebhookController.Verify"] = "Meta handshake, checked against Meta:WebhookVerifyToken",
        ["WhatsAppWebhookController.Receive"] = "Meta delivery, checked with X-Hub-Signature-256",
        ["TelegramWebhookController.Receive"] = "per-connection secret header",
        ["InfobipWhatsAppWebhookController.Receive"] = "per-connection token in the query string",
        ["InfobipWhatsAppWebhookController.ReceiveAccountEvent"] = "account-level Infobip:WebhookToken",
        ["LeadsController.Create"] = "public lead capture (see NOTE-03)",
        ["GoogleAuthController.Start"] = "public sign-in page (anti-forgery token)",
        ["GoogleAuthController.Callback"] = "Google OAuth callback",
        ["TikTokController.Reachable"] = "TikTok reachability check",
        ["TikTokController.Receive"] = "TikTok webhook, no verification yet (logs only the body size)",
        ["ConversationsController.SendMessage"] = "AI sends are checked inside the action against the ingest key; staff sends require sign-in",
    };

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Select(m => (t, m)));

    private static bool IsProtected(Type controller, MethodInfo action)
    {
        var attributes = controller.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true)).ToList();
        if (action.GetCustomAttributes(true).Any(a => a is IAllowAnonymous))
        {
            // an explicit action-level [AllowAnonymous] only counts as protected when a key filter is also present
            return attributes.Any(a => a is RequireIngestKeyAttribute or RequirePlatformAdminKeyAttribute);
        }
        var signedIn = controller.GetCustomAttributes(true).OfType<IAuthorizeData>().Any() || action.GetCustomAttributes(true).OfType<IAuthorizeData>().Any()
                       || InheritsAuthorize(controller);
        var keyed = attributes.Any(a => a is RequireIngestKeyAttribute or RequirePlatformAdminKeyAttribute);
        var classAnonymous = controller.GetCustomAttributes(true).Any(a => a is IAllowAnonymous);
        return keyed || (signedIn && !classAnonymous);
    }

    private static bool InheritsAuthorize(Type t) => t.BaseType is { } b && b != typeof(ControllerBase) &&
        (b.GetCustomAttributes(true).OfType<IAuthorizeData>().Any() || InheritsAuthorize(b));

    [Fact]
    public void There_are_controllers_to_audit() => Assert.True(Actions().Count() > 150);

    [Fact]
    public void Every_action_is_protected_or_consciously_allowed()
    {
        var open = Actions()
            .Where(a => !IsProtected(a.Controller, a.Action))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .Where(name => !AllowedAnonymous.ContainsKey(name))
            .OrderBy(n => n)
            .ToList();
        Assert.True(open.Count == 0, "Unprotected endpoints: " + string.Join(", ", open));
    }

    [Fact]
    public void The_allow_list_has_no_stale_entries()
    {
        var names = Actions().Select(a => $"{a.Controller.Name}.{a.Action.Name}").ToHashSet();
        Assert.All(AllowedAnonymous.Keys, k => Assert.Contains(k, names));
    }

    [Fact]
    public void Every_platform_admin_and_ingest_controller_maps_domain_errors_through_ApiErrors()
    {
        var missing = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<RequirePlatformAdminKeyAttribute>() is not null || t.GetCustomAttribute<RequireIngestKeyAttribute>() is not null)
            .Where(t => t.GetCustomAttribute<ApiErrorsAttribute>(true) is null)
            .Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "Missing [ApiErrors]: " + string.Join(", ", missing));
    }
}
