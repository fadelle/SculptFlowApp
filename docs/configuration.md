# Configuration

## Two kinds of settings

**Tunable settings** are limits, defaults and thresholds that an admin may want to change without a deploy. Code reads
them through `IConfigManager` by section and key:

```csharp
var minutes = _config.AvailabilityDefaultDurationMinutes;   // typed property, one per setting (use this)
var same = _config.GetInt("Availability", "DefaultDurationMinutes");   // generic, for code that works on any setting
```

The manager returns the value stored in `config.settings` for that section and key. When no row exists, it returns the
constant default declared in `Common/Statics/ConfigDefaults`.

- **Declaring a setting.** Add a `ConfigDefinition` to `ConfigDefaults` (section, key, default, type, optional min/max,
  description), list it in `ConfigDefaults.All`, and add its typed property `{Section}{Key}` to `IConfigManager` and
  `ConfigManager` (a test fails if the two lists disagree). Only declared settings appear on the admin portal's Configuration page,
  and only they can be stored. The settings API refuses any value that doesn't parse as the setting's type or falls outside
  its range.
- **Caching.** `ConfigManager` is a singleton. It keeps an in-memory copy of the table, which `ConfigRefreshJob` loads at
  startup and refreshes every 60 seconds. The settings API also refreshes it right after a change, so changes apply
  without a restart. Reads never wait on the database.
- **Failures.** A stored value that doesn't parse, for example one edited by hand in the database, is ignored: the default
  applies and a warning is logged. If the database can't be reached, the last values it read stay in use, or the defaults
  if it hasn't read any yet.
- **Changing a value.** Use the admin portal's Configuration page. It calls `/api/platform-admin/settings`, and every
  change is audited.

**Environment settings and secrets** stay in `appsettings.json`, bound to options classes in `Common/Configs`. Secrets come
from user-secrets in development and environment variables in production. This covers connection strings, API keys,
client secrets, public URLs and provider ids. They never go in `config.settings`.

## Declared settings (56)

Generated from `Common/Statics/ConfigDefaults`; keep this table in step when you add one.

| Section | Key | Default | Type / range | Meaning |
|---|---|---|---|---|
| Availability | DefaultDurationMinutes | 30 | int 5–480 | Consultation length (minutes) for clinics that haven't set their own booking settings. |
| Availability | DefaultBufferMinutes | 0 | int 0–240 | Gap (minutes) between consultations for clinics that haven't set their own booking settings. |
| Availability | DefaultNoticeMinutes | 240 | int 0–10080 | Minimum notice (minutes) before a bookable slot, for clinics that haven't set their own. |
| Availability | DefaultHorizonDays | 60 | int 1–365 | How many days ahead patients can book, for clinics that haven't set their own. |
| Availability | MaxRangeDays | 14 | int 1–60 | The most days one free-slots request may cover. |
| Billing | Enabled | true | bool | Master switch for usage billing and plan entitlements. Off = nothing is rated or reserved and every clinic may use everything. |
| Billing | GracePeriodDays | 7 | int 0–60 | How long a past-due subscription keeps working before it expires. |
| Billing | ReservationTimeoutHours | 72 | int 1–720 | A message charge still waiting for its delivery outcome is settled or released after this long. |
| Billing | MaintenanceIntervalMinutes | 5 | int 1–60 | How often the billing worker renews due subscriptions and resolves stale reservations. |
| Billing | SignupPlanCode | (empty) | string | Plan code a newly registered clinic starts on (first period free). Empty = no plan until an admin assigns one. |
| Billing | Currency | USD | string | The one account currency (ISO 4217, e.g. USD). Balances, plans and rates are stored in it: change it only before any money has moved. |
| Billing | RateBackdateToleranceMinutes | 5 | int 0–1440 | How far in the past a new rate may start or an old one may end (clock skew allowance). |
| Billing | RecentTransactions | 15 | int 1–100 | How many recent wallet transactions a clinic's billing page shows. |
| Knowledge | MinScore | 0.30 | decimal 0–1 | Minimum similarity for a search result, for new clinics (each clinic can change its own). |
| Knowledge | ChunkMaxChars | 1000 | int 200–4000 | Chunk size in characters for new clinics (stored per clinic in tokens). |
| Knowledge | ChunkOverlapChars | 150 | int 0–1000 | Chunk overlap in characters for new clinics. |
| Knowledge | ChunkMinChars | 200 | int 1–2000 | A trailing piece shorter than this is merged into the previous chunk. |
| Knowledge | MaxUploadBytes | 5242880 | int 1024–26214400 | Largest knowledge-base file a clinic can upload (bytes). |
| Knowledge | MaxExtractedChars | 250000 | int 1000–2000000 | Most text kept from one uploaded document. |
| Knowledge | MinChunkSizeTokens | 50 | int 10–1000 | Smallest chunk size a clinic may choose (tokens). |
| Knowledge | MaxChunkSizeTokens | 1000 | int 50–4000 | Largest chunk size a clinic may choose (tokens). |
| Knowledge | MinTopK | 1 | int 1–50 | Smallest number of search results a clinic may choose. |
| Knowledge | MaxTopK | 10 | int 1–50 | Largest number of search results a clinic may choose. |
| WebScraping | MaxPages | 100 | int 1–500 | Most distinct pages one website crawl visits. |
| WebScraping | MaxDepth | 3 | int 0–6 | Link depth from the start page. |
| WebScraping | MaxConcurrency | 3 | int 1–6 | Simultaneous requests to one website. |
| WebScraping | RequestTimeoutSeconds | 15 | int 2–60 | Timeout of one page request. |
| WebScraping | MaxResponseBytes | 2000000 | int 50000–10000000 | Largest page the crawler reads (bytes, after decompression). |
| WebScraping | MaxRedirects | 5 | int 0–10 | Redirects followed for one page. |
| WebScraping | PolitenessDelayMs | 300 | int 0–5000 | Pause before each request (robots.txt Crawl-delay can raise it). |
| WebScraping | MaxRunMinutes | 20 | int 1–120 | Wall-clock limit of one crawl. |
| WebScraping | MinTextChars | 80 | int 1–5000 | Pages with less readable text are skipped. |
| WebScraping | MaxTextChars | 200000 | int 1000–1000000 | Most text kept from one page. |
| WebScraping | MaxQueryVariantsPerPath | 5 | int 1–50 | Different query strings allowed for one path (stops calendar and filter traps). |
| WebScraping | MaxLinksPerPage | 300 | int 10–2000 | Most links taken from one page. |
| WebScraping | MaxSourcesPerClinic | 25 | int 1–200 | How many websites one clinic may add. |
| WebScraping | DevAllowedHosts | (empty) | string | Development environment only: comma-separated host:port entries the crawler may reach despite the SSRF rules (e.g. a local test site). Ignored outside Development. |
| WebScraping | UserAgent | SculptFlowBot/1.0 (+https://sculptflowapp.onrender.com; clinic knowledge-base crawler) | string | The crawler's User-Agent header. |
| Benchmark | GenerationSampleSize | 20 | int 1–200 | Chunks sent to n8n per question-generation request. |
| Benchmark | SamplePoolSize | 60 | int 1–1000 | Chunks sampled to choose the generation chunks from. |
| Benchmark | MinSourceChunkChars | 80 | int 1–5000 | Shortest chunk that may be used to generate a question. |
| Benchmark | MaxConsecutiveErrors | 5 | int 1–100 | A benchmark run stops after this many retrieval errors in a row. |
| Benchmark | StaleRunHours | 2 | int 1–48 | A run still marked running after this long is treated as abandoned. |
| Benchmark | GenerationTimeoutMinutes | 30 | int 1–240 | How long to wait for n8n to send generated questions. |
| Benchmark | MaxQuestionsPerChunk | 3 | int 1–10 | Most generated questions accepted per chunk. |
| Campaigns | DefaultInactiveDays | 60 | int 1–3650 | Default "inactive for" days of a reactivation audience. |
| Embeddings | BaseUrl | https://api.openai.com/v1 | string | Base URL of the OpenAI-compatible embeddings API. |
| Embeddings | Model | text-embedding-3-small | string | Embedding model. Stored vectors are tied to it: changing it means re-embedding every knowledge document. |
| Embeddings | Dimensions | 1536 | int 1–4096 | Vector size the model returns. Must equal the vector(N) size of knowledge.knowledge_chunks.embedding (1536). |
| Embeddings | ApiKey | (empty) | string | SECRET. API key of the embeddings API. Never shown back: the Configuration page and API only say whether it is set. |
| Embeddings | MaxInputsPerRequest | 64 | int 1–2048 | Texts sent to the embeddings API in one request. |
| Integrations | OAuthStateLifetimeMinutes | 15 | int 1–120 | How long a calendar or TikTok connect link stays valid. |
| Integrations | TokenRefreshMarginMinutes | 5 | int 0–60 | Calendar and TikTok access tokens are refreshed this long before they expire. |
| Infobip | TimeoutSeconds | 20 | int 5–120 | Timeout of one call to Infobip. |
| Infobip | MaxSendAttempts | 3 | int 1–5 | Total tries for a send Infobip clearly refused (429/503/no connection). |
| Dashboard | LeadsPageSize | 25 | int 5–200 | Leads per page on the dashboard. |

Only the Infobip HTTP timeout needs a new HTTP client to pick a change up (a new one is created per request scope), and the
billing worker reads its switch and interval on every cycle. Everything else is read where it is used, so a change applies
at once.

## What stays outside the configuration manager

- **Secrets** (user-secrets / environment variables): `ConnectionStrings:Postgres`, `PlatformAdmin:ApiKey`,
  `N8n:IngestApiKey`, `Meta:AppSecret`, `Meta:WebhookVerifyToken`, `Infobip:ApiKey`, `Infobip:WebhookToken`, every
  `ClientSecret`. The one exception is the embeddings API key, which Mohammad moved into the configuration manager as a
  **secret setting** (`IsSecret` in `ConfigDefaults`): it is stored in `config.settings` like the others, but the settings
  API and the Configuration page only say whether it is set, the admin audit log never records it, and the manager never
  logs it. The seed script stores it empty; set it on the Configuration page.
- **Per environment** (appsettings / environment variables): `App:PublicBaseUrl`, `Infobip:BaseUrl`, `Telegram:ApiBaseUrl`,
  the n8n webhook URLs, `Meta:AppId` / `GraphApiVersion` / login config ids, the Google, Microsoft and TikTok client ids and
  `TenantId`, `WhatsApp:Provider`, `Logging`, `AllowedHosts`.
- **Structured rules** in appsettings: `Billing:WhatsApp:*` and `Billing:ProviderBilling:Defaults:*` (lists and maps).
- **Constants** that are data or protocol limits: column lengths, Telegram's 4096-character limit, characters per token,
  the billing idempotency key length, clinic slug rules, preview lengths, API paging limits.

Three settings are tied to stored data, so change them with care: `Billing` / `Currency` (balances, plans and rates are
stored in it), `Embeddings` / `Model` and `Embeddings` / `Dimensions` (stored vectors were built with them; `Dimensions` must
equal the `vector(1536)` column).

## Seeding

`Database/seed-config.sql` inserts one row per declared setting with its default (run it after `schema.sql`). It never
overwrites an existing row, so values changed on the Configuration page survive a re-run. It is generated from
`ConfigDefaults`, and `ConfigSettingsTests` checks it covers every declared setting. A seeded row pins its value: if a
default later changes in code, databases seeded earlier keep the old value until it is changed or reset on the page.

**Deploying:** environment variables for the moved keys (for example `Billing__Enabled` or `Knowledge__MinScore` on Render)
no longer do anything. Set those values on the admin portal's Configuration page instead.
