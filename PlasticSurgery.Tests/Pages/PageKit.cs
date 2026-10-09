using System.Security.Claims;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace PlasticSurgery.Tests.Pages;

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

    public static Mock<ICurrentClinicContext> ClinicContext(Clinic? clinic)
    {
        var mock = new Mock<ICurrentClinicContext>();
        mock.Setup(c => c.GetClinicAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        mock.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clinic?.Id);
        return mock;
    }

    public static Clinic SomeClinic(string name = "Glow Clinic", string timezone = "UTC") => new() { Id = Guid.NewGuid(), Name = name, Timezone = timezone, Slug = "glow" };
}
