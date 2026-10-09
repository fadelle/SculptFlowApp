using System.Net;
using System.Text;

namespace PlasticSurgery.Tests.Support;

/// <summary>
/// A scripted HttpMessageHandler: queue responses (or exceptions) in the order the code under test will call, or map a
/// URL to a responder. Every request is captured (with its body read up front, since the request is disposed afterwards).
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers);

    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _queue = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    public List<Captured> Requests { get; } = new();

    public FakeHttpHandler Enqueue(HttpStatusCode status, string body = "", string contentType = "application/json", Action<HttpResponseMessage>? configure = null)
    {
        _queue.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public FakeHttpHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _queue.Enqueue(responder);
        return this;
    }

    public FakeHttpHandler EnqueueThrow(Exception ex)
    {
        _queue.Enqueue(_ => throw ex);
        return this;
    }

    public FakeHttpHandler Always(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _fallback = responder;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers) headers[h.Key] = string.Join(",", h.Value);
        }
        Requests.Add(new Captured(request.Method, request.RequestUri!, body, headers));

        var responder = _queue.Count > 0 ? _queue.Dequeue() : _fallback
            ?? throw new InvalidOperationException($"FakeHttpHandler: no response queued for {request.Method} {request.RequestUri}");
        return responder(request);
    }

    public HttpClient CreateClient(string? baseAddress = null) =>
        new(this) { BaseAddress = baseAddress is null ? null : new Uri(baseAddress) };
}
