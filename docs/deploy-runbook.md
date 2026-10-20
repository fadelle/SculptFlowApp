# Deploy runbook: moving to Omni (Milestone A)

One order for every step of the first Omni deploy, covering this app (`Omni.App`) and the admin portal (`Omni.Admin`,
its own repo). Both live on the branch `refactor/generic-platform` until the cutover. The database changes are not an
in-place upgrade: a **new** Supabase project is filled from the old one by a copy script, so the live old project keeps
running untouched until the cutover.

Background: `docs/system-design.html` §2.4 (deploy), §7.4 (n8n import), §8.5 (the copy), `docs/configuration.md`
(settings), `docs/billing.md` (seats and plans).

## Never do these

- **Never run the portal's `Database/admin-schema.sql` on the old (live) project.** It rewrites the admin tables: the old
  live portal and the copy script both read the old columns, so both would break. If it already ran there, stop and have
  `copy-from-old.sql` taught to accept the new shape before going on.
- **Never run `Database/seed.sql` or `Database/seed-demo.sql` on the new project.** They are demo data for local
  databases.
- Never commit a real host, password or key into either repo (the copy script takes them as `psql` variables).

## Values you will need

| What | Where it goes | Note |
|---|---|---|
| New Supabase project connection string (key=value, session pooler) | main app and portal `ConnectionStrings__Postgres` | same database for both apps |
| Old project database host, user, password | typed on the `psql` command line only | direct connection (`db.<old-ref>.supabase.co`); see step 4 if it can't be reached |
| A **new** ingest key (long random string) | main app `N8n__IngestApiKey` + the n8n credential "Omni ingest key" | the old key appeared in git history and in the old n8n exports |
| Platform-admin API key (keep the current one or make a new one) | main app `PlatformAdmin__ApiKey` = portal `MainApp__PlatformAdminApiKey` | the portal can't sign anyone in without it |
| The main app's public URL | main app `App__PublicBaseUrl`, portal `MainApp__PublicBaseUrl`, n8n base URL | the portal's `appsettings.json` points at the live URL, so staging must override it |

No other environment variable changed name in Milestone A. Five database settings did (`docs/configuration.md`); the copy
moves them.

## Phase 1: build the new database (no downtime)

1. Create the new Supabase project. `schema.sql` creates the `vector` and `btree_gist` extensions and the copy creates
   `postgres_fdw`; if Supabase refuses a `create extension`, enable it under Database → Extensions and re-run.
2. In the new project run, in this order (SQL editor, or `psql` from this repo's root):

   ```
   psql "<new connection string>" -f Database/schema.sql
   psql "<new connection string>" -f Database/seed-config.sql
   ```

3. Rehearse the copy (it can be repeated with `force=1` at the cutover). From this repo's root, on a machine with `psql`:

   ```
   psql "<new connection string>" -v old_host=db.<old-ref>.supabase.co -v old_user=postgres -v old_password=<old password> -f Database/migrations/2026-10-generic/copy-from-old.sql
   ```

   Check the end of the output: every table `ok`, `Result: ok`, the owner/member list (the earliest user of each client
   becomes its `client` user, everyone else a `member`) and the list of settings carried over. Any error or mismatch saves
   nothing.
4. If the new project can't reach the old direct host (the run fails at the foreign server), use the old project's
   **session** pooler instead: `-v old_host=<region>.pooler.supabase.com -v old_user=postgres.<old-ref>`. (Inferred from
   how Supabase connections work; the script was only tested against a local server.)
5. Then, still in the new project:

   ```
   psql "<new connection string>" -c "analyze;"
   psql "<new connection string>" -f ../<portal repo folder>/Database/admin-schema.sql
   ```

   `admin-schema.sql` changes nothing here (the copy already created `admin.admin_audit_log` in its final shape); running
   it confirms the portal's schema matches.

## Phase 2: staging (old production still live)

6. Main app: a second Render service from branch `refactor/generic-platform` (Dockerfile; it runs `Omni.App.dll`, so
   remove any start-command override that names the old dll). Environment: the production values, except
   `ConnectionStrings__Postgres` = new project, `App__PublicBaseUrl` = the staging URL, `N8n__IngestApiKey` = the new key,
   and the three `N8n__*WebhookUrl` left empty until step 8.
7. Portal: a second Render service from its `refactor/generic-platform` branch (runs `Omni.Admin.dll`). Environment:
   `ConnectionStrings__Postgres` = new project, `MainApp__PublicBaseUrl` = the staging main app URL,
   `MainApp__PlatformAdminApiKey` = the staging main app's `PlatformAdmin__ApiKey`.
8. n8n: import `docs/n8n/omni-ai-assistant.json`, `omni-calendar-sync.json`, `omni-retrieval-benchmark.json` (they import
   as new, inactive workflows next to the old ones). Create the Header Auth credential **Omni ingest key** (name
   `X-Ingest-Key`, value = the new key) and pick it on each HTTP node if n8n didn't; attach the OpenAI credential; replace
   `https://sculptflowapp.onrender.com` in the HTTP nodes with the staging URL; activate; copy the three production
   webhook URLs into the staging service's `N8n__AiWebhookUrl`, `N8n__CalendarSyncWebhookUrl`,
   `N8n__KnowledgeBenchmarkWebhookUrl`. Leave the old workflows on: production still uses them.
9. Check on staging:
   - Portal: sign in with your existing admin email and password (migrated hash). Admins, Clients, Business types,
     Billing → Plans: every plan needs a `max_users` (Seats) value, or its clients can't add members.
   - Dashboard: sign in as a migrated client user (their old password works) and a member; open Inbox, Contacts,
     Bookings, Services, Campaigns, Knowledge Base, Business info, Members, Billing.
   - AI: Meta's webhook still points at production, so test through a Telegram bot connected on staging (or the old
     production number only after the cutover). Run a short conversation in English, Arabic and French (ask, book,
     reschedule, cancel, hand-off): replies come in the contact's language and the stages move.

## Phase 3: cutover (short downtime)

10. Stop writes on the old side: suspend the production main app and portal services on Render and deactivate the old n8n
    workflows. Meta retries undelivered webhooks, so messages sent meanwhile arrive after the restart.
11. Copy again so the new project has the latest data:

    ```
    psql "<new connection string>" -v old_host=... -v old_user=... -v old_password=... -v force=1 -f Database/migrations/2026-10-generic/copy-from-old.sql
    psql "<new connection string>" -c "analyze;"
    ```

    `force=1` empties every table the copy fills, then copies. Check `Result: ok` and the owner list again.
12. Merge `refactor/generic-platform` into `main` in both repos and push.
13. Production main app on Render: `ConnectionStrings__Postgres` = new project, `N8n__IngestApiKey` = the new key, the three
    `N8n__*WebhookUrl` = the new workflows' URLs; remove any start-command override naming the old dll. Production portal:
    `ConnectionStrings__Postgres` = new project, `MainApp__PlatformAdminApiKey` = the main app's `PlatformAdmin__ApiKey`,
    `MainApp__PublicBaseUrl` = the production URL.
14. In the new n8n workflows, change the base URL from the staging URL to the production URL.
15. Resume (deploy) both production services. Everyone is signed out once (new cookie names `omni.auth`, `omni.admin`).
16. Smoke test production: portal sign-in, a client sign-in, a WhatsApp message to a real number gets an AI reply, a
    booking appears in the calendar and syncs.

## Phase 4: after the cutover

17. Delete the old n8n workflow exports in the workspace root's `docs/n8n/` folder (they hold the old ingest key) and the
    old workflows in n8n once the new ones have run cleanly.
18. Suspend or delete the two staging services.
19. Keep the old Supabase project paused for a while, then retire it and rotate its keys.
20. The automation tester was not updated in Milestone A: it still calls the old routes, so don't run it against the new app
    until it is.
21. Later, if you rename the GitHub repos, Render services or the public domain: update `App__PublicBaseUrl`, the portal's
    `MainApp__PublicBaseUrl`, the n8n base URL, the Google sign-in and calendar OAuth redirect URIs, the Outlook and TikTok
    redirect URIs, the Meta webhook URL, and reconnect Telegram bots (their webhooks are registered on the public URL). Then
    have the old Render host token removed from the old-names guard allowlists in both repos.

## First admin on a project without old data

The copy brings the old admins over. On an empty project (schema, seed-config, portal `admin-schema.sql`, no copy), make the
first admin from the main app's container or a local checkout:

```
ADMIN_PASSWORD=<at least 10 characters> dotnet Omni.App.dll create-admin you@example.com "Your Name"
```

(`dotnet run --project Omni.App -- create-admin …` from a checkout.) Add more admins from the portal's Admins page.
