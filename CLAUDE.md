# SculptFlow — instructions for AI assistants

Never `git commit` or push unless Mohammad says so.

## Design doc (standing rule)

`docs/system-design.html` must always match the code. Whenever a change adds, removes or alters a feature (pages/JS, API
routes or auth, service rules, SignalR events, workers, integrations/OAuth, n8n contracts, `Database/schema.sql`, config
keys), update it in the same change: section 12 maps change kinds to sections; update "Last verified against code" in the
header; add a change-log row in section 12; note any drift you find in section 11. Write from the code, not from
`PROJECT_HANDOFF.md` (can be stale). Keep it plain HTML + CSS, no build step, no external scripts.

## Code architecture (standing rule, agreed 2026-10-06)

The admin portal (`..\SculptFlowAdmin`) uses the same layout. Namespace = folder path (`PlasticSurgery.Business.Services.Leads`).

| Folder | What goes there |
|---|---|
| `Controllers/Admin` | Platform-admin APIs (`/api/platform-admin/{domain}`, `[RequirePlatformAdminKey]`), called by the admin portal |
| `Controllers/Client` | Clinic dashboard APIs (signed-in staff); base `DashboardApiController` |
| `Controllers/Integrations` | Webhooks, n8n ingest and AI tool endpoints, OAuth callbacks |
| `Controllers/Filters` | Auth/filter attributes (`RequireIngestKey`, `RequirePlatformAdminKey`, `ApiErrors`) |
| `Pages/` | Razor Pages (PageModels follow the controller rules) |
| `Hubs/` | SignalR hubs |
| `Business/Services/<Feature>` | What controllers and PageModels call |
| `Business/Engines/<Feature>` | Backend/background logic: webhook processing, parsers, billing rules, chunking, scoring, scraping |
| `Business/Managers` | Cross-cutting helpers with state/dependencies: current clinic, event log, live notifications |
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
| `core` | clinics, procedures |
| `identity` | identity_users/_claims/_logins/_tokens, clinic_users |
| `crm` | leads, conversations, messages |
| `scheduling` | appointments, procedure_bookings, availability rules/exceptions, clinic_booking_settings, calendar integrations and syncs |
| `channels` | channel_integrations, whatsapp_templates, whatsapp_health_events, tiktok_integrations |
| `marketing` | campaigns, campaign_recipients |
| `knowledge` | every knowledge_* table |
| `activity` | events, notifications |
| `billing` | plans, rate cards, subscriptions, accounts, usage, ledger, channel account settings |
| `config` | settings (configuration overrides) |
| `admin` | the admin portal's own tables (SculptFlowAdmin repo) |

- New table → its area's schema; add a schema only for a genuinely new area (never `auth`, `storage`, `realtime`,
  `extensions`, `graphql`, `vault`: Supabase reserves them).
- `Database/schema.sql`, every `ToTable`, and raw SQL are schema-qualified (`crm.leads`). Changes stay re-runnable (guard
  renames/constraint adds in `do $$` blocks).
- When a table moves or is added, update the admin portal's `ApplicationDbContext` copy in the same change.

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

## Other docs (read on demand)

**Do not read `PROJECT_HANDOFF.md` unless Mohammad asks** (old Omni → new app migration notes, not being implemented).
Others: `README.md` (setup), `docs/billing.md`, `docs/configuration.md`, `docs/system-design.html`.

Keep threads short; if a task is unrelated to the current thread's task or the thread has grown long, suggest or start a
new thread.
