# Plastic Surgery Clinic Platform — MVP database + API + dashboard

This covers the first build slice: the Postgres schema, the ASP.NET Core Web API, and three
Razor Pages dashboard pages (Main Numbers, Interested People, Appointments), per the plan's
Phase 1 scope.

## Important: what was and wasn't verified

- **The SQL is real-tested.** `Database/schema.sql` and `Database/seed.sql` were run against an
  actual local Postgres 16 instance, including the dedup unique constraint (verified it rejects a
  duplicate `external_lead_id`) and the dashboard aggregate queries (verified the Main Numbers and
  per-procedure numbers against the seeded data by hand).
- **The C# builds and runs.** Verified end-to-end from Claude Code (which has `dotnet` access):
  `dotnet build` succeeds with 0 warnings/0 errors, `schema.sql` + `seed.sql` were run against a
  real Supabase Postgres instance (via the session pooler — the direct `db.*.supabase.co` host is
  IPv6-only and may not resolve on IPv4-only networks), and the app was run with `dotnet run` and
  the Main Numbers, Interested People, and Appointments dashboard pages were all confirmed live in
  a browser, correctly reading the seeded data from Supabase.

## Setup

1. **Database.** Point at a real Postgres instance (Supabase, or local) and run, in order:
   ```
   psql "<your connection string>" -f Database/schema.sql
   psql "<your connection string>" -f Database/seed.sql   # optional but recommended — gives the dashboard real numbers to show
   ```
   Both are idempotent (safe to re-run).

2. **Connection string.** From the `PlasticSurgery/PlasticSurgery` project folder:
   ```
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Port=5432;Database=...;Username=...;Password=..."
   ```
   (or edit `appsettings.Development.json` directly, but don't commit real credentials to git).
   For Supabase, use the connection string from Project Settings → Database — the **pooler**
   connection string (port 6543) is usually the right one for an app server.

3. **Clinic slug.** The dashboard has no login yet (per your call — MVP stays open), so it shows
   whichever clinic matches `Clinic:DefaultSlug` in `appsettings.json` (defaults to
   `demo-clinic`, which is what `seed.sql` creates). Change that config value, or the seed data,
   to match whichever clinic you want to see.

4. **Run it.**
   ```
   dotnet restore
   dotnet build
   dotnet run --project PlasticSurgery
   ```
   Dashboard: `/dashboard`, `/dashboard/leads`, `/dashboard/appointments`.
   Swagger (API explorer, dev only): `/swagger`.

## Architecture

- **Schema ownership:** `Database/schema.sql` is the source of truth (matches the plan's own
  "use SQL migrations, not the UI" principle). `ApplicationDbContext` maps onto it explicitly via
  Fluent API — EF Core is not used to generate or apply migrations here, so there's no
  `Migrations/` folder to keep in sync by hand.
- **One service layer, two front doors:** `Services/*` holds all the business logic (lead
  create/dedup, status transitions, appointment booking, dashboard aggregates). The REST API
  controllers (`Controllers/*`, for n8n/Meta webhooks/the AI agent) and the Razor Pages dashboard
  both call the same services directly, rather than the dashboard making HTTP calls to its own
  API — same logic, no redundant network hop inside one process.
- **Clinic scoping without auth yet:** every service method and API endpoint takes `clinicId`
  explicitly (query string on GET/PATCH, request body on POST) and filters by it — this is the
  plan's MULTI-CLINIC RULE, built in now so that adding real auth later means deriving
  `clinicId` from the authenticated caller instead of trusting the query string, not retrofitting
  scoping that wasn't there.
- **Dedup:** `POST /api/leads` returns the existing lead (200, not 201) if `externalLeadId` was
  already seen for that clinic — safe for n8n/webhook retries. Enforced at the database level too
  (`ux_leads_clinic_external_lead_id`), not just in application code.

## API endpoints

| Method | Path | Notes |
|---|---|---|
| POST | `/api/leads` | create or get-existing (dedup by `externalLeadId`) |
| GET | `/api/leads?clinicId=&status=&procedureId=&search=&skip=&take=` | |
| GET | `/api/leads/{id}?clinicId=` | |
| PATCH | `/api/leads/{id}?clinicId=` | partial update |
| POST | `/api/leads/{id}/status?clinicId=` | `{ status, qualificationStatus }` |
| GET | `/api/procedures?clinicId=&activeOnly=` | |
| POST | `/api/procedures` | |
| GET | `/api/appointments/available?clinicId=&days=` | naive placeholder slots — see below |
| POST | `/api/appointments` | |
| PATCH | `/api/appointments/{id}/status?clinicId=` | |
| GET | `/api/appointments?clinicId=&status=&from=&to=&skip=&take=` | |
| POST | `/api/conversations` | |
| GET | `/api/conversations/{id}?clinicId=` | conversation + its messages |
| POST | `/api/conversations/{id}/messages?clinicId=` | |
| POST | `/api/procedure-bookings` | |
| PATCH | `/api/procedure-bookings/{id}?clinicId=` | |
| GET | `/api/procedure-bookings?clinicId=&status=&skip=&take=` | |
| GET | `/api/dashboard/summary?clinicId=` | Main Numbers |
| GET | `/api/dashboard/leads?clinicId=&status=&skip=&take=` | Interested People |
| GET | `/api/dashboard/appointments?clinicId=&status=&skip=&take=` | Appointments |
| GET | `/api/dashboard/procedures?clinicId=` | leads/bookings/revenue per procedure |
| GET | `/api/channel-integrations?clinicId=` | WhatsApp/Instagram/Facebook connection status |
| PUT | `/api/channel-integrations` | save one channel's credentials (upsert) |
| POST | `/api/channel-integrations/{channel}/disconnect?clinicId=` | clear a channel's credentials |

## UI

The dashboard has its own hand-rolled CSS (`wwwroot/css/site.css`) — no Bootstrap/jQuery. Sidebar
layout: Main Numbers / Interested People / Appointments up top (daily-use pages), **Settings**
pinned at the bottom of the sidebar, separated by a divider, leading to **Channels &
Integrations** (`/settings/integrations`) — where WhatsApp Business / Instagram / Facebook
Messenger get connected per clinic. Settings lives apart from the main nav deliberately: it's rare
admin work (done once per clinic, not daily), not something a receptionist opens every hour — see
Project 5/13 in the plan for why that separation will matter more once real auth/roles exist.

## Known placeholders / deliberately deferred

- **`GET /api/appointments/available`** generates naive fixed hourly slots (9am–5pm, clinic
  timezone) minus existing bookings — there's no real calendar integration yet (Project 6).
  Replace `AppointmentService.GetAvailableSlotsAsync` when Google Calendar/Calendly wiring lands.
- **Dashboard pages built:** Main Numbers, Interested People, Appointments (the three the plan's
  First Milestone flow needs). Ads, Automations, and Old Leads pages are not built — they depend
  on data this MVP doesn't generate yet (ad spend tracking, automation run logs, reactivation
  campaigns), matching Phase 2/3 of the plan's build order.
- **"Old leads recovered"** on the Main Numbers page is hardcoded to 0 — wire it up once Project
  8 (lead reactivation) exists and reactivation attempts are tracked.
- **No auth.** Every endpoint is open and clinic-scoped only by an explicit `clinicId` parameter.
  Do not point this at real patient data until auth is added — see the plan's Compliance section.
- **WhatsApp's 24-hour messaging window** isn't modeled anywhere yet — `messages`/`conversations`
  don't distinguish "can send a free-form reply" from "must use a pre-approved template". This
  will matter as soon as Project 5/7 (conversation + follow-up engine) send outbound WhatsApp
  messages via the Business API.
- **Channels & Integrations doesn't actually call WhatsApp/Meta yet.** Settings → Channels &
  Integrations saves credentials (phone number ID, access token, etc.) and marks a channel
  "connected" once a token is on file, but nothing calls the Graph API to verify it actually
  works, and no webhook receiver consumes it yet — that's Project 5. `last_verified_at`/
  `last_error` on `channel_integrations` are there for when that wiring lands.
- **`channel_integrations.access_token`/`webhook_verify_token` are stored as plain text** — fine
  for local dev, not for a real WhatsApp/Meta app or real patient data. Move to an encrypted
  column or a secrets manager before going further than local testing.

## Next steps

- Run `dotnet build`, fix whatever the compiler finds (send me the errors).
- Point `ConnectionStrings:Postgres` at your real Supabase project and re-run schema.sql there.
- From here, the natural next slice is Project 5 (WhatsApp/Instagram conversation system) or the
  first n8n workflow (Test Lead → Webhook → `POST /api/leads` → Log Event), per the plan's "First
  Development Milestone."
