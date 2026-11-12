using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Omni.App.Controllers.Filters;

namespace Omni.App.Tests.Controllers;

/// <summary>QA t4-ai-17: invalid bodies answered English problem details naming internal types.</summary>
public class InvalidRequestBodyTests
{
    [Theory]
    [InlineData("$.topK", "topK")]
    [InlineData("request.Name", "name")]
    [InlineData("$", "body")]
    [InlineData("", "body")]
    [InlineData("$.items[2].Code", "code")]
    public void A_field_is_named_by_its_json_name_never_a_type(string key, string expected) =>
        Assert.Equal(expected, InvalidRequestBody.FieldName(key));

    [Fact]
    public void The_answer_is_an_error_text_and_the_wrong_fields()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("$.topK", "The JSON value could not be converted to Omni.App.Entities.Requests.Knowledge.UpdateKnowledgeSettingsRequest.");
        state.AddModelError("request", "The request field is required.");
        var http = new DefaultHttpContext { RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IStringLocalizer>(TextKit.Localizer()).BuildServiceProvider() };

        var body = JsonSerializer.Serialize(InvalidRequestBody.Of(http, state));

        Assert.Contains("\"fields\":[\"request\",\"topK\"]", body);
        Assert.Contains("topK", body);
        Assert.DoesNotContain("Omni.App.", body);
    }

    [Fact]
    public void The_error_page_always_has_a_reference()
    {
        var page = new Omni.App.Pages.ErrorModel { PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = new DefaultHttpContext { TraceIdentifier = "0HN1:2" } } };
        Assert.True(page.ShowRequestId); // without OnGet: a failed POST re-executes the page without calling it
    }
}
