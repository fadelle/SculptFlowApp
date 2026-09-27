# MySculptFlow (SculptFlow)

Multi-tenant SaaS for plastic surgery clinics. A lead messages a clinic on WhatsApp or Telegram, an AI agent
(orchestrated by n8n) answers from the clinic's Knowledge Base, qualifies the lead and books consultations, and
staff can take over any conversation from the dashboard Inbox. Clinics also run WhatsApp template campaigns
(especially old-lead reactivation) against their existing leads.

**For the full, current state of the project read [`PROJECT_HANDOFF.md`](PROJECT_HANDOFF.md) first.** It is the
single source of truth for architecture, tables, endpoints, config keys, migrations, decisions and open TODOs.
This README is only a short orientation.

## Stack

- .NET 10, ASP.NET Core Razor Pages (staff dashboard) + Web API controllers (dashboard JS, n8n, Meta, Telegram).
  Project/assembly name is `PlasticSurgery`; only user-visible text says MySculptFlow.
- EF Core / Npgsql on Supabase PostgreSQL with pgvector. The schema is hand-written in `Database/schema.sql`
  (idempotent, appended blocks), not EF migrations; `ApplicationDbContext` is kept in sync by hand.
- ASP.NET Core Identity (role-free); every user belongs to exactly one clinic via `clinic_users`.
- SignalR (`/hubs/inbox`) for live Inbox, calendar and notification updates; PostgreSQL stays the source of truth.
- Hand-rolled CSS and small vanilla-JS files in `wwwroot/` (no frontend framework).
- Deployed as a Docker image on Render (`https://sculptflowapp.onrender.com`) from `main`.

## Main features

- Inbox (WhatsApp + Telegram) with AI / human modes, take over / return to AI, unread tracking, 24h WhatsApp window
- Leads, procedures, appointments month calendar, structured clinic availability, AI booking tools
  (`get_available_slots`, `schedule_consultation`, `cancel_consultation`, `get_my_appointments`)
- WhatsApp Embedded Signup, templates, health monitoring; Telegram bot channel
- Campaigns with audience picker (old leads who never booked, all contactable, custom filters, manual)
- Knowledge Base: manual entries, PDF/DOCX/TXT upload, website crawling, semantic search, retrieval benchmark
- Staff notification bell; Calendar Integrations (one-way Google/Outlook sync through a separate n8n workflow)
- Self-service registration (new user → new isolated clinic), Staff list, Clinic Info settings

## Local setup

1. **Database.** Run against a Postgres instance with pgvector (Supabase or local), in order:
   ```
   psql "<connection string>" -f Database/schema.sql
   psql "<connection string>" -f Database/seed.sql   # optional demo clinic, procedures and leads
   ```
   Both are idempotent.

2. **Secrets.** From the `PlasticSurgery` project folder, use user-secrets (never commit real values):
   ```
   dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Port=5432;Database=...;Username=...;Password=..."
   ```
   Use key=value format (not a `postgresql://` URI) and, for Supabase, the session pooler host. The other secrets
   (`Meta:*`, `N8n:*`, `Embeddings:ApiKey`) are listed in `PROJECT_HANDOFF.md` §20.

3. **Run.**
   ```
   dotnet build
   dotnet run --project PlasticSurgery --launch-profile https
   ```
   Open `https://localhost:7276`, register an account (this creates a new clinic) and you land on `/dashboard`.
   Swagger is at `/swagger` in Development only. There is no Razor runtime compilation, so `.cshtml`/`.cs` changes
   need a stop, build and run.

## Known gaps

See `PROJECT_HANDOFF.md` §17 (pending work) and §22 (TODOs). The most important: rotate the secrets that were
once in git history, derive `clinicId` server-side for the AI endpoints, verify Meta webhook signatures, add
forwarded-headers handling and persistent DataProtection keys, and protect the anonymous `POST /api/leads`.
There is no automated test project.
