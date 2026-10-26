# Deploy runbook: moving to Omni (Milestones A and B, then C)

One order for every step of the first Omni deploy, covering this app (`Omni.App`) and the admin portal (`Omni.Admin`,
its own repo). Both live on the branch `refactor/generic-platform` until the cutover; Milestone A (generic business types)
and Milestone B (English, Arabic and French dashboard) ship together in this one deploy. The database changes are not an
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
moves them. Milestone B adds three database settings, created by `seed-config.sql`: `Localization:Languages` (`en,ar,fr`:
the languages people can pick; English is always on), `Localization:LanguageCookieDays` (365) and
`Localization:ShowTextKeys` (off; the review mode is refused in Production whatever the value). No environment variable.

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
   `X-Ingest-Key`, value = the new key) and pick it on each HTTP node if n8n didn't (including the AI workflow's
   `update_contact`, `update_contact_details`, `update_stage` and `get_services` tools); attach the OpenAI credential; replace
   `https://sculptflowapp.onrender.com` in the HTTP nodes with the staging URL; activate; copy the three production
   webhook URLs into the staging service's `N8n__AiWebhookUrl`, `N8n__CalendarSyncWebhookUrl`,
   `N8n__KnowledgeBenchmarkWebhookUrl`. Leave the old workflows on: production still uses them.
9. Check on staging:
   - Portal: sign in with your existing admin email and password (migrated hash). Admins, Clients, Business types,
     Billing → Plans: every plan needs a `max_users` (Seats) value, or its clients can't add members.
   - Dashboard: sign in as a migrated client user (their old password works) and a member; open Inbox, Contacts,
     Bookings, Services, Campaigns, Knowledge Base, Business info, Members, Billing. Switch the language in the account
     menu to العربية (pages turn right to left) and Français, and back to the business default.
   - Portal: change a client's Dashboard language on its details page; that client's Business info shows it.
   - AI: Meta's webhook still points at production, so test through a Telegram bot connected on staging (or the old
     production number only after the cutover). Run a short conversation in English, Arabic (Arabic script and Arabizi)
     and French (ask, book, reschedule, cancel, hand-off, then an emoji-only message): replies come in the language of the
     contact's latest message (the emoji gets the stored language), the contact page shows the language the AI stored, and
     the stages move.

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
17. Languages: `Localization:Languages` stays `en,ar,fr` (portal Configuration page) unless you want to hold a language
    back; `Localization:ShowTextKeys` stays off. Set the default language of any client that should open in Arabic or
    French (portal → client → Dashboard language); everyone else keeps English until they pick a language themselves.

## Phase 4: after the cutover

18. Delete the old n8n workflow exports in the workspace root's `docs/n8n/` folder (they hold the old ingest key) and the
    old workflows in n8n once the new ones have run cleanly.
19. Suspend or delete the two staging services.
20. Keep the old Supabase project paused for a while, then retire it and rotate its keys.
21. The automation tester was not updated in Milestone A: it still calls the old routes, so don't run it against the new app
    until it is.
22. Later, if you rename the GitHub repos, Render services or the public domain: update `App__PublicBaseUrl`, the portal's
    `MainApp__PublicBaseUrl`, the n8n base URL, the Google sign-in and calendar OAuth redirect URIs, the Outlook and TikTok
    redirect URIs, the Meta webhook URL, and reconnect Telegram bots (their webhooks are registered on the public URL). Then
    have the old Render host token removed from the old-names guard allowlists in both repos.

## Milestone C: go-live features (branch `feature/go-live`)

Milestone C (emails, invites, STOP opt-out, contact export and delete, booking reminders, the new plans with a free
trial, add-ons and AI bots) lives on `feature/go-live` in both repos, on top of `refactor/generic-platform`. Either merge
it into `refactor/generic-platform` before Phase 1 and do the steps below inside Phases 1 and 2, or deploy A+B first and
then do them as an upgrade of the new project (merge `feature/go-live` into `main` in both repos, push, then these steps).
Every database step below is re-runnable and keeps existing rows.

C1. **Brevo:** create the account, verify the sending domain (SPF, DKIM, DMARC records at your DNS host; without them
    resets and invites land in spam), make an API key and set it as `Brevo__ApiKey` on the main app's Render service
    (secret: never a database setting). In Production without the key every email fails ("couldn't send"); outside
    Production the emails are written to the log instead.
C2. **Sender and links:** on the portal's Configuration page set `Email:FromAddress` (an address on the verified domain)
    and, if you like, `Email:FromName` (default "Omni"). `App__PublicBaseUrl` must be the URL people open: reset and
    invite links are built from it.
C3. **Database**, from this repo's root:

    ```
    psql "<connection string>" -f Database/schema.sql
    psql "<connection string>" -f Database/seed-config.sql
    psql "<connection string>" -f Database/seed-billing.sql
    ```

    `schema.sql` adds `identity.account_tokens`, `scheduling.reminder_settings`, `scheduling.booking_reminders`,
    `billing.addons`, `billing.client_addons`, `billing.plans.trial_days` and the ledger type `addon_charge`.
    `seed-config.sql` adds the new settings (`Accounts`, `Email`, `OptOut`, `Reminders`). `seed-billing.sql` adds the six
    launch plans (`trial`, `inbox`, `inbox-bookings`, `growth`, `ai-pro`, `business`), the four add-ons (extra user $8,
    channel $5, AI bot $15, WhatsApp number $10 per month) and an `ai_reply` rate of $0.02 on the default rate card; it never
    changes rows that exist, so prices edited in the portal survive a re-run. A re-run still adds plan features a plan
    lacks, such as `reporting` (on for Growth, AI Pro and Business; off for the others).
C4. **Signup plan:** `seed-config.sql` only inserts `Billing:SignupPlanCode = trial` where no row exists, and the copy
    brought the old value over. If new signups should start on the 7-day Free Trial, set it to `trial` on the
    Configuration page (with `Billing:Enabled` on). A trial ends instead of renewing; move the client to a paid plan in the
    portal (Billing → client → Change plan).
C5. **Existing clients:** plans made before Milestone C have no `max_ai_bots`, so their clients keep the AI on every
    channel. Move them to the new plans when you are ready; on the new plans the AI answers only on as many connected
    channels as the plan's AI bots (plus add-ons), in connection order. Check the add-on prices on Billing → Add-ons.
C6. **WhatsApp Manager:** create a utility template for booking reminders in English, Arabic and French, with three body
    values: {{1}} the contact's name, {{2}} the date and time, {{3}} the business name. Once approved, each client picks
    them in Settings → Booking reminders and turns reminders on (they are off by default). Until then WhatsApp reminders
    are skipped and the booking page says why.
C7. **n8n:** nothing to import for Milestone C (the AI is simply not called for a STOP or START message).
C8. **Check on staging:** "Forgot password?" sends an email whose link works once; an owner invites a member, who accepts
    and signs in; a phone that sends STOP gets one confirmation and is then refused campaigns and Inbox templates (START
    undoes it); export and delete a test contact from the dashboard and from the portal; with reminders on, a booking 25
    hours ahead gets both reminders (or a reason on its booking page); Settings → Billing shows the trial's days left and
    any add-ons; the portal's Add-ons page and a client's "Change add-ons" work.

## In-code workflows (n8n or code, switchable)

The three n8n workflows also exist as code in the main app: the AI bot (`BotEngine`), calendar sync (`CalendarSyncClient`)
and the benchmark question generator (`KnowledgeBenchmarkGenerator`). Each has a switch on the portal's Configuration page,
a dropdown with `n8n` or `code`: `Ai / BotEngine`, `Calendars / SyncEngine`, `Benchmark / GeneratorEngine`. All three
default to `n8n`, so a deploy changes nothing until you flip one; every switch is read per message, sync or generation, so a
flip applies within a minute (the settings refresh), with no redeploy. The n8n steps above stay as they are while n8n is in use.

W1. **Database**, from this repo's root (re-runnable, keeps existing rows):

    ```
    psql "<connection string>" -f Database/schema.sql
    psql "<connection string>" -f Database/seed-config.sql
    ```

    `schema.sql` adds `crm.bot_memory` (the code bot's conversation memory); `seed-config.sql` adds the `Ai` settings,
    `Benchmark / GeneratorModel`, `Benchmark / GeneratorEngine` and `Calendars / SyncEngine`.
W2. **AI key:** on the Configuration page set `Ai / ApiKey` (an OpenAI key; secret, never shown back), or leave it empty to
    use `Embeddings / ApiKey`.
W3. **Models and limits:** check `Ai / BotModel` (gpt-6-luna, as the n8n agent), `Ai / FormatterModel` (gpt-4o-mini),
    `Benchmark / GeneratorModel` (gpt-4o), `Ai / MaxToolIterations` (10), `Ai / MemoryExchanges` (10), `Ai / TimeoutSeconds`
    (60) and `Ai / MaxConcurrentRuns` (4).
W4. **Flip one switch at a time to `code` and test it before the next:**
    - `Ai / BotEngine`: message the bot on WhatsApp and on Telegram (a question, availability, booking, rescheduling,
      cancelling, asking for a person) and use the bot editor's test panel. The portal's message trace shows every step
      (`bot.run_started`, `bot.model_called`, `bot.decision`, tools, `bot.action_result`). Memory starts empty for each
      conversation on the code bot (n8n's memory lived in n8n).
    - `Calendars / SyncEngine`: with a Google and an Outlook calendar connected, create, reschedule and cancel a booking and
      check the event appears, moves and disappears on each. A failed call now marks the connection unhealthy (with the
      usual notification) where n8n stayed silent.
    - `Benchmark / GeneratorEngine`: on the portal's Search benchmark page, Generate Test Cases; it answers "pending" and the
      questions arrive within a minute or two (a model problem fails the generation with its reason).
W5. **Switching back:** set the switch to `n8n` again on the Configuration page. Work already queued for the code engine
    still finishes; new messages, syncs and generations go to n8n. Keep the n8n workflows active until every switch has
    stayed on `code` for a while.

## First admin on a project without old data

The copy brings the old admins over. On an empty project (schema, seed-config, portal `admin-schema.sql`, no copy), make the
first admin from the main app's container or a local checkout:

```
ADMIN_PASSWORD=<at least 10 characters> dotnet Omni.App.dll create-admin you@example.com "Your Name"
```

(`dotnet run --project Omni.App -- create-admin …` from a checkout.) Add more admins from the portal's Admins page.
