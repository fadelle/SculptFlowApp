using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Entities.Dtos.Infobip;

namespace PlasticSurgery.Business.Contracts.HttpClients.Infobip;

/// <summary>
/// Thin client for the Infobip REST API — SculptFlow's own single Infobip account (Infobip:BaseUrl +
/// Infobip:ApiKey, set as env vars Infobip__BaseUrl / Infobip__ApiKey, never in appsettings or the DB). Every
/// clinic is just a different sender number on that account.
///
/// Auth is the "Authorization: App {apiKey}" header. The key is never logged: HttpClient logging only records
/// method + URL (no headers), and this class only ever logs status codes and Infobip's error ids — never
/// request bodies (patient text) or recipient numbers.
///
/// Retries: a send is retried (Infobip:MaxSendAttempts, default 3, short backoff) only when Infobip clearly
/// didn't take it — HTTP 429 / 503, or a connection that failed before any response. Everything else (4xx,
/// other 5xx, a timeout after the request went out) is NOT retried, because the message may already be on its
/// way and a retry could send it twice. Each attempt reuses the same caller-chosen messageId.
/// </summary>
public interface IInfobipClient
{
    /// <summary>True when Infobip:BaseUrl and Infobip:ApiKey are both set.</summary>
    bool IsConfigured { get; }

    Task<InfobipSendResult> SendWhatsAppTextAsync(InfobipWhatsAppTextMessage message, CancellationToken ct = default);

    Task<InfobipSendResult> SendWhatsAppTemplateAsync(InfobipWhatsAppTemplateMessage message, CancellationToken ct = default);

    /// <summary>GET /whatsapp/1/senders/{sender}/business-info — proves the number is a registered WhatsApp
    /// sender on SculptFlow's Infobip account. Null when Infobip says it isn't (400/403/404).</summary>
    Task<InfobipSenderInfo?> GetWhatsAppSenderAsync(string sender, CancellationToken ct = default);

    /// <summary>POST /whatsapp/2/senders/{sender}/templates — submits a template for WhatsApp review. Retried only
    /// like a send (429/503/no connection), since a second create of the same name would be refused.</summary>
    Task<InfobipTemplateInfo> CreateWhatsAppTemplateAsync(string sender, object template, CancellationToken ct = default);

    /// <summary>GET /whatsapp/2/senders/{sender}/templates/{id}.</summary>
    Task<InfobipTemplateInfo> GetWhatsAppTemplateAsync(string sender, string templateId, CancellationToken ct = default);
}
