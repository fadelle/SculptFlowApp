# Omni — instructions for AI assistants

Shared rules (no commits, token saving, model choice): see the workspace root `..\CLAUDE.md`.

## Design doc (standing rule)

`docs/system-design.html` must always match the code. Whenever a change adds, removes or alters a feature (pages/JS, API
routes or auth, service rules, SignalR events, workers, integrations/OAuth, n8n contracts, `Database/schema.sql`, config
keys), update it in the same change: section 12 maps change kinds to sections; update "Last verified against code" in the
header; add a change-log row in section 12; note any drift you find in section 11. Write from the code, not from
`PROJECT_HANDOFF.md` (can be stale). Keep it plain HTML + CSS, no build step, no external scripts.

## UI (standing rule, agreed 2026-10-09)

UI: follow docs/UI_GUIDE.md for every screen; keep it up to date.

## Code architecture (standing rule, agreed 2026-10-06)

The admin portal (`..\SculptFlowAdmin`) uses the same layout. Namespace = folder path (`Omni.App.Business.Services.Contacts`).

| Folder | What goes there |
|---|---|
| `Controllers/Admin` | Platform-admin APIs (`/api/platform-admin/{domain}`, `[RequirePlatformAdminKey]`), called by the admin portal |
| `Controllers/Client` | Client dashboard APIs (signed-in `client` and `member` users); base `DashboardApiController` |
| `Controllers/Integrations` | Webhooks, n8n ingest and AI tool endpoints, OAuth callbacks |
| `Controllers/Filters` | Auth/filter attributes (`RequireIngestKey`, `RequirePlatformAdminKey`, `ApiErrors`) |
| `Pages/` | Razor Pages (PageModels follow the controller rules) |
| `Hubs/` | SignalR hubs |
| `Middleware/` | ASP.NET Core middleware (`RequestTimingMiddleware`: per-route API timings) |
| `Business/Services/<Feature>` | What controllers and PageModels call |
| `Business/Engines/<Feature>` | Backend/background logic: webhook processing, parsers, billing rules, chunking, scoring, scraping |
| `Business/Managers` | Cross-cutting helpers with state/dependencies: current client, event log, live notifications |
| `Business/Providers/<Area>` | One adapter per external provider behind a shared interface |
| `Business/HttpClients/<Provider>` | Typed clients for external APIs |
| `Business/Jobs` | Hosted background workers and queues |
| `Business/Mappers` | Static entity → response mapping |
| `Business/Contracts/<same path>` | Interface of each class above, mirrored path |
| `Entities/Models` | EF table classes |
| `Entities/Requests/<Feature>`, `Entities/Responses/<Feature>`, `Entities/Dtos/<Feature>` | Input, output, other data shapes |
| `Common/Enums`, `Common/Statics`, `Common/Configs`, `Common/Helpers` (static only), `Common/Exceptions`, `Common/Converters`, `Common/Extensions` | Enums/string vocabularies, constants/cache keys/event names/SQL names, options classes, helpers, exceptions, JSON converters, DI extensions |
| `Persistence/Contexts` | DbContexts |
| `Persistence/Repositories/<Feature>`, `Persistence/Contracts/<Feature>` | Repositories + interfaces; `IUnitOfWork`/`UnitOfWork` at root |
| `Persistence/Helpers` | EF/Postgres helpers (`DbErrors`); nothing outside Persistence references EF Core or Npgsql |

Rules:

1. One top-level type per file, named after the type (service, interface, each request/response/DTO separate). Only private
   nested helpers stay inside a class. Partial parts: `Type.Part.cs`.
2. Flow: Controller/PageModel → Service → Repository. Services may call services, engines, managers, providers, HTTP
   clients. Controllers/PageModels hold no logic and never touch a DbContext.
3. Database access only in repositories: one per area, intent-named methods (`GetByPhoneAsync`), no generic
   `Repository<T>`, never hand out `IQueryable`. Return entities (tracked if the caller will change them, `...ReadOnlyAsync`
   otherwise) or Dtos for read-only projections. Repositories never call `SaveChanges`; set-based statements
   (`ExecuteUpdate`/`ExecuteDelete`, raw SQL, row locks) are fine when the method name says so.
4. Services own saves/transactions via `IUnitOfWork` (`SaveChangesAsync`, `TrySaveChangesAsync`, `BeginTransactionAsync`,
   `DiscardChanges`). One DbContext per request. Unique violation → `DuplicateRecordException`, exclusion violation →
   `OverlappingRecordException`; never catch EF/Npgsql types in Business.
5. Billing money operations use `IBillingUnitOfWorkFactory.Create()` → `IBillingUnitOfWork` (own context + billing
   repositories). Balances change only via `Business/Engines/Billing/BillingLedger.PostAsync`. See `docs/billing.md`.
6. Entity → response mapping lives in `Business/Mappers/<Feature>` (static).
7. Every non-static Service, Engine, Manager, Provider, HttpClient, Job and Repository has an interface in the mirrored
   `Contracts` path and is registered against it in DI (repositories in `Common/Extensions/PersistenceModule`).
8. Use feature subfolders in each layer; reuse an existing one before adding a new one.
9. **No try/catch for errors in controllers.** Services throw; `[ApiErrors]` maps exceptions (`ArgumentException` 400,
   `KeyNotFoundException` 404, duplicate/overlap 409, `InvalidOperationException` 422, provider failures 502/503, body
   `{ "error": message }`). Put it on new JSON API controllers (`DashboardApiController` has it), never on provider
   webhooks (they must return 500 so the provider retries). Catch in a controller only when that action's response must
   differ, with a comment saying why.

## Database schemas (standing rule, agreed 2026-10-07)

Every table lives in its area's Postgres schema, never `public` (only `set_updated_at()` and extensions).

| Schema | Tables |
|---|---|
| `core` | business_types, clients, services |
| `identity` | users (role admin/client/member), user_claims/_logins/_tokens, client_users |
| `crm` | pipeline_stages, contacts, orders, conversations, messages, inbox_settings |
| `scheduling` | bookings, availability rules/exceptions, booking_settings, calendar integrations and syncs |
| `channels` | channel_integrations, whatsapp_templates, whatsapp_health_events, tiktok_integrations |
| `marketing` | campaigns, campaign_recipients |
| `knowledge` | every knowledge_* table |
| `activity` | events, notifications, api_route_stats (API timings), app_logs (error log), message_trace_steps (message trace) |
| `billing` | plans, rate cards, subscriptions, accounts, usage, ledger, channel account settings |
| `config` | settings (configuration overrides) |
| `admin` | the admin portal's own tables (admin portal repo, project `Omni.Admin`) |

- New table → its area's schema; add a schema only for a genuinely new area (never `auth`, `storage`, `realtime`,
  `extensions`, `graphql`, `vault`: Supabase reserves them).
- `Database/schema.sql`, every `ToTable`, and raw SQL are schema-qualified (`crm.contacts`). Changes stay re-runnable (guard
  renames/constraint adds in `do $$` blocks).
- The admin portal has no copy of these tables (it reads and writes only through `/api/platform-admin`), so a table change
  only needs the main app's code, plus the platform-admin API shapes if a portal page shows that data.

- `Database/migrations/2026-10-generic/copy-from-old.sql` copies the old project's data into a fresh Omni project (see
  `docs/system-design.html` §8.5). A `schema.sql` change to a table it copies (a column, a value vocabulary, a rename) must
  update the script, `test-copy-assert.sql` and `test-old-extras.sql` in the same change; run `test-copy.ps1`
  (needs a local PostgreSQL install, ~1 minute) before committing it. `Guards/CopyScriptCoverageTests` catches new/removed tables.

## Configuration (standing rule, agreed 2026-10-07)

Full detail and the declared-settings table: `docs/configuration.md`.

- Tunable values (limits, defaults, thresholds, timeouts, feature switches) are read through `IConfigManager`, one typed
  property per setting named `{Section}{Key}`. Never add a private `const` or an appsettings key for a tunable value.
- **Adding a setting:** (1) declare a `ConfigDefinition` in `Common/Statics/ConfigDefaults` and add it to
  `ConfigDefaults.All`; (2) add the typed property to `IConfigManager` + `ConfigManager`; (3) read it through the property
  each time it's needed (don't cache in a field); (4) add it to the table in `docs/configuration.md` and a row to
  `Database/seed-config.sql` (`insert ... on conflict (section, key) do nothing`). `ConfigSettingsTests` fails if a
  declared setting lacks a property or seed row.
- A secret may be a setting only when Mohammad asks (so far `Embeddings`/`ApiKey`), declared `IsSecret: true`. Every other
  secret stays in user-secrets / environment variables, never in tracked files or appsettings.
- `appsettings.json` / `Common/Configs` options hold only remaining secrets (via user-secrets/env), per-environment values
  (public URL, provider base URLs/client ids, connection strings) and structured rules (`Billing:WhatsApp`,
  `Billing:ProviderBilling`).

## Caching (standing rule, agreed 2026-10-07)

- Cache through `ICacheManager.GetOrCreateAsync(key, ttl, factory)` only (never a static dictionary or `IMemoryCache`
  directly). It sits on `ICacheAdapter`: `MemoryCacheAdapter` today; a Redis adapter registered in `Program.cs` replaces
  it without other changes.
- Every key and lifetime lives in `Common/Statics/CacheKeys` as `area:thing:id`, so an area can be dropped by prefix.
- Cache plain DTOs/records or read-only entity copies (stored as JSON copies), never tracked EF entities, and nothing
  whose staleness costs money (balances, ledger) or that changes every request (messages, conversations, bookings).
- Table caches (agreed 2026-10-14) live in the repository's `...ReadOnlyAsync` reads: clients, business types (with stages),
  inbox settings, client memberships, services, availability rules/settings/exceptions, calendar connections, channel connections, TikTok, WhatsApp
  templates, rating cards, rates, channel account billing. Code that will change a row loads it tracked (uncached).
  Their lifetimes are settings, one per `CacheArea` (`Cache:*Minutes`), passed as an area to `CacheReads`, never as a
  fixed `TimeSpan`.
- Clearing is automatic for tracked saves: `Persistence/Helpers/CacheInvalidationInterceptor` removes the keys
  `CacheKeys.EvictionsFor(row)` names for every added/changed/deleted row after the save, and again when its transaction
  commits or rolls back. A new cached table = keys in `CacheKeys` + a case in `EvictionsFor`. Set-based writes
  (`ExecuteUpdate`/`ExecuteDelete`, raw SQL) bypass it, so their repository method removes the keys itself.
- Other caches (computed results, e.g. entitlements): the service that changes the data removes its key (or prefix)
  right after the save; the lifetime is only a safety net. Add a line to the `CacheKeys` comment naming who clears it.
- Settings stay in `ConfigManager`'s own snapshot (read synchronously everywhere); cache clear-all reloads it.
- Admin: `/api/platform-admin/cache` (list, get, remove key/prefix, clear all) and the portal's Cache page.

## UI text and business types (standing rule, Milestone A S3)

- Every UI string a change writes or edits goes into `Omni.App/Resources/i18n/en.json` (flat keys `area.element.state`):
  Razor `@T["key"]` (`@T.Html("key", args)` when an argument is HTML, e.g. a `LocalTimeHelper.Time` tag), page models an
  injected `IStringLocalizer` with `…Key` constants, JS `omniText.t('key', [args])`, server messages `throw new ArgumentException("key").WithArgs(...)`
  (`[ApiErrors]` and `TextError.Localize` turn keys into text; a plain message still shows as written). An argument that is
  itself text is a `TextArg` (`TextArg.Of("key")`, `TextArg.Value(storedValue)`). Notifications take a `NotificationText`
  (keys + args, worded per reader). A message only n8n or an operator reads may stay English with `// text-ok: <reason>`
  on its line (`Guards/ServerMessageKeysTests`); text stored outside a request is worded with the localizer before saving.
- A stored value shown as a label goes through `T.ValueLabel(value)` (key `values.{value in camelCase}`; also
  `T.CampaignStatusLabel`, `T.TemplateStatusLabel`, `T.CategoryLabel`), never an English map in code; JS keeps
  `omniText.t('key')` literal (the guard only sees literal keys) and formats dates with `omniText.lang`.
- Placeholders: `{0}`… for arguments (`{0:N0}` formats a number in the viewer's language); `{contact}` `{contacts}` `{Contact}` `{Contacts}` (same for member, service, booking,
  order, client) for the client's business-type words. Never glue a word into a sentence Arabic can't follow.
  `Guards/UiTextKeysTests` fails on a missing or unused key or an unknown placeholder.
- Languages (Milestone B): `ar.json` and `fr.json` have the same keys as `en.json` (a missing key shows English). Once a
  key group is complete in a language (no longer listed in `Guards/i18n-incomplete.txt`), a new key in that group needs its
  translation too, and a translation equal to its English must be listed in `Guards/i18n-same-as-english.txt`; a
  translation never adds a placeholder its English lacks. Counts: plural families `key.one` / `key.other` (Arabic may add
  `zero`, `two`, `few`, `many`) read with `T.Plural("key", n)` / `T.PluralKey` / `omniText.plural('key', n)`, never
  `n == 1 ? … : …`; put the count in the text as `{0}`, also in the `.one` form (French uses it for 0).
- Dates and numbers people see: `value.Display("N0")`, `TextCulture.Date(day, DateStyle.…)` (C#/Razor), `omniText.locale`
  (JS). `CurrentCulture` is always invariant (machine values: form posts, `data-*`, URLs, JSON); the request's language is the
  `CurrentUICulture` (`AppCulture`). `Guards/DisplayFormatTests` fails on a display pattern in Pages without a culture.
- Which language a request uses: `ITextContext` (`RequestTextMiddleware`): `users.language` → `clients.default_language`
  → browser for signed-in users, the `omni.lang` cookie → browser for visitors, always English for an `/api` call without
  a signed-in user (portal, n8n, webhooks); only languages in `Localization:Languages` with a strings file.
- Business types (`core.business_types`) give each client its words, AI profile and pipeline stages
  (`crm.pipeline_stages`). Code and reports work with stage **categories** (`StageCategory`), never one type's stage keys:
  move a contact with `IBusinessTypeService.GetPipelineAsync` → `ClientPipeline` (`EntryKey`, `MoveTo(current, category)`,
  `Resolve(keyOrCategory)`); people see labels. No code branches on a business type's code.

## Users and roles (standing rule, Milestone A S7)

- One users table, `identity.users` (`AppUser : IdentityUser`), with `role` (`UserRole`): `admin` uses only the admin
  portal (it signs in through `/api/platform-admin/auth/login`), `client` and `member` use only this app. Only `client`
  manages members (`MemberService`, `UnauthorizedAccessException` → 403). Seats = active client + member users (`max_users`).
- Access is checked on every request, never only at login: `CurrentClientContext` (`UserAccess`: role, client, language)
  and the cookie's `OnValidatePrincipal`. A rule that depends on the role takes the caller's `UserAccess`.
- One active `client_users` row per user is a rule in code (check it before activating one), not an index.

## Platform-admin APIs (standing rule, agreed 2026-10-07)

The Omni.Admin portal reads and writes everything about clients through `/api/platform-admin/{domain}`; it has no
access to the main tables. Cross-client reads live in `Persistence/Repositories/PlatformAdmin` (+ `Entities/Dtos/PlatformAdmin`,
`Business/Mappers/PlatformAdmin`); `Business/Services/PlatformAdmin` delegates every write to the service a client uses
(so rules, provider calls and cache clearing happen once) and answers `PlatformAdminChange { clientId }` for the portal's
audit. Details never carry secrets. A new portal feature = endpoint here + client/service/page in the portal, with the
portal's copy of the request/response shapes kept in step.

## Old names guard (Milestone A)

`Omni.App.Tests/Guards/LeftoverNamesTests` fails when an old word (clinic, lead, procedure, appointment, staff, patient, <!-- leftover-ok: lists the old words -->
consultation, ai_agent, max_agents, the old product names…) appears in a tracked file outside <!-- leftover-ok: lists the old words -->
`Guards/leftover-names-allowlist.txt`, a `leftover-ok: <reason>` line marker or a `leftover-ok-begin:` / `leftover-ok-end`
block. `PendingScopes` is empty since Milestone A and stays empty: new code uses the new names.

## Other docs (read on demand)

**Do not read `PROJECT_HANDOFF.md` unless Mohammad asks** (old Omni → new app migration notes, not being implemented).
Others: `README.md` (setup), `docs/billing.md`, `docs/configuration.md`, `docs/system-design.html`,
`docs/deploy-runbook.md` (first Omni deploy: the one order of database, Render and n8n steps for both apps; keep it in
step with any change to that order).
