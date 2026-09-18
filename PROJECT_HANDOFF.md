# SculptFlow — Project Handoff

_Last updated: 2026-09-18. Written for session continuity — read this first after any context reset._

---

## 1. Product overview

SculptFlow is a multi-tenant SaaS platform for plastic surgery clinics. Core loop: a lead messages
a clinic on WhatsApp → an AI agent (orchestrated via n8n) carries the first-line conversation,
qualifies the lead, books consultations → staff can take over any conversation at any moment from a
dashboard Inbox → clinics can also run WhatsApp template campaigns (reactivation, reminders) to
existing leads. Every actual message — whether from the customer, the AI, staff, or a campaign —
lives in one unified conversation history per lead, in Postgres.

## 2. Current architecture

- **.NET 10**, ASP.NET Core Razor Pages (staff dashboard) + Web API controllers (REST, consumed by
  the dashboard's own JS and by n8n/Meta).
- **EF Core / Npgsql** against **Supabase PostgreSQL**. Schema is **hand-authored in
  `Database/schema.sql`, not EF Core migrations** — `ApplicationDbContext.OnModelCreating` must be
  kept in sync by hand every time the schema changes. This is a deliberate, stated project
  convention, not an oversight.
- **No Razor runtime compilation** — `.cshtml`/`.cs` edits require a full stop → `dotnet build` →
  `dotnet run` cycle. `wwwroot/js|css` are served live, no restart needed.
- **SignalR** (`Hubs/InboxHub.cs`, route `/hubs/inbox`) — PostgreSQL is the single source of truth;
  SignalR only tells connected clients "something changed," they always re-fetch via REST.
- **Hand-rolled CSS** (`wwwroot/css/site.css`) — no Bootstrap, no jQuery, dark sidebar admin layout.
- **ASP.NET Core Identity** for authentication (role-free) — see §5.
- **n8n** is the AI orchestration layer — see §8. It no longer sees raw Meta webhook payloads.
- **Meta WhatsApp Cloud API** — direct Graph API integration, Embedded Signup for onboarding.
- A scratchpad **`sqlrunner`** console utility (outside the repo, in the session's temp scratchpad)
  is used to run raw SQL against Supabase directly via `PG_CONN` env var — supports plain script
  execution, `--query <file>`, `--introspect`, `--counts`.

## 3. Database tables / entities

All tables are `clinic_id`-scoped except the Identity tables (scoped via `clinic_users` instead).

| Table | Purpose |
|---|---|
| `clinics` | Tenant root. Also carries `address`, `operating_hours`, `consultation_info` (free text, read by the AI's `get_clinic_info` tool). |
| `procedures` | Clinic's service catalog. |
| `leads` | A person of interest. `source`, `external_lead_id` (webhook dedup), `status`, `qualification_status`. |
| `conversations` | One per (lead, channel). `mode` (`ai`/`human`/`approval`), `last_customer_message_at`, `service_window_expires_at` (24h WhatsApp window — see §10). |
| `messages` | Every message, any origin, one table. `direction`, `sender_type`, `origin`, `channel`, `message_type`, `content`, `external_message_id` (idempotency), `delivery_status` + per-state timestamps (`delivered_at`/`read_at`/`failed_at`/`deleted_at`), `failure_code`/`failure_reason`, `metadata_json` (media/interactive/location payloads), `whatsapp_template_id`, `campaign_id`, `campaign_recipient_id`. |
| `appointments` | Consultations. `status`, `scheduled_start/end`. |
| `procedure_bookings` | A lead actually booking/undergoing a procedure. |
| `events` | Audit/analytics log. Free-text `event_type`. **Now has real FK relationships** to `leads`/`conversations`/`appointments` (fixed this session — see §17). |
| `channel_integrations` | Per-clinic connection config for WhatsApp/Instagram/Facebook — functions as "whatsapp_connections". Health fields: `account_status`, `account_review_status`, `phone_quality_rating`, `phone_status`, `name_status`, `is_healthy`, `health_level`, `last_problem_code/message`, `last_webhook_at`, `last_health_event_at`, plus `pin` (2-step verification PIN for phone registration). |
| `whatsapp_templates` | Message templates. `channel_integration_id`, `meta_template_id`, `status` (CHECK constraint **dropped** — tolerates new Meta statuses), `quality_rating`, `previous_category`/`current_category`, `components` (jsonb raw). |
| `campaigns` | Bulk template send campaigns. `status` (draft/scheduled/running/completed/cancelled/failed). |
| `campaign_recipients` | One row per lead per campaign. `status` lifecycle (pending→queued→sent→delivered/read/failed), links back to the `messages` row it produced. |
| `whatsapp_health_events` | Append-only history behind `channel_integrations`' current health state. |
| `clinic_users` | Links an Identity user to a clinic. `id, clinic_id, user_id (text), is_active`. |
| `identity_users`, `identity_user_claims`, `identity_user_logins`, `identity_user_tokens` | ASP.NET Core Identity's standard tables, role-free variant, remapped to snake_case. |

## 4. Multi-tenant model

Two independent resolution paths that both terminate at `clinic_id`:

```
LOGIN:    Authenticated User → clinic_users (is_active=true) → clinic_id
WEBHOOK:  Meta phone_number_id / WABA id → channel_integrations → clinic_id
```

- **Dashboard/API**: `ICurrentClinicContext` (`Services/ICurrentClinicContext.cs`) resolves clinic
  from the logged-in user. No controller/page accepts `clinicId` from the browser anymore.
- **Webhook**: `MetaWebhookProcessor.ResolveClinicAsync` resolves via `phone_number_id` (primary)
  or `waba_id` (fallback) against `channel_integrations`.
- **n8n/AI Agent** (`AiController`, and the AI branch of `ConversationsController.SendMessage`):
  **still takes `clinicId` explicitly as a trusted parameter**, gated only by the shared-secret
  `X-Ingest-Key` header — **not** derived server-side. This was explicitly flagged as a gap against
  the strictest "never trust clinicId from n8n/AI Agent" rule the user later stated; not yet fixed.
- **Enforced uniqueness** (added this session):
  - `channel_integrations`: `UNIQUE(clinic_id, channel)` (one WhatsApp connection per clinic) +
    `UNIQUE(phone_number_id) WHERE phone_number_id IS NOT NULL` (a number can't belong to two
    clinics). `whatsapp_business_id` deliberately **not** unique (one WABA can have multiple
    numbers).
  - `clinic_users`: `UNIQUE(clinic_id, user_id)` + `UNIQUE(user_id) WHERE is_active` (one active
    clinic membership per user).

## 5. Authentication / login

- **ASP.NET Core Identity**, role-free (`ApplicationDbContext : IdentityUserContext<IdentityUser>`
  — deliberately not `IdentityDbContext`, so no `AspNetRoles`/`AspNetUserRoles` tables exist at
  all). No owner/manager/receptionist/surgeon distinction anywhere — every clinic_users row grants
  full access to that clinic.
- Wired up via `AddIdentityCore<IdentityUser>()` + `.AddSignInManager()` + explicit
  `AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(...)` (not the full
  `AddIdentity<TUser,TRole>`, which would drag in roles).
- **Pages/Account/Login.cshtml**, **Register.cshtml**, **Logout.cshtml** — hand-rolled, styled with
  `site.css` (no Identity UI scaffolding, no Bootstrap). `Layout = null` on these pages (no
  sidebar). Register auto-links the new user to the single existing demo clinic via
  `IClinicContext.GetDefaultClinicAsync()` — this MVP has no clinic-creation wizard; that's the one
  remaining caller of the old `IClinicContext`.
- **`DashboardApiController`** (`Controllers/DashboardApiController.cs`) — base class with
  `[Authorize]` + a `GetClinicIdAsync()` helper, inherited by 8 controllers (Leads, Appointments,
  Procedures, Dashboard, ProcedureBookings, ChannelIntegrations, WhatsAppTemplates, Campaigns,
  WhatsAppHealth).
- **`ConversationsController`** does NOT inherit that base — `SendMessage` serves two callers on
  one route (staff via cookie, AI via `X-Ingest-Key` + `sender:"ai"` body field) so it can't be
  blanket-`[Authorize]`d; every other action on it is individually `[Authorize]`d.
- **`InboxHub`** is `[Authorize]`, resolves clinic via `ICurrentClinicContext` in `OnConnectedAsync`.
- Razor Pages: `AuthorizeFolder("/")` with explicit `AllowAnonymousToPage` for
  `/Account/Login`, `/Account/Register`, `/Error`.
- Test account still in the DB: `testauth@example.com` / `Test12345`, linked to clinic
  `abb02743-4563-4c5a-91e2-80508fb25a77` (slug `demo-clinic`).

## 6. WhatsApp integration

- **Embedded Signup**: full-page OAuth redirect (not the JS SDK popup — the popup's code is tied to
  an internal `redirect_uri` we can't reproduce server-side, causes OAuth subcode 36008; full-page
  redirect sidesteps this). `wwwroot/js/meta-connect.js` drives it.
- **`ChannelIntegrationService.ConnectWhatsAppAsync`** — exchanges the code, auto-discovers
  WABA/phone number via Graph API when the popup can't deliver them, **registers the phone number**
  for Cloud API use (`POST /{phone-number-id}/register` with an auto-generated 6-digit 2-step PIN —
  this was a missing step found and fixed this session; without it Meta won't let the number
  send/receive at all). Saves everything to `channel_integrations`, scoped to the logged-in user's
  clinic via `ICurrentClinicContext` (never trusts the request body's `ClinicId`).
- **`MetaGraphClient`** (`Services/MetaGraphClient.cs`) — all raw Meta Graph API HTTP calls live
  here: OAuth token exchange, WABA/phone discovery, template create/status, phone registration.
- **`WhatsAppService`** — the actual message-sending client (`SendTextMessageAsync`,
  `SendTemplateMessageAsync`), used by every send path (staff, AI, campaign) — no duplicated
  sending logic anywhere.
- Settings → Channels & Integrations page still has a **manual-entry fallback form** that does
  *not* call Meta's phone registration — known gap, only Embedded Signup registers the number.

## 7. Webhook GET/POST flow

Meta now posts **directly** to .NET (n8n is no longer in the inbound path):

```
Meta ──GET/POST──> Controllers/WhatsAppWebhookController.cs
                    Route: /api/integrations/whatsapp/webhook
                    No auth at all (Meta can't present any of our existing auth mechanisms)

GET  → reads hub.mode / hub.verify_token / hub.challenge
        compares hub.verify_token to config Meta:WebhookVerifyToken
        match + hub.mode=="subscribe" → returns hub.challenge as text/plain, HTTP 200
        else → 403

POST → [FromBody] JsonElement rawBody
        → IMetaWebhookProcessor.ProcessAsync(rawBody)
             (Integrations/WhatsApp/MetaWebhookProcessor.cs)
          → MetaWebhookParser.Parse (raw Meta JSON → IReadOnlyList<ParsedMetaEvent>)
               — the ONLY place that understands Meta's entry[].changes[].field/value shape
          → for each parsed event: resolve clinic (phone_number_id → waba_id fallback)
          → dispatch to Handlers/ (one per ParsedMetaEventKind):
               CustomerMessageHandler, BusinessAppEchoHandler, MessageStatusHandler,
               TemplateEventHandler, HealthEventHandler, HistoryHandler, AppStateSyncHandler,
               UnknownEventHandler
             — each Handler is a thin adapter that resolves/creates Lead+Conversation as needed,
               then calls the SAME existing services (IMessageService.IngestAsync,
               IWhatsAppTemplateService.ApplyMetaEventAsync, IWhatsAppHealthService.ApplyHealthEventAsync)
               that the old normalized per-domain endpoints called — zero duplicated persistence/
               SignalR logic.
          → returns ONE flat WhatsAppWebhookResponse (Processed, EventType, ShouldRunAi, ClinicId,
            WhatsAppConnectionId, ConversationId, LeadId, MessageId, Mode, MessageType, Content,
            SelectedValue, Status, TemplateId, HealthLevel, ...). When a payload contains multiple
            events, every one is persisted/broadcast, but the single response reflects the
            AI-eligible one if any exists, else the last one processed.
        Controller: if result.ShouldRunAi → call IAiTriggerNotifier (see §8); else nothing further.
        Always returns HTTP 200 to Meta (fast ack, regardless of n8n reachability).
```

**Interactive message normalization** (button_reply/list_reply): `messageText` is always the
user-visible title, never raw Meta JSON. `selectedValue` carries the stable Meta reply id
separately (e.g. `messageText: "Rhinoplasty"`, `selectedValue: "rhinoplasty"`). Verified live.

`Controllers/WhatsAppIntegrationEventsController.cs` no longer has a `webhook` action (removed to
avoid a route collision with the new controller) — it still owns `POST templates/events` and
`POST health/events`, ingest-key protected, for already-normalized/tooling callers.

**Not implemented**: Meta's `X-Hub-Signature-256` HMAC body-signature verification. The GET
handshake only covers one-time subscription setup, not per-delivery authenticity — currently any
POST body is accepted. Flagged, not requested/built.

## 8. n8n / AI flow

```
Before:  Meta → n8n (classifies + forwards raw JSON) → .NET
Now:     Meta → .NET (classifies + processes) → n8n (normalized trigger, AI-eligible only)
```

**Outbound call to n8n** (`Services/IAiTriggerNotifier.cs`), POSTed to config `N8n:AiWebhookUrl`
(**currently unset** — real n8n URL still needed), exact shape:
```json
{
  "clinicId": "...", "conversationId": "...", "leadId": "...", "messageId": "...",
  "channel": "whatsapp", "messageType": "text|interactive", "messageText": "...",
  "selectedValue": "..."
}
```
Never throws (Meta still needs its fast 200 regardless of n8n's reachability) — logs a warning and
skips if the URL isn't configured or the call fails.

**AI reply flow** (unchanged across all this session's refactors): n8n's AI Agent finishes by
calling `POST /api/conversations/{conversationId}/messages/send` with
`{ "content": "...", "sender": "ai" }` + `X-Ingest-Key` header + `?clinicId=...` query param. This
is handled by the SAME route staff use, branching internally:
- `sender != "ai"` → staff path, requires login cookie, resolves clinic via `ICurrentClinicContext`,
  flips conversation to human mode.
- `sender == "ai"` → requires `X-Ingest-Key`, takes `clinicId` from query (trusted via the key, not
  derived), calls `MessageService.SendAiReplyAsync`, which **rechecks `conversation.mode == ai`
  fresh from the database immediately before sending** (a human may have taken over while the AI
  was "thinking") — returns `409 { sent:false, code:"conversation_in_human_mode" }` if not, never
  flips mode on success.

**AI tool surface** — `Controllers/AiController.cs`, route `/api/ai/*`, `[RequireIngestKey]`. 9 of
10 originally planned tools implemented: `get_clinic_info`, `get_procedures`, `get_lead_context`,
`update_lead` (identity fields like name/phone/email excluded on purpose), `get_available_slots`,
`book_consultation`, `reschedule_consultation`, `cancel_consultation`, `handoff_to_human`.
**Not implemented**: `get_approved_clinic_answer` (knowledge-base search) — no FAQ/knowledge table
exists yet, explicitly deferred.

## 9. SignalR

Single hub: `Hubs/InboxHub.cs`, route `/hubs/inbox`, `[Authorize]`. Clients join
`clinic:{clinicId}` groups (server-resolved via `ICurrentClinicContext`, never client-supplied) and
optionally `conversation:{conversationId}` groups. Events (`Services/IInboxNotifier.cs`):

| Event | Fired by | Consumed by |
|---|---|---|
| `NewMessage` | Any message send/ingest path | `inbox.js` |
| `MessageStatusUpdated` | Delivery status webhook | `inbox.js` (ticks: ✓/✓✓/✓✓ blue/⚠) |
| `ConversationUpdated` | Conversation closed | `inbox.js` |
| `ConversationModeChanged` | Take over / return to AI / staff or AI send | `inbox.js` |
| `WhatsAppTemplateUpdated` | Template status/quality change | `wwwroot/js/whatsapp-templates.js` |
| `WhatsAppHealthUpdated` | Health event applied | `wwwroot/js/whatsapp-health.js` (Health page + dashboard indicator) |

## 10. Inbox / conversation modes

- `Conversation.Mode`: `ai` / `human` / `approval`. Legacy `AiEnabled`/`HumanTakeover` booleans kept
  in sync via `ConversationModeSync.Apply(conversation, mode)` — the single place that changes mode.
- `Message.Origin`: `whatsapp_customer`, `whatsapp_business_app` (Coexistence echo),
  `dashboard` (staff), `ai`, `system`, `campaign`. `SenderType`: `lead`/`ai`/`staff`/`system`.
  `Direction`: `inbound`/`outbound`.
- **24h WhatsApp service window**: `Conversation.LastCustomerMessageAt` /
  `ServiceWindowExpiresAt`, `IsServiceWindowOpen(now)` helper. Reset **only** by a genuine
  `whatsapp_customer` inbound message — staff/AI/campaign sends never touch it. Enforced
  server-side (`MessageService.SendAsync` throws `ServiceWindowClosedException`), not just hidden
  in the UI. When closed, Inbox composer disables and shows a "Send Template" panel instead
  (`inbox.js`).
- A manually-sent template from the Inbox is `origin=dashboard` + `message_type=template` — `origin
  =campaign` is reserved strictly for actual Campaign-initiated sends.

## 11. Implemented features (chronological, this engagement)

1. WhatsApp Templates + Campaigns + 24h service-window enforcement (backend + Razor UI)
2. AI agent tool surface (`AiController`) + Settings → Clinic Info page
3. WhatsApp phone number registration (2-step PIN) on Embedded Signup connect
4. Webhook event routing for Inbox delivery states, Templates, Health (initially 3 separate
   n8n-facing normalized endpoints)
5. Consolidated into one raw Meta webhook endpoint + parser/processor/handler architecture
6. Corrected business-app-echo detection to use Meta's real `smb_message_echoes` field; added
   `history`/`smb_app_state_sync` handling
7. ASP.NET Core Identity + `clinic_users` + `CurrentClinicContext`; migrated every controller/page
   off browser-supplied `clinicId`
8. DB uniqueness constraints (`phone_number_id`, one-active-clinic-per-user)
9. Split webhook entry point so Meta calls .NET directly; n8n now gets only normalized AI triggers
10. Interactive-message normalization (`messageText`/`selectedValue`)

## 12. Campaign / reactivation design

- `CampaignService` — creates a `Campaign` + one `CampaignRecipient` per (deduped) lead, all
  `Pending`. Never sends synchronously for the whole list.
- **Bounded batch processing**: `ProcessBatchAsync(batchSize=20 default)` processes a small batch of
  `Queued` recipients per call — meant to be called repeatedly (dashboard "Send more" button, or an
  n8n schedule hitting `POST /api/campaigns/{id}/process-batch`) until `CampaignCompleted` is true.
  `SendAsync` transitions Draft/Scheduled → Running (queues every Pending recipient) then processes
  the first batch.
- **Per-recipient variable mapping**: `Campaigns/Create.cshtml` lets each `{{n}}` placeholder box
  contain literal text (same for every recipient) or a token (`{LeadFullName}`, `{LeadFirstName}`,
  `{LeadPhone}`) resolved per-lead server-side at creation time into `VariablesByLeadId`.
  Intentionally no per-row input grid.
- Every actual send goes through `MessageService.SendCampaignTemplateAsync` (shares the same core
  as the Inbox's template send, just stamps `origin=campaign` + links `CampaignId`/
  `CampaignRecipientId`) — writes a normal `Message` row in the lead's own Conversation. **No
  separate CampaignMessage table exists or should ever be created.**
  `CampaignService.ProcessRecipientAsync` resolves/creates the lead's Conversation via
  `IConversationService.GetOrCreateForLeadAsync` first.
  `CampaignStatsResponse` (TotalRecipients/Sent/Delivered/Read/Failed/Replied) is always computed
  live from `CampaignRecipient` rows — never cached on `Campaign`.
- Delivery/read/failed status webhooks roll into the linked `CampaignRecipient` automatically
  (`MessageService.HandleStatusUpdateAsync` → `ApplyDeliveryStatusToRecipient`) — still just one
  `Message` row, no second write path.
- A campaign reply from the customer is a completely normal inbound message in the same
  Conversation — there is no special "campaign conversation."

## 13. Pending work

- Set the real `N8n:AiWebhookUrl` (currently unset, notifier gracefully no-ops with a warning)
- Close the `clinicId` trust gap for `AiController`/AI-send: derive clinic from existing
  `leadId`/`conversationId` records server-side instead of accepting it as a trusted parameter
- Meta `X-Hub-Signature-256` webhook signature verification (real per-delivery authenticity check)
- Knowledge-base search AI tool (`get_approved_clinic_answer`) — no schema/design started
- Manual-entry Settings form doesn't register the phone number (only Embedded Signup does)
- Multi-clinic signup — Register currently hardcodes new users into the single default clinic; no
  clinic-creation wizard exists

## 14. Important constraints / decisions

- **Reuse over new tables**: `channel_integrations` plays the role of "whatsapp_connections" —
  deliberately not a separate table, to avoid duplicating clinic/WABA/phone_number_id/access_token
  concerns. Same for `Message` — no `CampaignMessage` table, ever.
- **PostgreSQL is the source of truth; SignalR only notifies.** Every client handler re-fetches from
  REST rather than trusting the SignalR payload's content as authoritative.
- **No role-based authorization in this MVP** — explicit user decision. Every `clinic_users` row is
  full access, no owner/manager/receptionist/surgeon distinction, no permissions matrix.
- **Central sending service** — `WhatsAppService` is the only thing that calls Meta's send API;
  staff/AI/campaign paths all funnel through `MessageService`'s shared core methods, never
  duplicate the HTTP call.
- **Idempotency**: messages by `(clinic_id, channel, external_message_id)`; templates upsert by
  `(clinic_id, meta_template_id)` falling back to `(clinic_id, name, language)`; health events by
  `(channel_integration_id, event_type, occurred_at)`.
- **n8n holds no business state** — every decision (is this AI-eligible, what's the current mode)
  is computed in .NET and hard-coded into the response; n8n only branches on `shouldRunAi`.
- **Standing operational instructions** (persistent memory, apply going forward): always leave the
  app running after any change unless explicitly told to shut down; `.cshtml`/`.cs` edits need a
  full stop → build → run cycle, `wwwroot` JS/CSS changes are live with no restart.
- Windows **Smart App Control** was intermittently blocking local `dotnet run` builds this session
  (`FileLoadException`, Code Integrity policy) — the user turned it off to resolve. Worth knowing if
  build/run issues resurface on this machine.

## 15. Current API endpoints

**Dashboard-facing** (require login, clinic resolved server-side via `ICurrentClinicContext`):
- `GET/PATCH /api/leads`, `GET /api/leads/{id}`, `POST /api/leads/{id}/status` (`Create` is
  `[AllowAnonymous]` — n8n/Meta intake, not currently ingest-key protected — pre-existing gap)
- `GET/POST /api/appointments`, `PATCH /api/appointments/{id}/status`, `GET /api/appointments/available`
- `GET/POST /api/procedures`
- `GET /api/dashboard/summary|leads|appointments|procedures`
- `POST /api/procedure-bookings`, `PATCH /api/procedure-bookings/{id}`, `GET /api/procedure-bookings`
- `GET/PUT /api/channel-integrations`, `POST /api/channel-integrations/{channel}/disconnect`,
  `POST /api/channel-integrations/whatsapp/connect`, `POST /api/channel-integrations/facebook/connect`,
  `POST /api/channel-integrations/whatsapp/debug-token` (debug-only)
- `GET/POST /api/whatsapp/templates`, `GET /api/whatsapp/templates/{id}`,
  `POST /api/whatsapp/templates/{id}/sync`
- `GET/POST /api/campaigns`, `GET /api/campaigns/{id}`, `POST /api/campaigns/{id}/schedule|send|process-batch|cancel`
- `GET /api/whatsapp/health`, `GET /api/whatsapp/health/events`
- `GET/POST /api/conversations`, `GET /api/conversations/{id}`, `GET /api/conversations/{id}/messages`,
  `POST /api/conversations/{id}/messages`, `POST /api/conversations/{id}/messages/send` (dual-mode,
  see §8), `POST /api/conversations/{id}/messages/send-template`, `POST /api/conversations/{id}/take-over|return-to-ai|close`

**Trusted server-to-server** (`[RequireIngestKey]`, shared-secret header, no login):
- `POST /api/messages/ingest` (legacy normalized message/status ingest — still valid, unused by new webhook flow)
- `POST /api/integrations/whatsapp/templates/events`, `POST /api/integrations/whatsapp/health/events`
  (normalized, for tooling/tests — not the primary Meta path anymore)
- `GET/PATCH/POST /api/ai/*` — 9 AI tool endpoints (see §8)

**Public, no auth at all** (Meta calls these directly):
- `GET/POST /api/integrations/whatsapp/webhook`

**Razor Pages** (all require login except `/Account/Login`, `/Account/Register`, `/Error`):
`/dashboard`, `/dashboard/leads`, `/dashboard/appointments`, `/inbox`, `/settings/integrations`,
`/settings/clinic-info`, `/WhatsApp/Templates`, `/WhatsApp/Health`, `/Campaigns`,
`/Campaigns/Create`, `/Campaigns/{id}`, `/Account/Login`, `/Account/Register`, `/Account/Logout`.

## 16. Current configuration keys

Via `dotnet user-secrets` (from `PlasticSurgery/PlasticSurgery/`):
- `ConnectionStrings:Postgres` — Supabase connection string (session pooler host, not direct — IPv6 issue)
- `Meta:AppSecret` — Meta app secret
- `Meta:WebhookVerifyToken` — GET handshake token (set this session: `792b52f643d517cade1eed2fdf4bba93`)
- `N8n:IngestApiKey` — shared secret for `[RequireIngestKey]`-protected endpoints (`98e2fb3f66604768a4bd658b32e9a952`)
- `N8n:AiWebhookUrl` — **not set** — n8n's AI-trigger webhook URL, needed for §8 to actually fire

Via `appsettings.json`/`appsettings.Development.json` (non-secret):
- `Clinic:DefaultSlug` — which clinic Register.cshtml links new users to (default `"demo-clinic"`)
- `Meta:AppId`, `Meta:GraphApiVersion`, `Meta:WhatsAppLoginConfigId`, `Meta:FacebookLoginConfigId`

## 17. Recent migrations (schema.sql, all applied live to Supabase, chronological)

1. Service window columns on `conversations`; `whatsapp_templates`, `campaigns`,
   `campaign_recipients` tables; `messages` gains `whatsapp_template_id`/`campaign_id`/
   `campaign_recipient_id`; `origin` CHECK extended with `'campaign'`
2. `clinics` gains `address`/`operating_hours`/`consultation_info` (AI tool surface)
3. `channel_integrations` gains `pin` (phone registration)
4. `messages` gains per-status timestamps/failure fields/`metadata_json`; `channel_integrations`
   gains health fields; `whatsapp_templates` gains `channel_integration_id`/`quality_rating`/
   `components`/etc., **status/category CHECK constraints dropped**; new `whatsapp_health_events` table
5. `events` gains real FK constraints to `leads`/`conversations`/`appointments`/`clinics` (bug fix —
   EF had no dependency info, could insert an `events` row before its referenced `leads` row in the
   same `SaveChanges` batch)
6. Identity tables (`identity_users`, `identity_user_claims`, `identity_user_logins`,
   `identity_user_tokens`) + `clinic_users`
7. Unique partial index on `channel_integrations.phone_number_id`; unique partial index on
   `clinic_users.user_id WHERE is_active`

## 18. Known TODOs

- [ ] Set real `N8n:AiWebhookUrl`
- [ ] Derive `clinicId` server-side for `AiController`/AI-send instead of trusting the parameter
- [ ] Implement Meta `X-Hub-Signature-256` verification on the webhook POST endpoint
- [ ] Design + build knowledge-base search (`get_approved_clinic_answer`)
- [ ] Wire phone registration into the Settings manual-entry connect form (currently Embedded-Signup-only)
- [ ] Decide on real multi-clinic signup (currently every new user → the one default clinic)
- [ ] Consider protecting `LeadsController.Create` (currently `[AllowAnonymous]`, no ingest-key —
      pre-existing gap, not introduced this session but never closed either)
