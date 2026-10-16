using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Business.Providers.Logging;

/// <summary>
/// The error log's logging provider (its contract is the framework's ILoggerProvider; registered in Program.cs as
/// ILoggerProvider, so every ILogger in the app also writes here). Each logger hands captured warnings/errors to
/// IAppLogCollector; nothing on the logging path touches the database. Supports the framework's shared log scopes, so
/// scope values (client, conversation, message ids, MVC action) end up on the row.
/// </summary>
public sealed class DatabaseLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IAppLogCollector _collector;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _time;
    private IExternalScopeProvider? _scopes;

    public DatabaseLoggerProvider(IAppLogCollector collector, IHttpContextAccessor http, TimeProvider time)
    {
        _collector = collector;
        _http = http;
        _time = time;
    }

    public ILogger CreateLogger(string categoryName) => new DatabaseLogger(categoryName, _collector, _http, _time, () => _scopes);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }
}
