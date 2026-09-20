# SculptFlow (MySculptFlow) — Project Handoff

_Last updated: 2026-09-19 (Telegram added). Written for session continuity — read this first after any context reset.
This is the ONE handoff/state file; update it in place, don't create another._

> **No secrets live in this file.** Earlier versions of it (git commit `2aec468`, already on GitHub)
> contained the real `Meta:WebhookVerifyToken` and `N8n:IngestApiKey` values — they were removed here
> but remain in git history, so **rotate both** (see §22). Config key *names* are listed in §20; values
> live in `dotnet user-secrets` locally and in Render env vars in production.

---

## 1. Product overview

SculptFlow (UI brand: **MySculptFlow**) is a multi-tenant SaaS platform for plastic surgery clinics. Core
loop: a lead messages a clinic on WhatsApp → an AI agent (orchestrated via n8n) carries the first-line
conversation, answers from the clinic's Knowledge Base, qualifies the lead, books consultations → staff
can take over any conversation at any moment from a dashboard Inbox → clinics run WhatsApp template
campaigns (especially **old-lead reactivation**) to existing leads. Every actual message — customer, AI,
staff, or campaign — lives in one unified conversation history per lead, in Postgres.

## 2. Current architecture

- **.NET 10**, ASP.NET Core Razor Pages (staff dashboard) + Web API controllers (REST, consumed by the
  dashboard's own JS and by n8n/Meta). Assembly/DLL name is **`PlasticSurgery`** (`PlasticSurgery.dll`);
  only user-visible text was renamed to MySculptFlow — namespaces/project names were left alone.
- **EF Core / Npgsql** against **Supabase PostgreSQL**. Schema is **hand-authored in `Database/schema.sql`,
  not EF Core migrations** (no `Migrations/` folder exists) — each change is an appended idempotent SQL
  block, applied live to Supabase, and `ApplicationDbContext.OnModelCreating` is kept in sync by hand.
  Deliberate project convention.
- **No Razor runtime compilation** — `.cshtml`/`.cs` edits need stop → `dotnet build` → `dotnet run`.
  `wwwroot/js|css` are served live.
- **SignalR** (`Hubs/InboxHub.cs`, `/hubs/inbox`) — PostgreSQL is the source of truth; SignalR only says
  "something changed", clients re-fetch via REST.
- **Hand-rolled CSS** (`wwwroot/css/site.css`) — no Bootstrap/jQuery, dark sidebar admin layout. Small
  vanilla-JS files in `wwwroot/js` (no frontend framework/libs): `inbox.js`, `meta-connect.js`,
  `whatsapp-templates.js`, `whatsapp-health.js`, `campaign-audience.js`, `multi-select.js`,
  `status-help.js`.
- **Razor Pages call services directly** (DI); API controllers expose the same services over HTTP for JS,
  n8n/AI and tooling. Business logic lives in `Services/`, never in pages/controllers.
- **ASP.NET Core Identity** (role-free) for auth — §5. **n8n** = AI orchestration — §8.
  **Meta WhatsApp Cloud API** — direct Graph API integration, Embedded Signup.
- **Hosting**: Docker image → **Render** (`https://sculptflowapp.onrender.com`); code on GitHub — §15.
- **pgvector** (Postgres extension, v0.8.2 already installed on Supabase) powers the Knowledge Base — §11.
- Scratchpad tools (NOT in the repo, in the session temp scratchpad): **`sqlrunner`** (C# console: run SQL
  file, `--query <file>`, `--introspect`, `--counts`, reads `PG_CONN`) and **`fakeembed`** (tiny local
  OpenAI-compatible embeddings stub used to test the Knowledge Base without a real API key).

## 3. Database tables / entities

All tables are `clinic_id`-scoped except Identity tables (scoped via `clinic_users`).

| Table | Purpose |
|---|---|
| `clinics` | Tenant root. `name` is shown in the sidebar. Also `address`, `operating_hours`, `consultation_info` (free text, read by AI `get_clinic_info`). |
| `procedures` | Clinic service catalog: `name`, `code`, `description`, `consultation_duration` (minutes), `is_active`. Managed at `/Procedures` — §12. Never hard-deleted. |
| `leads` | A person of interest. `source` (free text, no CHECK), `external_lead_id` (dedup), `status`, `qualification_status` (both CHECK-constrained), `procedure_id`, `last_contact_at`, `marketing_opt_in`/`opted_out_at` (the contactability flags — there is **no** `do_not_contact`/`contact_consent` column), `campaign_name` (acquisition campaign — NOT related to outbound SculptFlow campaigns). |
| `conversations` | One per (lead, channel). `mode` (`ai`/`human`/`approval`), `last_customer_message_at`, `service_window_expires_at` (24h WhatsApp window — §10). CHECK on `channel`: `whatsapp,instagram,website,facebook,sms,email,telegram` (Telegram: `external_thread_id` = chat id). |
| `messages` | Every message, one table: `direction`, `sender_type`, `origin`, `channel`, `message_type`, `content`, `external_message_id` (idempotency), delivery status + timestamps, `failure_code/reason`, `metadata_json`, `whatsapp_template_id`, `campaign_id`, `campaign_recipient_id`. |
| `appointments` | Consultations. `status`: `booked, confirmed, attended, no_show, canceled, rescheduled`. |
| `procedure_bookings` | A lead considering/booked/completing a procedure. |
| `events` | Audit/analytics log (real FKs to leads/conversations/appointments/clinics). |
| `channel_integrations` | Per-clinic connection config for WhatsApp/Instagram/Facebook (acts as "whatsapp_connections"). CHECK `channel in ('whatsapp','instagram','facebook','telegram')`. Fields include `access_token` (Telegram: bot token), `webhook_verify_token` (Telegram: webhook secret), `phone_number_id`, `whatsapp_business_id`, `page_id`, `pin`, health fields, and Telegram's `telegram_bot_id`, `telegram_bot_username`, `webhook_status`, `webhook_registered_at` — §23. |
| `whatsapp_templates` | Message templates (status/category CHECKs dropped). |
| `campaigns` | Outbound campaigns. `campaign_type` (free string), `channel` (CHECK `whatsapp,instagram,messenger`), `whatsapp_template_id` (nullable), `audience_type` (CHECK `all_eligible,reactivation_no_consultation,custom`), `audience_filters` (jsonb), `status` (`draft,scheduled,running,paused,completed,cancelled,failed`). |
| `campaign_recipients` | Audience **snapshot**, one row per lead per campaign (`UNIQUE(campaign_id, lead_id)`). `status`: `pending,queued,sent,delivered,read,replied,booked,failed,skipped`; `skip_reason`, `failure_code`, `failure_reason`, `external_message_id`, `appointment_id`, `replied_at`, `booked_at`, plus sent/delivered/read/failed timestamps; `message_id` → the outbound `messages` row. |
| `whatsapp_health_events` | Append-only WhatsApp health history. |
| `knowledge_documents` | Staff-entered knowledge (title, category, content, `is_active`) — §11. |
| `knowledge_chunks` | Embedded slices of documents; `embedding vector(1536)` — §11. |
| `knowledge_search_settings` | One row per clinic of KB retrieval/embedding settings — §11. |
| `clinic_users` | Links an Identity user to a clinic (`UNIQUE(clinic_id,user_id)` + `UNIQUE(user_id) WHERE is_active`). |
| `identity_users`, `identity_user_claims`, `identity_user_logins`, `identity_user_tokens` | ASP.NET Identity tables, role-free, snake_case. |

Clinic `demo-clinic` = `abb02743-4563-4c5a-91e2-80508fb25a77` ("Demo Aesthetic Clinic"). Seed data
(`Database/seed.sql`): 5 procedures (Rhinoplasty, Breast Augmentation, Facelift, Liposuction, Tummy Tuck)
and 5 test leads.

## 4. Multi-tenant model

Two independent resolution paths that terminate at `clinic_id`:

```
LOGIN:    Authenticated User → clinic_users (is_active=true) → clinic_id      (dashboard, pages, dashboard APIs)
WEBHOOK:  Meta phone_number_id / WABA id → channel_integrations → clinic_id   (WhatsApp inbound)
```

- `ICurrentClinicContext` resolves the clinic from the logged-in user. **No page/dashboard controller
  accepts `clinicId` from the browser**; request bodies' `ClinicId` is overwritten server-side.
- `AiController` and the AI branch of `ConversationsController.SendMessage` still take `clinicId` as a
  trusted parameter gated only by the `X-Ingest-Key` shared secret (known gap, not fixed).
- New Knowledge Base / Procedures / settings data are all clinic-scoped; verified with a second temp
  clinic in tests (404 across clinics; search never crosses clinics).
- Enforced uniqueness: `channel_integrations` `UNIQUE(clinic_id,channel)` + unique `phone_number_id`
  (WABA id deliberately NOT unique); `clinic_users` as above; `campaign_recipients (campaign_id,lead_id)`;
  `knowledge_search_settings(clinic_id)`; procedure **name unique per clinic (case-insensitive) is
  enforced in the service**, not by a DB constraint.

## 5. Authentication / login

- ASP.NET Core Identity, **role-free** (`ApplicationDbContext : IdentityUserContext<IdentityUser>`); no
  owner/manager/etc. — every `clinic_users` row has full access to its clinic. `AddIdentityCore` +
  explicit cookie auth. Password policy: non-alphanumeric not required.
- Pages `Account/Login|Register|Logout` are hand-rolled (`Layout = null`); restyled this session: centered
  brand row (`.auth-brand`), full-width taller submit button (`.auth-submit`), centered footer text.
  **Registration = NEW USER → NEW CLINIC** (open signup; uncommitted): fields Full name, Clinic name, Email,
  Password, Confirm password. `ClinicRegistrationService` runs ONE transaction: Identity user → `full_name`
  claim (identity_user_claims) → `clinics` row (slug from the name — lowercase ASCII, accents stripped,
  hyphenated, `-2`/random suffix on collision; `email` = the user's email, phone/address blank) →
  `clinic_users` membership → default `knowledge_search_settings` row; any failure rolls everything back (no
  orphan user/clinic; verified with an injected mid-transaction failure). Then it signs the user in and redirects to
  `/dashboard`. Registration can never join an existing clinic (joining will be a separate invitation/staff flow —
  not built). `Clinic:DefaultSlug` and `IClinicContext.GetDefaultClinicAsync` were removed. A new clinic starts
  fully empty and isolated (verified: no demo/other-clinic leads, conversations, messages, appointments,
  procedures, KB, campaigns or channel connections; 404 on every cross-clinic id). **No email verification,
  CAPTCHA or rate limiting** on signup yet — anyone who can reach the site can create a clinic.
  **Staff page** (`/Staff`, sidebar "Staff"; uncommitted): read-only list of the current clinic's `clinic_users`
  (name from the `full_name` claim, email, Active/Inactive, joined date, "You" badge) via `IStaffService`;
  `GET /api/staff` (login required, clinic from `ICurrentClinicContext`). No add/remove/invite yet.
- `DashboardApiController` = `[Authorize]` base + `GetClinicIdAsync()`; used by the dashboard API
  controllers (Leads, Appointments, Procedures, Dashboard, ProcedureBookings, ChannelIntegrations,
  WhatsAppTemplates, Campaigns, WhatsAppHealth, **Knowledge**). `ConversationsController` is separate
  (dual-mode `SendMessage`). `InboxHub` is `[Authorize]`. Razor Pages: `AuthorizeFolder("/")` with
  anonymous Login/Register/Error.
- Accounts in the DB: `testauth@example.com` (test account) and `fadelle38@gmail.com` (the owner's
  account, created via the Register page). Passwords are intentionally not recorded here.
- Operational note: the owner's own PowerShell may run under a different profile and not see the
  `dotnet user-secrets` store (it reported "No secrets configured"); Claude Code's shell can. Secrets file:
  `%APPDATA%\Microsoft\UserSecrets\fd2e992d-9bcf-48ed-9103-5e4aaf375724\secrets.json`.

## 6. WhatsApp integration

- **Embedded Signup**: full-page OAuth redirect (not the JS SDK popup — popup's `redirect_uri` can't be
  reproduced server-side → OAuth subcode 36008). `wwwroot/js/meta-connect.js`.
- `ChannelIntegrationService.ConnectWhatsAppAsync` exchanges the code, discovers WABA/phone, **registers
  the phone number** (`POST /{phone-number-id}/register` with an auto-generated 6-digit PIN), saves to
  `channel_integrations` scoped by `ICurrentClinicContext`.
- `MetaGraphClient` = all raw Graph API calls. `WhatsAppService` = the only thing that calls Meta's send
  API (used by staff, AI and campaign paths via `MessageService`).
- Settings → Integrations manual-entry fallback form does not register the phone number (known gap).

## 7. Webhook GET/POST flow

Meta posts **directly** to .NET (n8n is not in the inbound path):

```
Meta ──GET/POST──> Controllers/WhatsAppWebhookController.cs   /api/integrations/whatsapp/webhook   (no auth)
GET  → hub.mode/hub.verify_token/hub.challenge; token == config Meta:WebhookVerifyToken → echo challenge (200) else 403
POST → raw JSON → IMetaWebhookProcessor.ProcessAsync
         → MetaWebhookParser (only place that knows Meta's entry[].changes[].field shapes)
         → resolve clinic (phone_number_id → waba_id fallback)
         → Handlers/: CustomerMessage, BusinessAppEcho, MessageStatus, TemplateEvent, HealthEvent,
                       History, AppStateSync, Unknown  (thin adapters over existing services)
         → ONE flat WhatsAppWebhookResponse (…, ShouldRunAi, ClinicId, ConversationId, LeadId, MessageId, …)
       if ShouldRunAi → IAiTriggerNotifier calls n8n; always HTTP 200 to Meta.
```

Interactive replies normalize to `messageText` = visible title, `selectedValue` = stable Meta id.
`WhatsAppIntegrationEventsController` keeps `POST templates/events` and `POST health/events`
(ingest-key protected). **Not implemented**: Meta `X-Hub-Signature-256` body-signature verification.

## 8. n8n / AI flow

```
Meta → .NET (classify + process) → n8n (normalized trigger, only when AI should reply) → AI Agent → AI tools → .NET
```

- **Outbound trigger** (`Services/IAiTriggerNotifier.cs`) → `N8n:AiWebhookUrl` (**still unset**). Payload as
  actually coded: `clinicId, conversationId, leadId, messageId, channel, messageType, messageText,
  selectedValue`. (The owner has described n8n as receiving only `clinicId`; the code sends more —
  don't change it without being asked. Note `get_lead_context` needs a `leadId` and
  `handoff_to_human` a conversation id, so those tools depend on n8n having them.)
- **AI reply**: `POST /api/conversations/{id}/messages/send` with `{content, sender:"ai"}` + `X-Ingest-Key`
  + `?clinicId=`; `MessageService.SendAiReplyAsync` re-checks `conversation.mode == ai` fresh from the DB
  → `409 conversation_in_human_mode` if a human took over; never flips mode.
- **AI tool surface** — `Controllers/AiController.cs`, `/api/ai/*`, `[RequireIngestKey]` (`X-Ingest-Key`),
  clinicId passed explicitly (query, or body for knowledge search):

| Tool | Endpoint | Responsibility |
|---|---|---|
| `search_clinic_knowledge` | `POST /api/ai/knowledge/search` | Clinic-approved **information**: hours, address, parking, policies, consultation info, pricing, payment/financing, doctors, procedure explanations, preparation, recovery, FAQs — §11 |
| `get_clinic_info` | `GET /api/ai/clinic-info` | Hours/address/contact/consultation rules from `clinics`. **Kept**, but partly redundant with the KB; may be retired later (decision deferred). |
| `get_procedures` | `GET /api/ai/procedures?clinicId=` | Structured procedure records (id, name, active, duration) for booking. **Always ACTIVE procedures only** — no `activeOnly` parameter (a stray `activeOnly=false` is ignored). Stays; KB explains, this is authoritative. |
| `get_lead_context` | `GET /api/ai/leads/{leadId}` | What's known about this lead |
| `update_lead` | `PATCH /api/ai/leads/{leadId}` | Interest, language, timeline, notes, follow-up, qualification (no identity fields). Rejects an inactive/foreign `procedureId` (400). |
| `get_available_slots` | `GET /api/ai/appointments/available` | Live availability |
| `book_consultation` | `POST /api/ai/appointments/book` | Real booking; inactive/foreign procedure → 400 |
| `reschedule_consultation` / `cancel_consultation` | `POST /api/ai/appointments/{id}/reschedule|cancel` | |
| `handoff_to_human` | `POST /api/ai/conversations/{id}/handoff` | Switch conversation to human |

Rule of thumb (documented in `AiController`'s class comment): **Knowledge Base = information;
structured APIs = live data and actions.** The KB is never the source of truth for lead data,
availability, bookings, conversation state or handoff.

## 9. SignalR

`Hubs/InboxHub.cs`, `/hubs/inbox`, `[Authorize]`; groups `clinic:{id}` (server-resolved) and
`conversation:{id}`. Events (`IInboxNotifier`): `NewMessage`, `MessageStatusUpdated`,
`ConversationUpdated`, `ConversationModeChanged`, `WhatsAppTemplateUpdated`, `WhatsAppHealthUpdated`.
Consumers: `inbox.js`, `whatsapp-templates.js`, `whatsapp-health.js`.

## 10. Inbox / conversation modes

- `Conversation.Mode`: `ai`/`human`/`approval` (`ConversationModeSync.Apply` is the only place that changes it).
- `Message.Origin`: `whatsapp_customer`, `telegram_customer`, `whatsapp_business_app`, `dashboard`, `ai`, `system`, `campaign`.
  `SenderType`: `lead/ai/staff/system`. `Direction`: `inbound/outbound`.
- **Unread tracking** (uncommitted): `conversations.last_read_at` (shared by the clinic's staff; existing rows
  backfilled as read once). `ConversationListRow.UnreadCount` = inbound messages newer than it.
  `POST /api/conversations/{id}/read` (`IConversationService.MarkReadAsync`, clinic-scoped, notifies via
  `ConversationUpdated`) is called when staff open a conversation or a customer message arrives in the one already
  open. `inbox.js`: blue count badge + bold + left border on unread rows, and `(N) Inbox` in the tab title, live via
  SignalR. **"Needs Human" badge = mode human AND customer spoke last AND unread** — it clears once staff open the conversation.
- **24h service window** (`LastCustomerMessageAt`/`ServiceWindowExpiresAt`, `IsServiceWindowOpen`) is
  WhatsApp-specific, reset only by a genuine customer inbound, enforced server-side
  (`ServiceWindowClosedException`). `MessageService` text sends are routed by
  `conversation.Channel` through `IChannelSender` (WhatsApp + Telegram, §23); other channels still throw;
  template sends are WhatsApp-only.

## 11. Knowledge Base (clinic-specific, semantic search for the AI)

**Purpose**: each clinic keeps its own approved information; the AI searches ONLY that clinic's knowledge
and composes the answer itself. .NET stores, chunks, embeds, searches and isolates; n8n/AI decides when to
search and what to ask. Two ways in: **manual entry** or **upload ONE PDF/DOCX/TXT** (see "Document upload" below) — no OCR, batch upload or crawling.

**Tables** (`Database/schema.sql`, applied live):
- `knowledge_documents(id, clinic_id FK cascade, title varchar(200), category varchar(50) default 'general'
  [no CHECK — extensible; UI offers general, faq, policy, doctor, procedure, pricing, consultation, payment,
  preparation, recovery], content text, is_active bool, created_at, updated_at)`; indexes on clinic_id,
  category, is_active; `updated_at` trigger.
- `knowledge_chunks(id, clinic_id, knowledge_document_id FK cascade, chunk_index, content, embedding
  vector(1536) NOT NULL, created_at, updated_at)`; indexes on clinic_id and knowledge_document_id. The
  `embedding` column is **not mapped in EF** (`KnowledgeChunk` has no Embedding property) — chunks are
  written/searched with raw SQL (`VectorLiteral` formats `[..]` and SQL casts `::vector`), so no EF vector
  package is used. Never insert chunks via `DbContext.Add`.
- `knowledge_search_settings` — see below.

**pgvector**: `create extension if not exists vector` (v0.8.2). **Vector index type = none (exact scan)** —
deliberate: every search is already narrowed by `clinic_id` (btree index), a clinic has hundreds of chunks,
exact scan has perfect recall, and HNSW would apply the clinic filter after its approximate scan and can
drop results. Add `using hnsw (embedding vector_cosine_ops)` only if one clinic reaches tens of thousands of
chunks (and then update `vector_index_type`).

**Embeddings** (`IEmbeddingService` → `OpenAiEmbeddingService`, any OpenAI-compatible `POST {BaseUrl}/embeddings`):
- Actual model: **`text-embedding-3-small`**, **1536 dimensions** (`dimensions` param sent for
  `text-embedding-3*`). Every response is length-checked against the expected dimension.
- Key from config `Embeddings:ApiKey` (secret; **not yet set in Render**). Batches of 64. Provider failures
  surface as `InvalidOperationException` → dashboard shows a friendly error; API returns **503**
  (verified: nothing half-saved).
- Per-call `model`/`dimensions` overrides: the KB passes the **clinic's persisted settings**, so a clinic's
  vectors always use the model/dimension recorded for it.
- The title is prepended when embedding each chunk (`"{title}\n\n{chunk}"`); stored/returned chunk text
  stays plain.

**Chunking** (`IKnowledgeChunkingService.Chunk(content, chunkSizeTokens, chunkOverlapTokens)`): tokens are
approximated as **4 characters per token** (no tokenizer). Split on blank lines → long paragraphs on
sentences → over-long sentences at word boundaries → greedily pack up to max (≥200 chars) → merge a tiny
trailing chunk (`Knowledge:ChunkMinChars`, default 200, system-wide) into the previous → each chunk after
the first is prefixed with a word-aligned tail of the previous (overlap). A short document is one chunk.

**Settings — `knowledge_search_settings`** (one row per clinic; `UNIQUE(clinic_id)` =
`ux_knowledge_search_settings_clinic_id`; lazily created with system defaults by
`IKnowledgeSettingsService.GetAsync`, race-safe; `updated_at` trigger):

| Column | Default | Editable in UI/API? |
|---|---|---|
| `embedding_model` | `text-embedding-3-small` (from `Embeddings:Model`) | **Read-only** |
| `vector_dimension` | `1536` (from `Embeddings:Dimensions`) | **Read-only** |
| `similarity_method` | `cosine` | **Read-only** |
| `vector_index_type` | `none` (shown as "None — exact scan") | **Read-only** |
| `chunk_size_tokens` | `250` (= old 1000 chars) | Editable, 50–1000 |
| `chunk_overlap_tokens` | `38` (≈ old 150 chars) | Editable, 0 … size/2 |
| `top_k` | `5` | Editable, 1–10 |
| `minimum_similarity` | `0.30` (from `Knowledge:MinScore`) | Editable, 0–1 |

DB CHECKs enforce the ranges (`ck_knowledge_search_settings_*`; overlap < size). **No secrets are stored in
this table.** `UpdateKnowledgeSettingsRequest` has only the 4 editable fields, so read-only ones can't be
changed via form or API (verified by tamper tests). The `Embeddings__*`/`Knowledge__*` config keys are now
only **defaults for a clinic's first row**; after that the persisted row is what's used. Changing the
embedding model/dimension is unsupported from the UI (needs re-embedding everything + altering
`vector(N)`).

**Search** (`IKnowledgeSearchService`): read settings → embed query with the clinic's model/dim →
`select … from knowledge_chunks c join knowledge_documents d … where c.clinic_id=@clinic and
d.clinic_id=@clinic and d.is_active order by c.embedding <=> @q limit @n` → drop rows below
`minimum_similarity` → score = `1 - cosine distance` rounded to 4 dp. Result count =
`clamp(limit ?? top_k, 1, top_k)` — **top_k is the clinic's ceiling; the AI's `limit` can only lower it**.
Nothing relevant → `{"results":[]}` (no fabricated answer). Inactive documents never appear.

**Rebuild/re-index behavior**: creating/updating a document computes embeddings **before** opening the
transaction, then swaps chunks transactionally (a failed embedding call leaves the old chunks intact).
**Saving an entry always re-chunks + re-embeds with the current settings** (that's how new chunk
size/overlap reach existing entries). Changing settings does **not** auto re-chunk existing entries —
re-save each one; there is **no bulk "reindex all" action** (not implemented). Activate/deactivate does not
re-embed; delete removes the chunks.

**AI endpoint**: `POST /api/ai/knowledge/search` (`[RequireIngestKey]`).
Request `{ "clinicId": "...", "query": "...", "limit": 5 }` (limit optional); response
`{ "results": [ { "documentId", "title", "category", "content", "score" } ] }`. 400 if clinicId/query
missing; 503 if the embedding provider fails. No `conversationId`.

**Document upload** (added after Telegram; uncommitted): staff pick *Manual Entry* or *Upload Document* on
`/KnowledgeBase/Edit` (tabs; "Upload Document" button on the list opens it preselected, `?mode=upload`), or
`POST /api/knowledge/upload` (multipart: `file`, optional `title` [defaults to file name], `category`, `isActive`).
One file only; **PDF, DOCX, TXT**; max **5 MB** (`Knowledge:MaxUploadBytes`, clamped to ≤25 MB) and max **250,000
extracted characters** (`Knowledge:MaxExtractedChars`, guards decompression bombs/embedding cost).
`IKnowledgeService.CreateFromUploadAsync` → `IDocumentTextExtractor` (`Services/DocumentTextExtractor.cs`): type is
checked by extension **and** magic bytes (`%PDF-`, `PK\x03\x04`; TXT rejects NUL bytes; UTF-8/UTF-16 BOM/Latin-1
fallback); **PDF via PdfPig 0.1.16** (`ContentOrderTextExtractor`), **DOCX via DocumentFormat.OpenXml 3.5.1**
(all paragraphs incl. table cells, no deleted text); `KnowledgeTextNormalizer` cleans (CRLF, control/zero-width chars,
whitespace, ≤1 blank line). No text (scanned PDF/blank DOCX/empty TXT) → clear 400 "…does not contain readable text.
Scanned documents are not supported yet."; nothing is saved on any failure. Then the extracted text goes through the
SAME `CreateCoreAsync` as a manual entry: clinic's chunk settings → same embedding model/dimension → transactional
chunk write → immediately searchable via the unchanged `search_clinic_knowledge`. Schema: `knowledge_documents` +
`source_type` (`manual`|`upload`, CHECK), `original_file_name`, `mime_type`, `file_size_bytes`; extracted text lives in
the existing `content` column. **The original binary is discarded** (only text + metadata kept), so re-saving never
needs the file. Editing an uploaded document changes title/category/active only (service ignores a supplied
`content`; the page shows the text read-only — re-upload to change it); re-saving re-chunks from stored `content`
with the current settings; deactivate/delete behave as before. There is **still no bulk "reindex all"** action —
re-save each document. List shows a Source column (Manual / Uploaded + file name). Not built: OCR, multi-file,
legacy `.doc`, password-protected PDFs (reported as unreadable). Verified locally with generated PDF/DOCX/TXT plus 15
bad-file cases and two-clinic isolation (all 404 across clinics; upload ignores a posted `clinicId`).

**Website scraping** (uncommitted): a STANDALONE ingestion subsystem in `Integrations/Knowledge/WebScraping/` — a third
way into the SAME `knowledge_documents` → `knowledge_chunks` → embeddings → `search_clinic_knowledge` pipeline (no second
KB/vector/search). The crawler owns crawling, URL identity, fetching, extraction, page state, runs and change
detection; the KB owns content, chunking, embeddings and search. Bridge = two small crawler-agnostic methods on
`IKnowledgeService`: `CreateFromSourceAsync` / `ReplaceSourceContentAsync` (page text → the clinic's normal
chunk/embed settings). UI: `/KnowledgeBase/Edit` has a 3rd tab **Website** (URL, "Crawl website" | "Single page only",
category, active); `/KnowledgeBase/Websites` (list) and `/KnowledgeBase/Websites/{id}` (status, live counters,
pages + failures, Re-scrape / Deactivate / Delete, run history; auto-refreshes while running). API (login,
clinic from `ICurrentClinicContext`): `POST/GET /api/knowledge/websites`, `GET …/{id}`, `GET …/{id}/pages?status=`,
`POST …/{id}/rescrape|active`, `DELETE …/{id}`. **Tables** (all `clinic_id`-scoped, cascade from clinic):
`knowledge_website_sources` (unique `(clinic_id, normalized_start_url)`, crawl_mode, category, is_active, status,
`boilerplate_block_hashes` jsonb), `knowledge_website_pages` (unique `(website_source_id, normalized_url)`,
canonical_url, http_status, `content_hash` = SHA-256 of normalized extracted TEXT, etag, last_modified, status
`discovered|processing|indexed|unchanged|skipped|duplicate|failed|removed`, failure_reason, `knowledge_document_id`
→ knowledge_documents ON DELETE SET NULL, duplicate_of_page_id, `links` jsonb, missing_count, removed_at; content_hash
index is NOT unique), `knowledge_website_scrape_runs` (status `pending|crawling|completed|completed_with_errors|failed`,
pages_discovered/processed/indexed/new/changed/unchanged/skipped/duplicate/failed/removed, error_summary);
`knowledge_documents` + `source_url`, `source_type` CHECK now `manual|upload|website`. **Background**: request writes a
`pending` run and enqueues its id (`IWebsiteScrapeQueue`, an in-process Channel); `WebsiteScrapeWorker` (hosted
service) runs one crawl at a time in its own DI scope; state is in the DB; on startup runs left `crawling` are marked
failed ("interrupted by restart") and `pending` ones re-queued. **Engine** (`WebsiteScrapeProcessor`): 1) CRAWL breadth-
first, level by level, limited concurrency, politeness delay, robots.txt; 2) ANALYZE (canonical/redirect aliases, site-
wide boilerplate removal, text render + SHA-256, duplicate detection); 3) APPLY (unchanged → nothing touched and NO
embedding call; changed → same document updated, only ITS chunks rebuilt; new → new document). **URL identity**
(`UrlNormalizer`, the only place): http/https only, no credentials, lower-case host (IDN→punycode), default ports
dropped, fragment removed, dot-segments/`//` collapsed, `index.html|htm|php` folded, trailing slash removed (root
`/`), tracking params (`utm_*`, fbclid, gclid, msclkid, …) removed but other query params KEPT and sorted; www/non-www
and http/https default ports = one site, a different explicit port = a different site. **Same-site only**; external
links, images/CSS/JS/fonts/media/archives/XML are never fetched or stored; PDFs/Office files are recorded as
`skipped` ("use Upload Document"). **Dedup**: canonical link (same-site only, ignored if it points at the home page
from a deep page) and redirects make alias pages `duplicate`; identical text within one website → one owner
(previous owner kept, else canonical/shallowest/shortest) — never across clinics. **Extraction**: AngleSharp (HTML5
parser, no regex on markup); main/[role=main]/article/known content containers else body; drops script/style/
noscript/hidden/aria-hidden/nav/aside/header/dialog/form controls and cookie/consent/popup/menu/breadcrumb/share
widgets; keeps headings, paragraphs, lists ("- "), table rows ("a | b"), details/FAQ text, image alt text; footer text
kept only for contact-looking lines; blocks repeated on ≥50% of ≥4 pages are stripped everywhere except the start page
(its hashes persist per source for partial recrawls). **Change detection**: ETag/Last-Modified conditional requests
(304 → unchanged, stored `links` keep the crawl going) + content hash. **Removed pages**: 410 → removed at once; 404 or
"no longer linked" → removed after 2 consecutive complete crawls (never when a crawl was cut short by limits, or the
start page failed); removed = row kept + document DEACTIVATED (never deleted); a returning page is re-activated, a
hand-deactivated document is never re-activated by a crawl. **Limits** (`Knowledge:WebScraping`, clamped): MaxPages 100,
MaxDepth 3, MaxConcurrency 3, RequestTimeoutSeconds 15, MaxResponseBytes 2 MB, MaxRedirects 5, PolitenessDelayMs 300,
MaxRunMinutes 20, MinTextChars 80, MaxTextChars 200k, MaxQueryVariantsPerPath 5, MaxSourcesPerClinic 25; crawl-trap
filters (calendar/filter/session params, repeated/deep paths, >4 query params, per-path variant cap). **SSRF**
(`SsrfGuard`): http/https + ports 80/443 only; localhost/.local/.internal/single-label names blocked; DNS answers must
ALL be public (private, loopback, link-local incl. 169.254.169.254, CGNAT, multicast, IPv6 ULA/link-local, mapped/6to4/
Teredo/NAT64 forms); checked before every request AND again in the socket connect callback (anti-DNS-rebinding);
redirects followed manually and re-validated, leaving the site is refused; no proxy/cookies. Only in the Development
environment `DevAllowedHosts` (exact host:port) bypasses this, for tests. robots.txt is respected (`SculptFlowBot`
group else `*`, longest match, `*`/`$`, Crawl-delay ≤5 s); an unreachable/5xx robots.txt stops the crawl.
Deactivating a source hides all its documents; activating restores only pages that are in the KB; deleting removes the
source, its rows and the documents it created (manual/upload/other sources/other clinics untouched). Not built: JS
rendering (JS-only pages are skipped as "no readable text"), sitemap reading, OCR, authenticated sites, batch imports,
per-source limits, concurrent crawls. Tested end-to-end against a purpose-built fake site (single page, multi-page,
URL variants, alias/duplicate content, unchanged/changed/new/removed recrawls, external/assets, 500/404/timeout,
robots/noindex, SSRF forms, tenant isolation, restart recovery).

**Dashboard**: `/KnowledgeBase` (list: title, category, Active/Inactive, last updated; Edit /
Activate-Deactivate / Delete; "Add Knowledge" and "Settings" buttons), `/KnowledgeBase/Edit/{id?}`
(title, category, content, active), **`/KnowledgeBase/Settings`** (4 editable inputs; 4 clearly disabled
read-only fields with a "Read-only" badge and explanation; help text that chunk changes apply on next
save). Sidebar link "Knowledge Base". API (`KnowledgeController`, login required):
`GET/POST /api/knowledge`, `GET/PUT/DELETE /api/knowledge/{id}`, `POST /api/knowledge/{id}/active`,
`GET/PUT /api/knowledge/settings`.

**Files**: entities `KnowledgeDocument`(+`KnowledgeCategory` labels)/`KnowledgeChunk`/`KnowledgeSearchSettings`;
services `IEmbeddingService`+`OpenAiEmbeddingService`, `KnowledgeChunkingService`, `KnowledgeService`,
`KnowledgeSearchService`, `KnowledgeSettingsService`, `VectorLiteral`; `Dtos/KnowledgeDtos.cs`;
`Pages/KnowledgeBase/{Index,Edit,Settings}`.

**Testing done** (with the local `fakeembed` stub, not a real key): chunking, ranking, top-k ceiling,
min-similarity, inactive exclusion, empty results, edit/deactivate/reactivate/delete, cross-clinic
isolation, unique constraint, CHECK constraints, provider-down → 503, persisted model/dim really used.
**Not yet verified with a real OpenAI key**: real similarity scores are distributed differently than the
stub's — if real searches come back empty, lower `minimum_similarity` (≈0.2) on the Settings page.

## 12. Procedures management

- **Page** `/Procedures` (list: name, code, consultation "N min", Active/Inactive, Edit,
  Activate/Deactivate; "Add Procedure") and `/Procedures/Edit/{id?}` (name, optional code, duration in
  minutes, short description). Sidebar link "Procedures" (under Appointments). Styling mirrors the KB pages.
- **Rules** (`IProcedureService`/`ProcedureService`): name required ≤200 and **unique per clinic
  (case-insensitive)**; code ≤100; description ≤500 (page tells staff detailed info belongs in the KB);
  duration 1–480 or empty. **Never deleted** (leads/appointments/bookings reference procedures) — only
  activated/deactivated. No new fields/migration were needed (columns already existed).
- **Endpoints** (`ProceduresController`, login required): `GET /api/procedures?activeOnly=`,
  `GET /api/procedures/{id}`, `POST /api/procedures`, `PUT /api/procedures/{id}`,
  `POST /api/procedures/{id}/active`. Bad input → 400.
- **Inactive procedure rules**: row + all history preserved. Hidden from the AI `get_procedures` (active
  only, always), from Campaign procedure dropdowns (active only), and blocked for **new activity** via
  `IProcedureService.EnsureUsableAsync` (throws `ArgumentException` → controllers return 400 "inactive
  and can't be used for new activity" / "not found for this clinic"): new appointments
  (`AppointmentService.CreateAsync`, so also AI `book_consultation`), new procedure bookings
  (`ProcedureBookingService.CreateAsync`), and a lead's procedure being **changed**
  (`LeadService.UpdateAsync`/`UpdateContextAsync` — resubmitting a lead's current, now-inactive
  procedure still works). This also rejects another clinic's procedure id.
- **Deliberately unchanged**: lead **intake** (`LeadsController.Create`/`CreateOrGetAsync`, webhook/n8n)
  never rejects a lead for referencing an inactive procedure. Campaign audience filtering by an inactive
  procedure still works (it filters `Lead.ProcedureId`). `GetProcedureStatsAsync` (dashboard money page)
  still lists all procedures incl. inactive.

## 13. Campaigns / reactivation

**Backend model** (built earlier, extended): `Campaign` = orchestration + audience definition;
`CampaignRecipient` = the audience **snapshot** created at creation time (never re-derived). Every send
goes through `MessageService.SendCampaignTemplateAsync` and writes a normal `Message` in the lead's own
Conversation — **no CampaignMessage table, ever**. Batches of 20 (`ProcessBatchAsync`); delivery/read/failed
webhooks roll into the recipient. Templates must be approved (`WhatsAppTemplateStatus.Approved`).

**Audience (single source of truth: `ICampaignAudienceService`/`CampaignAudienceService`)** — the ONLY place
eligibility is computed (never duplicated in JS; UI counts call the API):
- **Mandatory exclusions always**: same clinic, has a phone, `marketing_opt_in = true`, `opted_out_at is null`.
- `all_eligible` = mandatory exclusions only.
- `reactivation_no_consultation` ("old leads who never booked"): prior interest (has a conversation OR
  `status != new`) AND inactive (`last_contact_at` null/older than cutoff AND no conversation
  `last_message_at` newer than cutoff; default **60 days**, UI offers 30/60/90) AND **no appointment with
  status booked/confirmed/rescheduled/attended** (canceled and no_show do NOT count → still eligible), plus
  optional procedure/source filters. Derived live — **no** `is_old_lead`/`incomplete_booking` flag exists.
- `custom` = mandatory exclusions + `CampaignAudienceFilters` (jsonb): `inactiveDays, procedureId,
  leadStatuses, sources, qualificationStatuses, createdAfter/Before, lastContactedAfter/Before,
  appointmentStatuses, countries, cities` (a `languages` filter was added then removed). All map to
  existing Lead/Appointment columns.
- `CampaignService.CreateAsync`: explicit `LeadIds` win (manual selection); otherwise the audience is
  resolved by the audience service for any type (incl. custom). Non-contactable leads become `skipped`
  recipients with a `skip_reason` (`no_phone`, `opted_out`, `marketing_opt_in_false`).
- **Endpoints**: `GET /api/campaigns/audience-preview?audienceType=&filters=` → `{matchingLeads}`;
  `GET /api/campaigns/audience-preview/leads?...&limit=` (≤50, default 10) → count + first N names/phones;
  `GET/POST /api/campaigns`, `GET /api/campaigns/{id}`, `POST …/{id}/schedule|send|process-batch|cancel`.

**Campaign creation UI** (`/Campaigns/Create`, `Create.cshtml(.cs)`, `campaign-audience.js`,
`multi-select.js`): "Who do you want to reach?" — **four clinic-facing cards** (no internal enum names
shown), mapped onto the 3 backend audience types via two hidden fields (`AudienceType`,
`ManualSelection`):
1. **Old leads who never booked** — default, "Recommended" badge, accent border. Controls: Inactive for
   30/60/90 days, Procedure (all/specific, active only), optional Lead source. Big
   "N leads ready for reactivation" count + **Preview leads**.
2. **All contactable leads** — "N contactable leads" count + Preview; no filters.
3. **Build a custom audience** — Procedure + Inactive for by default; **Advanced filters ▾** collapsed:
   lead status, qualification status, lead source, appointment status (searchable checkbox dropdowns with
   chips — reusable `multi-select.js`), created/last-contacted date ranges, country, city. "N matching
   leads" + Preview.
4. **Select leads manually** — the original search + checkbox list (`audience_type=custom` +
   explicit `LeadIds`).
- Counts refresh (350 ms debounce) from the preview API. Status/category values shown with friendly
  labels via `Pages/Shared/FilterLabelHelper.cs` ("No-show", "Needs human review", …); stored values
  unchanged. `_StatusHelp.cshtml` + `status-help.js` = a "?" modal explaining Lead status, Qualification
  status, Lead source, Appointment status (also on the Lead edit page).
- Limitation: body variables (`{LeadFullName}` etc.) only work with **manual selection** (they need the lead
  list up front); audience-based campaigns with variables are rejected with a clear message.
- Fixed along the way: an invalid **nested `<form>`** in the old page broke the submit button; **"Send
  later"** crashed (Npgsql needs UTC `DateTimeOffset`) — now `ToUniversalTime()`.
- **Test-data gotcha**: a real send attempt (even one Meta rejects) creates a `Conversation` for the lead,
  which makes them count as "prior interest" — clean up test conversations. Meta's sandbox rejects
  non-allow-listed numbers ("recipient not in allowed list").

## 14. Staff-editable fields & dashboard pages

- **Lead status, Qualification status, Lead source** editable at **`/dashboard/leads/{id}`**
  (`LeadDetail`), **Appointment status** at **`/dashboard/appointments/{id}`** (`AppointmentDetail`);
  reached by clicking a name in the Interested People / Appointments lists. Plain form posts through the
  same services the APIs use: `POST /api/leads/{id}/status` (`UpdateLeadStatusRequest` gained optional
  `Source`) and `PATCH /api/appointments/{id}/status`; new `IAppointmentService.GetByIdAsync` +
  `GET /api/appointments/{id}`.
- Lead source dropdown = distinct sources on file (`ILeadService.GetDistinctSourcesAsync`) + starter set
  (facebook, instagram, google, website, referral, whatsapp) + "Other…" (free text); Source has no enum/CHECK.
  (The Campaign filter lists only sources actually in use — intentional difference.)
- `LeadService.UpdateStatusAsync` now bumps `last_contact_at` **only on a real status change**, so
  metadata-only edits can't reset the reactivation inactivity clock.
- Who writes these fields: lead status — creation, first staff reply (→contacted), booking, attended/no-show,
  procedure booking, staff edit; qualification — AI `update_lead`, staff; source — set at creation
  (n8n/intake, or `whatsapp` for auto-created leads) and staff edit; appointment status — booking (booked),
  AI reschedule/cancel, staff edit (confirmed/attended/no_show/rescheduled…).
- **Search by name/phone** on both list pages (`IDashboardService.GetLeadsAsync/GetAppointmentsAsync` got a
  `search` param; also `/api/dashboard/leads|appointments?search=`).
- **Branding/nav**: app renamed **MySculptFlow** (logo badge "SF"); sidebar shows the **logged-in clinic's
  name** (from `clinics.name` via `ICurrentClinicContext`, fallback "Clinic Dashboard"); nav: Inbox, Main
  Numbers, Interested People, Appointments, **Procedures**, [WhatsApp] Templates, Health, Campaigns,
  **Knowledge Base**, API (Swagger), Settings, Clinic Info.

## 15. Deployment (Docker / Render / GitHub)

- **Dockerfile** (repo root): multi-stage — `mcr.microsoft.com/dotnet/sdk:10.0` restores/publishes
  `PlasticSurgery/PlasticSurgery.csproj`; runtime `aspnet:10.0` runs `dotnet PlasticSurgery.dll`;
  `ASPNETCORE_ENVIRONMENT=Production`; `CMD sh -c "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet
  PlasticSurgery.dll"` (Render injects `PORT`, terminates TLS). `.dockerignore` excludes bin/obj/.git/etc.
  Docker isn't installed on the dev machine, so the image build has only been exercised by Render.
- **Render**: service live at `https://sculptflowapp.onrender.com`. Env var names in §20. After editing env
  vars use "Save, rebuild, and deploy" (Save-only doesn't restart). Git push to `main` triggers deploy if
  Auto-Deploy is on, else Manual Deploy → Deploy latest commit.
- Lessons: `ConnectionStrings__Postgres` must be **key=value**, not a `postgresql://` URI (URI → Npgsql
  "Format of the initialization string… index 0"); use the **session pooler** host, not the direct one; a
  wrong password gives `28P01`, not the format error; "No such host is known" = DNS/host problem in the
  string or network. Startup warnings that are known/benign for now: DataProtection keys stored in the
  container (users get signed out on redeploy), "Failed to determine the https port for redirect".
- **Gap**: `Program.cs` has no forwarded-headers handling (`UseForwardedHeaders`); behind Render's proxy
  the app sees HTTP, which may affect Meta OAuth redirect URIs / cookies.
- **GitHub**: `https://github.com/fadelle/SculptFlowApp`, branch `main`. Commits: `2aec468` initial;
  `a0f218a` Dockerfile; `6f13697` .dockerignore; `5ff4d55` rename + sidebar clinic name; `5a0b25a`
  login/register polish; `974f9b2` Knowledge Base; `da36238` Procedures + AI active-only.
  **Uncommitted at time of writing**: the `knowledge_search_settings` work (entity, settings service,
  Settings page, chunking/embedding/search changes, `schema.sql` block, `appsettings.json` `Embeddings`/
  `Knowledge` keys, and this handoff update). The settings table is already applied to Supabase — the live
  DB is ahead of `main` until that is pushed (Render's code still lacks the settings code).
- Git identity is configured; LF→CRLF warnings on commit are harmless. Commit trailer used:
  `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.

## 16. Implemented features (chronological, this engagement)

1. WhatsApp Templates + Campaigns + 24h service-window enforcement
2. AI tool surface + Clinic Info page
3. WhatsApp phone registration (PIN) on Embedded Signup
4. Webhook event routing (Inbox delivery states, Templates, Health) → consolidated raw Meta webhook +
   parser/processor/handlers; `smb_message_echoes`, `history`, `smb_app_state_sync`
5. Identity + `clinic_users` + `CurrentClinicContext`; all pages/controllers off browser `clinicId`
6. DB uniqueness constraints; Meta now calls .NET directly (n8n only gets AI triggers); interactive
   message normalization
7. Campaign/reactivation data model (audience types, filters, recipient snapshot, attribution columns)
8. Campaign creation UI: audience picker (4 choices), advanced filters, previews, help modal
9. Staff editing of lead/qualification/source/appointment status + list search
10. Docker + Render deployment; GitHub repo; rename to MySculptFlow; login polish
11. Knowledge Base (docs, chunks, pgvector embeddings, semantic search, dashboard, AI tool) +
    per-clinic persisted settings page
12. Procedures management page with inactive-procedure rules; AI `get_procedures` active-only

## 17. Pending work

- **Set `Embeddings__ApiKey` in Render** (real OpenAI-compatible key) — KB saving/search won't work in
  production without it; then tune `minimum_similarity` against real scores
- Commit/push the uncommitted `knowledge_search_settings` work (§15)
- Set the real `N8n__AiWebhookUrl` (notifier no-ops with a warning until then); wire the n8n
  `search_clinic_knowledge` HTTP tool (clinicId from workflow context, AI supplies only `query`)
- **Rotate leaked secrets** (§22); close the `clinicId`-trust gap for AI endpoints
- Meta `X-Hub-Signature-256` verification; forwarded-headers in `Program.cs`; persist DataProtection keys
- KB: optional bulk "reindex all"; possible future HNSW index; maybe merge/retire `get_clinic_info`
- Manual-entry integrations form doesn't register the phone number
- Staff invitations (join an existing clinic), signup email verification/CAPTCHA/rate limiting
- **Telegram is implemented (§23) but uncommitted/undeployed** — set `App__PublicBaseUrl` on Render, push, then test with the real bot

## 18. Important constraints / decisions

- **Reuse over new tables**: `channel_integrations` = "whatsapp_connections"; no `CampaignMessage`; the KB
  reuses existing patterns; audience logic lives only in `CampaignAudienceService`.
- **PostgreSQL is the source of truth; SignalR only notifies.**
- **No role-based authorization** (explicit decision).
- **Central sending service**: only `WhatsAppService` calls Meta's send API; all paths go through `MessageService`.
- **Idempotency**: messages by `(clinic_id, channel, external_message_id)`; templates by
  `(clinic_id, meta_template_id)` → `(clinic_id, name, language)`; health events by
  `(channel_integration_id, event_type, occurred_at)`.
- **n8n holds no business state**; .NET decides AI eligibility/mode.
- **Schema is hand-written SQL**, appended idempotently to `schema.sql` and applied live; EF model kept in
  sync manually; extensible strings (category, campaign_type, lead source) have no CHECK, closed sets do.
- **Secrets never in the repo or in `knowledge_search_settings`** — user-secrets locally, Render env vars in prod.
- **Knowledge Base ≠ structured data**: procedures/lead/booking data stay in structured APIs.
- Standing operating instructions: leave the app running after changes unless told to shut down; run it with
  `dotnet run --launch-profile https` (**https://localhost:7276**; the default profile is http-only :5274);
  `.cshtml`/`.cs` changes need stop → build → run. Clean up test data after live testing (shared Supabase,
  now also feeding Render). The Claude Code auto-mode classifier blocks printing secrets (e.g.
  `dotnet user-secrets list` values) — don't work around it. The browser tool's screenshots are flaky in this
  environment (use page text / `curl` with cookie jars for verification). Windows Smart App Control once
  blocked `dotnet run` (user disabled it).

## 19. Current API endpoints

**Dashboard-facing** (login required; clinic from `ICurrentClinicContext`):
- Leads: `GET/PATCH /api/leads`, `GET/PATCH /api/leads/{id}`, `POST /api/leads/{id}/status`
  (`POST /api/leads` is `[AllowAnonymous]` intake — not ingest-key protected, known gap)
- Appointments: `GET/POST /api/appointments`, `GET /api/appointments/{id}`,
  `PATCH /api/appointments/{id}/status`, `GET /api/appointments/available`
- Procedures: `GET/POST /api/procedures`, `GET/PUT /api/procedures/{id}`, `POST /api/procedures/{id}/active`
- Knowledge websites: `POST/GET /api/knowledge/websites`, `GET /api/knowledge/websites/{id}` (+`/pages`), `POST …/{id}/rescrape|active`, `DELETE …/{id}`
- Knowledge: `POST /api/knowledge/upload` (multipart, one file), `GET/POST /api/knowledge`, `GET/PUT/DELETE /api/knowledge/{id}`,
  `POST /api/knowledge/{id}/active`, `GET/PUT /api/knowledge/settings`
- Campaigns: `GET/POST /api/campaigns`, `GET /api/campaigns/{id}`,
  `POST …/{id}/schedule|send|process-batch|cancel`, `GET /api/campaigns/audience-preview`,
  `GET /api/campaigns/audience-preview/leads`
- Dashboard: `GET /api/dashboard/summary|leads|appointments|procedures` (`?search` on leads/appointments)
- Procedure bookings: `POST/GET /api/procedure-bookings`, `PATCH /api/procedure-bookings/{id}`
- Integrations: `GET/PUT /api/channel-integrations`, `POST …/{channel}/disconnect`,
  `POST …/whatsapp/connect`, `POST …/facebook/connect`, `POST …/whatsapp/debug-token`
- WhatsApp: `GET/POST /api/whatsapp/templates`, `GET …/{id}`, `POST …/{id}/sync`,
  `GET /api/whatsapp/health`, `GET /api/whatsapp/health/events`
- Conversations: `GET/POST /api/conversations`, `GET /api/conversations/{id}`,
  `GET/POST /api/conversations/{id}/messages`, `POST …/messages/send` (dual-mode), `POST …/messages/send-template`,
  `POST …/take-over|return-to-ai|close`

**Trusted server-to-server** (`[RequireIngestKey]` / `X-Ingest-Key`): `POST /api/messages/ingest`,
`POST /api/integrations/whatsapp/templates/events`, `POST /api/integrations/whatsapp/health/events`, and the
AI tools under `/api/ai/*` (§8, incl. `POST /api/ai/knowledge/search`).

**Public** (Meta): `GET/POST /api/integrations/whatsapp/webhook`. **Public** (Telegram, secret-token protected):
`POST /api/integrations/telegram/webhook/{connectionId}`. Dashboard adds
`POST /api/channel-integrations/telegram/connect|refresh` (disconnect uses the existing `…/{channel}/disconnect`).

**Razor pages** (login required except Login/Register/Error): `/dashboard`, `/dashboard/leads`,
`/dashboard/leads/{id}`, `/dashboard/appointments`, `/dashboard/appointments/{id}`, `/inbox`,
`/Procedures`, `/Procedures/Edit/{id?}`, `/KnowledgeBase`, `/KnowledgeBase/Edit/{id?}`,
`/KnowledgeBase/Settings`, `/Campaigns`, `/Campaigns/Create`, `/Campaigns/{id}`,
`/WhatsApp/Templates`, `/WhatsApp/Health`, `/settings/integrations`, `/settings/clinic-info`,
`/Account/Login|Register|Logout`.

## 20. Current configuration keys (names only — values are secrets or defaults)

**Secrets** (local: `dotnet user-secrets` in `PlasticSurgery/PlasticSurgery/`; Render: env vars with `__`):
- `ConnectionStrings:Postgres` / `ConnectionStrings__Postgres` — Supabase **session pooler**, key=value format
- `Meta:AppSecret` / `Meta__AppSecret`
- `Meta:WebhookVerifyToken` / `Meta__WebhookVerifyToken` — must match Meta's webhook config
- `N8n:IngestApiKey` / `N8n__IngestApiKey` — must match the n8n `X-Ingest-Key` header
- `N8n:AiWebhookUrl` / `N8n__AiWebhookUrl` — **unset**
- `Embeddings:ApiKey` / `Embeddings__ApiKey` — **not set anywhere real yet** (needed for the KB)
- Telegram needs no secret in config (the bot token is entered in the UI and stored per clinic), but needs
  `App:PublicBaseUrl` / `App__PublicBaseUrl` — the public HTTPS origin (**required on Render**); `Telegram:ApiBaseUrl`
  is a test-only override.

**Non-secret** (`appsettings.json`, overridable by env): (`Clinic:DefaultSlug` was removed —
registration no longer uses a default clinic; a leftover env var is harmless);
`Meta:AppId`, `Meta:GraphApiVersion` (`v21.0`), `Meta:WhatsAppLoginConfigId`, `Meta:FacebookLoginConfigId`;
`Embeddings:BaseUrl` (`https://api.openai.com/v1`), `Embeddings:Model` (`text-embedding-3-small`),
`Embeddings:Dimensions` (`1536` — must equal the `vector(N)` column); `Knowledge:MaxUploadBytes` (5242880),
`Knowledge:MaxExtractedChars` (250000); `Knowledge:WebScraping:*` (crawler limits — see §11; `DevAllowedHosts` is Development-only, leave empty); `Knowledge:MinScore` (0.30),
`Knowledge:ChunkMaxChars` (1000), `Knowledge:ChunkOverlapChars` (150), `Knowledge:ChunkMinChars` (200) —
the first three are **defaults for a clinic's first settings row only**; `ChunkMinChars` is system-wide.
Render sets `PORT` itself; the Dockerfile sets `ASPNETCORE_ENVIRONMENT`/`ASPNETCORE_URLS`.

## 21. Migrations (all in `Database/schema.sql`, applied live to Supabase, chronological)

1. Service-window columns; `whatsapp_templates`, `campaigns`, `campaign_recipients`; `messages` template/
   campaign links; `origin` CHECK + `'campaign'`
2. `clinics` address/operating_hours/consultation_info
3. `channel_integrations.pin`
4. `messages` status timestamps/failure/metadata; `channel_integrations` health fields;
   `whatsapp_templates` extras + CHECKs dropped; `whatsapp_health_events`
5. `events` real FKs
6. Identity tables + `clinic_users`
7. Unique partial indexes: `channel_integrations.phone_number_id`, `clinic_users(user_id) where is_active`
8. **Campaigns + old-lead reactivation model**: `campaigns` + `campaign_type`, `channel` (CHECK), `audience_type`
   (CHECK), `audience_filters jsonb`, `whatsapp_template_id` nullable, status CHECK + `paused`, index on
   `campaign_type`; `campaign_recipients` + `external_message_id`, `appointment_id` FK, `skip_reason`,
   `failure_code`, `replied_at`, `booked_at`, `error_message` → `failure_reason`, status CHECK + `replied,
   booked, skipped`, partial indexes
9. **Knowledge Base**: `create extension vector`; `knowledge_documents`; `knowledge_chunks` (`vector(1536)`,
   no ANN index)
10. **`knowledge_search_settings`** (unique `clinic_id`, 4 CHECKs, trigger) — applied live, **not yet on `main`**

11. **Telegram**: `channel_integrations` + `telegram_bot_id`, `telegram_bot_username`, `webhook_status`,
    `webhook_registered_at`; `telegram` added to the channel CHECKs (`channel_integrations`, `conversations`) and
    `telegram_customer` to `ck_messages_origin`; partial unique `ux_channel_integrations_telegram_bot_id`
    (connected rows only) and `ux_conversations_clinic_channel_thread` — applied live, **not yet on `main`**
12. **KB document upload**: `knowledge_documents` + `source_type` (default `manual`, CHECK `manual|upload`),
    `original_file_name`, `mime_type`, `file_size_bytes` — applied live, **not yet on `main`**
13. **KB website scraping**: `knowledge_website_sources`, `knowledge_website_pages`, `knowledge_website_scrape_runs` (+ indexes/CHECKs/triggers); `knowledge_documents` + `source_url`, `source_type` CHECK adds `website` — applied live, **not yet on `main`**

No migration was needed for Procedures, lead/appointment editing, or the audience UI.

## 22. Known TODOs

- [ ] Set `Embeddings__ApiKey` in Render (real key); verify KB end-to-end with real embeddings
- [ ] Commit + push the uncommitted settings work; update Render deploy
- [ ] **Rotate** `Meta:WebhookVerifyToken` and `N8n:IngestApiKey` (were in git history via this file's
      first version) and update Meta + n8n; consider resetting the Supabase DB password (it was shown in
      chat) and updating user-secrets + Render
- [ ] Set real `N8n__AiWebhookUrl`; build the n8n `search_clinic_knowledge` tool
- [ ] Derive `clinicId` server-side for AI endpoints instead of trusting the parameter
- [ ] Meta `X-Hub-Signature-256` verification; `UseForwardedHeaders`; persist DataProtection keys
- [ ] Protect/decide `LeadsController.Create` (`[AllowAnonymous]`, no ingest key)
- [ ] Manual integrations form should register the WhatsApp phone number
- [ ] Staff invitations to join an existing clinic; email verification / CAPTCHA / rate limiting on signup
- [ ] New clinics have no procedures — staff add them at `/Procedures` (no starter set is seeded)
- [ ] Optional: KB bulk reindex; HNSW index at scale; retire `get_clinic_info`
- [ ] Stray `webhook_test_template` template exists in the DB from earlier testing (harmless)

## 23. Telegram integration (direct Bot API) — IMPLEMENTED, not yet committed/deployed

Telegram is a channel adapter alongside WhatsApp; nothing about the WhatsApp webhook/parser/sender/templates/
health/coexistence was changed (only the shared send path was made channel-routable — see below).

```
Telegram ──POST──> TelegramWebhookController  /api/integrations/telegram/webhook/{connectionId}   (anonymous)
   connectionId = channel_integrations.id ; row must be channel=telegram AND status=connected else 404
   X-Telegram-Bot-Api-Secret-Token must equal the row's stored secret (constant-time) else 403
   clinic_id comes from the STORED row, never from the payload
   → TelegramWebhookProcessor → TelegramUpdateParser (only place that knows Telegram JSON)
   → ILeadService.GetOrCreateByExternalIdAsync  (lead = "telegram:{chatId}", Phone stays NULL, Source="telegram", SourceDetail="@username")
   → IConversationService.GetOrCreateForLeadAsync(…, externalThreadId = chat.id)
   → IMessageService.IngestAsync (unchanged: persist → conversation fields → SignalR → AI eligibility)
   → if AiEligible (text, mode=ai, not a duplicate) → IAiTriggerNotifier (same payload as WhatsApp, channel="telegram")
Outbound: n8n/dashboard → MessageService → IChannelSender (by conversation.Channel) → WhatsAppChannelSender | TelegramChannelSender → Bot API sendMessage
```

- **Connect** (`Settings → Channels & Integrations → Telegram`, `POST /api/channel-integrations/telegram/connect {botToken}`):
  validate token shape → `getMe` → reject if that bot is connected to another clinic → reuse/mint the clinic's
  row id → generate secret (256 random bits, base64url) → `setWebhook(url, secret_token, allowed_updates=["message"])`
  → only then persist (status connected, webhook_status active) → `getWebhookInfo` confirms. Failure before
  persist leaves nothing behind. Reconnect reuses the same connectionId and rotates the secret.
  `POST …/telegram/refresh` ("Check webhook" button) re-reads `getWebhookInfo`.
- **Disconnect** (`POST /api/channel-integrations/telegram/disconnect` or the page button): `deleteWebhook` (best
  effort), status → disconnected, **token + secret cleared**, webhook_status `not_registered`; bot id/username
  kept for display; leads/conversations/messages untouched (history stays visible; sending into an old
  Telegram conversation while disconnected → 422 "reconnect"). Inbound to a disconnected connection → 404.
- **Storage** (reuses `channel_integrations`): `access_token` = bot token, `webhook_verify_token` = webhook
  secret, `display_name` = "@bot". New columns: `telegram_bot_id`, `telegram_bot_username`, `webhook_status`,
  `webhook_registered_at` (`last_webhook_at` existed). Token/secret are plain text like every other channel
  credential in this MVP (DataProtection keys don't survive Render redeploys, so column encryption would
  brick tokens) but are **never returned** (DTO has only `hasAccessToken`/`hasWebhookVerifyToken`), never
  rendered, never logged (the Telegram `HttpClient` is registered `.RemoveAllLoggers()` because the token is in
  the URL path; exceptions are rebuilt from Telegram's description, never the URL).
- **Identity/idempotency**: lead `ExternalLeadId = "telegram:{chatId}"` (unique per clinic via the existing
  `ux_leads_clinic_external_lead_id`; same person on two clinics = two leads). Conversation `ExternalThreadId =
  chat.id`, new unique `ux_conversations_clinic_channel_thread`. Message `ExternalMessageId = "{chatId}:{message_id}"`
  (Telegram ids are only unique per chat) under the existing `(clinic_id, channel, external_message_id)` unique
  index — same key for inbound and outbound. Unique-violation races are caught (lead, conversation, message) —
  verified with 8 parallel identical deliveries → 1 row, 1 n8n trigger.
- **Update handling**: only `message` updates from private chats by non-bots. Text → `text`; `/cmd` → `command`
  (saved, **never AI-eligible**, so `/start` creates the lead/conversation but the AI does not reply); photo →
  `image`, voice/audio → `audio`, video/video_note → `video`, document, sticker, location, contact → `contacts`,
  anything else → `unsupported` (all saved with raw JSON in `metadata_json`, never AI). Edited messages, groups,
  channels, bot senders, non-message updates → ignored with 200.
- **n8n payload** (unchanged record `AiTriggerPayload`): `{clinicId, conversationId, leadId, messageId,
  channel:"telegram", messageType:"text", messageText, selectedValue:null}`. AI reply path unchanged:
  `POST /api/conversations/{id}/messages/send?clinicId=` + `X-Ingest-Key`, `sender:"ai"` → `SendAiReplyAsync`
  re-checks `mode == ai` (409 `conversation_in_human_mode` otherwise, nothing sent to Telegram).
- **MessageService extension**: new `IChannelSender { Channel; SendTextAsync(conversation, text) }`
  (`Services/IChannelSender.cs`). `WhatsAppChannelSender` = the 24h-window + phone checks moved verbatim out of
  MessageService (same messages/order); `TelegramChannelSender` (`Integrations/Telegram`) = this clinic's connected
  bot + chat id. `MessageService` resolves by `conversation.Channel`, stamps `Message.Channel` from the
  conversation. `WhatsAppSendException` and `TelegramApiException` share base `ChannelSendException` (controllers
  map it to 502). `IngestAsync`: inbound origin via `MessageOrigin.CustomerFor(channel)` (`telegram_customer`);
  `ServiceWindowExpiresAt` only set for WhatsApp. Template sends stay WhatsApp-only. Staff send flips to human as
  before; Take Over / Return to AI are channel-agnostic.
- **Inbox**: Telegram badge (TG, #229ED9 + paper-plane glyph) in `ChannelIconHelper.cs` and `inbox.js`;
  `telegram_customer` renders as "Customer"; composer never disabled for non-WhatsApp and the Send Template
  button is hidden. Integrations card shows bot, status, webhook status, last message received, Check webhook,
  Disconnect, "Reconnect or use a different bot".
- **Config**: `App:PublicBaseUrl` (HTTPS origin used to build the webhook URL; on Render
  `App__PublicBaseUrl=https://sculptflowapp.onrender.com`; locally an https tunnel) — if empty the request host is
  used unless it's localhost. `Telegram:ApiBaseUrl` (default `https://api.telegram.org`; test-only override for a
  fake Bot API). The bot token is entered in the UI, not config.
- **Files**: `Integrations/Telegram/{TelegramBotClient,TelegramUpdateParser,TelegramWebhookProcessor,
  TelegramChannelSender,TelegramIntegrationService}.cs`, `Controllers/TelegramWebhookController.cs`,
  `Services/{IChannelSender,DbErrors}.cs`; edits in MessageService, ConversationService, LeadService,
  ChannelIntegrationService, ChannelIntegrationsController, ConversationsController, Integrations page,
  ChannelIconHelper, inbox.js, DTOs/entities, Program.cs.
- **Verified** (local run against a fake Bot API + fake n8n, then all test data removed): connect (valid/malformed/
  rejected token), inbound text → lead/conversation/message/n8n, duplicate + concurrent duplicates, commands/media/
  groups ignored or saved without AI, human mode (saved, no n8n), AI reply, AI blocked after takeover (409, not
  sent), staff reply → human, Take Over/Return to AI, Telegram 403 → 502 with nothing persisted,
  disconnect/history/reconnect (secret rotated, old secret 403), two-clinic isolation (404s, separate leads, a bot
  can't serve two clinics, cross-secret 403), no token/secret in logs or HTML. **Not yet verified against the real
  Telegram API/real bot.**
- **Limitations / future**: outbound is text only; inbound media is stored as a placeholder + metadata (no
  download); no `/start` auto-greeting; >4096-char AI replies fail (502) rather than being split; Telegram leads
  have no phone so they're excluded from (WhatsApp) campaigns automatically; one bot ↔ one clinic; no
  `edited_message`. **Telegram Business** (`business_connection`/`business_message`/`business_connection_id`) is
  intentionally NOT built — seam: add the update types to `allowed_updates` in `SetWebhookAsync`, add a branch in
  `TelegramUpdateParser` mapping `business_message` onto `ParsedTelegramMessage`, and a business_connection_id →
  channel_integration lookup in the webhook controller; Lead/Conversation/Message/Inbox/n8n/send are unaffected.
