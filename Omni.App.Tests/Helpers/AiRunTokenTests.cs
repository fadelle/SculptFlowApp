using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Common.Helpers;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t4-ai-09: one ingest key was a master key for every client's AI tools, and the n8n webhooks had no auth.</summary>
public class AiRunTokenTests
{
    private const string Secret = "run-token-secret-for-tests";
    private static readonly Guid Client = Guid.NewGuid(), Bot = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_token_is_valid_only_for_its_client_and_bot_until_it_expires()
    {
        var token = AiRunToken.Issue(Secret, Client, Bot, Now);
        Assert.True(AiRunToken.IsValid(Secret, token, Client, Bot, Now));
        Assert.True(AiRunToken.IsValid(Secret, token, Client, Bot, Now + AiRunToken.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(AiRunToken.IsValid(Secret, token, Client, Bot, Now + AiRunToken.Lifetime + TimeSpan.FromSeconds(1)));
        Assert.False(AiRunToken.IsValid(Secret, token, Guid.NewGuid(), Bot, Now));   // another client
        Assert.False(AiRunToken.IsValid(Secret, token, Client, Guid.NewGuid(), Now));  // another bot
        Assert.False(AiRunToken.IsValid("other-secret", token, Client, Bot, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.abc.def")]
    [InlineData("v2.1793000000.sig")]
    [InlineData("v1.1793000000")]
    public void A_missing_or_malformed_token_is_refused(string? token) =>
        Assert.False(AiRunToken.IsValid(Secret, token, Client, Bot, Now));

    [Fact]
    public void A_token_with_a_far_future_expiry_is_refused_even_if_signed()
    {
        // Issued "now" by someone holding the secret is fine; a hand-made long expiry isn't accepted.
        var later = AiRunToken.Issue(Secret, Client, Bot, Now.AddDays(30));
        Assert.False(AiRunToken.IsValid(Secret, later, Client, Bot, Now));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" s3cret ", "s3cret")]
    public void The_secret_is_optional_and_trimmed(string? configured, string? expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AiRunToken.SecretKey] = configured }).Build();
        Assert.Equal(expected, AiRunToken.Secret(configuration));
    }

    [Theory]
    [InlineData("hook-secret", true)]
    [InlineData(null, false)]
    public async Task Posts_to_n8n_carry_the_webhook_secret_when_one_is_set(string? secret, bool expected)
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var http = new HttpClient(new CaptureHandler(async r => { sent = r; body = await r.Content!.ReadAsStringAsync(); }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [N8nWebhook.SecretKey] = secret }).Build();

        using var response = await N8nWebhook.PostAsync(http, "https://n8n.example/webhook/x", new { a = 1 }, new JsonSerializerOptions(JsonSerializerDefaults.Web), configuration, default);

        Assert.Equal(expected, sent!.Headers.TryGetValues(N8nWebhook.SecretHeader, out var values) && values.Single() == secret);
        Assert.Equal("{\"a\":1}", body);
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await capture(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
