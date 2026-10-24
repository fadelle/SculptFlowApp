-- =====================================================================
-- Billing seed (Milestone C): the plans on Omni's pricing page, the paid add-ons and the AI reply price.
--
--   Free Trial  $0 / 7 days  1 user,  1 channel,  inbox only
--   Inbox       $39 / month  2 users, 2 channels
--   + Bookings  $59 / month  3 users, 3 channels, bookings
--   Growth      $99 / month  5 users, 4 channels, bookings, campaigns, reporting, $10 credit
--   AI Pro     $169 / month  5 users, 4 channels, 2 WhatsApp numbers, 2 AI bots, AI assistant, reporting + advanced reporting, $20 credit
--   Business   $299 / month  12 users, unlimited channels, 5 WhatsApp numbers, 5 AI bots, reporting + advanced reporting, API access, $40 credit
--   AI reply   $0.02 (rate on the default rate card)
--   Add-ons    extra user $8, extra channel $5, extra AI bot $15, extra WhatsApp number $10 (per month)
--
-- Safe to re-run and safe in production: every row is inserted only when it is missing (plans by code, entitlements by
-- plan + key, add-ons by code, the default rate card when there is none, the ai_reply rate when the default card has
-- none), so prices and limits changed in the admin portal are never reset. Run after schema.sql and seed-config.sql.
-- Billing:SignupPlanCode = 'trial' (the default) starts every new client on the Free Trial.
-- =====================================================================

insert into billing.plans (code, name, description, price, currency, billing_period, included_usage_credit, trial_days, is_active, sort_order) values
  ('trial',          'Free Trial', 'Explore Omni for 7 days: 1 user, 1 channel, the inbox and contacts.', 0, 'USD', 'month', 0, 7, true, 10),
  ('inbox',          'Inbox',      'Never miss a customer conversation.', 39, 'USD', 'month', 0, null, true, 20),
  ('inbox-bookings', '+ Bookings', 'Turn conversations into confirmed bookings.', 59, 'USD', 'month', 0, null, true, 30),
  ('growth',         'Growth',     'Reach more customers: campaigns and $10 of monthly usage credit.', 99, 'USD', 'month', 10, null, true, 40),
  ('ai-pro',         'AI Pro',     'The AI assistant on your channels, advanced reporting and $20 of monthly usage credit.', 169, 'USD', 'month', 20, null, true, 50),
  ('business',       'Business',   'Communication and automation at scale, API access and $40 of monthly usage credit.', 299, 'USD', 'month', 40, null, true, 60)
on conflict (code) do nothing;

insert into billing.plan_entitlements (plan_id, entitlement_key, value)
select p.id, e.entitlement_key, e.value
from (values
  ('trial', 'max_users', '1'), ('trial', 'max_channel_connections', '1'), ('trial', 'max_whatsapp_numbers', '1'), ('trial', 'max_ai_bots', '0'),
  ('trial', 'bookings', 'false'), ('trial', 'campaigns', 'false'), ('trial', 'ai_assistant', 'false'), ('trial', 'reporting', 'false'), ('trial', 'advanced_reporting', 'false'),
  ('trial', 'api_access', 'false'),

  ('inbox', 'max_users', '2'), ('inbox', 'max_channel_connections', '2'), ('inbox', 'max_whatsapp_numbers', '1'), ('inbox', 'max_ai_bots', '0'),
  ('inbox', 'bookings', 'false'), ('inbox', 'campaigns', 'false'), ('inbox', 'ai_assistant', 'false'), ('inbox', 'reporting', 'false'), ('inbox', 'advanced_reporting', 'false'),
  ('inbox', 'api_access', 'false'),

  ('inbox-bookings', 'max_users', '3'), ('inbox-bookings', 'max_channel_connections', '3'), ('inbox-bookings', 'max_whatsapp_numbers', '1'),
  ('inbox-bookings', 'max_ai_bots', '0'), ('inbox-bookings', 'bookings', 'true'), ('inbox-bookings', 'campaigns', 'false'),
  ('inbox-bookings', 'ai_assistant', 'false'), ('inbox-bookings', 'reporting', 'false'), ('inbox-bookings', 'advanced_reporting', 'false'), ('inbox-bookings', 'api_access', 'false'),

  ('growth', 'max_users', '5'), ('growth', 'max_channel_connections', '4'), ('growth', 'max_whatsapp_numbers', '1'), ('growth', 'max_ai_bots', '0'),
  ('growth', 'bookings', 'true'), ('growth', 'campaigns', 'true'), ('growth', 'ai_assistant', 'false'), ('growth', 'reporting', 'true'), ('growth', 'advanced_reporting', 'false'),
  ('growth', 'api_access', 'false'),

  ('ai-pro', 'max_users', '5'), ('ai-pro', 'max_channel_connections', '4'), ('ai-pro', 'max_whatsapp_numbers', '2'), ('ai-pro', 'max_ai_bots', '2'),
  ('ai-pro', 'bookings', 'true'), ('ai-pro', 'campaigns', 'true'), ('ai-pro', 'ai_assistant', 'true'), ('ai-pro', 'reporting', 'true'), ('ai-pro', 'advanced_reporting', 'true'),
  ('ai-pro', 'api_access', 'false'),

  ('business', 'max_users', '12'), ('business', 'max_channel_connections', 'unlimited'), ('business', 'max_whatsapp_numbers', '5'),
  ('business', 'max_ai_bots', '5'), ('business', 'bookings', 'true'), ('business', 'campaigns', 'true'), ('business', 'ai_assistant', 'true'),
  ('business', 'reporting', 'true'), ('business', 'advanced_reporting', 'true'), ('business', 'api_access', 'true')
) as e(plan_code, entitlement_key, value)
join billing.plans p on p.code = e.plan_code
on conflict (plan_id, entitlement_key) do nothing;

insert into billing.addons (code, entitlement_key, name, unit_price, currency, is_active, sort_order) values
  ('extra-user',            'max_users',               'Extra user',            8,  'USD', true, 10),
  ('extra-channel',         'max_channel_connections', 'Extra channel',         5,  'USD', true, 20),
  ('extra-ai-bot',          'max_ai_bots',             'Extra AI bot',          15, 'USD', true, 30),
  ('extra-whatsapp-number', 'max_whatsapp_numbers',    'Extra WhatsApp number', 10, 'USD', true, 40)
on conflict (code) do nothing;

-- The default rate card (prices for every client without a plan or client card), when there is none yet.
insert into billing.rate_cards (code, name, description, is_default, is_active)
select 'default', 'Default prices', 'Prices for every client whose plan has no card of its own.', true, true
where not exists (select 1 from billing.rate_cards where is_default)
on conflict (code) do nothing;

-- AI replies: $0.02 each (estimated model cost $0.002), on the default card, when that card has no ai_reply rate yet.
insert into billing.rates (rate_card_id, event_type, unit, provider_cost, client_rate, currency, effective_from, notes, created_by)
select c.id, 'ai_reply', 'reply', 0.002, 0.02, 'USD', now(), 'Pricing page 2026-10-21: AI reply $0.02.', 'seed-billing'
from billing.rate_cards c
where c.is_default
  and not exists (select 1 from billing.rates r where r.rate_card_id = c.id and r.event_type = 'ai_reply');
