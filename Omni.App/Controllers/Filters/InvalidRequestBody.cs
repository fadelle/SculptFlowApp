using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// The answer to a request body the API can't read or that misses a required field (QA t4-ai-17): ASP.NET's own problem
/// details are English and name internal types ("Omni.App.Entities.Requests.Knowledge.UpdateKnowledgeSettingsRequest").
/// This answers <c>{ error, fields }</c> like every other refusal: the text in the caller's language and only the names of
/// the fields that are wrong ("topK", or "body" for a body that isn't JSON).
/// </summary>
public static class InvalidRequestBody
{
    public const string TextKey = "common.errors.invalidRequest";

    public static object Of(HttpContext http, ModelStateDictionary state)
    {
        var fields = state.Where(e => e.Value?.Errors.Count > 0).Select(e => FieldName(e.Key)).Distinct().Order().ToList();
        return new { error = ApiErrorsAttribute.Text(http, TextKey, string.Join(", ", fields)), fields };
    }

    /// <summary>"$.topK" → "topK", "request.Name" → "name", "" or "$" → "body": a JSON path or a bound argument, never a type.</summary>
    internal static string FieldName(string key)
    {
        var path = key.Trim().TrimStart('$').TrimStart('.');
        if (path.Length == 0) return "body";
        var last = path.Split('.').Last();
        var bracket = last.IndexOf('[');
        if (bracket > 0) last = last[..bracket];
        return last.Length == 0 ? "body" : char.ToLowerInvariant(last[0]) + last[1..];
    }
}
