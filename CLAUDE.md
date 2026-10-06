# SculptFlow — instructions for AI assistants

## Keep the system design document current (standing rule)

`docs/system-design.html` is the high-level and low-level design of SculptFlow: frontend, backend, workers, SignalR,
integrations, the n8n contracts and the database. It must always match the code.

**Whenever a change adds, removes or alters a feature, update `docs/system-design.html` in the same change.** That
includes new or changed Razor Pages or JS files, API routes or their auth, service rules, SignalR events, background
workers, integrations or OAuth flows, anything n8n sends or receives, `Database/schema.sql`, and config keys. Section 12
of the document maps each kind of change to the sections to edit. Then:

1. Update "Last verified against code" (and the code baseline commit, if known) in the header.
2. Add a row to the change log in section 12.
3. If you find the document already disagreed with the code, fix it and note it in section 11 (doc drift).

Write it from the code, not from `PROJECT_HANDOFF.md` alone; the handoff's status lines can be stale. Keep the file
plain HTML + CSS with no build step and no external scripts, so it stays easy to edit by hand.

## Code architecture (standing rule, agreed with Mohammad 2026-10-06)

Every new or changed file follows this layout. The admin portal (`..\SculptFlowAdmin`) uses the same one. The namespace
always equals the folder path (`PlasticSurgery.Business.Services.Leads`).

| Folder | What goes there |
|---|---|
| `Controllers/Admin` | Platform-admin APIs (`/api/platform-admin/{domain}`, `[RequirePlatformAdminKey]`), called by the admin portal |
| `Controllers/Client` | Clinic dashboard APIs (signed-in staff). `DashboardApiController` is their base |
| `Controllers/Integrations` | Webhooks, n8n ingest and AI tool endpoints, OAuth callbacks |
| `Controllers/Filters` | Auth/filter attributes (`RequireIngestKey`, `RequirePlatformAdminKey`) |
| `Pages/` | Razor Pages. PageModels follow the controller rules below |
| `Hubs/` | SignalR hubs |
| `Business/Services/<Feature>` | What controllers and PageModels call |
| `Business/Engines/<Feature>` | Backend and background logic: webhook processing, parsers, billing rules, chunking, scoring, scraping |
| `Business/Managers` | Cross-cutting helpers with state or dependencies: current clinic, event log, live (SignalR) notifications |
| `Business/Providers/<Area>` | One adapter per external provider behind a shared interface (WhatsApp providers, channel senders) |
| `Business/HttpClients/<Provider>` | Typed clients for external APIs (Meta, Infobip, Telegram, Google/Outlook, TikTok, OpenAI, n8n) |
| `Business/Jobs` | Hosted background workers and their queues |
| `Business/Mappers` | Static entity → response mapping |
| `Business/Contracts/<same path>` | The interface of each class above, in the mirrored path (`Contracts/Services/Leads/ILeadService.cs`) |
| `Entities/Models` | Database table classes (EF entities) |
| `Entities/Requests/<Feature>`, `Entities/Responses/<Feature>` | API/page input and output shapes |
| `Entities/Dtos/<Feature>` | Other data shapes: internal results, list rows, provider payloads |
| `Common/Enums` | Enums, and string vocabularies used like enums (`LeadStatus`, `ConversationMode`) |
| `Common/Statics` | Constants, cache keys, event type names, SQL procedure names |
| `Common/Configs` | Options classes bound from configuration |
| `Common/Helpers` | Static helpers only (anything with dependencies is a Manager or Engine) |
| `Common/Exceptions`, `Common/Converters`, `Common/Extensions` | Exceptions, JSON converters, DI registration extension methods |
| `Persistence/Contexts` | DbContexts |
| `Persistence/Repositories/<Feature>`, `Persistence/Contracts/<Feature>` | Repositories and their interfaces; `IUnitOfWork` / `UnitOfWork` at the root |
| `Persistence/Helpers` | EF/Postgres-specific helpers (e.g. `DbErrors`) — nothing outside Persistence references EF Core or Npgsql |

Rules:

1. **One top-level type per file**, named after the type: a service, its interface and each request/response/DTO are
   separate files. Only private nested helper types may stay inside a class; anything public gets its own file. Parts of
   a partial class are named `Type.Part.cs`.
2. **Request flow: Controller or PageModel → Service → Repository.** A service may call other services, engines,
   managers, providers and HTTP clients. Controllers and PageModels contain no logic and never touch a DbContext: they
   bind input, call a service, and turn the result into an HTTP response or page state.
3. **Database access only in repositories.** One repository per area with intent-named methods (`GetByPhoneAsync`), never
   a generic `Repository<T>` and never an `IQueryable` handed out. A repository returns entities (tracked when the caller
   will change them, `...ReadOnlyAsync` otherwise) or `Entities/Dtos` rows for read-only projections. Repositories never
   call `SaveChanges`; they may run set-based statements (`ExecuteUpdate`/`ExecuteDelete`, raw SQL, row locks) when the
   method name says so.
4. **Services own saves and transactions** through `IUnitOfWork` (`SaveChangesAsync`, `TrySaveChangesAsync` for
   "someone else inserted it first" races, `BeginTransactionAsync`, `DiscardChanges`). All repositories in a request share
   one DbContext, so one save writes everything the operation changed. A unique-index violation surfaces as
   `DuplicateRecordException`, an exclusion-constraint violation as `OverlappingRecordException` — never catch EF or Npgsql
   types in Business.
5. **Billing money operations use their own unit of work**: `IBillingUnitOfWorkFactory.Create()` gives an
   `IBillingUnitOfWork` with its own context and the billing repositories (accounts with `LockAsync`, usage, ledger, plans,
   rate cards, subscriptions). Balances change only through `Business/Engines/Billing/BillingLedger.PostAsync`. See
   `docs/billing.md`.
6. Entity → response mapping lives in `Business/Mappers/<Feature>` (static), not in services, repositories or controllers.
7. Every Service, Engine, Manager, Provider, HttpClient, Job and Repository that is not a static class has an interface
   in the mirrored `Contracts` path and is registered against it in DI (repositories in `Common/Extensions/PersistenceModule`).
8. Use feature subfolders inside each layer; reuse an existing feature folder before adding a new one.

## Other project docs

- `PROJECT_HANDOFF.md` — session handoff and project state (decisions, TODOs, history). Update it in place too.
- `README.md` — short orientation and local setup.
