namespace PlasticSurgery.Entities.Requests.Calendars;

public record SetCalendarSyncEnabledRequest(bool Enabled);

// ---------------------------------------------------------------------------------------------
// n8n contract — Business/Contracts/HttpClients/N8n/ICalendarSyncNotifier.cs sends this; CalendarIntegrationsIngestController receives the
// matching callback. Field names/casing match what n8n expects (System.Text.Json Web defaults: camelCase), same
// convention as AiTriggerPayload. Connect/disconnect/list-calendars no longer go through n8n at all — SculptFlow
// owns that OAuth relationship directly (see ICalendarProviderClient, CalendarOAuthController). n8n's only remaining
// job is executing the actual create/update/cancel call against the provider's calendar API, using a SculptFlow-
// issued access token that is already fresh by the time this is sent.
// ---------------------------------------------------------------------------------------------
