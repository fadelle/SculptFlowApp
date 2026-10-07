-- =====================================================================
-- Plastic Surgery Clinic Platform — MVP schema
-- =====================================================================
-- Run this directly against your Postgres/Supabase database
-- (psql, or the Supabase SQL Editor). This file is the source of
-- truth for the schema; the EF Core entity classes in
-- PlasticSurgery/Entities/Models are mapped to match it exactly via
-- Fluent API rather than owning migrations themselves — see
-- PlasticSurgery/Persistence/Contexts/ApplicationDbContext.cs.
--
-- Idempotent: safe to re-run (uses IF NOT EXISTS / OR REPLACE).
--
-- Tables live in one Postgres schema per area (never in public):
--   core        clinics, procedures
--   identity    ASP.NET Identity users/claims/logins/tokens, clinic_users
--   crm         leads, conversations, messages
--   scheduling  appointments, procedure bookings, availability, calendar sync
--   channels    channel integrations, WhatsApp templates/health, TikTok
--   marketing   campaigns, campaign recipients
--   knowledge   the knowledge base, website scraping, retrieval benchmarks
--   activity    events, notifications
--   billing     plans, rate cards, subscriptions, wallets, usage, ledger
--   config      settings (configuration overrides)
-- public keeps only the shared set_updated_at() function and extensions.
-- The admin portal's own tables live in admin.* (SculptFlowAdmin repo).
-- Always schema-qualify table names (core.clinics, crm.leads, ...).
-- =====================================================================

-- ---------------------------------------------------------------------
-- Helper: auto-maintain updated_at on every table that has one
-- ---------------------------------------------------------------------
create or replace function set_updated_at()
returns trigger as $$
begin
  new.updated_at = now();
  return new;
end;
$$ language plpgsql;

-- ---------------------------------------------------------------------
-- Schemas, and the one-time move of databases created before 2026-10-07
-- (when every table was in public). alter table ... set schema keeps
-- the data, indexes, constraints, triggers and foreign keys; it runs
-- only for a table still in public, so re-running this file is a no-op.
-- ---------------------------------------------------------------------
create schema if not exists core;
create schema if not exists identity;
create schema if not exists crm;
create schema if not exists scheduling;
create schema if not exists channels;
create schema if not exists marketing;
create schema if not exists knowledge;
create schema if not exists activity;
create schema if not exists billing;
create schema if not exists config;

do $$
declare t record;
begin
  for t in select * from (values
    ('clinics', 'core'),
    ('procedures', 'core'),
    ('identity_users', 'identity'),
    ('identity_user_claims', 'identity'),
    ('identity_user_logins', 'identity'),
    ('identity_user_tokens', 'identity'),
    ('clinic_users', 'identity'),
    ('leads', 'crm'),
    ('conversations', 'crm'),
    ('messages', 'crm'),
    ('appointments', 'scheduling'),
    ('procedure_bookings', 'scheduling'),
    ('clinic_availability_rules', 'scheduling'),
    ('clinic_availability_exceptions', 'scheduling'),
    ('clinic_booking_settings', 'scheduling'),
    ('calendar_integrations', 'scheduling'),
    ('calendar_integration_calendars', 'scheduling'),
    ('appointment_calendar_syncs', 'scheduling'),
    ('channel_integrations', 'channels'),
    ('whatsapp_templates', 'channels'),
    ('whatsapp_health_events', 'channels'),
    ('tiktok_integrations', 'channels'),
    ('campaigns', 'marketing'),
    ('campaign_recipients', 'marketing'),
    ('knowledge_documents', 'knowledge'),
    ('knowledge_chunks', 'knowledge'),
    ('knowledge_search_settings', 'knowledge'),
    ('knowledge_website_sources', 'knowledge'),
    ('knowledge_website_pages', 'knowledge'),
    ('knowledge_website_scrape_runs', 'knowledge'),
    ('knowledge_retrieval_benchmark_cases', 'knowledge'),
    ('knowledge_retrieval_benchmark_runs', 'knowledge'),
    ('knowledge_retrieval_benchmark_results', 'knowledge'),
    ('knowledge_retrieval_benchmark_generations', 'knowledge'),
    ('events', 'activity'),
    ('notifications', 'activity')
  ) as m(table_name, schema_name)
  loop
    if to_regclass('public.' || t.table_name) is not null
       and to_regclass(t.schema_name || '.' || t.table_name) is null then
      execute format('alter table public.%I set schema %I', t.table_name, t.schema_name);
    end if;
  end loop;
end $$;

-- ---------------------------------------------------------------------
-- 1. clinics
-- ---------------------------------------------------------------------
create table if not exists core.clinics (
  id            uuid primary key default gen_random_uuid(),
  name          varchar(200) not null,
  slug          varchar(100) not null unique,
  phone         varchar(50),
  email         varchar(200),
  website       varchar(300),
  country_code  varchar(10),
  timezone      varchar(100) not null default 'UTC',
  address            text,
  operating_hours    text,
  consultation_info  text,
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

drop trigger if exists trg_clinics_updated_at on core.clinics;
create trigger trg_clinics_updated_at
  before update on core.clinics
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 2. procedures  (catalog of services a clinic offers)
-- ---------------------------------------------------------------------
create table if not exists core.procedures (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references core.clinics(id) on delete cascade,
  name                   varchar(200) not null,
  code                   varchar(100),
  description            text,
  consultation_duration  integer,
  is_active              boolean not null default true,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_procedures_clinic_id on core.procedures(clinic_id);

drop trigger if exists trg_procedures_updated_at on core.procedures;
create trigger trg_procedures_updated_at
  before update on core.procedures
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 3. leads
-- ---------------------------------------------------------------------
create table if not exists crm.leads (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,
  procedure_id          uuid references core.procedures(id) on delete set null,

  full_name             varchar(200),
  first_name            varchar(100),
  last_name             varchar(100),
  phone                 varchar(50),   -- store normalized E.164 where possible
  email                 varchar(200),

  source                varchar(100),
  source_detail         varchar(200),
  campaign_name         varchar(200),
  external_lead_id      varchar(200),  -- id from Meta/Instagram/website form, used for dedup

  status                varchar(50) not null default 'new',
  qualification_status  varchar(50) not null default 'unknown',

  preferred_language    varchar(20),
  country_code          varchar(10),
  city                  varchar(100),
  desired_timeline      varchar(100),
  notes                 text,

  assigned_staff_id     uuid,

  -- consent / opt-out (Development Principle 9: build this in early)
  marketing_opt_in      boolean not null default true,
  opted_out_at          timestamptz,

  last_contact_at       timestamptz,
  next_followup_at      timestamptz,

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_leads_status check (status in (
    'new','contacted','qualified','consultation_booked','consultation_attended',
    'no_show','surgery_booked','not_interested','needs_human','lost'
  )),
  constraint ck_leads_qualification_status check (qualification_status in (
    'unknown','hot','warm','cold','needs_human','medical_question','spam'
  ))
);

-- Dedup: the same external lead (e.g. same Meta lead_id) should not be inserted twice per clinic.
create unique index if not exists ux_leads_clinic_external_lead_id
  on crm.leads(clinic_id, external_lead_id)
  where external_lead_id is not null;

create index if not exists ix_leads_clinic_id on crm.leads(clinic_id);
create index if not exists ix_leads_clinic_phone on crm.leads(clinic_id, phone);
create index if not exists ix_leads_clinic_email on crm.leads(clinic_id, email);
create index if not exists ix_leads_status on crm.leads(status);
create index if not exists ix_leads_procedure_id on crm.leads(procedure_id);
create index if not exists ix_leads_created_at on crm.leads(created_at);
create index if not exists ix_leads_next_followup_at on crm.leads(next_followup_at) where next_followup_at is not null;

drop trigger if exists trg_leads_updated_at on crm.leads;
create trigger trg_leads_updated_at
  before update on crm.leads
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 4. conversations
-- ---------------------------------------------------------------------
create table if not exists crm.conversations (
  id                      uuid primary key default gen_random_uuid(),
  clinic_id               uuid not null references core.clinics(id) on delete cascade,
  lead_id                 uuid not null references crm.leads(id) on delete cascade,
  channel                 varchar(50) not null,
  external_thread_id      varchar(200),
  status                  varchar(50) not null default 'active',
  ai_enabled              boolean not null default true,
  human_takeover          boolean not null default false,
  last_message_at         timestamptz,
  last_message_direction  varchar(20),
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now(),

  constraint ck_conversations_channel check (channel in (
    'whatsapp','instagram','website','facebook','sms','email'
  )),
  constraint ck_conversations_status check (status in ('active','closed','archived'))
);

create index if not exists ix_conversations_clinic_id on crm.conversations(clinic_id);
create index if not exists ix_conversations_lead_id on crm.conversations(lead_id);

drop trigger if exists trg_conversations_updated_at on crm.conversations;
create trigger trg_conversations_updated_at
  before update on crm.conversations
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 5. messages
-- ---------------------------------------------------------------------
create table if not exists crm.messages (
  id                   uuid primary key default gen_random_uuid(),
  clinic_id            uuid not null references core.clinics(id) on delete cascade,
  conversation_id      uuid not null references crm.conversations(id) on delete cascade,
  lead_id              uuid not null references crm.leads(id) on delete cascade,

  direction            varchar(20) not null,
  sender_type          varchar(20) not null,
  channel              varchar(50) not null,
  message_type         varchar(30) not null default 'text',
  content              text,

  external_message_id  varchar(200),
  delivery_status      varchar(30),
  is_ai_generated      boolean not null default false,

  sent_at              timestamptz,
  received_at          timestamptz,
  created_at           timestamptz not null default now(),

  constraint ck_messages_direction check (direction in ('inbound','outbound')),
  constraint ck_messages_sender_type check (sender_type in ('lead','ai','staff','system'))
);

create index if not exists ix_messages_clinic_id on crm.messages(clinic_id);
create index if not exists ix_messages_conversation_id on crm.messages(conversation_id);
create index if not exists ix_messages_lead_id on crm.messages(lead_id);
create index if not exists ix_messages_created_at on crm.messages(created_at);

-- ---------------------------------------------------------------------
-- 6. appointments
-- ---------------------------------------------------------------------
create table if not exists scheduling.appointments (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,
  lead_id               uuid not null references crm.leads(id) on delete cascade,
  procedure_id          uuid references core.procedures(id) on delete set null,

  appointment_type      varchar(50) not null default 'consultation',
  status                varchar(50) not null default 'booked',
  scheduled_start       timestamptz not null,
  scheduled_end         timestamptz,
  location_type         varchar(30),
  location_name         varchar(200),
  external_calendar_id  varchar(200),
  external_event_id     varchar(200),
  assigned_staff_id     uuid,
  notes                 text,

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_appointments_status check (status in (
    'booked','confirmed','attended','no_show','canceled','rescheduled'
  ))
);

create index if not exists ix_appointments_clinic_id on scheduling.appointments(clinic_id);
create index if not exists ix_appointments_lead_id on scheduling.appointments(lead_id);
create index if not exists ix_appointments_scheduled_start on scheduling.appointments(scheduled_start);
create index if not exists ix_appointments_status on scheduling.appointments(status);

drop trigger if exists trg_appointments_updated_at on scheduling.appointments;
create trigger trg_appointments_updated_at
  before update on scheduling.appointments
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 7. procedure_bookings  (a lead actually booking/undergoing a procedure)
-- ---------------------------------------------------------------------
create table if not exists scheduling.procedure_bookings (
  id                uuid primary key default gen_random_uuid(),
  clinic_id         uuid not null references core.clinics(id) on delete cascade,
  lead_id           uuid not null references crm.leads(id) on delete cascade,
  procedure_id      uuid not null references core.procedures(id) on delete restrict,
  appointment_id    uuid references scheduling.appointments(id) on delete set null,

  status            varchar(50) not null default 'considering',
  quoted_amount     numeric(12,2),
  deposit_amount    numeric(12,2),
  final_amount      numeric(12,2),
  currency_code     varchar(3),
  procedure_date    timestamptz,
  notes             text,

  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),

  constraint ck_procedure_bookings_status check (status in (
    'considering','quoted','deposit_paid','booked','completed','canceled','lost'
  ))
);

create index if not exists ix_procedure_bookings_clinic_id on scheduling.procedure_bookings(clinic_id);
create index if not exists ix_procedure_bookings_lead_id on scheduling.procedure_bookings(lead_id);
create index if not exists ix_procedure_bookings_status on scheduling.procedure_bookings(status);

drop trigger if exists trg_procedure_bookings_updated_at on scheduling.procedure_bookings;
create trigger trg_procedure_bookings_updated_at
  before update on scheduling.procedure_bookings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 8. events  (audit / analytics event log)
-- ---------------------------------------------------------------------
create table if not exists activity.events (
  id               uuid primary key default gen_random_uuid(),
  clinic_id        uuid not null references core.clinics(id) on delete cascade,
  lead_id          uuid references crm.leads(id) on delete set null,
  conversation_id  uuid references crm.conversations(id) on delete set null,
  appointment_id   uuid references scheduling.appointments(id) on delete set null,
  event_type       varchar(100) not null,
  source            varchar(50),
  metadata         jsonb not null default '{}'::jsonb,
  created_at       timestamptz not null default now()
);

create index if not exists ix_events_clinic_id on activity.events(clinic_id);
create index if not exists ix_events_event_type on activity.events(event_type);
create index if not exists ix_events_created_at on activity.events(created_at);

-- ---------------------------------------------------------------------
-- 9. channel_integrations  (per-clinic WhatsApp / Instagram / Facebook connection config —
--    Settings → Channels & Integrations in the dashboard)
-- ---------------------------------------------------------------------
create table if not exists channels.channel_integrations (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references core.clinics(id) on delete cascade,

  channel                varchar(30) not null,
  status                 varchar(20) not null default 'disconnected',

  display_name           varchar(200),  -- e.g. connected phone number or Page name, for display only

  -- WhatsApp Business API (Cloud API)
  phone_number_id        varchar(200),
  whatsapp_business_id   varchar(200),

  -- Facebook / Instagram (Meta Graph API)
  page_id                varchar(200),
  instagram_business_id  varchar(200),

  -- shared
  -- MVP NOTE: stored as plain text, matching the rest of this MVP (no auth yet). Move to an
  -- encrypted column / secrets manager before connecting a real app or real patient data.
  access_token           text,
  webhook_verify_token   varchar(200),
  -- WhatsApp only: two-step-verification PIN used to register the phone number for Cloud API use
  -- (POST /{phone-number-id}/register) — auto-generated on Embedded Signup connect.
  pin                    varchar(10),

  last_verified_at       timestamptz,
  last_error             text,

  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now(),

  constraint ck_channel_integrations_channel check (channel in ('whatsapp','instagram','facebook')),
  constraint ck_channel_integrations_status check (status in ('disconnected','connected','error'))
);

create unique index if not exists ux_channel_integrations_clinic_channel
  on channels.channel_integrations(clinic_id, channel);

drop trigger if exists trg_channel_integrations_updated_at on channels.channel_integrations;
create trigger trg_channel_integrations_updated_at
  before update on channels.channel_integrations
  for each row execute function set_updated_at();

-- =====================================================================
-- End of MVP schema (9 tables). LATER tables (staff, campaigns,
-- followups, payments, clinic_settings, knowledge_documents,
-- automation_runs, audit_logs) are intentionally not created yet —
-- see the plan's "LATER TABLES" section.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Inbox: message origin + conversation mode
--
-- origin tells the Inbox UI (and the AI-collision guard) which system actually produced a
-- message — a "staff" sender could be replying from the dashboard or directly from the WhatsApp
-- Business phone app (coexistence echoes), and those must never be confused with a customer
-- message or trigger the AI. mode replaces the old ai_enabled/human_takeover pair as the single
-- source of truth for whether AI may reply — those two booleans are kept (not dropped) for
-- backward compatibility and are kept in sync by application code, not written independently.
-- ---------------------------------------------------------------------

alter table crm.messages add column if not exists origin varchar(50) not null default 'system';
alter table crm.conversations add column if not exists mode varchar(30) not null default 'ai';

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_messages_origin') then
    alter table crm.messages add constraint ck_messages_origin check (origin in (
      'whatsapp_customer','whatsapp_business_app','dashboard','ai','system'
    ));
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_conversations_mode') then
    alter table crm.conversations add constraint ck_conversations_mode check (mode in ('ai','human','approval'));
  end if;
end $$;

create index if not exists ix_messages_origin on crm.messages(origin);
create index if not exists ix_conversations_mode on crm.conversations(mode);

-- Idempotency for WhatsApp webhook/n8n retries: the same external_message_id must never produce
-- two rows. Scoped by clinic + channel since external IDs are only unique within one provider.
create unique index if not exists ux_messages_clinic_channel_external_message_id
  on crm.messages(clinic_id, channel, external_message_id)
  where external_message_id is not null;

-- Backfill mode from the pre-existing human_takeover flag for any rows that predate this column
-- (harmless to re-run: app code keeps human_takeover in sync with mode going forward, so this
-- just re-derives the same value on subsequent runs).
update crm.conversations set mode = case when human_takeover then 'human' else 'ai' end;

-- ---------------------------------------------------------------------
-- WhatsApp Templates + Campaigns + 24-hour customer service window
--
-- The service window: WhatsApp only allows free-form (non-template) outbound messages within 24h
-- of the customer's last inbound message. last_customer_message_at/service_window_expires_at are
-- updated ONLY when a genuine inbound whatsapp_customer message is ingested (never by staff, AI,
-- or campaign sends — see MessageService.IngestAsync) — see Conversation.IsServiceWindowOpen().
-- ---------------------------------------------------------------------

alter table crm.conversations add column if not exists last_customer_message_at timestamptz;
alter table crm.conversations add column if not exists service_window_expires_at timestamptz;
create index if not exists ix_conversations_service_window on crm.conversations(service_window_expires_at);

-- messages: allow the new 'campaign' origin, and link a message back to whichever
-- template/campaign produced it (all nullable — most messages have none of these).
alter table crm.messages drop constraint if exists ck_messages_origin;
alter table crm.messages add constraint ck_messages_origin check (origin in (
  'whatsapp_customer','whatsapp_business_app','dashboard','ai','system','campaign'
));

alter table crm.messages add column if not exists whatsapp_template_id uuid;
alter table crm.messages add column if not exists campaign_id uuid;
alter table crm.messages add column if not exists campaign_recipient_id uuid;

create index if not exists ix_messages_campaign_id on crm.messages(campaign_id) where campaign_id is not null;
create index if not exists ix_messages_whatsapp_template_id on crm.messages(whatsapp_template_id) where whatsapp_template_id is not null;

-- ---------------------------------------------------------------------
-- whatsapp_templates
-- ---------------------------------------------------------------------
create table if not exists channels.whatsapp_templates (
  id                 uuid primary key default gen_random_uuid(),
  clinic_id          uuid not null references core.clinics(id) on delete cascade,

  meta_template_id   varchar(200),
  name               varchar(200) not null,
  category           varchar(30) not null,
  language           varchar(10) not null default 'en_US',
  status             varchar(20) not null default 'draft',

  header_type        varchar(20),
  header_content     text,
  body               text not null,
  footer             text,

  buttons_json       jsonb,
  variables_json     jsonb,

  rejection_reason   text,

  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now(),

  constraint ck_whatsapp_templates_category check (category in ('marketing','utility','authentication')),
  constraint ck_whatsapp_templates_status check (status in ('draft','pending','approved','rejected','paused','disabled')),
  constraint ck_whatsapp_templates_header_type check (header_type is null or header_type in ('none','text','image','video','document'))
);

create index if not exists ix_whatsapp_templates_clinic_id on channels.whatsapp_templates(clinic_id);
-- Meta's own uniqueness model: one template name is one thing per language per WABA (clinic, here).
create unique index if not exists ux_whatsapp_templates_clinic_name_language
  on channels.whatsapp_templates(clinic_id, name, language);

drop trigger if exists trg_whatsapp_templates_updated_at on channels.whatsapp_templates;
create trigger trg_whatsapp_templates_updated_at
  before update on channels.whatsapp_templates
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaigns
-- ---------------------------------------------------------------------
create table if not exists marketing.campaigns (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,
  name                  varchar(200) not null,
  whatsapp_template_id  uuid not null references channels.whatsapp_templates(id) on delete restrict,
  status                varchar(20) not null default 'draft',
  scheduled_at          timestamptz,
  started_at            timestamptz,
  completed_at          timestamptz,
  created_by_user_id    uuid,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_campaigns_status check (status in ('draft','scheduled','running','completed','cancelled','failed'))
);

create index if not exists ix_campaigns_clinic_id on marketing.campaigns(clinic_id);
create index if not exists ix_campaigns_status on marketing.campaigns(status);
create index if not exists ix_campaigns_scheduled_at on marketing.campaigns(scheduled_at) where scheduled_at is not null;
create index if not exists ix_campaigns_whatsapp_template_id on marketing.campaigns(whatsapp_template_id);

drop trigger if exists trg_campaigns_updated_at on marketing.campaigns;
create trigger trg_campaigns_updated_at
  before update on marketing.campaigns
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaign_recipients — one row per target lead. Every actual send still produces a normal row in
-- messages (message_id here points to it) — this table is reporting/lifecycle state, not a second
-- message store.
-- ---------------------------------------------------------------------
create table if not exists marketing.campaign_recipients (
  id                uuid primary key default gen_random_uuid(),
  clinic_id         uuid not null references core.clinics(id) on delete cascade,
  campaign_id       uuid not null references marketing.campaigns(id) on delete cascade,
  lead_id           uuid not null references crm.leads(id) on delete cascade,
  conversation_id   uuid references crm.conversations(id) on delete set null,
  message_id        uuid references crm.messages(id) on delete set null,

  phone_number      varchar(50) not null,
  variables_json    jsonb,
  status            varchar(20) not null default 'pending',
  error_message     text,

  queued_at         timestamptz,
  sent_at           timestamptz,
  delivered_at      timestamptz,
  read_at           timestamptz,
  failed_at         timestamptz,

  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),

  constraint ck_campaign_recipients_status check (status in ('pending','queued','sent','delivered','read','failed'))
);

create index if not exists ix_campaign_recipients_clinic_id on marketing.campaign_recipients(clinic_id);
create index if not exists ix_campaign_recipients_campaign_id on marketing.campaign_recipients(campaign_id);
create index if not exists ix_campaign_recipients_lead_id on marketing.campaign_recipients(lead_id);
create index if not exists ix_campaign_recipients_status on marketing.campaign_recipients(status);
-- Prevent the same lead being inserted into the same campaign twice.
create unique index if not exists ux_campaign_recipients_campaign_lead
  on marketing.campaign_recipients(campaign_id, lead_id);

drop trigger if exists trg_campaign_recipients_updated_at on marketing.campaign_recipients;
create trigger trg_campaign_recipients_updated_at
  before update on marketing.campaign_recipients
  for each row execute function set_updated_at();

-- Deferred FKs on messages — added last, now that the tables they reference exist.
do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_whatsapp_template_id') then
    alter table crm.messages add constraint fk_messages_whatsapp_template_id
      foreign key (whatsapp_template_id) references channels.whatsapp_templates(id) on delete set null;
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_campaign_id') then
    alter table crm.messages add constraint fk_messages_campaign_id
      foreign key (campaign_id) references marketing.campaigns(id) on delete set null;
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_campaign_recipient_id') then
    alter table crm.messages add constraint fk_messages_campaign_recipient_id
      foreign key (campaign_recipient_id) references marketing.campaign_recipients(id) on delete set null;
  end if;
end $$;

-- ---------------------------------------------------------------------
-- AI agent tool surface (Controllers/AiController.cs) — clinics gains the free-text fields
-- get_clinic_info reads. No new tables: appointment reschedule reuses scheduled_start/end,
-- and cancel/handoff reuse existing status/mode columns.
-- ---------------------------------------------------------------------
alter table core.clinics add column if not exists address text;
alter table core.clinics add column if not exists operating_hours text;
alter table core.clinics add column if not exists consultation_info text;

-- WhatsApp phone number registration (Cloud API requires this after Embedded Signup — see
-- ChannelIntegrationService.ConnectWhatsAppAsync / MetaGraphClient.RegisterPhoneNumberAsync).
alter table channels.channel_integrations add column if not exists pin varchar(10);

-- =====================================================================
-- WhatsApp webhook event routing: Inbox delivery states, Templates, Health
-- (n8n forwards normalized Meta webhook events here — see Controllers/AiController.cs's sibling
-- Controllers/WhatsAppIntegrationEventsController.cs). PostgreSQL stays authoritative; SignalR only
-- notifies. channel_integrations plays the role of "whatsapp_connections" (already one row per
-- clinic per channel) rather than a separate table duplicating the same WABA/phone_number_id.
-- =====================================================================

-- ---------------------------------------------------------------------
-- messages: per-status timestamps + failure detail + raw metadata for non-text content
-- (image/document/audio/video/location/contact/interactive). DeliveryStatus keeps the *latest*
-- state; these record *when* each transition happened without a second Message row per status.
-- ---------------------------------------------------------------------
alter table crm.messages add column if not exists delivered_at timestamptz;
alter table crm.messages add column if not exists read_at timestamptz;
alter table crm.messages add column if not exists failed_at timestamptz;
alter table crm.messages add column if not exists deleted_at timestamptz;
alter table crm.messages add column if not exists failure_code varchar(100);
alter table crm.messages add column if not exists failure_reason text;
alter table crm.messages add column if not exists metadata_json jsonb;

-- ---------------------------------------------------------------------
-- channel_integrations: WhatsApp account/phone health, populated from Meta's account_update,
-- account_review_update, phone_number_quality_update, phone_number_name_update webhooks.
-- Deliberately free text (no CHECK constraints) — see whatsapp_templates below for why.
-- ---------------------------------------------------------------------
alter table channels.channel_integrations add column if not exists meta_business_id varchar(200);
alter table channels.channel_integrations add column if not exists verified_name varchar(200);
alter table channels.channel_integrations add column if not exists account_status varchar(50);
alter table channels.channel_integrations add column if not exists account_review_status varchar(50);
alter table channels.channel_integrations add column if not exists phone_quality_rating varchar(50);
alter table channels.channel_integrations add column if not exists phone_status varchar(50);
alter table channels.channel_integrations add column if not exists name_status varchar(50);
alter table channels.channel_integrations add column if not exists is_healthy boolean not null default true;
alter table channels.channel_integrations add column if not exists health_level varchar(30);
alter table channels.channel_integrations add column if not exists last_problem_code varchar(100);
alter table channels.channel_integrations add column if not exists last_problem_message text;
alter table channels.channel_integrations add column if not exists last_webhook_at timestamptz;
alter table channels.channel_integrations add column if not exists last_health_event_at timestamptz;

-- Superseded by the unique filtered index below — a Meta phone_number_id must never map to more
-- than one clinic (webhook routing: phone_number_id -> channel_integration -> clinic_id depends on
-- this being unambiguous).
drop index if exists ix_channel_integrations_phone_number_id;
create unique index if not exists ux_channel_integrations_phone_number_id
  on channels.channel_integrations(phone_number_id) where phone_number_id is not null;

-- WABA id stays non-unique on purpose: one WABA can contain multiple phone numbers, so multiple
-- rows legitimately sharing a whatsapp_business_id is expected, not a conflict.
create index if not exists ix_channel_integrations_whatsapp_business_id on channels.channel_integrations(whatsapp_business_id);

-- ---------------------------------------------------------------------
-- whatsapp_templates: link back to the connection that owns it, Meta's quality/category-change
-- fields, and the raw "components" array as last reported by a webhook (kept alongside — not
-- instead of — the split header/body/footer/buttons fields the create-template UI uses).
--
-- Status/category CHECK constraints are DROPPED here on purpose: Meta's own vocabularies for
-- these aren't guaranteed stable (e.g. "flagged", "in_appeal" are real Meta template statuses
-- beyond our original 6), and the requirement is to tolerate unknown/new values rather than reject
-- a webhook because Meta introduced a status we didn't anticipate.
-- ---------------------------------------------------------------------
alter table channels.whatsapp_templates add column if not exists channel_integration_id uuid;
alter table channels.whatsapp_templates add column if not exists quality_rating varchar(30);
alter table channels.whatsapp_templates add column if not exists previous_category varchar(30);
alter table channels.whatsapp_templates add column if not exists current_category varchar(30);
alter table channels.whatsapp_templates add column if not exists components jsonb;
alter table channels.whatsapp_templates add column if not exists last_meta_event_at timestamptz;

alter table channels.whatsapp_templates drop constraint if exists ck_whatsapp_templates_status;
alter table channels.whatsapp_templates drop constraint if exists ck_whatsapp_templates_category;

create index if not exists ix_whatsapp_templates_channel_integration_id on channels.whatsapp_templates(channel_integration_id);
-- Meta template ids aren't globally unique across clinics by themselves, but scoped by clinic they
-- are — this is the upsert key ApplyMetaEventAsync uses (not unique, since Meta sends null template
-- ids for pure category-limit updates that still need to land somewhere findable by name+language).
create index if not exists ix_whatsapp_templates_clinic_meta_template_id
  on channels.whatsapp_templates(clinic_id, meta_template_id) where meta_template_id is not null;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_whatsapp_templates_channel_integration_id') then
    alter table channels.whatsapp_templates add constraint fk_whatsapp_templates_channel_integration_id
      foreign key (channel_integration_id) references channels.channel_integrations(id) on delete set null;
  end if;
end $$;

-- ---------------------------------------------------------------------
-- whatsapp_health_events: append-only history behind channel_integrations' current health state.
-- event_type/severity/status are free text for the same "tolerate unknown Meta values" reason as
-- whatsapp_templates — raw_metadata keeps whatever n8n forwarded, verbatim, alongside the
-- normalized fields.
-- ---------------------------------------------------------------------
create table if not exists channels.whatsapp_health_events (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null references core.clinics(id) on delete cascade,
  channel_integration_id   uuid not null references channels.channel_integrations(id) on delete cascade,

  event_type               varchar(100) not null,
  severity                 varchar(20),

  status                   varchar(50),
  code                     varchar(100),
  message                  text,

  raw_metadata             jsonb,

  occurred_at              timestamptz not null,
  created_at               timestamptz not null default now()
);

create index if not exists ix_whatsapp_health_events_clinic_id on channels.whatsapp_health_events(clinic_id);
create index if not exists ix_whatsapp_health_events_channel_integration_id on channels.whatsapp_health_events(channel_integration_id);
create index if not exists ix_whatsapp_health_events_occurred_at on channels.whatsapp_health_events(occurred_at);
-- Practical idempotency for webhook retries: Meta doesn't hand these a stable event id, so dedupe
-- on (connection, event type, occurred_at) — see WhatsAppHealthService.ApplyHealthEventAsync.
create unique index if not exists ux_whatsapp_health_events_connection_type_occurred
  on channels.whatsapp_health_events(channel_integration_id, event_type, occurred_at);

-- =====================================================================
-- ASP.NET Core Identity (authentication only) + clinic_users
--
-- MVP scope decision: ignore roles/permissions completely — no owner/manager/receptionist/surgeon,
-- no permissions matrix. Identity is used ONLY to authenticate a user; clinic_users is the ONLY
-- thing that ties an authenticated user to a clinic (assumed one active membership per user — see
-- Services/ICurrentClinicContext.cs, the sole reader of this table for every dashboard page/API).
--
-- These tables mirror ASP.NET Core Identity's standard IdentityUserContext<IdentityUser> shape
-- (deliberately the role-free variant — no AspNetRoles/AspNetUserRoles/AspNetRoleClaims), renamed
-- to this schema's snake_case convention via Fluent API in ApplicationDbContext.OnModelCreating
-- rather than keeping EF's default "AspNetUsers" etc. IdentityUser.Id is a string (Identity's
-- default key type — populated as a GUID string by the app, not DB-generated), which is why
-- clinic_users.user_id is text, not uuid.
-- =====================================================================

create table if not exists identity.identity_users (
  id                          text primary key,
  user_name                   varchar(256),
  normalized_user_name        varchar(256),
  email                       varchar(256),
  normalized_email            varchar(256),
  email_confirmed             boolean not null default false,
  password_hash               text,
  security_stamp              text,
  concurrency_stamp           text,
  phone_number                text,
  phone_number_confirmed      boolean not null default false,
  two_factor_enabled          boolean not null default false,
  lockout_end                 timestamptz,
  lockout_enabled             boolean not null default true,
  access_failed_count         integer not null default 0
);

create unique index if not exists "UserNameIndex" on identity.identity_users(normalized_user_name);
create index if not exists "EmailIndex" on identity.identity_users(normalized_email);

create table if not exists identity.identity_user_claims (
  id            integer generated always as identity primary key,
  user_id       text not null references identity.identity_users(id) on delete cascade,
  claim_type    text,
  claim_value   text
);

create index if not exists ix_identity_user_claims_user_id on identity.identity_user_claims(user_id);

create table if not exists identity.identity_user_logins (
  login_provider          varchar(128) not null,
  provider_key             varchar(128) not null,
  provider_display_name   text,
  user_id                  text not null references identity.identity_users(id) on delete cascade,

  primary key (login_provider, provider_key)
);

create index if not exists ix_identity_user_logins_user_id on identity.identity_user_logins(user_id);

create table if not exists identity.identity_user_tokens (
  user_id          text not null references identity.identity_users(id) on delete cascade,
  login_provider   varchar(128) not null,
  name             varchar(128) not null,
  value            text,

  primary key (user_id, login_provider, name)
);

-- ---------------------------------------------------------------------
-- clinic_users
-- ---------------------------------------------------------------------
create table if not exists identity.clinic_users (
  id            uuid primary key default gen_random_uuid(),
  clinic_id     uuid not null references core.clinics(id) on delete cascade,
  user_id       text not null references identity.identity_users(id) on delete cascade,
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create unique index if not exists ux_clinic_users_clinic_user on identity.clinic_users(clinic_id, user_id);
-- Superseded by the unique filtered index below — enforces "one active clinic membership per
-- user", the MVP assumption CurrentClinicContext's first-active-row lookup relies on.
-- Historical/inactive rows for the same user are unrestricted; only one active row per user can
-- ever exist.
drop index if exists ix_clinic_users_user_id;
create unique index if not exists ux_clinic_users_user_active on identity.clinic_users(user_id) where is_active = true;

drop trigger if exists trg_clinic_users_updated_at on identity.clinic_users;
create trigger trg_clinic_users_updated_at
  before update on identity.clinic_users
  for each row execute function set_updated_at();

-- =====================================================================
-- Campaigns + Old Lead Reactivation — data model
-- campaigns/campaign_recipients already existed (WhatsApp Templates & Campaigns, above). This adds
-- what "old lead reactivation" needs: how a campaign's audience was chosen (audience_type /
-- audience_filters, so "all eligible" / "reactivation, never booked" / "custom filter" are
-- first-class instead of always requiring an explicit lead list) and reply/booking attribution on
-- campaign_recipients. See Services/ICampaignAudienceService.cs for how eligibility is computed —
-- deliberately NOT a stored lead.is_old_lead / incomplete_booking flag; derived live from
-- leads/conversations/appointments every time a campaign is created.
-- =====================================================================

alter table marketing.campaigns add column if not exists campaign_type varchar(30) not null default 'custom';
alter table marketing.campaigns add column if not exists channel varchar(30) not null default 'whatsapp';
alter table marketing.campaigns add column if not exists audience_type varchar(30) not null default 'custom';
alter table marketing.campaigns add column if not exists audience_filters jsonb;

-- Nullable at the DB level for forward-compatibility with a future non-template campaign type;
-- every campaign actually sendable today still requires one — enforced in
-- CampaignService.CreateAsync, not the database.
alter table marketing.campaigns alter column whatsapp_template_id drop not null;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_campaigns_channel') then
    alter table marketing.campaigns add constraint ck_campaigns_channel check (channel in ('whatsapp','instagram','messenger'));
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_campaigns_audience_type') then
    alter table marketing.campaigns add constraint ck_campaigns_audience_type check (audience_type in ('all_eligible','reactivation_no_consultation','custom'));
  end if;
end $$;

-- campaign_type is deliberately NOT constrained (like leads.source) — new campaign types can be
-- added later without a migration.

-- 'paused' added to the campaign status lifecycle.
alter table marketing.campaigns drop constraint if exists ck_campaigns_status;
alter table marketing.campaigns add constraint ck_campaigns_status
  check (status in ('draft','scheduled','running','paused','completed','cancelled','failed'));

create index if not exists ix_campaigns_campaign_type on marketing.campaigns(campaign_type);

-- ---------------------------------------------------------------------
-- campaign_recipients: reply/booking attribution + skip tracking
-- ---------------------------------------------------------------------
alter table marketing.campaign_recipients add column if not exists external_message_id varchar(200);
alter table marketing.campaign_recipients add column if not exists appointment_id uuid;
alter table marketing.campaign_recipients add column if not exists skip_reason varchar(50);
alter table marketing.campaign_recipients add column if not exists failure_code varchar(100);
alter table marketing.campaign_recipients add column if not exists replied_at timestamptz;
alter table marketing.campaign_recipients add column if not exists booked_at timestamptz;

-- error_message -> failure_reason, matching the messages table's failure_code/failure_reason naming.
-- (Guarded so re-running this file is a no-op once renamed.)
do $$ begin
  if exists (select 1 from information_schema.columns
             where table_schema = 'marketing' and table_name = 'campaign_recipients' and column_name = 'error_message') then
    alter table marketing.campaign_recipients rename column error_message to failure_reason;
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_campaign_recipients_appointment_id') then
    alter table marketing.campaign_recipients add constraint fk_campaign_recipients_appointment_id
      foreign key (appointment_id) references scheduling.appointments(id) on delete set null;
  end if;
end $$;

alter table marketing.campaign_recipients drop constraint if exists ck_campaign_recipients_status;
alter table marketing.campaign_recipients add constraint ck_campaign_recipients_status
  check (status in ('pending','queued','sent','delivered','read','replied','booked','failed','skipped'));

create index if not exists ix_campaign_recipients_external_message_id
  on marketing.campaign_recipients(external_message_id) where external_message_id is not null;
create index if not exists ix_campaign_recipients_appointment_id
  on marketing.campaign_recipients(appointment_id) where appointment_id is not null;
-- ux_campaign_recipients_campaign_lead (UNIQUE(campaign_id, lead_id)) already exists from the
-- original campaign_recipients table above — no change needed for the "no duplicate lead in the
-- same campaign" requirement.


-- =====================================================================
-- Clinic Knowledge Base — knowledge_documents (what staff type in) + knowledge_chunks (the
-- embedded slices the AI agent searches semantically). Everything is scoped by clinic_id; every
-- search filters on it. See Services/IKnowledgeService.cs and IKnowledgeSearchService.cs.
-- =====================================================================
create extension if not exists vector;

create table if not exists knowledge.knowledge_documents (
  id          uuid primary key default gen_random_uuid(),
  clinic_id   uuid not null references core.clinics(id) on delete cascade,
  title       varchar(200) not null,
  -- Extensible string (general, faq, policy, doctor, procedure, pricing, payment, consultation,
  -- preparation, recovery, ...) — deliberately no CHECK, like campaigns.campaign_type.
  category    varchar(50) not null default 'general',
  content     text not null,
  is_active   boolean not null default true,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create index if not exists ix_knowledge_documents_clinic_id on knowledge.knowledge_documents(clinic_id);
create index if not exists ix_knowledge_documents_category on knowledge.knowledge_documents(category);
create index if not exists ix_knowledge_documents_is_active on knowledge.knowledge_documents(is_active);

drop trigger if exists trg_knowledge_documents_updated_at on knowledge.knowledge_documents;
create trigger trg_knowledge_documents_updated_at
  before update on knowledge.knowledge_documents
  for each row execute function set_updated_at();

-- vector(1536) matches Embeddings:Dimensions (default: OpenAI text-embedding-3-small at 1536).
-- If you switch to a model with a different dimension, this column must be changed to match.
create table if not exists knowledge.knowledge_chunks (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references core.clinics(id) on delete cascade,
  knowledge_document_id  uuid not null references knowledge.knowledge_documents(id) on delete cascade,
  chunk_index            integer not null,
  content                text not null,
  embedding              vector(1536) not null,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_knowledge_chunks_clinic_id on knowledge.knowledge_chunks(clinic_id);
create index if not exists ix_knowledge_chunks_knowledge_document_id on knowledge.knowledge_chunks(knowledge_document_id);
-- Deliberately no ANN (hnsw/ivfflat) index yet: every search is already narrowed to one clinic by
-- ix_knowledge_chunks_clinic_id first, and a clinic's Knowledge Base is hundreds of chunks, not
-- millions — an exact scan of that slice is fast and has perfect recall (an ANN index would apply the
-- clinic filter *after* its approximate scan and can drop results). Add
--   create index ... using hnsw (embedding vector_cosine_ops);
-- only if a single clinic's chunk count grows into the tens of thousands.

drop trigger if exists trg_knowledge_chunks_updated_at on knowledge.knowledge_chunks;
create trigger trg_knowledge_chunks_updated_at
  before update on knowledge.knowledge_chunks
  for each row execute function set_updated_at();


-- =====================================================================
-- Knowledge Base search settings — one row per clinic (unique clinic_id), created lazily by
-- IKnowledgeSettingsService with the system defaults. Staff edit only chunk size/overlap, top_k and
-- minimum_similarity; embedding_model, vector_dimension, similarity_method and vector_index_type are
-- persisted for transparency but read-only in the UI (changing them means re-embedding and/or a
-- migration of knowledge_chunks.embedding). Secrets such as the embeddings API key are NEVER stored
-- here — they stay in configuration.
-- =====================================================================
create table if not exists knowledge.knowledge_search_settings (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,

  -- read-only in the UI
  embedding_model       varchar(100) not null,
  vector_dimension      integer not null,
  similarity_method     varchar(30) not null default 'cosine',
  vector_index_type     varchar(30) not null default 'none',

  -- editable tuning
  chunk_size_tokens     integer not null,
  chunk_overlap_tokens  integer not null,
  top_k                 integer not null,
  minimum_similarity    double precision not null,

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_knowledge_search_settings_chunk_size check (chunk_size_tokens between 50 and 1000),
  constraint ck_knowledge_search_settings_chunk_overlap check (chunk_overlap_tokens >= 0 and chunk_overlap_tokens < chunk_size_tokens),
  constraint ck_knowledge_search_settings_top_k check (top_k between 1 and 10),
  constraint ck_knowledge_search_settings_min_similarity check (minimum_similarity between 0 and 1)
);

create unique index if not exists ux_knowledge_search_settings_clinic_id on knowledge.knowledge_search_settings(clinic_id);

drop trigger if exists trg_knowledge_search_settings_updated_at on knowledge.knowledge_search_settings;
create trigger trg_knowledge_search_settings_updated_at
  before update on knowledge.knowledge_search_settings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- Telegram (direct Bot API channel)
--
-- Reuses channel_integrations (one row per clinic+channel, unchanged):
--   access_token         = the BotFather token (never returned to the browser, never logged)
--   webhook_verify_token = the random secret_token registered with setWebhook; Telegram echoes it back
--                          in the X-Telegram-Bot-Api-Secret-Token header on every delivery
--   display_name         = "@bot_username" (display only)
--   channel_integrations.id = the connectionId in /api/integrations/telegram/webhook/{connectionId}
-- Only the identifiers Telegram itself hands back need new columns.
-- ---------------------------------------------------------------------
alter table channels.channel_integrations add column if not exists telegram_bot_id varchar(50);
alter table channels.channel_integrations add column if not exists telegram_bot_username varchar(100);
-- 'active' | 'pending' | 'error' | 'not_registered' — generic on purpose so other webhook-registered
-- channels could reuse it; WhatsApp/Facebook rows leave it null.
alter table channels.channel_integrations add column if not exists webhook_status varchar(30);
alter table channels.channel_integrations add column if not exists webhook_registered_at timestamptz;

alter table channels.channel_integrations drop constraint if exists ck_channel_integrations_channel;
alter table channels.channel_integrations add constraint ck_channel_integrations_channel
  check (channel in ('whatsapp','instagram','facebook','telegram'));

-- One bot can have only one webhook, so one bot may be connected to only one clinic at a time
-- (a disconnected row keeps its bot id for display but no longer blocks another clinic).
create unique index if not exists ux_channel_integrations_telegram_bot_id
  on channels.channel_integrations(telegram_bot_id)
  where telegram_bot_id is not null and status = 'connected';

alter table crm.conversations drop constraint if exists ck_conversations_channel;
alter table crm.conversations add constraint ck_conversations_channel
  check (channel in ('whatsapp','instagram','website','facebook','sms','email','telegram'));

alter table crm.messages drop constraint if exists ck_messages_origin;
alter table crm.messages add constraint ck_messages_origin check (origin in (
  'whatsapp_customer','whatsapp_business_app','dashboard','ai','system','campaign','telegram_customer'
));

-- A Telegram chat maps to exactly one conversation per clinic+channel (external_thread_id = chat.id).
-- Partial: WhatsApp/other conversations leave external_thread_id null.
create unique index if not exists ux_conversations_clinic_channel_thread
  on crm.conversations(clinic_id, channel, external_thread_id)
  where external_thread_id is not null;

-- ---------------------------------------------------------------------
-- Knowledge Base: uploaded documents (PDF / DOCX / TXT)
--
-- An uploaded file is turned into text server-side and stored in knowledge_documents.content — the
-- same column manual entries use — then chunked/embedded by the SAME pipeline. So re-saving or
-- re-indexing never needs the original file, and the binary is NOT stored (only these metadata
-- columns). source_type = 'manual' (typed in) | 'upload' (extracted from a file).
-- ---------------------------------------------------------------------
alter table knowledge.knowledge_documents add column if not exists source_type varchar(20) not null default 'manual';
alter table knowledge.knowledge_documents add column if not exists original_file_name varchar(255);
alter table knowledge.knowledge_documents add column if not exists mime_type varchar(100);
alter table knowledge.knowledge_documents add column if not exists file_size_bytes bigint;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_knowledge_documents_source_type') then
    alter table knowledge.knowledge_documents add constraint ck_knowledge_documents_source_type
      check (source_type in ('manual','upload'));
  end if;
end $$;

-- ---------------------------------------------------------------------
-- Inbox: unread tracking
--
-- last_read_at = when staff last opened the conversation (shared across the clinic's staff). Unread =
-- inbound messages newer than it. Existing conversations are backfilled as "read" exactly once (when
-- the column is first added); conversations created later start NULL = everything unread.
-- ---------------------------------------------------------------------
do $$ begin
  if not exists (select 1 from information_schema.columns where table_schema = 'crm' and table_name = 'conversations' and column_name = 'last_read_at') then
    alter table crm.conversations add column last_read_at timestamptz;
    update crm.conversations set last_read_at = now();
  end if;
end $$;

-- =====================================================================
-- Knowledge Base: WEBSITE SCRAPING (standalone ingestion subsystem)
--
--   knowledge_website_sources      one website configured as a KB source (per clinic)
--   knowledge_website_pages        every URL the crawler knows about, with its crawl state
--   knowledge_website_scrape_runs  one row per crawl (history / progress / debugging)
--
-- The crawler OWNS crawling, URL identity, fetching, text extraction, page state, runs and change
-- detection. The existing Knowledge Base owns content, chunking, embeddings and search: a successfully
-- extracted page becomes (or updates) a knowledge_documents row (source_type = 'website') and flows through
-- the SAME chunk -> embed -> knowledge_chunks pipeline as manual entries and uploads. NO vectors and no
-- extracted text live in the scraping tables (the text is knowledge_documents.content).
-- Everything is scoped by clinic_id; nothing is ever deduplicated across clinics.
-- =====================================================================

-- knowledge_documents: 'website' as a source type + the page URL it came from.
alter table knowledge.knowledge_documents add column if not exists source_url varchar(2000);
alter table knowledge.knowledge_documents drop constraint if exists ck_knowledge_documents_source_type;
alter table knowledge.knowledge_documents add constraint ck_knowledge_documents_source_type
  check (source_type in ('manual','upload','website'));

create table if not exists knowledge.knowledge_website_sources (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,

  start_url             varchar(2000) not null,
  normalized_start_url  varchar(2000) not null,
  host                  varchar(255)  not null,
  crawl_mode            varchar(20)   not null default 'crawl_site',
  category              varchar(50)   not null default 'general',
  is_active             boolean       not null default true,
  status                varchar(30)   not null default 'pending',
  last_scraped_at       timestamptz,
  -- Hashes of blocks of text found repeated across the site pages (menus/footers/banners). Kept so a
  -- partial recrawl strips the same boilerplate as the full one. Hashes only - no page text.
  boilerplate_block_hashes jsonb,
  created_at            timestamptz   not null default now(),
  updated_at            timestamptz   not null default now(),

  constraint ck_kws_crawl_mode check (crawl_mode in ('single_page','crawl_site')),
  constraint ck_kws_status check (status in ('pending','crawling','completed','completed_with_errors','failed'))
);
-- The same website cannot be added twice for one clinic; two clinics may each add it.
create unique index if not exists ux_kws_clinic_start_url on knowledge.knowledge_website_sources(clinic_id, normalized_start_url);
create index if not exists ix_kws_clinic_id on knowledge.knowledge_website_sources(clinic_id);

drop trigger if exists trg_kws_updated_at on knowledge.knowledge_website_sources;
create trigger trg_kws_updated_at before update on knowledge.knowledge_website_sources
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_website_pages (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,
  website_source_id     uuid not null references knowledge.knowledge_website_sources(id) on delete cascade,

  url                   varchar(2000) not null,
  normalized_url        varchar(2000) not null,
  canonical_url         varchar(2000),
  title                 varchar(500),

  http_status           integer,
  content_type          varchar(200),

  -- SHA-256 of the NORMALIZED EXTRACTED TEXT (never of raw HTML).
  content_hash          varchar(64),
  etag                  varchar(500),
  last_modified_header  varchar(100),

  status                varchar(20)  not null default 'discovered',
  failure_reason        text,
  depth                 integer      not null default 0,

  -- The KB document this page produced (null for skipped/duplicate/failed/removed pages).
  knowledge_document_id uuid references knowledge.knowledge_documents(id) on delete set null,
  -- For status 'duplicate': the page whose identical content/canonical was kept instead.
  duplicate_of_page_id  uuid references knowledge.knowledge_website_pages(id) on delete set null,

  -- Internal links found on the page (normalized, capped) - lets a 304 Not Modified page still be crawled through.
  links                 jsonb,
  -- Removal handling: consecutive full crawls in which this page was gone (404/410 or no longer linked).
  missing_count         integer      not null default 0,
  removed_at            timestamptz,

  first_discovered_at   timestamptz  not null default now(),
  last_seen_at          timestamptz,
  last_scraped_at       timestamptz,
  created_at            timestamptz  not null default now(),
  updated_at            timestamptz  not null default now(),

  constraint ck_kwp_status check (status in
    ('discovered','processing','indexed','unchanged','skipped','duplicate','failed','removed'))
);
-- One row per (source, normalized URL): the same page is never stored twice for a source.
create unique index if not exists ux_kwp_source_normalized_url on knowledge.knowledge_website_pages(website_source_id, normalized_url);
create index if not exists ix_kwp_clinic_id on knowledge.knowledge_website_pages(clinic_id);
create index if not exists ix_kwp_source_status on knowledge.knowledge_website_pages(website_source_id, status);
create index if not exists ix_kwp_document_id on knowledge.knowledge_website_pages(knowledge_document_id) where knowledge_document_id is not null;
-- Deliberately NOT unique: identical text may legitimately exist on several pages / clinics.
create index if not exists ix_kwp_source_content_hash on knowledge.knowledge_website_pages(website_source_id, content_hash) where content_hash is not null;

drop trigger if exists trg_kwp_updated_at on knowledge.knowledge_website_pages;
create trigger trg_kwp_updated_at before update on knowledge.knowledge_website_pages
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_website_scrape_runs (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,
  website_source_id     uuid not null references knowledge.knowledge_website_sources(id) on delete cascade,

  status                varchar(30) not null default 'pending',
  started_at            timestamptz,
  completed_at          timestamptz,

  pages_discovered      integer not null default 0,
  pages_processed       integer not null default 0,
  pages_indexed         integer not null default 0,   -- new + changed
  pages_new             integer not null default 0,
  pages_changed         integer not null default 0,
  pages_unchanged       integer not null default 0,
  pages_skipped         integer not null default 0,
  pages_duplicate       integer not null default 0,
  pages_failed          integer not null default 0,
  pages_removed         integer not null default 0,

  error_summary         text,
  created_at            timestamptz not null default now(),

  constraint ck_kwr_status check (status in ('pending','crawling','completed','completed_with_errors','failed'))
);
create index if not exists ix_kwr_source_created on knowledge.knowledge_website_scrape_runs(website_source_id, created_at desc);
create index if not exists ix_kwr_clinic_id on knowledge.knowledge_website_scrape_runs(clinic_id);

-- =====================================================================
-- Knowledge Base: RETRIEVAL BENCHMARK (standalone diagnostic feature)
--
--   knowledge_retrieval_benchmark_cases    one realistic patient question + the ONE chunk that answers it
--   knowledge_retrieval_benchmark_runs     one benchmark execution: aggregate metrics + the settings it ran with
--   knowledge_retrieval_benchmark_results  one row per case per run: where the expected chunk/document ranked
--
-- Tests ONLY retrieval (question -> embedding -> pgvector -> ranked chunks). Nothing here is read by the
-- production search path, and no benchmark state lives on knowledge_documents / knowledge_chunks.
--
-- expected_document_id / expected_chunk_id are deliberately NOT foreign keys to the production tables:
-- chunks are rebuilt (new ids) whenever a document is re-saved, and a benchmark table must never block or
-- slow production ingestion. A case whose chunk disappeared or changed is detected in code and marked stale
-- (is_stale / stale_reason), and excluded from scoring instead of being counted as a retrieval failure.
-- Everything is scoped by clinic_id.
-- =====================================================================
create table if not exists knowledge.knowledge_retrieval_benchmark_cases (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references core.clinics(id) on delete cascade,

  question              text not null,

  expected_document_id  uuid not null,
  expected_chunk_id     uuid not null,

  case_type             varchar(20) not null default 'generated',
  is_reviewed           boolean not null default false,
  reviewed_at           timestamptz,

  -- SHA-256 (hex) of the expected chunk's content when the case was created + a short readable excerpt, so a
  -- rebuilt/changed chunk can be detected and a stale case still shows what it was about.
  source_chunk_hash     varchar(64),
  source_chunk_preview  varchar(400),
  source_document_title varchar(200),

  is_stale              boolean not null default false,
  stale_reason          varchar(30),

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_kbc_case_type check (case_type in ('generated','manual')),
  constraint ck_kbc_question_length check (char_length(question) between 1 and 1000)
);
create index if not exists ix_kbc_clinic_id on knowledge.knowledge_retrieval_benchmark_cases(clinic_id);
create index if not exists ix_kbc_clinic_chunk on knowledge.knowledge_retrieval_benchmark_cases(clinic_id, expected_chunk_id);
-- The same question for the same chunk is never stored twice.
create unique index if not exists ux_kbc_clinic_chunk_question
  on knowledge.knowledge_retrieval_benchmark_cases(clinic_id, expected_chunk_id, lower(question));

drop trigger if exists trg_kbc_updated_at on knowledge.knowledge_retrieval_benchmark_cases;
create trigger trg_kbc_updated_at before update on knowledge.knowledge_retrieval_benchmark_cases
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_retrieval_benchmark_runs (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null references core.clinics(id) on delete cascade,

  case_scope               varchar(20) not null default 'all',
  status                   varchar(20) not null default 'pending',
  started_at               timestamptz,
  completed_at             timestamptz,

  total_cases              integer not null default 0,   -- cases selected by the scope
  processed_cases          integer not null default 0,   -- progress while running
  scored_cases             integer not null default 0,   -- evaluated (excludes stale + errored) - the metrics' denominator
  stale_cases              integer not null default 0,
  error_cases              integer not null default 0,

  -- STRICT chunk-level metrics (the exact expected chunk) and secondary document-level metrics. 0..1, null when
  -- nothing was scored.
  chunk_top1_accuracy      double precision,
  chunk_top3_accuracy      double precision,
  chunk_top5_accuracy      double precision,
  document_top1_accuracy   double precision,
  document_top3_accuracy   double precision,
  document_top5_accuracy   double precision,
  chunk_mrr                double precision,
  document_mrr             double precision,
  average_latency_ms       double precision,

  -- Snapshot of the retrieval settings the run used (knowledge_search_settings stays the source of truth) plus
  -- what the index actually contained, since chunk settings only apply to entries saved after a change.
  embedding_model          varchar(100),
  vector_dimension         integer,
  chunk_size_tokens        integer,
  chunk_overlap_tokens     integer,
  similarity_method        varchar(30),
  top_k                    integer,
  minimum_similarity       double precision,
  indexed_chunk_count      integer,
  avg_chunk_chars          double precision,

  error_summary            text,
  created_at               timestamptz not null default now(),

  constraint ck_kbr_case_scope check (case_scope in ('all','generated','reviewed')),
  constraint ck_kbr_status check (status in ('pending','running','completed','failed'))
);
create index if not exists ix_kbr_clinic_created on knowledge.knowledge_retrieval_benchmark_runs(clinic_id, created_at desc);

create table if not exists knowledge.knowledge_retrieval_benchmark_results (
  id                          uuid primary key default gen_random_uuid(),
  clinic_id                   uuid not null references core.clinics(id) on delete cascade,
  benchmark_run_id            uuid not null references knowledge.knowledge_retrieval_benchmark_runs(id) on delete cascade,
  -- set null (not cascade): deleting a case keeps the run's history intact via the snapshots below.
  benchmark_case_id           uuid references knowledge.knowledge_retrieval_benchmark_cases(id) on delete set null,

  question                    text not null,
  expected_document_id        uuid not null,
  expected_chunk_id           uuid not null,
  expected_document_title     varchar(200),
  expected_chunk_preview      varchar(400),

  expected_chunk_rank         integer,
  expected_document_best_rank integer,
  expected_chunk_score        double precision,
  -- The expected chunk was in the top-K by similarity but scored under the clinic's minimum similarity, so
  -- production search would not have returned it (a strict miss).
  expected_below_threshold    boolean not null default false,

  chunk_top1_pass             boolean not null default false,
  chunk_top3_pass             boolean not null default false,
  chunk_top5_pass             boolean not null default false,
  document_top1_pass          boolean not null default false,
  document_top3_pass          boolean not null default false,
  document_top5_pass          boolean not null default false,

  result_classification       varchar(30) not null,
  stale_reason                varchar(30),
  error_message               text,

  returned_count              integer not null default 0,
  -- Ordered top-K candidates as production search saw them (rank, ids, title, score, passed-threshold, preview).
  retrieved_json              jsonb,
  latency_ms                  integer,

  created_at                  timestamptz not null default now(),

  constraint ck_kbres_classification check (result_classification in
    ('EXACT_CHUNK_HIT','DOCUMENT_ONLY_HIT','MISS','STALE_CASE','ERROR'))
);
create index if not exists ix_kbres_run on knowledge.knowledge_retrieval_benchmark_results(benchmark_run_id);
create index if not exists ix_kbres_clinic_id on knowledge.knowledge_retrieval_benchmark_results(clinic_id);
create index if not exists ix_kbres_case_created on knowledge.knowledge_retrieval_benchmark_results(benchmark_case_id, created_at desc);

-- Retrieval Benchmark: which generation request created a generated case. The app generates a generation_id per
-- "Generate Test Cases" request, sends it to the n8n generator, requires it echoed back, and stores it on every case
-- that response produced (null for manual cases and for cases created before this column existed).
alter table knowledge.knowledge_retrieval_benchmark_cases add column if not exists generation_id uuid;
create index if not exists ix_kbc_clinic_generation on knowledge.knowledge_retrieval_benchmark_cases(clinic_id, generation_id) where generation_id is not null;

-- Retrieval Benchmark: ASYNC question generation. One row per "Generate Test Cases" request. `id` IS the generationId that is
-- sent to the n8n generator and that n8n's callback must carry (POST .../generations/{id}/questions). The row is created
-- (pending) BEFORE anything is sent, so even an instant callback finds it, and it remembers exactly which chunks were sent so
-- the reply can be validated against that set (and the clinic is taken from THIS row, never from the caller).
create table if not exists knowledge.knowledge_retrieval_benchmark_generations (
  id                    uuid primary key,
  clinic_id             uuid not null references core.clinics(id) on delete cascade,

  status                varchar(20) not null default 'pending',   -- pending | completed | failed

  chunks_sent           integer not null default 0,
  sent_chunks           jsonb,                                     -- [{documentId, chunkId, documentTitle}] (no chunk text)

  questions_returned    integer not null default 0,
  cases_created         integer not null default 0,
  rejected_count        integer not null default 0,
  rejected_json         jsonb,                                     -- first rejected items with reasons (capped)
  raw_response          text,                                      -- n8n's reply/callback body, capped

  error_message         text,
  completed_at          timestamptz,
  created_at            timestamptz not null default now(),

  constraint ck_kbg_status check (status in ('pending','completed','failed'))
);
create index if not exists ix_kbg_clinic_created on knowledge.knowledge_retrieval_benchmark_generations(clinic_id, created_at desc);

-- Results remember which generation their case came from, so scores can be reported per generation even after the case is
-- deleted (the case FK is ON DELETE SET NULL).
alter table knowledge.knowledge_retrieval_benchmark_results add column if not exists generation_id uuid;
create index if not exists ix_kbres_generation on knowledge.knowledge_retrieval_benchmark_results(clinic_id, generation_id) where generation_id is not null;

-- Backfill: generations created by the earlier synchronous version left only a tag on their cases.
insert into knowledge.knowledge_retrieval_benchmark_generations
  (id, clinic_id, status, chunks_sent, questions_returned, cases_created, completed_at, created_at)
select generation_id, clinic_id, 'completed', count(*), count(*), count(*), min(created_at), min(created_at)
from knowledge.knowledge_retrieval_benchmark_cases
where generation_id is not null
group by generation_id, clinic_id
on conflict (id) do nothing;

update knowledge.knowledge_retrieval_benchmark_results r
set generation_id = c.generation_id
from knowledge.knowledge_retrieval_benchmark_cases c
where r.benchmark_case_id = c.id and c.generation_id is not null and r.generation_id is null;

-- Retrieval Benchmark: a pending generation can be stopped by staff ("Stop generating"). Status 'cancelled' frees Generate at once
-- and makes the app refuse n8n's late callback for that generation.
alter table knowledge.knowledge_retrieval_benchmark_generations drop constraint if exists ck_kbg_status;
alter table knowledge.knowledge_retrieval_benchmark_generations add constraint ck_kbg_status
  check (status in ('pending','completed','failed','cancelled'));

-- Retrieval Benchmark: "Run Benchmark" for ONE specific generation (not just all/generated/reviewed). The run
-- remembers which generation it was scoped to, so the run history and the generation's own row can both show it.
alter table knowledge.knowledge_retrieval_benchmark_runs add column if not exists generation_id uuid;
alter table knowledge.knowledge_retrieval_benchmark_runs drop constraint if exists ck_kbr_case_scope;
alter table knowledge.knowledge_retrieval_benchmark_runs add constraint ck_kbr_case_scope
  check (case_scope in ('all','generated','reviewed','generation'));
create index if not exists ix_kbr_generation on knowledge.knowledge_retrieval_benchmark_runs(clinic_id, generation_id) where generation_id is not null;

-- =====================================================================
-- Structured clinic availability (the booking source of truth; clinics.operating_hours stays free text for the AI to quote)
-- ---------------------------------------------------------------------
-- Weekly schedule: one row per (clinic, weekday). day_of_week follows .NET DayOfWeek: 0 = Sunday ... 6 = Saturday.
-- Times are the clinic's LOCAL wall-clock (clinics.timezone). To allow several windows per day later, drop
-- ux_clinic_availability_rules_day - the slot service already iterates every rule row for a day.
create table if not exists scheduling.clinic_availability_rules (
  id           uuid primary key default gen_random_uuid(),
  clinic_id    uuid not null references core.clinics(id) on delete cascade,
  day_of_week  smallint not null check (day_of_week between 0 and 6),
  is_open      boolean not null default true,
  start_time   time not null,
  end_time     time not null,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  constraint ck_clinic_availability_rules_range check (end_time > start_time)
);
create unique index if not exists ux_clinic_availability_rules_day on scheduling.clinic_availability_rules(clinic_id, day_of_week);
drop trigger if exists trg_clinic_availability_rules_updated_at on scheduling.clinic_availability_rules;
create trigger trg_clinic_availability_rules_updated_at before update on scheduling.clinic_availability_rules
  for each row execute function set_updated_at();

-- Booking rules: one row per clinic. No row yet = defaults are shown in the UI (nothing is offered until a weekly rule is open).
create table if not exists scheduling.clinic_booking_settings (
  id                                    uuid primary key default gen_random_uuid(),
  clinic_id                             uuid not null unique references core.clinics(id) on delete cascade,
  default_consultation_duration_minutes integer not null default 30 check (default_consultation_duration_minutes > 0),
  buffer_minutes                        integer not null default 0 check (buffer_minutes >= 0),
  minimum_booking_notice_minutes        integer not null default 240 check (minimum_booking_notice_minutes >= 0),
  maximum_advance_booking_days          integer not null default 60 check (maximum_advance_booking_days > 0),
  created_at                            timestamptz not null default now(),
  updated_at                            timestamptz not null default now()
);
drop trigger if exists trg_clinic_booking_settings_updated_at on scheduling.clinic_booking_settings;
create trigger trg_clinic_booking_settings_updated_at before update on scheduling.clinic_booking_settings
  for each row execute function set_updated_at();

-- Date exceptions override the weekly schedule for that local date: closed all day, or a custom window (which may also
-- open a normally-closed day).
create table if not exists scheduling.clinic_availability_exceptions (
  id          uuid primary key default gen_random_uuid(),
  clinic_id   uuid not null references core.clinics(id) on delete cascade,
  date        date not null,
  is_closed   boolean not null default true,
  start_time  time,
  end_time    time,
  reason      varchar(200),
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  constraint ck_clinic_availability_exceptions_window
    check (is_closed or (start_time is not null and end_time is not null and end_time > start_time))
);
create unique index if not exists ux_clinic_availability_exceptions_date on scheduling.clinic_availability_exceptions(clinic_id, date);
drop trigger if exists trg_clinic_availability_exceptions_updated_at on scheduling.clinic_availability_exceptions;
create trigger trg_clinic_availability_exceptions_updated_at before update on scheduling.clinic_availability_exceptions
  for each row execute function set_updated_at();

-- =====================================================================
-- Notifications — a simple per-clinic notification center for staff (bell/list). Created ONLY from confirmed
-- backend events (a message actually sent, an appointment actually booked...), never from an AI intention alone.
-- ---------------------------------------------------------------------
create table if not exists activity.notifications (
  id                      uuid primary key default gen_random_uuid(),
  clinic_id               uuid not null references core.clinics(id) on delete cascade,
  type                    varchar(40) not null
    check (type in ('NEW_LEAD','HANDOFF','APPOINTMENT_BOOKED','APPOINTMENT_RESCHEDULED','APPOINTMENT_CANCELLED',
                     'CAMPAIGN_REPLY','OUTBOUND_MESSAGE_FAILED','INTEGRATION_UNHEALTHY')),
  title                   varchar(200) not null,
  message                 text,
  lead_id                 uuid references crm.leads(id) on delete set null,
  conversation_id         uuid references crm.conversations(id) on delete set null,
  appointment_id          uuid references scheduling.appointments(id) on delete set null,
  channel_integration_id  uuid references channels.channel_integrations(id) on delete set null,
  -- Precomputed relative URL (e.g. "/inbox?conversationId=..." or "/dashboard/appointments/{id}") so the
  -- bell UI never has to know which entity type maps to which page.
  link                    varchar(300),
  is_read                 boolean not null default false,
  read_at                 timestamptz,
  created_at              timestamptz not null default now()
);
create index if not exists ix_notifications_clinic_created on activity.notifications(clinic_id, created_at desc);
create index if not exists ix_notifications_clinic_unread on activity.notifications(clinic_id) where is_read = false;

-- =====================================================================
-- Calendar Integrations — one-way (SculptFlow -> external) appointment sync via a dedicated n8n workflow.
-- SculptFlow never talks to Google/Microsoft directly and never stores their OAuth tokens: n8n owns the
-- actual provider connection (see Services/ICalendarSyncNotifier.cs), SculptFlow only stores the opaque
-- reference n8n gives back plus what staff chose (which calendar, sync on/off).
-- ---------------------------------------------------------------------
create table if not exists scheduling.calendar_integrations (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null references core.clinics(id) on delete cascade,
  provider                 varchar(20) not null check (provider in ('google','outlook')),
  status                   varchar(20) not null default 'disconnected'
    check (status in ('disconnected','pending','connected','error')),

  -- Opaque handle n8n returns on a successful connect — n8n looks THIS up to find the stored OAuth
  -- tokens for this clinic+provider. Never a raw access/refresh token; SculptFlow cannot use it itself.
  external_connection_ref  varchar(200),
  account_display_name     varchar(200),  -- e.g. the connected Google/Outlook account's email, for display only

  selected_calendar_id     varchar(200),  -- external calendar id (opaque to SculptFlow)
  selected_calendar_name   varchar(200),  -- human-readable, from the cached list below

  sync_enabled             boolean not null default false,

  -- Health, same shape/intent as channel_integrations' WhatsApp health fields — "requires attention"
  -- when a PREVIOUSLY working connection starts failing syncs, not on every transient hiccup.
  is_healthy               boolean not null default true,
  last_problem_message     text,
  last_synced_at           timestamptz,

  created_at               timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);
create unique index if not exists ux_calendar_integrations_clinic_provider on scheduling.calendar_integrations(clinic_id, provider);
drop trigger if exists trg_calendar_integrations_updated_at on scheduling.calendar_integrations;
create trigger trg_calendar_integrations_updated_at before update on scheduling.calendar_integrations
  for each row execute function set_updated_at();

-- The calendars n8n reported for a connected account — cached so the picker doesn't need a live round
-- trip on every page load. Replaced wholesale on connect / "refresh calendars".
create table if not exists scheduling.calendar_integration_calendars (
  id                       uuid primary key default gen_random_uuid(),
  calendar_integration_id  uuid not null references scheduling.calendar_integrations(id) on delete cascade,
  external_calendar_id     varchar(200) not null,
  name                     varchar(200) not null,
  is_primary               boolean not null default false,
  created_at               timestamptz not null default now()
);
create index if not exists ix_calendar_integration_calendars_integration on scheduling.calendar_integration_calendars(calendar_integration_id);
create unique index if not exists ux_calendar_integration_calendars_ext
  on scheduling.calendar_integration_calendars(calendar_integration_id, external_calendar_id);

-- One row per (appointment, connected calendar) — the dedup/consistency mechanism: the SAME row is
-- reused across create -> reschedule -> cancel, so a reschedule updates the stored external_event_id's
-- event instead of ever creating a second one. last_request_id correlates an outstanding n8n call with
-- its callback.
create table if not exists scheduling.appointment_calendar_syncs (
  id                       uuid primary key default gen_random_uuid(),
  appointment_id           uuid not null references scheduling.appointments(id) on delete cascade,
  calendar_integration_id  uuid not null references scheduling.calendar_integrations(id) on delete cascade,
  external_event_id        varchar(200),
  status                   varchar(20) not null default 'pending'
    check (status in ('pending','synced','failed','canceled')),
  last_request_id          uuid,
  last_error               text,
  created_at               timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);
create unique index if not exists ux_appointment_calendar_syncs_appt_integration
  on scheduling.appointment_calendar_syncs(appointment_id, calendar_integration_id);
create index if not exists ix_appointment_calendar_syncs_integration on scheduling.appointment_calendar_syncs(calendar_integration_id);
drop trigger if exists trg_appointment_calendar_syncs_updated_at on scheduling.appointment_calendar_syncs;
create trigger trg_appointment_calendar_syncs_updated_at before update on scheduling.appointment_calendar_syncs
  for each row execute function set_updated_at();

-- appointment_calendar_syncs: the sync row didn't know which operation (create/update/cancel) its last request
-- was for, so a successful cancel callback couldn't be told apart from a successful create/update one.
alter table scheduling.appointment_calendar_syncs add column if not exists last_operation varchar(10);

-- calendar_integrations: connect/list-calendars/disconnect now happen directly against Google/Outlook from
-- SculptFlow (no longer via n8n) — the clinic's own OAuth tokens are stored here so SculptFlow can refresh them
-- and hand a fresh access token to n8n's sync webhook for the one thing n8n still does (the actual create/update/
-- cancel API call). MVP NOTE (same as channel_integrations.access_token): plain text — move to an encrypted
-- column/secrets manager before this handles real patient data at scale.
alter table scheduling.calendar_integrations add column if not exists access_token text;
alter table scheduling.calendar_integrations add column if not exists refresh_token text;
alter table scheduling.calendar_integrations add column if not exists token_expires_at timestamptz;

-- =====================================================================
-- TikTok Login Kit — account connection only (NOT a messaging channel in this phase, so it's its own table
-- rather than a row in channel_integrations — see Data/Entities/TikTokIntegration.cs). SculptFlow owns the
-- whole OAuth2 relationship directly: connect, callback, token refresh, disconnect — no n8n involvement.
-- ---------------------------------------------------------------------
create table if not exists channels.tiktok_integrations (
  id                        uuid primary key default gen_random_uuid(),
  clinic_id                 uuid not null references core.clinics(id) on delete cascade,
  status                    varchar(20) not null default 'disconnected'
    check (status in ('disconnected','pending','connected','error')),

  open_id                   varchar(200),  -- TikTok's stable per-app user id
  union_id                  varchar(200),  -- only present if this app is part of a token "union" group
  display_name              varchar(200),
  avatar_url                 varchar(500),

  access_token               text,
  refresh_token              text,
  token_expires_at           timestamptz,
  refresh_token_expires_at   timestamptz,  -- TikTok refresh tokens themselves expire (~1 year)

  -- Health, same shape/intent as calendar_integrations/channel_integrations — "requires attention" when a
  -- PREVIOUSLY working connection starts failing, not on every transient hiccup.
  is_healthy                 boolean not null default true,
  last_problem_message       text,

  created_at                  timestamptz not null default now(),
  updated_at                   timestamptz not null default now()
);
create unique index if not exists ux_tiktok_integrations_clinic on channels.tiktok_integrations(clinic_id);
drop trigger if exists trg_tiktok_integrations_updated_at on channels.tiktok_integrations;
create trigger trg_tiktok_integrations_updated_at before update on channels.tiktok_integrations
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------
-- WhatsApp provider (BSP) per connection — Infobip for the MVP, Meta Cloud API directly later.
-- Which provider WhatsApp uses is a global switch (WhatsApp:Provider config), not a column; these say
-- which provider a row was connected through. provider null = 'meta' (every row from before this).
-- Infobip rows: provider_sender_id = the business WhatsApp number (digits only), webhook_verify_token =
-- the per-connection webhook secret; the Infobip API key/base URL are SculptFlow-wide env vars, never here.
-- Infobip webhook URL: /api/integrations/whatsapp/connections/{channel_integrations.id}/events?token={webhook_verify_token} (provider-neutral on purpose)
-- Free text (no CHECK): new BSPs shouldn't need a schema change.
-- ---------------------------------------------------------------
alter table channels.channel_integrations add column if not exists provider varchar(30);
alter table channels.channel_integrations add column if not exists provider_sender_id varchar(100);

-- One Infobip sender can serve only one clinic at a time (SculptFlow's single Infobip account holds every
-- clinic's number). Partial on connected so a disconnected clinic doesn't block reassigning the number.
create unique index if not exists ux_channel_integrations_provider_sender
  on channels.channel_integrations(channel, provider, provider_sender_id)
  where provider_sender_id is not null and status = 'connected';

-- Which WhatsApp provider a template was submitted through (null = 'meta', every template from before this).
-- A WhatsApp approval only holds on the account it was reviewed on, so the app sends/syncs a template only while
-- its provider is the active one (WhatsApp:Provider). meta_template_id holds that provider's template id.
alter table channels.whatsapp_templates add column if not exists provider varchar(30);

-- =====================================================================
-- SUBSCRIPTIONS & USAGE BILLING — schema "billing"  (module: PlasticSurgery/Billing, guide: docs/billing.md)
-- ---------------------------------------------------------------------
-- Every billing table lives in its own Postgres schema, billing.*, like the product tables live in core.*, crm.* and so on.
-- Three separate questions, never collapsed into one:
--   1. Subscription: what the clinic pays SculptFlow for access (a plan: price, period, entitlements, and an
--      optional monetary included credit per period).
--   2. Provider billing responsibility: who pays the upstream provider (Meta, an SMS operator...) for a connected
--      channel account: customer_direct | platform_funded | external_provider_direct | no_provider_usage_fee.
--   3. SculptFlow usage billing: whether SculptFlow itself charges the clinic for that usage (by default only when
--      SculptFlow pays the provider).
-- Usage is a BILLABLE EVENT. Every event can be recorded (usage analytics, provider cost where known), but only
-- events SculptFlow charges are rated, reserved, settled and paid (included credit first, then the prepaid wallet).
-- Money is numeric(18,6) in the account currency (one currency for now: Billing:Currency, default USD).
-- Every balance change is one append-only row in billing.ledger_entries; billing.accounts caches the balances
-- and must always equal the ledger sums (BillingQueryService.ReconcileAsync checks it).
-- Concurrency: every money operation locks the clinic's billing.accounts row (select ... for update) inside one
-- transaction. Idempotency: usage records and ledger entries carry an idempotency key unique per clinic.
-- Nothing here is enforced until Billing:Enabled = true.
-- =====================================================================
create schema if not exists billing;
create extension if not exists btree_gist;   -- for the "no overlapping rate versions" exclusion constraint

-- Plans: what a clinic subscribes to. Price changes apply from the next renewal (history is in the ledger).
create table if not exists billing.plans (
  id                     uuid primary key default gen_random_uuid(),
  code                   varchar(50) not null unique check (code ~ '^[a-z0-9_-]+$'),
  name                   varchar(100) not null,
  description            text,
  price                  numeric(18,2) not null default 0 check (price >= 0),
  currency               char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  billing_period         varchar(10) not null default 'month' check (billing_period in ('month','year')),
  -- Monetary credit granted each period, usable for any usage SculptFlow charges. Unused credit expires at renewal.
  included_usage_credit  numeric(18,2) not null default 0 check (included_usage_credit >= 0),
  rate_card_id           uuid,          -- optional plan rate card; FK added after billing.rate_cards exists
  is_active              boolean not null default true,
  sort_order             integer not null default 0,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);
drop trigger if exists trg_plans_updated_at on billing.plans;
create trigger trg_plans_updated_at before update on billing.plans
  for each row execute function public.set_updated_at();

-- Entitlements: one row per (plan, key). Keys are defined in code (Billing/Entitlements.cs): features hold
-- true/false, limits hold a number or 'unlimited'. A key missing from a plan means "off" / 0.
create table if not exists billing.plan_entitlements (
  id               uuid primary key default gen_random_uuid(),
  plan_id          uuid not null references billing.plans(id) on delete cascade,
  entitlement_key  varchar(60) not null check (entitlement_key ~ '^[a-z0-9_]+$'),
  value            varchar(20) not null check (value ~ '^(true|false|unlimited|[0-9]{1,9})$'),
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now()
);
create unique index if not exists ux_plan_entitlements_key on billing.plan_entitlements(plan_id, entitlement_key);
drop trigger if exists trg_plan_entitlements_updated_at on billing.plan_entitlements;
create trigger trg_plan_entitlements_updated_at before update on billing.plan_entitlements
  for each row execute function public.set_updated_at();

-- Rate cards: named price lists. Lookup order for a billable event: the clinic's own card (clinic_id set,
-- custom pricing) -> the plan's card -> the default card. The first card with a matching rate wins.
create table if not exists billing.rate_cards (
  id           uuid primary key default gen_random_uuid(),
  code         varchar(50) not null unique check (code ~ '^[a-z0-9_-]+$'),
  name         varchar(100) not null,
  description  text,
  clinic_id    uuid references core.clinics(id) on delete cascade,   -- set = this clinic's custom-pricing card
  is_default   boolean not null default false,
  is_active    boolean not null default true,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  constraint ck_rate_cards_default_not_client check (not (is_default and clinic_id is not null))
);
create unique index if not exists ux_rate_cards_default on billing.rate_cards(is_default) where is_default;
create unique index if not exists ux_rate_cards_clinic on billing.rate_cards(clinic_id) where clinic_id is not null;
drop trigger if exists trg_rate_cards_updated_at on billing.rate_cards;
create trigger trg_rate_cards_updated_at before update on billing.rate_cards
  for each row execute function public.set_updated_at();

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'fk_plans_rate_card') then
    alter table billing.plans add constraint fk_plans_rate_card
      foreign key (rate_card_id) references billing.rate_cards(id) on delete set null;
  end if;
end $$;

-- Rates: one price version for one billable event type, optionally narrowed by destination country, operator,
-- provider and provider billing responsibility (null = any). The most specific match wins inside a card.
-- provider_cost = what the upstream provider charges per unit (paid by whoever the usage's responsibility says);
-- client_rate = what SculptFlow charges the clinic per unit. They're independent: a customer-direct "service fee"
-- rate can have client_rate 0.01 and the provider cost only for reporting. Rates with provider_billing = null apply
-- only to platform_funded charging (and to cost reporting for any usage): SculptFlow never charges a customer-paid
-- account a platform rate by fallback.
-- A version is never edited: a price change closes the current row (effective_to) and adds a new one.
create table if not exists billing.rates (
  id                uuid primary key default gen_random_uuid(),
  rate_card_id      uuid not null references billing.rate_cards(id) on delete cascade,
  event_type        varchar(60) not null check (event_type ~ '^[a-z0-9_]+$'),
  country_code      char(2) check (country_code ~ '^[A-Z]{2}$'),
  operator          varchar(60),
  provider          varchar(30),
  provider_billing  varchar(30) check (provider_billing in
                      ('customer_direct','platform_funded','external_provider_direct','no_provider_usage_fee')),
  unit              varchar(20) not null default 'unit',
  provider_cost     numeric(18,6) not null default 0 check (provider_cost >= 0),
  client_rate       numeric(18,6) not null check (client_rate >= 0),
  currency          char(3) not null check (currency ~ '^[A-Z]{3}$'),
  effective_from    timestamptz not null,
  effective_to      timestamptz,
  notes             text,
  created_by        varchar(200),
  created_at        timestamptz not null default now(),
  constraint ck_rates_period check (effective_to is null or effective_to > effective_from)
);
create index if not exists ix_rates_lookup on billing.rates(event_type, rate_card_id);
-- Two versions of the same rate (same card, event, country, operator, provider, responsibility) may never overlap.
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'ex_rates_no_overlap') then
    alter table billing.rates add constraint ex_rates_no_overlap exclude using gist (
      rate_card_id with =,
      event_type with =,
      (coalesce(country_code, '')) with =,
      (coalesce(operator, '')) with =,
      (coalesce(provider, '')) with =,
      (coalesce(provider_billing, '')) with =,
      tstzrange(effective_from, effective_to, '[)') with &&);
  end if;
end $$;
-- Price and scope are immutable; only effective_to (closing a version) and notes may change.
create or replace function billing.rates_guard() returns trigger as $$
begin
  if new.rate_card_id <> old.rate_card_id or new.event_type <> old.event_type
     or new.country_code is distinct from old.country_code or new.operator is distinct from old.operator
     or new.provider is distinct from old.provider or new.provider_billing is distinct from old.provider_billing
     or new.unit <> old.unit or new.provider_cost <> old.provider_cost or new.client_rate <> old.client_rate
     or new.currency <> old.currency or new.effective_from <> old.effective_from then
    raise exception 'billing.rates: a rate version can''t be edited; close it and add a new version (rate %)', old.id;
  end if;
  return new;
end;
$$ language plpgsql;
drop trigger if exists trg_rates_guard on billing.rates;
create trigger trg_rates_guard before update on billing.rates
  for each row execute function billing.rates_guard();

-- One billing account (wallet) per clinic. spendable = wallet_balance + included_credit_balance - reserved_amount.
-- The wallet only finances what the clinic owes SculptFlow. wallet_balance can only go below zero when a settlement
-- costs more than was reserved (usage that already happened); reservations, subscription charges and manual
-- debits never take it below zero.
create table if not exists billing.accounts (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null unique references core.clinics(id) on delete cascade,
  currency                 char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  wallet_balance           numeric(18,6) not null default 0,
  included_credit_balance  numeric(18,6) not null default 0 check (included_credit_balance >= 0),
  reserved_amount          numeric(18,6) not null default 0 check (reserved_amount >= 0),
  created_at               timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);
drop trigger if exists trg_accounts_updated_at on billing.accounts;
create trigger trg_accounts_updated_at before update on billing.accounts
  for each row execute function public.set_updated_at();

-- One subscription per clinic (changing plan updates this row; history is in events + the ledger).
-- active: normal. past_due: renewal couldn't be paid, still usable during the grace period (Billing:GracePeriodDays).
-- expired: grace ran out. cancelled: ended on request. Expired/cancelled clinics can still sign in and see their
-- data, but can't send messages, run campaigns or use the AI until a plan is started again.
create table if not exists billing.subscriptions (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null unique references core.clinics(id) on delete cascade,
  plan_id               uuid not null references billing.plans(id),
  status                varchar(20) not null check (status in ('active','past_due','expired','cancelled')),
  current_period_start  timestamptz not null,
  current_period_end    timestamptz not null,
  cancel_at_period_end  boolean not null default false,
  past_due_since        timestamptz,
  ended_at              timestamptz,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  constraint ck_subscriptions_period check (current_period_end > current_period_start)
);
create index if not exists ix_subscriptions_due on billing.subscriptions(current_period_end)
  where status in ('active','past_due');
drop trigger if exists trg_subscriptions_updated_at on billing.subscriptions;
create trigger trg_subscriptions_updated_at before update on billing.subscriptions
  for each row execute function public.set_updated_at();

-- Provider billing settings of one connected channel account (channels.channel_integrations row): who pays the upstream
-- provider, and whether SculptFlow charges usage on it. No row (or null values) = the defaults (channel/provider
-- defaults in code + Billing:ProviderBilling:Defaults). applies_to_provider: the override only holds while the
-- account is connected through that provider (a reconnect through another provider falls back to the defaults).
create table if not exists billing.channel_account_settings (
  channel_integration_id  uuid primary key references channels.channel_integrations(id) on delete cascade,
  clinic_id               uuid not null references core.clinics(id) on delete cascade,
  provider_billing        varchar(30) check (provider_billing in
                            ('customer_direct','platform_funded','external_provider_direct','no_provider_usage_fee')),
  omni_usage_billing      boolean,
  applies_to_provider     varchar(30),
  reason                  text,
  updated_by              varchar(200),
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now()
);
create index if not exists ix_channel_account_settings_clinic on billing.channel_account_settings(clinic_id);
drop trigger if exists trg_channel_account_settings_updated_at on billing.channel_account_settings;
create trigger trg_channel_account_settings_updated_at before update on billing.channel_account_settings
  for each row execute function public.set_updated_at();

-- Usage records (CDRs): one row per billable event, whether or not SculptFlow charges it.
--   charge_status    = SculptFlow's money: reserved -> settled | released; failed = refused (no rate / not enough
--                      balance, nothing charged); not_charged = SculptFlow doesn't bill this usage (customer-paid
--                      provider, no provider fee...) — never touches the wallet, included credit or ledger.
--   provider_outcome = what happened at the provider: pending -> billable (e.g. delivered) | not_billable (failed).
--   provider_billing = who paid the provider for it (snapshot of the account's responsibility at the time).
-- Prices are snapshotted at rating time, so later rate or setting changes never alter history.
-- message/conversation/campaign/channel_integration ids are plain columns (no FK) on purpose: financial history
-- must outlive deleted rows.
create table if not exists billing.usage_records (
  id                      uuid primary key default gen_random_uuid(),
  clinic_id               uuid not null references core.clinics(id) on delete cascade,
  billing_account_id      uuid not null references billing.accounts(id) on delete cascade,
  idempotency_key         varchar(200) not null,
  event_type              varchar(60) not null,
  channel                 varchar(30) not null,
  channel_integration_id  uuid,
  quantity                numeric(18,6) not null check (quantity >= 0),
  unit                    varchar(20),
  country_code            char(2),
  operator                varchar(60),
  provider                varchar(30),
  provider_billing        varchar(30) not null default 'platform_funded' check (provider_billing in
                            ('customer_direct','platform_funded','external_provider_direct','no_provider_usage_fee')),
  rate_id                 uuid references billing.rates(id),
  rate_card_id            uuid references billing.rate_cards(id),
  rate_source             varchar(20),
  unit_provider_cost      numeric(18,6),
  unit_price              numeric(18,6),
  provider_cost           numeric(18,6),
  amount                  numeric(18,6),
  reserved_amount         numeric(18,6) not null default 0 check (reserved_amount >= 0),
  credit_amount           numeric(18,6) not null default 0 check (credit_amount >= 0),
  wallet_amount           numeric(18,6) not null default 0 check (wallet_amount >= 0),
  refunded_amount         numeric(18,6) not null default 0 check (refunded_amount >= 0),
  currency                char(3) not null,
  charge_status           varchar(20) not null check (charge_status in ('reserved','settled','released','failed','not_charged')),
  provider_outcome        varchar(20) not null default 'pending' check (provider_outcome in ('pending','billable','not_billable')),
  failure_reason          varchar(60),
  release_reason          varchar(100),
  message_id              uuid,
  conversation_id         uuid,
  campaign_id             uuid,
  source                  varchar(30) not null default 'channel',
  actor                   varchar(200),
  occurred_at             timestamptz not null,
  reserved_at             timestamptz,
  settled_at              timestamptz,
  released_at             timestamptz,
  refunded_at             timestamptz,
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now(),
  constraint ck_usage_records_settled
    check (charge_status <> 'settled' or (amount is not null and credit_amount + wallet_amount = amount)),
  constraint ck_usage_records_not_charged
    check (charge_status <> 'not_charged' or (reserved_amount = 0 and credit_amount = 0 and wallet_amount = 0)),
  constraint ck_usage_records_refund check (refunded_amount <= coalesce(amount, 0))
);
create unique index if not exists ux_usage_records_key on billing.usage_records(clinic_id, idempotency_key);
create index if not exists ix_usage_records_clinic_occurred on billing.usage_records(clinic_id, occurred_at desc);
create index if not exists ix_usage_records_open on billing.usage_records(created_at)
  where charge_status = 'reserved' or (charge_status = 'not_charged' and provider_outcome = 'pending');
create index if not exists ix_usage_records_message on billing.usage_records(message_id) where message_id is not null;
-- Once the money side is final (settled/released/failed/not_charged) only the refund columns and the provider
-- outcome may change. (An uncharged record still waiting for its provider outcome may get its final quantity and
-- provider cost once, when that outcome arrives — no money is involved.)
create or replace function billing.usage_records_guard() returns trigger as $$
begin
  if old.charge_status <> 'reserved'
     and not (old.charge_status = 'not_charged' and old.provider_outcome = 'pending'
              and new.charge_status = 'not_charged' and new.amount is not distinct from old.amount
              and new.credit_amount = 0 and new.wallet_amount = 0 and new.reserved_amount = 0)
     and (
       new.charge_status is distinct from old.charge_status or new.amount is distinct from old.amount
       or new.quantity is distinct from old.quantity or new.credit_amount is distinct from old.credit_amount
       or new.wallet_amount is distinct from old.wallet_amount or new.reserved_amount is distinct from old.reserved_amount
       or new.unit_price is distinct from old.unit_price or new.provider_cost is distinct from old.provider_cost
       or new.rate_id is distinct from old.rate_id or new.idempotency_key is distinct from old.idempotency_key
       or new.provider_billing is distinct from old.provider_billing) then
    raise exception 'billing.usage_records: record % is final (%) and can no longer change', old.id, old.charge_status;
  end if;
  new.updated_at = now();
  return new;
end;
$$ language plpgsql;
drop trigger if exists trg_usage_records_guard on billing.usage_records;
create trigger trg_usage_records_guard before update on billing.usage_records
  for each row execute function billing.usage_records_guard();

-- Financial ledger: only real SculptFlow money movements (never uncharged usage). Append-only (updates are rejected
-- by a trigger). balance_after = that balance right after this entry. Signs: credits to a balance are positive,
-- debits negative. A subscription_charge of 0 is allowed so every started period (trial, free plan) has exactly one
-- charge row.
create table if not exists billing.ledger_entries (
  id                  uuid primary key default gen_random_uuid(),
  seq                 bigint generated by default as identity,   -- posting order (one operation's rows share created_at)
  clinic_id           uuid not null references core.clinics(id) on delete cascade,
  billing_account_id  uuid not null references billing.accounts(id) on delete cascade,
  entry_type          varchar(40) not null check (entry_type in ('wallet_top_up','usage_debit',
                        'included_credit_consumption','usage_refund','subscription_charge','included_credit_grant',
                        'included_credit_expiry','manual_adjustment')),
  balance_type        varchar(20) not null check (balance_type in ('wallet','included_credit')),
  amount              numeric(18,6) not null,
  balance_after       numeric(18,6) not null,
  currency            char(3) not null,
  idempotency_key     varchar(200) not null,
  usage_record_id     uuid references billing.usage_records(id),
  subscription_id     uuid references billing.subscriptions(id),
  plan_id             uuid,
  source              varchar(30) not null,
  actor               varchar(200),
  reason              text,
  reference           varchar(200),
  correlation_id      varchar(200),
  created_at          timestamptz not null default now(),
  constraint ck_ledger_entries_sign check (
       (entry_type in ('wallet_top_up','usage_refund','included_credit_grant') and amount > 0)
    or (entry_type in ('usage_debit','included_credit_consumption','included_credit_expiry') and amount < 0)
    or (entry_type = 'subscription_charge' and amount <= 0)
    or (entry_type = 'manual_adjustment' and amount <> 0)),
  constraint ck_ledger_entries_balance_type check (
       (entry_type in ('wallet_top_up','usage_debit','subscription_charge') and balance_type = 'wallet')
    or (entry_type in ('included_credit_consumption','included_credit_grant','included_credit_expiry')
        and balance_type = 'included_credit')
    or entry_type in ('usage_refund','manual_adjustment'))
);
create unique index if not exists ux_ledger_entries_key on billing.ledger_entries(clinic_id, idempotency_key);
create index if not exists ix_ledger_entries_clinic_seq on billing.ledger_entries(clinic_id, seq desc);
create index if not exists ix_ledger_entries_usage on billing.ledger_entries(usage_record_id)
  where usage_record_id is not null;
create or replace function billing.ledger_entries_immutable() returns trigger as $$
begin
  raise exception 'billing.ledger_entries is append-only: post a correcting entry instead of editing %', old.id;
end;
$$ language plpgsql;
drop trigger if exists trg_ledger_entries_immutable on billing.ledger_entries;
create trigger trg_ledger_entries_immutable before update on billing.ledger_entries
  for each row execute function billing.ledger_entries_immutable();

-- Every existing clinic gets an (empty) billing account; new clinics get one at registration (and lazily anyway).
insert into billing.accounts (clinic_id, currency)
select id, 'USD' from core.clinics
on conflict (clinic_id) do nothing;

-- =====================================================================
-- Configuration: config.settings
--
-- One row = the value of one setting, found by (section, key), e.g. ('Availability', 'DefaultDurationMinutes').
-- The app reads settings through IConfigManager: the row's value when there is one, otherwise the setting's constant
-- default in Common/Statics/ConfigDefaults (which is also the list of settings that exist). The app caches the rows
-- and re-reads them every minute and right after a change. Never secrets: those stay in user-secrets / environment
-- variables. Written through the platform-admin API (/api/platform-admin/settings), which the admin portal's
-- Configuration page calls.
-- =====================================================================
create table if not exists config.settings (
  section      varchar(100) not null,
  key          varchar(100) not null,
  value        text not null,
  description  text,
  updated_by   varchar(200),
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  primary key (section, key)
);

drop trigger if exists trg_settings_updated_at on config.settings;
create trigger trg_settings_updated_at
  before update on config.settings
  for each row execute function public.set_updated_at();

-- UTC guard: every moment in time is stored as timestamptz (UTC); only the clinic-local availability columns
-- (clinic_availability_rules/exceptions start_time/end_time/date, read together with clinics.timezone) are
-- zone-less on purpose. If any other column ever drifts to `timestamp without time zone`, convert it here,
-- reading its existing values as UTC (what the app always wrote). Checked 2026-09-29: none exist, so this is a no-op.
do $$
declare col record;
begin
  for col in
    select table_schema, table_name, column_name from information_schema.columns
    where table_schema in ('core', 'identity', 'crm', 'scheduling', 'channels', 'marketing', 'knowledge', 'activity', 'billing', 'config') and data_type = 'timestamp without time zone'
  loop
    execute format('alter table %I.%I alter column %I type timestamptz using %I at time zone ''UTC''',
                   col.table_schema, col.table_name, col.column_name, col.column_name);
  end loop;
end $$;
