# Subscriptions & usage billing

Developer guide for the billing module (`Omni.App/Billing/`, tables in the Postgres schema `billing.*`). The
high-level picture and diagram are in `docs/system-design.html` §2.6; this file is the "how it works and how to extend it"
version.

## Three separate questions — never collapse them

| | 1. Platform subscription | 2. Provider billing responsibility | 3. Omni usage billing |
|---|---|---|---|
| Question | What does the client pay Omni for access? | Who pays the upstream provider (Meta, SMS operator…)? | Does Omni charge the client for this usage? |
| Decided by | the client's **plan** | the **connected channel account** | the account's arrangement (+ rate cards for the price) |
| Code | `SubscriptionService`, `EntitlementService` | `ProviderBillingService` | `BillingService`, `MessageBillingService` |
| Money | wallet pays the plan price each period | never Omni's wallet unless Omni pays the provider | included credit first, then wallet |

There are no per-channel quotas. A plan may include a **monetary** credit (e.g. Growth: $99/month with $25 of credit) spent
on whatever Omni charges, at its own selling price. Unused credit expires at renewal.

Everything is off until `Billing:Enabled = true`: nothing is recorded or charged, every client may use every feature, the
worker idles. Plans, rate cards, wallets and account arrangements can be prepared through the admin API while it's off.

## 1. Subscription model

- `billing.plans` (+ `billing.plan_entitlements`), one `billing.subscriptions` row per client.
- Status: `active` → (renewal not paid) `past_due` → (grace over, `Billing:GracePeriodDays`) `expired`; or `cancelled`
  (now, or at period end). `past_due` keeps the plan working during grace.
- Start / change plan (`POST /api/platform-admin/billing/clients/{id}/subscription`, or `Billing:SignupPlanCode` at signup):
  new period from now; wallet pays the price (unless `chargeFirstPeriod=false`); old credit expires; plan credit granted.
  No proration.
- Renewal (`BillingMaintenanceWorker`, every few minutes): next contiguous period, same charges (plan + add-ons). Can't
  pay → `past_due`; every run retries, so a top-up renews it.
- Trials: a plan with `trial_days` (1–90, price 0) is a free trial. Its period lasts that many days and it never renews:
  at the end the subscription expires (event `subscription_expired` with `trialEnded`), the credit expires, and an admin
  moves the client to a paid plan. `Billing:SignupPlanCode` defaults to `trial`, the seeded Free Trial.
- Add-ons (`billing.addons` catalog, `billing.client_addons` per client, `IAddonService`): extra units of a limit
  (`extra-user` $8, `extra-channel` $5, `extra-ai-bot` $15, `extra-whatsapp-number` $10 per month in the seed). The
  client's limit is the plan's value + its units (`AddonLimits`; `unlimited` stays unlimited). Added units are charged
  from the wallet at once (`addon_charge`, no proration, refused when the wallet can't pay or there is no active
  subscription); every period start charges the plan and the add-ons together (one `addon_charge` row); removing units
  refunds nothing. Set from the admin portal: `PUT clients/{id}/addons/{code}` `{ "quantity": 2 }` + `Idempotency-Key`.
- Expired / cancelled / no plan: the client signs in and sees all its data, but can't send messages, run campaigns,
  use the AI assistant or connect channels.

### Entitlements

Ask `IEntitlementService`, never compare plan names:

```csharp
var e = await _entitlements.GetAsync(clientId, ct);
if (e.CanUseCampaigns) ...            // also CanUseBookings, CanUseAiAssistant, CanUseApi, CanUseAdvancedReporting, CanSendMessages
var max = e.MaximumUsers;            // null = unlimited; also MaximumWhatsAppNumbers, MaximumChannelConnections, MaximumAiBots
await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);      // throws EntitlementDeniedException (422)
await _entitlements.EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxUsers, newCount, ct);
```

Keys live in `Common/Statics/EntitlementKeys.cs`: features take `true`/`false`, limits a number or `unlimited`. A key a plan doesn't
list is off / 0. Wired today: sending (subscription active), campaigns, bookings (`bookings`, below), the AI bot (`ai_assistant`: AI trigger, conversation
routing and Return to AI, see `InboxSettingsService`), channel connects
(`max_channel_connections`, `max_whatsapp_numbers`), AI bots (`max_ai_bots`: the first that many connected channels, in
connection order, get the AI; on the others conversations go to the team with reason `ai_bot_limit` and Return to AI is
refused; a plan without the key has no AI bot limit, see `InboxSettingsService`) and seats (`max_users`: active client and member users, checked when
the client user adds or reactivates a member, see `MemberService`). `api_access` and `advanced_reporting` are defined but
nothing checks them yet.

**`bookings`** (an Inbox-only plan leaves it off): `[RequireFeature(EntitlementKeys.Bookings)]` (`Controllers/Filters`) on
`/api/bookings` and `/api/calendar-integrations` (422 like any `EnsureFeatureAsync` refusal) and on the AI booking tools
`/api/ai/bookings/*` with `AiTool = true` (200 `{ success: false, code: "FEATURE_NOT_INCLUDED", instruction }`, so the AI
stops offering bookings and hands off instead of reading a billing message to the contact). `BookingService` refuses every
booking change (create, book, schedule, reschedule, cancel, status), `CalendarIntegrationService` refuses connect, refresh,
choosing a calendar and turning sync on (turning it off and disconnecting stay allowed) and pushes nothing to external
calendars. The Bookings pages and Settings → Calendar Integrations say "not included" and the sidebar hides both links.
Billing off: unchanged. There are no booking reminders in the app today (campaign type `reminder` is only a label).

## 2. Provider billing responsibility

Configured per **connected channel account** (a `channel_integrations` row), never assumed per channel — two WhatsApp
accounts can differ.

| Value | Admin label | Meaning | Omni usage billing by default |
|---|---|---|---|
| `customer_direct` | Customer pays provider directly | e.g. the client's own WABA with its own Meta payment method | off |
| `platform_funded` | Omni pays provider | an SMS aggregator Omni owns | **on** |
| `external_provider_direct` | Customer pays external provider | the client's own Viber/SMS/… provider account that Omni only integrates | off |
| `no_provider_usage_fee` | No provider usage fee | Telegram, Messenger, TikTok conversations | off |

Resolution (`ProviderBillingService.ResolveAsync`): the account's override in `billing.channel_account_settings` → `Billing:ProviderBilling:Defaults`
`"{channel}_{provider}"` → `"{channel}"` → built-in defaults. Built-ins: WhatsApp, SMS, Viber, RCS, email, voice =
`platform_funded`; Telegram, Facebook/Messenger, Instagram, TikTok, website = `no_provider_usage_fee`.

WhatsApp defaults to `platform_funded` because that's how billing behaved before this concept existed. A client
connected through Meta's Embedded Signup usually pays Meta itself: set that account to `customer_direct`, or
set `Billing:ProviderBilling:Defaults:whatsapp_meta = customer_direct` once that's the norm.

The account override can also set **Omni usage billing** explicitly: on for a customer-paid account (an Omni usage
fee, priced by rates set for that responsibility) or off for a funded one (Omni absorbs the cost).

## 3. Omni usage billing

```
Channel account ─► provider billing responsibility ─┐
Usage ─► channel policy ─► Billable event ──────────┴─► Omni charges it?
     yes ─► rating ─► reservation ─► send ─► provider outcome ─► settle (credit, then wallet) | release ─► ledger
     no  ─► usage record (provider cost where known) ─► provider outcome — no rate needed, no hold, no wallet, no ledger
```

### Billable events

`BillableEvent` = client, **idempotency key**, event type (`whatsapp_marketing_message`, `sms_segment`, `telegram_message`,
`voice_minute`, `ai_token`, …), channel, quantity, unit, country/operator/provider, **`ProviderBilling`**, **`ChargeUsage`**
(null = only when `platform_funded`), connected account, occurred-at, related message/conversation/campaign. Event types are
free text (`[a-z0-9_]+`); a new one needs only rates — and only if Omni charges it.

### Usage records: two separate statuses

- `charge_status` (Omni's money): `reserved` → `settled` | `released`; `failed` = refused (no rate / not enough
  balance); **`not_charged`** = Omni doesn't bill it.
- `provider_outcome` (the provider side): `pending` → `billable` (delivered) | `not_billable` (failed).
- `provider_billing` snapshots who paid the provider; `provider_cost` holds the provider's cost when a rate knows it
  (paid by Omni only when `platform_funded`); `amount` is what Omni charged (0 when not charged).

### Wallet and ledger

One `billing.accounts` row per client: `wallet_balance`, `included_credit_balance`, `reserved_amount`.
**Spendable = wallet + included credit − reserved.** The wallet and included credit only finance what the client owes
Omni. Every change is a `billing.ledger_entries` row in the same transaction (append-only), and
`GET …/reconciliation` checks they agree. Uncharged usage never creates a ledger row.

### Rate cards

- Cards: the **default** card, optional **plan** cards, at most one **client** card (custom pricing). Lookup: client card →
  plan card → default card; first card with a match wins; inside a card the most specific rate wins (country 8, operator 4,
  provider 2, provider billing 1 — null = any).
- A rate version has provider cost and client rate as **independent** numbers, a currency and an effective period.
  Versions never change (DB trigger) and never overlap (exclusion constraint); adding one closes the current one; none can
  start in the past. Usage snapshots its prices, so history never changes.
- `provider_billing` on a rate: null = Omni-funded pricing (and cost reporting for any account). Set to e.g.
  `customer_direct` it is a **Omni usage fee** for customer-paid accounts. Charging a non-funded account only ever uses
  a rate for exactly its responsibility — never the platform rate by fallback.
- Charged usage with no rate → refused (`rate_not_found`), recorded as failed, logged; nothing is guessed. Uncharged usage
  never fails for a missing rate.

### Reservation / settlement / release

| Call | Charged usage | Uncharged usage |
|---|---|---|
| `ReserveAsync(event)` | rate it, require spendable ≥ amount, hold it (`reserved`) | record it (`not_charged`, provider cost if known) |
| `SettleAsync(client, key, finalQuantity?)` | return the hold, charge final amount: included credit, then wallet; ledger rows | outcome → `billable` (+ final quantity) |
| `ReleaseAsync(client, key, reason)` | return the hold (`released`) | outcome → `not_billable` |
| `ChargeAsync(event)` | reserve + settle in one step (still prepaid) | record as already `billable` |
| `RefundAsync(client, usageId, reason)` | full amount back to where it came from, once | n/a |

**Idempotency.** Same key twice → the first result (`Duplicate = true`). Settle twice → one charge. Settle after release
(or release after settle) → `Conflict`, nothing changes. Admin top-ups, adjustments and plan starts take an
`Idempotency-Key` header; renewals derive their keys from the period.

**Concurrency / transactions.** Each operation uses its own unit of work (`IBillingUnitOfWorkFactory.Create()`, its own
`DbContext`), locks the client's `billing.accounts` row (`unit.Accounts.LockAsync`, `select … for update`), checks
idempotency after the lock, changes balances only through `BillingLedger.PostAsync`, writes, commits once.

## Channel integration

`IChannelBillingPolicy` (one per channel) says WHAT the usage is; `ProviderBillingService` says WHO pays and WHETHER Omni
charges. `MessageBillingService` joins them for every outbound message: subscription check → resolve the account's
arrangement → record or reserve (key `{channel}:message:{messageId}`) → provider call (release on failure) → status callbacks
settle or release. A failure to record *uncharged* usage is logged and never blocks the message.

- **WhatsApp** (`Business/Engines/Billing/WhatsAppBillingPolicy.cs`): template category → `whatsapp_{category}_message`,
  country from the contact's number, provider `meta`, outcome on delivered/read/failed. Free-form replies and
  utility templates inside the 24h window are not recorded (configurable under `Billing:WhatsApp`).
- **Telegram**: `telegram_message` per outbound message; default `no_provider_usage_fee` → recorded, never charged.

### AI replies (`ai_reply`)

Every AI assistant reply (`MessageService.SendAiReplyAsync`, the n8n `messages/send` with `sender: "ai"`) is charged one
`ai_reply`, on top of the channel's own message usage above:

1. `IMessageBillingService.ReserveAiReplyAsync` reserves it before anything is sent: key `ai:reply:{messageId}`, channel
   `ai`, quantity 1, unit `reply`, source `system`. Who pays the AI provider: `Billing:ProviderBilling:Defaults:ai`
   (built-in `platform_funded`, so Omni charges it; set it to e.g. `no_provider_usage_fee` to only record it).
2. Then the channel's message usage is reserved and the message sent. A send failure (or a refused channel reservation)
   releases the `ai_reply` hold; acceptance settles it at once: included credit first, then the wallet.
3. Refused (no `ai_reply` rate → `rate_not_found`, not enough credit + wallet → `insufficient_funds`): the refusal is a
   `failed` usage row like any other, nothing is sent, the conversation is handed to the team
   (`ConversationService.HandOverUnpaidAiReplyAsync`: human mode, needs-human stage, a Handoff notification saying why,
   `human_handoff` event from `system` with reason `ai_reply_{reason}`) and n8n gets 409 `ai_reply_refused`. Members
   press Return to AI once the wallet is topped up.
4. A reservation left open (crash between send and settle) is settled by the maintenance worker when the AI message row
   exists, released when it doesn't.

`Billing:ChargeAiReplies` (default `true`) turns the charge off without touching rates. **Charging needs an `ai_reply`
rate**: with billing on and no rate, every AI reply is refused and goes to the team. An `ai_message` ingested by n8n
(sent outside the app) is not charged.

## Examples

| Situation | Provider billing | Usage record | Wallet / credit / ledger |
|---|---|---|---|
| Customer-funded WABA (client's own Meta card) | `customer_direct` | yes, Meta's cost recorded as paid by the client | untouched |
| Omni-funded WhatsApp (today's default) | `platform_funded` | yes, cost + Omni price | reserved, then credit/wallet + ledger |
| SMS through Omni's aggregator (future) | `platform_funded` | yes, `sms_segment` × segments, destination pricing | reserved, then credit/wallet + ledger |
| Viber on the client's own provider account (future) | `external_provider_direct` | yes | untouched (unless an Omni fee is turned on) |
| Messenger / TikTok / Telegram conversations | `no_provider_usage_fee` | yes (or nothing, if the policy doesn't describe it) | untouched — monetized through the subscription |
| Customer-funded WABA **with an Omni usage fee** | `customer_direct` + usage billing on | yes | the fee only (rate with `provider_billing = customer_direct`) |

## How to add a new billable channel

1. Write `XxxBillingPolicy : IChannelBillingPolicy` next to the channel (`Integrations/Xxx/`), returning the event type and
   quantity (SMS: `sms_segment` × segments, country + operator; email: `email_recipient` × recipients; voice: `voice_minute`,
   settle the real duration via `finalQuantity`).
2. Register it in `BillingModule.AddBilling` (`services.AddScoped<IChannelBillingPolicy, XxxBillingPolicy>()`).
3. Add the channel's built-in default responsibility in `ProviderBillingService` if it isn't `platform_funded`.
4. If the channel sends through `MessageService`, nothing else is needed. Otherwise resolve the arrangement with
   `IProviderBillingService.ResolveAsync`, then call `IBillingService.ReserveAsync` / `SettleAsync` / `ReleaseAsync` with a
   stable key.
5. Add rates for the new event types where Omni charges them.

## How to add a new plan

The launch plans, add-ons and the `ai_reply` rate are in `Database/seed-billing.sql` (re-runnable; it never changes rows
that exist). Run it once per database after `schema.sql`.

```
POST /api/platform-admin/billing/plans
X-Platform-Admin-Key: …
{ "code": "growth", "name": "Growth", "price": 99, "billingPeriod": "month", "includedUsageCredit": 25,
  "rateCardCode": null, "entitlements": { "campaigns": "true", "bookings": "true", "ai_assistant": "true", "max_users": "5",
  "max_whatsapp_numbers": "1", "max_channel_connections": "3" } }
```

Bookings and the AI are separate switches: an Inbox plan sets `bookings` and `ai_assistant` to `false`, Inbox + Booking
sets `bookings` only, AI plans set `ai_assistant` (and usually `bookings`) and an `includedUsageCredit` that AI replies
are paid from first.

Change it with `PUT /plans/{code}` (applies from each subscriber's next renewal) or `PUT /plans/{code}/entitlements`.
Deactivate instead of deleting. Assign: `POST /clients/{id}/subscription` with `{ "planCode": "growth" }` and an
`Idempotency-Key` header.

## How to add a new rate

```
POST /api/platform-admin/billing/rate-cards/default/rates
{ "eventType": "whatsapp_marketing_message", "countryCode": "LB", "provider": "meta", "unit": "message",
  "providerCost": 0.055, "clientRate": 0.07, "providerBilling": null }
```

Starts now (or later), closes the current version of the same rate. AI replies (example price, about $0.02 each; set
`providerCost` to what one reply costs Omni at the AI provider):

```
POST /api/platform-admin/billing/rate-cards/default/rates
{ "eventType": "ai_reply", "unit": "reply", "providerCost": 0.004, "clientRate": 0.02, "providerBilling": null }
```

An Omni usage fee for customer-paid WABAs:
`"providerBilling": "customer_direct", "clientRate": 0.01`. Custom pricing for one client: `POST /rate-cards` with
`{ "code": "client-acme", "name": "Acme pricing", "clientId": "…" }`, then add only what differs. Check with
`GET /clients/{id}/quote?eventType=…&country=…&provider=…&providerBilling=…`.

## How to set who pays the provider for an account

```
GET  /api/platform-admin/billing/clients/{clientId}/channel-accounts
PUT  /api/platform-admin/billing/channel-accounts/{channelIntegrationId}/provider-billing
     { "providerBilling": "customer_direct", "omniUsageBilling": null, "reason": "Own WABA, own Meta card" }
POST /api/platform-admin/billing/channel-accounts/{channelIntegrationId}/provider-billing/reset   { "reason": "…" }
GET  /api/platform-admin/billing/provider-billing      (the four modes with labels, and the defaults per channel)
```

Changes apply to future usage only; recorded usage keeps its snapshot. Each change is audited in `events`
(`provider_billing_changed`, with before/after, actor and reason).

## Going live (once)

1. Apply the billing block of `Database/schema.sql` to Supabase (creates schema `billing`; needs `btree_gist`).
2. Set `PlatformAdmin__ApiKey` on Render (a long random secret) and the same value as `MainApp__PlatformAdminApiKey` on the admin portal.
3. Run `Database/seed-billing.sql` (plans, add-ons, the default rate card and its `ai_reply` rate), then add the other
   rates (without `ai_reply` every AI reply is refused).
   Give every plan a `max_users` ("Seats") value: seats are enforced, so a plan without it lets a client add no members.
   Set `bookings` = true on every plan that includes bookings: a plan without it gets no Bookings pages, AI booking tools
   or calendar sync.
4. Give every existing client a plan (and top up wallets as payments arrive); set each WhatsApp account's arrangement.
5. Optionally `Billing` / `SignupPlanCode`, then `Billing` / `Enabled` on the admin portal's Configuration page (they are
   database settings now; the old `Billing__*` environment variables do nothing). No redeploy needed.

## Admin API (all under `/api/platform-admin/billing`, header `X-Platform-Admin-Key`, actor in `X-Admin-Actor`; normally used through the Omni.Admin portal's Billing pages)

Plans: `GET/POST plans`, `GET/PUT plans/{code}`, `PUT plans/{code}/entitlements`, `GET entitlements` ·
Add-ons: `GET addons`, `PUT addons/{code}`, `GET clients/{id}/addons`, `PUT clients/{id}/addons/{code}` ·
Rate cards: `GET/POST rate-cards`, `PUT rate-cards/{code}`, `GET/POST rate-cards/{code}/rates`, `POST rates/{id}/close` ·
Provider billing: `GET provider-billing`, `GET clients/{id}/channel-accounts`, `PUT channel-accounts/{id}/provider-billing`,
`POST channel-accounts/{id}/provider-billing/reset` ·
Clients: `GET accounts`, `GET clients/{id}`, `POST clients/{id}/subscription` (+ `/cancel`, `/resume`, `/renew`),
`POST clients/{id}/wallet/top-ups`, `POST clients/{id}/wallet/adjustments`, `GET clients/{id}/usage`,
`POST clients/{id}/usage/{usageId}/refund`, `GET clients/{id}/ledger`, `GET clients/{id}/reconciliation`,
`GET clients/{id}/quote` · Reporting: `GET report?from=&to=` (usage revenue, provider cost paid by Omni vs paid
externally, margin, subscription revenue, top-ups). The client's own read-only view: Settings → Billing (Omni charges
apart from provider-direct usage) and `GET /api/billing/summary|usage|transactions`.

## Postponed on purpose

Postpaid / credit limits (would add `credit_limit` to the spendable formula), invoicing, tax, automatic payments / a payment
gateway, multiple currencies (columns exist; one currency is enforced), reseller billing, proration, partial refunds,
markup-percentage pricing (rates hold an independent client rate today; a `pricing_mode` column would be the place),
and the SMS / Viber / email / RCS / voice /
AI channels themselves.

## Tests

`Omni.App.Tests` — pure rules run anywhere; the database tests need a throwaway PostgreSQL:

```
$env:OMNI_TEST_DB = "Host=localhost;Port=5432;Username=postgres;Password=…"   # never Supabase
dotnet test Omni.App.Tests
```

Each run creates `omni_test_*`, applies `Database/schema.sql`, and drops it. Every money test ends by checking the
ledger chain and that balances reconcile.
