using System.Diagnostics;
using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Middleware;

namespace Omni.App.Business.Providers.Logging;

/// <summary>
/// One logger category of <see cref="DatabaseLoggerProvider"/>. Turns a captured log call into an AppLog row: rendered
/// message and template, exception, fingerprint, trace id (Activity.Current), the request's method and route template
/// (never the raw path), clinic/conversation/message ids found in the values or scopes, and every other value as
/// properties with secrets masked. Never throws: a logging failure must not break the code that logged.
/// </summary>
public sealed class DatabaseLogger : ILogger
{
    private const string TemplateKey = "{OriginalFormat}";

    // Values already stored in their own columns, or noise from the hosting scope (the raw path can carry ids or tokens).
    private static readonly HashSet<string> SkippedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        TemplateKey, "ClinicId", "ConversationId", "MessageId", "TraceId", "SpanId", "ParentId", "RequestPath", "RequestId", "ConnectionId"
    };

    // A value whose name contains one of these is stored as "***".
    private static readonly string[] SecretMarkers =
        ["token", "secret", "password", "passwd", "authorization", "apikey", "api_key", "signature", "cookie", "credential"];

    private readonly string _category;
    private readonly IAppLogCollector _collector;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _time;
    private readonly Func<IExternalScopeProvider?> _scopes;

    public DatabaseLogger(string category, IAppLogCollector collector, IHttpContextAccessor http, TimeProvider time,
        Func<IExternalScopeProvider?> scopes)
    {
        _category = category;
        _collector = collector;
        _http = http;
        _time = time;
        _scopes = scopes;
    }

    // The provider supports external scopes, so the framework keeps scopes itself and never calls this.
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => _collector.ShouldCapture(logLevel, _category);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || AppLogLevels.NameOf(logLevel) is not { } level) return;
        try
        {
            _collector.Add(Build(level, eventId, state, exception, formatter));
        }
        catch
        {
            // Never let the error log break the caller; the console logger still has the entry.
        }
    }

    private AppLog Build<TState>(string level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new List<KeyValuePair<string, object?>>();
        string? template = null;
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair.Key == TemplateKey) template = pair.Value?.ToString();
                else values.Add(pair);
            }
        }
        _scopes()?.ForEachScope(static (scope, list) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs) list.AddRange(scopePairs);
        }, values);

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message)) message = exception?.Message ?? template ?? string.Empty;

        var exceptionType = exception?.GetType().FullName;
        var category = Cap(_category, AppLogLimits.MaxCategoryLength)!;
        var row = new AppLog
        {
            OccurredAt = _time.GetUtcNow(),
            Level = level,
            Category = category,
            Message = Cap(message, AppLogLimits.MaxMessageLength)!,
            MessageTemplate = Cap(template, AppLogLimits.MaxTemplateLength),
            ExceptionType = Cap(exceptionType, AppLogLimits.MaxExceptionTypeLength),
            ExceptionMessage = Cap(exception?.Message, AppLogLimits.MaxExceptionMessageLength),
            StackTrace = Cap(exception?.ToString(), AppLogLimits.MaxStackLength),
            // Without a template (a plain string message) the message itself names the problem.
            Fingerprint = AppLogFingerprint.Compute(category, template ?? Cap(message, 200), exceptionType, AppLogFingerprint.TopFrame(exception)),
            TraceId = TraceIdOf(values),
            ClinicId = GuidOf(values, "ClinicId"),
            ConversationId = GuidOf(values, "ConversationId"),
            MessageId = GuidOf(values, "MessageId"),
            Instance = AppLogLimits.InstanceName,
            Properties = PropertiesJson(values, eventId)
        };

        var context = _http.HttpContext;
        if (context is not null)
        {
            row.Method = Cap(context.Request.Method, 10);
            row.Route = Cap(RequestTimingMiddleware.RouteOf(context), AppLogLimits.MaxRouteLength);
        }
        return row;
    }

    private static string? TraceIdOf(List<KeyValuePair<string, object?>> values)
    {
        var activity = Activity.Current;
        if (activity is not null && activity.TraceId != default) return activity.TraceId.ToHexString();
        var fromScope = values.LastOrDefault(v => string.Equals(v.Key, "TraceId", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
        return fromScope is { Length: 32 } ? fromScope : null;
    }

    // The log call's own values come first, so they beat a scope's.
    private static Guid? GuidOf(List<KeyValuePair<string, object?>> values, string key)
    {
        foreach (var pair in values)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            switch (pair.Value)
            {
                case Guid guid when guid != Guid.Empty:
                    return guid;
                case string text when Guid.TryParse(text, out var parsed) && parsed != Guid.Empty:
                    return parsed;
            }
        }
        return null;
    }

    private static string PropertiesJson(List<KeyValuePair<string, object?>> values, EventId eventId)
    {
        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name)) properties["EventId"] = eventId.Name ?? eventId.Id.ToString();

        var length = 2;
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(key) || SkippedKeys.Contains(key) || properties.ContainsKey(key)) continue;
            var text = IsSecret(key) ? "***" : Cap(value?.ToString(), AppLogLimits.MaxPropertyValueLength);
            length += key.Length + (text?.Length ?? 4) + 6;
            if (length > AppLogLimits.MaxPropertiesLength)
            {
                properties["_truncated"] = "true";
                break;
            }
            properties[key] = text;
        }
        return JsonSerializer.Serialize(properties);
    }

    private static bool IsSecret(string key) =>
        SecretMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? Cap(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
