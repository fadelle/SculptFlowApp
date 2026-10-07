# Subscriptions & usage billing

Developer guide for the billing module (`PlasticSurgery/Billing/`, tables in the Postgres schema `billing.*`). The
high-level picture and diagram are in `docs/system-design.html` §2.6; this file is the "how it works and how to extend it"
version.

## Three separate questions — never collapse them

| | 1. Platform subscription | 2. Provider billing responsibility | 3. SculptFlow usage billing |
|---|---|---|---|
| Question | What does the clinic pay SculptFlow for access? | Who pays the upstream provider (Meta, SMS operator…)? | Does SculptFlow charge the clinic for this usage? |
| Decided by | the clinic's **plan** | the **connected channel account** | the account's arrangement (+ rate cards for the price) |
| Code | `SubscriptionService`, `EntitlementService` | `ProviderBillingService` | `BillingService`, `MessageBillingService` |
| Money | wallet pays the plan price each period | never SculptFlow's wallet unless SculptFlow pays the provider | included credit first, then wallet |

There are no per-channel quotas. A plan may include a **monetary** credit (e.g. Growth: $99/month with $25 of credit) spent
on whatever SculptFlow charges, at its own selling price. Unused credit expires at renewal.

Everything is off until `Billing:Enabled = true`: nothing is recorded or charged, every clinic may use every feature, the
worker idles. Plans, rate cards, wallets and account arrangements can be prepared through the admin API while it's off.

## 1. Subscription model

- `billing.plans` (+ `billing.plan_entitlements`), one `billing.subscriptions` row per clinic.
- Status: `active` → (renewal not paid) `past_due` → (grace over, `Billing:GracePeriodDays`) `expired`; or `cancelled`
  (now, or at period end). `past_due` keeps the plan working during grace.
- Start / change plan (`POST /api/platform-admin/billing/clinics/{id}/subscription`, or `Billing:SignupPlanCode` at signup):
  new period from now; wallet pays the price (unless `chargeFirstPeriod=false`); old credit expires; plan credit granted.
  No proration.
- Renewal (`BillingMaintenanceWorker`, every few minutes): next contiguous period, same charges. Can't pay → `past_due`;
  every run retries, so a top-up renews it.
- Expired / cancelled / no plan: the clinic signs in and sees all its data, but can't send messages, run campaigns,
  use the AI agent or connect channels.

### Entitlements

Ask `IEntitlementService`, never compare plan names:

```csharp
var e = await _entitlements.GetAsync(clinicId, ct);
if (e.CanUseCampaigns) ...            // also CanUseAiAgent, CanUseApi, CanUseAdvancedReporting, CanSendMessages
var max = e.MaximumAgents;            // null = unlimited; also MaximumWhatsAppNumbers, MaximumChannelConnections
await _entitlements.EnsureFeatureAsync(clinicId, EntitlementKeys.Campaigns, ct);      // throws EntitlementDeniedException (422)
await _entitlements.EnsureWithinLimitAsync(clinicId, EntitlementKeys.MaxAgents, newCount, ct);
```

Keys live in `Common/Statics/EntitlementKeys.cs`: features take `true`/`false`, limits a number or `unlimited`. A key a plan doesn't
list is off / 0. Wired today: sending (subscription active), campaigns, AI trigger (`ai_agent`), channel connects
(`max_channel_connections`, `max_whatsapp_numbers`). `max_agents`, `api_access` and `advanced_reporting` are defined but
nothing checks them yet.

## 2. Provider billing responsibility

Configured per **connected channel account** (a `channel_integrations` row), never assumed per channel — two WhatsApp
accounts can differ.

| Value | Admin label | Meaning | SculptFlow usage billing by default |
|---|---|---|---|
| `customer_direct` | Customer pays provider directly | e.g. the clinic's own WABA with its own Meta payment method | off |
| `platform_funded` | SculptFlow pays provider | SculptFlow's Infobip account, an SMS aggregator SculptFlow owns | **on** |
| `external_provider_direct` | Customer pays external provider | the clinic's own Infobip/Viber/… account that SculptFlow only integrates | off |
| `no_provider_usage_fee` | No provider usage fee | Telegram, Messenger, TikTok conversations | off |

Resolution (`ProviderBillingService.ResolveAsync`): the account's override in `billing.channel_account_settings` (only
while the account is still connected through the provider it was set for) → `Billing:ProviderBilling:Defaults`
`"{channel}_{provider}"` → `"{channel}"` → built-in defaults. Built-ins: WhatsApp, SMS, Viber, RCS, email, voice =
`platform_funded`; Telegram, Facebook/Messenger, Instagram, TikTok, website = `no_provider_usage_fee`.

WhatsApp defaults to `platform_funded` for every provider because that's how billing behaved before this concept existed.
A clinic connected through Meta's own Embedded Signup usually pays Meta itself: set that account to `customer_direct`, or
set `Billing:ProviderBilling:Defaults:whatsapp_meta = customer_direct` once that's the norm.

The account override can also set **SculptFlow usage billing** explicitly: on for a customer-paid account (a SculptFlow usage
fee, priced by rates set for that responsibility) or off for a funded one (SculptFlow absorbs the cost).

## 3. SculptFlow usage billing

```
Channel account ─► provider billing responsibility ─┐
Usage ─► channel policy ─► Billable event ──────────┴─► SculptFlow charges it?
     yes ─► rating ─► reservation ─► send ─► provider outcome ─► settle (credit, then wallet) | release ─► ledger
     no  ─► usage record (provider cost where known) ─► provider outcome — no rate needed, no hold, no wallet, no ledger
```

### Billable events

`BillableEvent` = clinic, **idempotency key**, event type (`whatsapp_marketing_message`, `sms_segment`, `telegram_message`,
`voice_minute`, `ai_token`, …), channel, quantity, unit, country/operator/provider, **`ProviderBilling`**, **`ChargeUsage`**
(null = only when `platform_funded`), connected account, occurred-at, related message/conversation/campaign. Event types are
free text (`[a-z0-9_]+`); a new one needs only rates — and only if SculptFlow charges it.

### Usage records: two separate statuses

- `charge_status` (SculptFlow's money): `reserved` → `settled` | `released`; `failed` = refused (no rate / not enough
  balance); **`not_charged`** = SculptFlow doesn't bill it.
- `provider_outcome` (the provider side): `pending` → `billable` (delivered) | `not_billable` (failed).
- `provider_billing` snapshots who paid the provider; `provider_cost` holds the provider's cost when a rate knows it
  (paid by SculptFlow only when `platform_funded`); `amount` is what SculptFlow charged (0 when not charged).

### Wallet and ledger

One `billing.accounts` row per clinic: `wallet_balance`, `included_credit_balance`, `reserved_amount`.
**Spendable = wallet + included credit − reserved.** The wallet and included credit only finance what the clinic owes
SculptFlow. Every change is a `billing.ledger_entries` row in the same transaction (append-only), and
`GET …/reconciliation` checks they agree. Uncharged usage never creates a ledger row.

### Rate cards

- Cards: the **default** card, optional **plan** cards, at most one **clinic** card (custom pricing). Lookup: clinic card →
  plan card → default card; first card with a match wins; inside a card the most specific rate wins (country 8, operator 4,
  provider 2, provider billing 1 — null = any).
- A rate version has provider cost and client rate as **independent** numbers, a currency and an effective period.
  Versions never change (DB trigger) and never overlap (exclusion constraint); adding one closes the current one; none can
  start in the past. Usage snapshots its prices, so history never changes.
- `provider_billing` on a rate: null = SculptFlow-funded pricing (and cost reporting for any account). Set to e.g.
  `customer_direct` it is a **SculptFlow usage fee** for customer-paid accounts. Charging a non-funded account only ever uses
  a rate for exactly its responsibility — never the platform rate by fallback.
- Charged usage with no rate → refused (`rate_not_found`), recorded as failed, logged; nothing is guessed. Uncharged usage
  never fails for a missing rate.

### Reservation / settlement / release

| Call | Charged usage | Uncharged usage |
|---|---|---|
| `ReserveAsync(event)` | rate it, require spendable ≥ amount, hold it (`reserved`) | record it (`not_charged`, provider cost if known) |
| `SettleAsync(clinic, key, finalQuantity?)` | return the hold, charge final amount: included credit, then wallet; ledger rows | outcome → `billable` (+ final quantity) |
| `ReleaseAsync(clinic, key, reason)` | return the hold (`released`) | outcome → `not_billable` |
| `ChargeAsync(event)` | reserve + settle in one step (still prepaid) | record as already `billable` |
| `RefundAsync(clinic, usageId, reason)` | full amount back to where it came from, once | n/a |

**Idempotency.** Same key twice → the first result (`Duplicate = true`). Settle twice → one charge. Settle after release
(or release after settle) → `Conflict`, nothing changes. Admin top-ups, adjustments and plan starts take an
`Idempotency-Key` header; renewals derive their keys from the period.

**Concurrency / transactions.** Each operation uses its own unit of work (`IBillingUnitOfWorkFactory.Create()`, its own
`DbContext`), locks the clinic's `billing.accounts` row (`unit.Accounts.LockAsync`, `select … for update`), checks
idempotency after the lock, changes balances only through `BillingLedger.PostAsync`, writes, commits once.

## Channel integration

`IChannelBillingPolicy` (one per channel) says WHAT the usage is; `ProviderBillingService` says WHO pays and WHETHER SculptFlow
charges. `MessageBillingService` joins them for every outbound message: subscription check → resolve the account's
arrangement → record or reserve (key `{channel}:message:{messageId}`) → provider call (release on failure) → status callbacks
settle or release. A failure to record *uncharged* usage is logged and never blocks the message.

- **WhatsApp** (`Business/Engines/Billing/WhatsAppBillingPolicy.cs`): template category → `whatsapp_{category}_message`,
  country from the lead's number, provider = `WhatsApp:Provider`, outcome on delivered/read/failed. Free-form replies and
  utility templates inside the 24h window are not recorded (configurable under `Billing:WhatsApp`).
- **Telegram**: `telegram_message` per outbound message; default `no_provider_usage_fee` → recorded, never charged.

## Examples

| Situation | Provider billing | Usage record | Wallet / credit / ledger |
|---|---|---|---|
| Customer-funded WABA (clinic's own Meta card) | `customer_direct` | yes, Meta's cost recorded as paid by the clinic | untouched |
| SculptFlow-funded WhatsApp (SculptFlow's Infobip sender) | `platform_funded` | yes, cost + SculptFlow price | reserved, then credit/wallet + ledger |
| SMS through SculptFlow's aggregator (future) | `platform_funded` | yes, `sms_segment` × segments, destination pricing | reserved, then credit/wallet + ledger |
| Viber on the clinic's own provider account (future) | `external_provider_direct` | yes | untouched (unless a SculptFlow fee is turned on) |
| Messenger / TikTok / Telegram conversations | `no_provider_usage_fee` | yes (or nothing, if the policy doesn't describe it) | untouched — monetized through the subscription |
| Customer-funded WABA **with a SculptFlow usage fee** | `customer_direct` + usage billing on | yes | the fee only (rate with `provider_billing = customer_direct`) |

## How to add a new billable channel

1. Write `XxxBillingPolicy : IChannelBillingPolicy` next to the channel (`Integrations/Xxx/`), returning the event type and
   quantity (SMS: `sms_segment` × segments, country + operator; email: `email_recipient` × recipients; voice: `voice_minute`,
   settle the real duration via `finalQuantity`).
2. Register it in `BillingModule.AddBilling` (`services.AddScoped<IChannelBillingPolicy, XxxBillingPolicy>()`).
3. Add the channel's built-in default responsibility in `ProviderBillingService` if it isn't `platform_funded`.
4. If the channel sends through `MessageService`, nothing else is needed. Otherwise resolve the arrangement with
   `IProviderBillingService.ResolveAsync`, then call `IBillingService.ReserveAsync` / `SettleAsync` / `ReleaseAsync` with a
   stable key.
5. Add rates for the new event types where SculptFlow charges them.

## How to add a new plan

```
POST /api/platform-admin/billing/plans
X-Platform-Admin-Key: …
{ "code": "growth", "name": "Growth", "price": 99, "billingPeriod": "month", "includedUsageCredit": 25,
  "rateCardCode": null, "entitlements": { "campaigns": "true", "ai_agent": "true", "max_agents": "5",
  "max_whatsapp_numbers": "1", "max_channel_connections": "3" } }
```

Change it with `PUT /plans/{code}` (applies from each subscriber's next renewal) or `PUT /plans/{code}/entitlements`.
Deactivate instead of deleting. Assign: `POST /clinics/{id}/subscription` with `{ "planCode": "growth" }` and an
`Idempotency-Key` header.

## How to add a new rate

```
POST /api/platform-admin/billing/rate-cards/default/rates
{ "eventType": "whatsapp_marketing_message", "countryCode": "LB", "provider": "infobip", "unit": "message",
  "providerCost": 0.055, "clientRate": 0.07, "providerBilling": null }
```

Starts now (or later), closes the current version of the same rate. A SculptFlow usage fee for customer-paid WABAs:
`"providerBilling": "customer_direct", "clientRate": 0.01`. Custom pricing for one clinic: `POST /rate-cards` with
`{ "code": "clinic-acme", "name": "Acme pricing", "clinicId": "…" }`, then add only what differs. Check with
`GET /clinics/{id}/quote?eventType=…&country=…&provider=…&providerBilling=…`.

## How to set who pays the provider for an account

```
GET  /api/platform-admin/billing/clinics/{clinicId}/channel-accounts
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
3. Create the default rate card and its rates, and the plans.
4. Give every existing clinic a plan (and top up wallets as payments arrive); set each WhatsApp account's arrangement.
5. Optionally `Billing__SignupPlanCode`. Then `Billing__Enabled=true` and redeploy.

## Admin API (all under `/api/platform-admin/billing`, header `X-Platform-Admin-Key`, actor in `X-Admin-Actor`; normally used through the SculptFlowAdmin portal's Billing pages)

Plans: `GET/POST plans`, `GET/PUT plans/{code}`, `PUT plans/{code}/entitlements`, `GET entitlements` ·
Rate cards: `GET/POST rate-cards`, `PUT rate-cards/{code}`, `GET/POST rate-cards/{code}/rates`, `POST rates/{id}/close` ·
Provider billing: `GET provider-billing`, `GET clinics/{id}/channel-accounts`, `PUT channel-accounts/{id}/provider-billing`,
`POST channel-accounts/{id}/provider-billing/reset` ·
Clinics: `GET accounts`, `GET clinics/{id}`, `POST clinics/{id}/subscription` (+ `/cancel`, `/resume`, `/renew`),
`POST clinics/{id}/wallet/top-ups`, `POST clinics/{id}/wallet/adjustments`, `GET clinics/{id}/usage`,
`POST clinics/{id}/usage/{usageId}/refund`, `GET clinics/{id}/ledger`, `GET clinics/{id}/reconciliation`,
`GET clinics/{id}/quote` · Reporting: `GET report?from=&to=` (usage revenue, provider cost paid by SculptFlow vs paid
externally, margin, subscription revenue, top-ups). The clinic's own read-only view: Settings → Billing (SculptFlow charges
apart from provider-direct usage) and `GET /api/billing/summary|usage|transactions`.

## Postponed on purpose

Postpaid / credit limits (would add `credit_limit` to the spendable formula), invoicing, tax, automatic payments / a payment
gateway, multiple currencies (columns exist; one currency is enforced), reseller billing, proration, partial refunds,
markup-percentage pricing (rates hold an independent client rate today; a `pricing_mode` column would be the place),
recurring per-number / per-seat charges, and the SMS / Viber / email / RCS / voice /
AI channels themselves.

## Tests

`PlasticSurgery.Tests` — pure rules run anywhere; the database tests need a throwaway PostgreSQL:

```
$env:SCULPTFLOW_TEST_DB = "Host=localhost;Port=5432;Username=postgres;Password=…"   # never Supabase
dotnet test PlasticSurgery.Tests
```

Each run creates `sculptflow_test_*`, applies `Database/schema.sql`, and drops it. Every money test ends by checking the
ledger chain and that balances reconcile.
