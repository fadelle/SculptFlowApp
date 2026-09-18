namespace PlasticSurgery.Services;

public record FacebookPageInfo(string PageId, string PageName, string PageAccessToken);
public record WhatsAppPhoneNumberInfo(string DisplayPhoneNumber, string? VerifiedName);
public record ClientWabaInfo(string BusinessId, string BusinessName, string WabaId, string WabaName, string Relationship);

/// <summary>
/// Thin wrapper around the Meta Graph API calls needed to turn a Facebook Login / WhatsApp
/// Embedded Signup result into something we can store: exchanging the short-lived code/token the
/// browser hands us for real access tokens, then looking up the Page/phone number it grants
/// access to. See Controllers/ChannelIntegrationsController.cs for how these get used.
/// </summary>
public interface IMetaGraphClient
{
    /// <summary>
    /// Exchanges the authorization code FB.login() returns (response_type: 'code') for a user
    /// access token. redirectUri must exactly match the page URL FB.login() was called from
    /// (Meta implicitly associates the code with that URL even for popup-based JS SDK logins) —
    /// pass null only for flows that don't need it.
    /// </summary>
    Task<string> ExchangeCodeForTokenAsync(string code, string? redirectUri = null, CancellationToken ct = default);

    /// <summary>Exchanges a short-lived user token for a long-lived one (~60 days, effectively non-expiring for Page tokens derived from it).</summary>
    Task<string> GetLongLivedTokenAsync(string shortLivedToken, CancellationToken ct = default);

    /// <summary>Returns the first Facebook Page the logged-in user manages, with that Page's own (long-lived) access token.</summary>
    Task<FacebookPageInfo?> GetFirstManagedPageAsync(string userAccessToken, CancellationToken ct = default);

    /// <summary>Looks up display info for a WhatsApp phone number (from Embedded Signup).</summary>
    Task<WhatsAppPhoneNumberInfo?> GetWhatsAppPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken ct = default);

    /// <summary>DEBUG-ONLY: asks Meta what scopes/permissions a token actually carries (via /debug_token).</summary>
    Task<IReadOnlyList<string>> GetGrantedScopesAsync(string accessToken, CancellationToken ct = default);

    /// <summary>
    /// Fallback discovery for when there's no popup postMessage to read the WABA/phone number
    /// from (e.g. the full-page-redirect flow): walks /me/businesses → for each business,
    /// /{business_id}/client_whatsapp_business_accounts — the WABAs a customer has shared with
    /// this business as a Tech Provider/partner.
    /// </summary>
    Task<IReadOnlyList<ClientWabaInfo>> GetClientWhatsAppBusinessAccountsAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Lists the phone numbers registered under a WhatsApp Business Account.</summary>
    Task<IReadOnlyList<WabaPhoneNumberInfo>> GetPhoneNumbersForWabaAsync(string wabaId, string accessToken, CancellationToken ct = default);

    /// <summary>
    /// Registers a phone number for Cloud API use (POST /{phone-number-id}/register) with a
    /// 6-digit two-step-verification PIN — required after Embedded Signup before the number can
    /// actually send/receive messages via the Cloud API. Safe to call again on an already-registered
    /// number (Meta treats it as (re)confirming/updating the PIN), so ConnectWhatsAppAsync calls it
    /// on every connect rather than only the first time.
    /// </summary>
    Task RegisterPhoneNumberAsync(string phoneNumberId, string accessToken, string pin, CancellationToken ct = default);

    /// <summary>
    /// Submits a new WhatsApp message template to Meta for review (POST /{waba-id}/message_templates).
    /// Meta's response includes its own template id and an initial status (usually "PENDING").
    /// See WhatsAppTemplateService.CreateAsync for how the template body/buttons get turned into components.
    /// </summary>
    Task<MetaTemplateCreateResult> CreateMessageTemplateAsync(
        string wabaId, string accessToken, string name, string category, string language,
        object components, CancellationToken ct = default);

    /// <summary>Refreshes a submitted template's current review status/rejection reason from Meta.</summary>
    Task<MetaTemplateStatusResult> GetMessageTemplateStatusAsync(string metaTemplateId, string accessToken, CancellationToken ct = default);
}

public record WabaPhoneNumberInfo(string PhoneNumberId, string DisplayPhoneNumber, string? VerifiedName);
public record MetaTemplateCreateResult(string Id, string Status);
public record MetaTemplateStatusResult(string Status, string? RejectedReason);
