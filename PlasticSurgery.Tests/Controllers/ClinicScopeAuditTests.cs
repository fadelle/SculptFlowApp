using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using PlasticSurgery.Controllers.Client;

namespace PlasticSurgery.Tests.Controllers;

/// <summary>
/// Every dashboard API action must refuse a signed-in user who has no active clinic membership (Forbid) before touching any
/// service. Done by reflection so a newly added action that forgets the check fails here.
/// </summary>
public class ClinicScopeAuditTests
{
    private static readonly HashSet<string> NoClinicCheckByDesign = new()
    {
        "LeadsController.Create",                     // public lead capture, clinic comes from the request (NOTE-03)
        "ChannelIntegrationsController.DebugWhatsAppToken", // not clinic specific (NOTE-04)
        "ConversationsController.SendMessage",        // AI sends use the ingest key; staff path is covered in ConversationsControllerTests
    };

    private static object MockOf(Type type)
    {
        var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
        return mock.Object;
    }

    private static object? DefaultArg(Type type)
    {
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (type == typeof(string)) return "x";
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type.IsClass && !type.IsAbstract) return RuntimeHelpers.GetUninitializedObject(type);
        return null;
    }

    public static IEnumerable<object[]> Actions()
    {
        var controllers = typeof(DashboardApiController).Assembly.GetTypes()
            .Where(t => typeof(DashboardApiController).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var type in controllers)
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any()))
                yield return [$"{type.Name}.{method.Name}"];
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_refuses_users_without_a_clinic(string name)
    {
        if (NoClinicCheckByDesign.Contains(name)) return;
        var (typeName, methodName) = (name.Split('.')[0], name.Split('.')[1]);
        var type = typeof(DashboardApiController).Assembly.GetTypes().Single(t => t.Name == typeName);
        var method = type.GetMethod(methodName)!;

        var ctor = type.GetConstructors().Single();
        var clinic = new Mock<ICurrentClinicContext>();
        clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(ICurrentClinicContext) ? clinic.Object : MockOf(p.ParameterType)).ToArray();
        var controller = (ControllerBase)ctor.Invoke(args);
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        var parameters = method.GetParameters().Select(p => DefaultArg(p.ParameterType)).ToArray();
        var task = (Task)method.Invoke(controller, parameters)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        var action = result is IConvertToActionResult c ? c.Convert() : (IActionResult)result!;
        Assert.IsType<ForbidResult>(action);
    }
}
