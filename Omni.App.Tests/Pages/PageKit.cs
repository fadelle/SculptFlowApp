using System.Security.Claims;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Omni.App.Tests.Pages;

/// <summary>Wires a PageModel the way the framework would, without a server: HttpContext, signed-in user, local-URL
/// checks and TempData.</summary>
internal static class PageKit
{
    public static T Attach<T>(this T model, ClaimsPrincipal? user = null, Action<HttpContext>? configure = null) where T : PageModel
    {
        var http = new DefaultHttpContext();
        if (user is not null) http.User = user;
        configure?.Invoke(http);

        var actionContext = new ActionContext(http, new RouteData(), new PageActionDescriptor());
        model.PageContext = new PageContext(actionContext) { ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) };

        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.IsLocalUrl(It.IsAny<string?>())).Returns<string?>(s => s is not null && s.StartsWith('/') && !s.StartsWith("//") && !s.StartsWith("/\\"));
        model.Url = url.Object;

        model.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return model;
    }

    public static ClaimsPrincipal UserWithId(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    public static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    public static string RedirectTarget(IActionResult result) => result switch
    {
        LocalRedirectResult l => l.Url,
        RedirectResult r => r.Url,
        RedirectToPageResult p => "page:" + (p.PageName ?? "(same)"),
        _ => throw new Xunit.Sdk.XunitException("Unexpected " + result.GetType().Name),
    };

    /// <summary>The signed-in user (<paramref name="userId"/>, <paramref name="role"/>) works in <paramref name="client"/>;
    /// null = no access at all.</summary>
    public static Mock<ICurrentClientContext> ClientContext(Client? client, string role = UserRole.Client, string userId = "user-1")
    {
        var mock = new Mock<ICurrentClientContext>();
        mock.Setup(c => c.GetClientAsync(It.IsAny<CancellationToken>())).ReturnsAsync(client);
        mock.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(client?.Id);
        mock.Setup(c => c.GetAccessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(client is null ? null : new UserAccess(userId, client.Id, role, null, null, null));
        return mock;
    }

    public static Client SomeClient(string name = "Glow Client", string timezone = "UTC") => new() { Id = Guid.NewGuid(), Name = name, Timezone = timezone, Slug = "glow" };
}
