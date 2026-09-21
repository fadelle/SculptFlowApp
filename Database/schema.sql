-- =====================================================================
-- Plastic Surgery Clinic Platform — MVP schema
-- =====================================================================
-- Run this directly against your Postgres/Supabase database
-- (psql, or the Supabase SQL Editor). This file is the source of
-- truth for the schema; the EF Core entity classes in /Data/Entities
-- are mapped to match it exactly via Fluent API rather than owning
-- migrations themselves — see Data/ApplicationDbContext.cs.
--
-- Idempotent: safe to re-run (uses IF NOT EXISTS / OR REPLACE).
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
-- 1. clinics
-- ---------------------------------------------------------------------
create table if not exists clinics (
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

drop trigger if exists trg_clinics_updated_at on clinics;
create trigger trg_clinics_updated_at
  before update on clinics
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 2. procedures  (catalog of services a clinic offers)
-- ---------------------------------------------------------------------
create table if not exists procedures (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references clinics(id) on delete cascade,
  name                   varchar(200) not null,
  code                   varchar(100),
  description            text,
  consultation_duration  integer,
  is_active              boolean not null default true,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_procedures_clinic_id on procedures(clinic_id);

drop trigger if exists trg_procedures_updated_at on procedures;
create trigger trg_procedures_updated_at
  before update on procedures
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 3. leads
-- ---------------------------------------------------------------------
create table if not exists leads (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,
  procedure_id          uuid references procedures(id) on delete set null,

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
  on leads(clinic_id, external_lead_id)
  where external_lead_id is not null;

create index if not exists ix_leads_clinic_id on leads(clinic_id);
create index if not exists ix_leads_clinic_phone on leads(clinic_id, phone);
create index if not exists ix_leads_clinic_email on leads(clinic_id, email);
create index if not exists ix_leads_status on leads(status);
create index if not exists ix_leads_procedure_id on leads(procedure_id);
create index if not exists ix_leads_created_at on leads(created_at);
create index if not exists ix_leads_next_followup_at on leads(next_followup_at) where next_followup_at is not null;

drop trigger if exists trg_leads_updated_at on leads;
create trigger trg_leads_updated_at
  before update on leads
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 4. conversations
-- ---------------------------------------------------------------------
create table if not exists conversations (
  id                      uuid primary key default gen_random_uuid(),
  clinic_id               uuid not null references clinics(id) on delete cascade,
  lead_id                 uuid not null references leads(id) on delete cascade,
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

create index if not exists ix_conversations_clinic_id on conversations(clinic_id);
create index if not exists ix_conversations_lead_id on conversations(lead_id);

drop trigger if exists trg_conversations_updated_at on conversations;
create trigger trg_conversations_updated_at
  before update on conversations
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 5. messages
-- ---------------------------------------------------------------------
create table if not exists messages (
  id                   uuid primary key default gen_random_uuid(),
  clinic_id            uuid not null references clinics(id) on delete cascade,
  conversation_id      uuid not null references conversations(id) on delete cascade,
  lead_id              uuid not null references leads(id) on delete cascade,

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

create index if not exists ix_messages_clinic_id on messages(clinic_id);
create index if not exists ix_messages_conversation_id on messages(conversation_id);
create index if not exists ix_messages_lead_id on messages(lead_id);
create index if not exists ix_messages_created_at on messages(created_at);

-- ---------------------------------------------------------------------
-- 6. appointments
-- ---------------------------------------------------------------------
create table if not exists appointments (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,
  lead_id               uuid not null references leads(id) on delete cascade,
  procedure_id          uuid references procedures(id) on delete set null,

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

create index if not exists ix_appointments_clinic_id on appointments(clinic_id);
create index if not exists ix_appointments_lead_id on appointments(lead_id);
create index if not exists ix_appointments_scheduled_start on appointments(scheduled_start);
create index if not exists ix_appointments_status on appointments(status);

drop trigger if exists trg_appointments_updated_at on appointments;
create trigger trg_appointments_updated_at
  before update on appointments
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 7. procedure_bookings  (a lead actually booking/undergoing a procedure)
-- ---------------------------------------------------------------------
create table if not exists procedure_bookings (
  id                uuid primary key default gen_random_uuid(),
  clinic_id         uuid not null references clinics(id) on delete cascade,
  lead_id           uuid not null references leads(id) on delete cascade,
  procedure_id      uuid not null references procedures(id) on delete restrict,
  appointment_id    uuid references appointments(id) on delete set null,

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

create index if not exists ix_procedure_bookings_clinic_id on procedure_bookings(clinic_id);
create index if not exists ix_procedure_bookings_lead_id on procedure_bookings(lead_id);
create index if not exists ix_procedure_bookings_status on procedure_bookings(status);

drop trigger if exists trg_procedure_bookings_updated_at on procedure_bookings;
create trigger trg_procedure_bookings_updated_at
  before update on procedure_bookings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 8. events  (audit / analytics event log)
-- ---------------------------------------------------------------------
create table if not exists events (
  id               uuid primary key default gen_random_uuid(),
  clinic_id        uuid not null references clinics(id) on delete cascade,
  lead_id          uuid references leads(id) on delete set null,
  conversation_id  uuid references conversations(id) on delete set null,
  appointment_id   uuid references appointments(id) on delete set null,
  event_type       varchar(100) not null,
  source            varchar(50),
  metadata         jsonb not null default '{}'::jsonb,
  created_at       timestamptz not null default now()
);

create index if not exists ix_events_clinic_id on events(clinic_id);
create index if not exists ix_events_event_type on events(event_type);
create index if not exists ix_events_created_at on events(created_at);

-- ---------------------------------------------------------------------
-- 9. channel_integrations  (per-clinic WhatsApp / Instagram / Facebook connection config —
--    Settings → Channels & Integrations in the dashboard)
-- ---------------------------------------------------------------------
create table if not exists channel_integrations (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references clinics(id) on delete cascade,

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
  on channel_integrations(clinic_id, channel);

drop trigger if exists trg_channel_integrations_updated_at on channel_integrations;
create trigger trg_channel_integrations_updated_at
  before update on channel_integrations
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

alter table messages add column if not exists origin varchar(50) not null default 'system';
alter table conversations add column if not exists mode varchar(30) not null default 'ai';

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_messages_origin') then
    alter table messages add constraint ck_messages_origin check (origin in (
      'whatsapp_customer','whatsapp_business_app','dashboard','ai','system'
    ));
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_conversations_mode') then
    alter table conversations add constraint ck_conversations_mode check (mode in ('ai','human','approval'));
  end if;
end $$;

create index if not exists ix_messages_origin on messages(origin);
create index if not exists ix_conversations_mode on conversations(mode);

-- Idempotency for WhatsApp webhook/n8n retries: the same external_message_id must never produce
-- two rows. Scoped by clinic + channel since external IDs are only unique within one provider.
create unique index if not exists ux_messages_clinic_channel_external_message_id
  on messages(clinic_id, channel, external_message_id)
  where external_message_id is not null;

-- Backfill mode from the pre-existing human_takeover flag for any rows that predate this column
-- (harmless to re-run: app code keeps human_takeover in sync with mode going forward, so this
-- just re-derives the same value on subsequent runs).
update conversations set mode = case when human_takeover then 'human' else 'ai' end;

-- ---------------------------------------------------------------------
-- WhatsApp Templates + Campaigns + 24-hour customer service window
--
-- The service window: WhatsApp only allows free-form (non-template) outbound messages within 24h
-- of the customer's last inbound message. last_customer_message_at/service_window_expires_at are
-- updated ONLY when a genuine inbound whatsapp_customer message is ingested (never by staff, AI,
-- or campaign sends — see MessageService.IngestAsync) — see Conversation.IsServiceWindowOpen().
-- ---------------------------------------------------------------------

alter table conversations add column if not exists last_customer_message_at timestamptz;
alter table conversations add column if not exists service_window_expires_at timestamptz;
create index if not exists ix_conversations_service_window on conversations(service_window_expires_at);

-- messages: allow the new 'campaign' origin, and link a message back to whichever
-- template/campaign produced it (all nullable — most messages have none of these).
alter table messages drop constraint if exists ck_messages_origin;
alter table messages add constraint ck_messages_origin check (origin in (
  'whatsapp_customer','whatsapp_business_app','dashboard','ai','system','campaign'
));

alter table messages add column if not exists whatsapp_template_id uuid;
alter table messages add column if not exists campaign_id uuid;
alter table messages add column if not exists campaign_recipient_id uuid;

create index if not exists ix_messages_campaign_id on messages(campaign_id) where campaign_id is not null;
create index if not exists ix_messages_whatsapp_template_id on messages(whatsapp_template_id) where whatsapp_template_id is not null;

-- ---------------------------------------------------------------------
-- whatsapp_templates
-- ---------------------------------------------------------------------
create table if not exists whatsapp_templates (
  id                 uuid primary key default gen_random_uuid(),
  clinic_id          uuid not null references clinics(id) on delete cascade,

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

create index if not exists ix_whatsapp_templates_clinic_id on whatsapp_templates(clinic_id);
-- Meta's own uniqueness model: one template name is one thing per language per WABA (clinic, here).
create unique index if not exists ux_whatsapp_templates_clinic_name_language
  on whatsapp_templates(clinic_id, name, language);

drop trigger if exists trg_whatsapp_templates_updated_at on whatsapp_templates;
create trigger trg_whatsapp_templates_updated_at
  before update on whatsapp_templates
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaigns
-- ---------------------------------------------------------------------
create table if not exists campaigns (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,
  name                  varchar(200) not null,
  whatsapp_template_id  uuid not null references whatsapp_templates(id) on delete restrict,
  status                varchar(20) not null default 'draft',
  scheduled_at          timestamptz,
  started_at            timestamptz,
  completed_at          timestamptz,
  created_by_user_id    uuid,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_campaigns_status check (status in ('draft','scheduled','running','completed','cancelled','failed'))
);

create index if not exists ix_campaigns_clinic_id on campaigns(clinic_id);
create index if not exists ix_campaigns_status on campaigns(status);
create index if not exists ix_campaigns_scheduled_at on campaigns(scheduled_at) where scheduled_at is not null;
create index if not exists ix_campaigns_whatsapp_template_id on campaigns(whatsapp_template_id);

drop trigger if exists trg_campaigns_updated_at on campaigns;
create trigger trg_campaigns_updated_at
  before update on campaigns
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaign_recipients — one row per target lead. Every actual send still produces a normal row in
-- messages (message_id here points to it) — this table is reporting/lifecycle state, not a second
-- message store.
-- ---------------------------------------------------------------------
create table if not exists campaign_recipients (
  id                uuid primary key default gen_random_uuid(),
  clinic_id         uuid not null references clinics(id) on delete cascade,
  campaign_id       uuid not null references campaigns(id) on delete cascade,
  lead_id           uuid not null references leads(id) on delete cascade,
  conversation_id   uuid references conversations(id) on delete set null,
  message_id        uuid references messages(id) on delete set null,

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

create index if not exists ix_campaign_recipients_clinic_id on campaign_recipients(clinic_id);
create index if not exists ix_campaign_recipients_campaign_id on campaign_recipients(campaign_id);
create index if not exists ix_campaign_recipients_lead_id on campaign_recipients(lead_id);
create index if not exists ix_campaign_recipients_status on campaign_recipients(status);
-- Prevent the same lead being inserted into the same campaign twice.
create unique index if not exists ux_campaign_recipients_campaign_lead
  on campaign_recipients(campaign_id, lead_id);

drop trigger if exists trg_campaign_recipients_updated_at on campaign_recipients;
create trigger trg_campaign_recipients_updated_at
  before update on campaign_recipients
  for each row execute function set_updated_at();

-- Deferred FKs on messages — added last, now that the tables they reference exist.
do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_whatsapp_template_id') then
    alter table messages add constraint fk_messages_whatsapp_template_id
      foreign key (whatsapp_template_id) references whatsapp_templates(id) on delete set null;
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_campaign_id') then
    alter table messages add constraint fk_messages_campaign_id
      foreign key (campaign_id) references campaigns(id) on delete set null;
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_messages_campaign_recipient_id') then
    alter table messages add constraint fk_messages_campaign_recipient_id
      foreign key (campaign_recipient_id) references campaign_recipients(id) on delete set null;
  end if;
end $$;

-- ---------------------------------------------------------------------
-- AI agent tool surface (Controllers/AiController.cs) — clinics gains the free-text fields
-- get_clinic_info reads. No new tables: appointment reschedule reuses scheduled_start/end,
-- and cancel/handoff reuse existing status/mode columns.
-- ---------------------------------------------------------------------
alter table clinics add column if not exists address text;
alter table clinics add column if not exists operating_hours text;
alter table clinics add column if not exists consultation_info text;

-- WhatsApp phone number registration (Cloud API requires this after Embedded Signup — see
-- ChannelIntegrationService.ConnectWhatsAppAsync / MetaGraphClient.RegisterPhoneNumberAsync).
alter table channel_integrations add column if not exists pin varchar(10);

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
alter table messages add column if not exists delivered_at timestamptz;
alter table messages add column if not exists read_at timestamptz;
alter table messages add column if not exists failed_at timestamptz;
alter table messages add column if not exists deleted_at timestamptz;
alter table messages add column if not exists failure_code varchar(100);
alter table messages add column if not exists failure_reason text;
alter table messages add column if not exists metadata_json jsonb;

-- ---------------------------------------------------------------------
-- channel_integrations: WhatsApp account/phone health, populated from Meta's account_update,
-- account_review_update, phone_number_quality_update, phone_number_name_update webhooks.
-- Deliberately free text (no CHECK constraints) — see whatsapp_templates below for why.
-- ---------------------------------------------------------------------
alter table channel_integrations add column if not exists meta_business_id varchar(200);
alter table channel_integrations add column if not exists verified_name varchar(200);
alter table channel_integrations add column if not exists account_status varchar(50);
alter table channel_integrations add column if not exists account_review_status varchar(50);
alter table channel_integrations add column if not exists phone_quality_rating varchar(50);
alter table channel_integrations add column if not exists phone_status varchar(50);
alter table channel_integrations add column if not exists name_status varchar(50);
alter table channel_integrations add column if not exists is_healthy boolean not null default true;
alter table channel_integrations add column if not exists health_level varchar(30);
alter table channel_integrations add column if not exists last_problem_code varchar(100);
alter table channel_integrations add column if not exists last_problem_message text;
alter table channel_integrations add column if not exists last_webhook_at timestamptz;
alter table channel_integrations add column if not exists last_health_event_at timestamptz;

-- Superseded by the unique filtered index below — a Meta phone_number_id must never map to more
-- than one clinic (webhook routing: phone_number_id -> channel_integration -> clinic_id depends on
-- this being unambiguous).
drop index if exists ix_channel_integrations_phone_number_id;
create unique index if not exists ux_channel_integrations_phone_number_id
  on channel_integrations(phone_number_id) where phone_number_id is not null;

-- WABA id stays non-unique on purpose: one WABA can contain multiple phone numbers, so multiple
-- rows legitimately sharing a whatsapp_business_id is expected, not a conflict.
create index if not exists ix_channel_integrations_whatsapp_business_id on channel_integrations(whatsapp_business_id);

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
alter table whatsapp_templates add column if not exists channel_integration_id uuid;
alter table whatsapp_templates add column if not exists quality_rating varchar(30);
alter table whatsapp_templates add column if not exists previous_category varchar(30);
alter table whatsapp_templates add column if not exists current_category varchar(30);
alter table whatsapp_templates add column if not exists components jsonb;
alter table whatsapp_templates add column if not exists last_meta_event_at timestamptz;

alter table whatsapp_templates drop constraint if exists ck_whatsapp_templates_status;
alter table whatsapp_templates drop constraint if exists ck_whatsapp_templates_category;

create index if not exists ix_whatsapp_templates_channel_integration_id on whatsapp_templates(channel_integration_id);
-- Meta template ids aren't globally unique across clinics by themselves, but scoped by clinic they
-- are — this is the upsert key ApplyMetaEventAsync uses (not unique, since Meta sends null template
-- ids for pure category-limit updates that still need to land somewhere findable by name+language).
create index if not exists ix_whatsapp_templates_clinic_meta_template_id
  on whatsapp_templates(clinic_id, meta_template_id) where meta_template_id is not null;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_whatsapp_templates_channel_integration_id') then
    alter table whatsapp_templates add constraint fk_whatsapp_templates_channel_integration_id
      foreign key (channel_integration_id) references channel_integrations(id) on delete set null;
  end if;
end $$;

-- ---------------------------------------------------------------------
-- whatsapp_health_events: append-only history behind channel_integrations' current health state.
-- event_type/severity/status are free text for the same "tolerate unknown Meta values" reason as
-- whatsapp_templates — raw_metadata keeps whatever n8n forwarded, verbatim, alongside the
-- normalized fields.
-- ---------------------------------------------------------------------
create table if not exists whatsapp_health_events (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null references clinics(id) on delete cascade,
  channel_integration_id   uuid not null references channel_integrations(id) on delete cascade,

  event_type               varchar(100) not null,
  severity                 varchar(20),

  status                   varchar(50),
  code                     varchar(100),
  message                  text,

  raw_metadata             jsonb,

  occurred_at              timestamptz not null,
  created_at               timestamptz not null default now()
);

create index if not exists ix_whatsapp_health_events_clinic_id on whatsapp_health_events(clinic_id);
create index if not exists ix_whatsapp_health_events_channel_integration_id on whatsapp_health_events(channel_integration_id);
create index if not exists ix_whatsapp_health_events_occurred_at on whatsapp_health_events(occurred_at);
-- Practical idempotency for webhook retries: Meta doesn't hand these a stable event id, so dedupe
-- on (connection, event type, occurred_at) — see WhatsAppHealthService.ApplyHealthEventAsync.
create unique index if not exists ux_whatsapp_health_events_connection_type_occurred
  on whatsapp_health_events(channel_integration_id, event_type, occurred_at);

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

create table if not exists identity_users (
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

create unique index if not exists "UserNameIndex" on identity_users(normalized_user_name);
create index if not exists "EmailIndex" on identity_users(normalized_email);

create table if not exists identity_user_claims (
  id            integer generated always as identity primary key,
  user_id       text not null references identity_users(id) on delete cascade,
  claim_type    text,
  claim_value   text
);

create index if not exists ix_identity_user_claims_user_id on identity_user_claims(user_id);

create table if not exists identity_user_logins (
  login_provider          varchar(128) not null,
  provider_key             varchar(128) not null,
  provider_display_name   text,
  user_id                  text not null references identity_users(id) on delete cascade,

  primary key (login_provider, provider_key)
);

create index if not exists ix_identity_user_logins_user_id on identity_user_logins(user_id);

create table if not exists identity_user_tokens (
  user_id          text not null references identity_users(id) on delete cascade,
  login_provider   varchar(128) not null,
  name             varchar(128) not null,
  value            text,

  primary key (user_id, login_provider, name)
);

-- ---------------------------------------------------------------------
-- clinic_users
-- ---------------------------------------------------------------------
create table if not exists clinic_users (
  id            uuid primary key default gen_random_uuid(),
  clinic_id     uuid not null references clinics(id) on delete cascade,
  user_id       text not null references identity_users(id) on delete cascade,
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create unique index if not exists ux_clinic_users_clinic_user on clinic_users(clinic_id, user_id);
-- Superseded by the unique filtered index below — enforces "one active clinic membership per
-- user", the MVP assumption CurrentClinicContext's first-active-row lookup relies on.
-- Historical/inactive rows for the same user are unrestricted; only one active row per user can
-- ever exist.
drop index if exists ix_clinic_users_user_id;
create unique index if not exists ux_clinic_users_user_active on clinic_users(user_id) where is_active = true;

drop trigger if exists trg_clinic_users_updated_at on clinic_users;
create trigger trg_clinic_users_updated_at
  before update on clinic_users
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

alter table campaigns add column if not exists campaign_type varchar(30) not null default 'custom';
alter table campaigns add column if not exists channel varchar(30) not null default 'whatsapp';
alter table campaigns add column if not exists audience_type varchar(30) not null default 'custom';
alter table campaigns add column if not exists audience_filters jsonb;

-- Nullable at the DB level for forward-compatibility with a future non-template campaign type;
-- every campaign actually sendable today still requires one — enforced in
-- CampaignService.CreateAsync, not the database.
alter table campaigns alter column whatsapp_template_id drop not null;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_campaigns_channel') then
    alter table campaigns add constraint ck_campaigns_channel check (channel in ('whatsapp','instagram','messenger'));
  end if;
end $$;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_campaigns_audience_type') then
    alter table campaigns add constraint ck_campaigns_audience_type check (audience_type in ('all_eligible','reactivation_no_consultation','custom'));
  end if;
end $$;

-- campaign_type is deliberately NOT constrained (like leads.source) — new campaign types can be
-- added later without a migration.

-- 'paused' added to the campaign status lifecycle.
alter table campaigns drop constraint if exists ck_campaigns_status;
alter table campaigns add constraint ck_campaigns_status
  check (status in ('draft','scheduled','running','paused','completed','cancelled','failed'));

create index if not exists ix_campaigns_campaign_type on campaigns(campaign_type);

-- ---------------------------------------------------------------------
-- campaign_recipients: reply/booking attribution + skip tracking
-- ---------------------------------------------------------------------
alter table campaign_recipients add column if not exists external_message_id varchar(200);
alter table campaign_recipients add column if not exists appointment_id uuid;
alter table campaign_recipients add column if not exists skip_reason varchar(50);
alter table campaign_recipients add column if not exists failure_code varchar(100);
alter table campaign_recipients add column if not exists replied_at timestamptz;
alter table campaign_recipients add column if not exists booked_at timestamptz;

-- error_message -> failure_reason, matching the messages table's failure_code/failure_reason naming.
alter table campaign_recipients rename column error_message to failure_reason;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'fk_campaign_recipients_appointment_id') then
    alter table campaign_recipients add constraint fk_campaign_recipients_appointment_id
      foreign key (appointment_id) references appointments(id) on delete set null;
  end if;
end $$;

alter table campaign_recipients drop constraint if exists ck_campaign_recipients_status;
alter table campaign_recipients add constraint ck_campaign_recipients_status
  check (status in ('pending','queued','sent','delivered','read','replied','booked','failed','skipped'));

create index if not exists ix_campaign_recipients_external_message_id
  on campaign_recipients(external_message_id) where external_message_id is not null;
create index if not exists ix_campaign_recipients_appointment_id
  on campaign_recipients(appointment_id) where appointment_id is not null;
-- ux_campaign_recipients_campaign_lead (UNIQUE(campaign_id, lead_id)) already exists from the
-- original campaign_recipients table above — no change needed for the "no duplicate lead in the
-- same campaign" requirement.


-- =====================================================================
-- Clinic Knowledge Base — knowledge_documents (what staff type in) + knowledge_chunks (the
-- embedded slices the AI agent searches semantically). Everything is scoped by clinic_id; every
-- search filters on it. See Services/IKnowledgeService.cs and IKnowledgeSearchService.cs.
-- =====================================================================
create extension if not exists vector;

create table if not exists knowledge_documents (
  id          uuid primary key default gen_random_uuid(),
  clinic_id   uuid not null references clinics(id) on delete cascade,
  title       varchar(200) not null,
  -- Extensible string (general, faq, policy, doctor, procedure, pricing, payment, consultation,
  -- preparation, recovery, ...) — deliberately no CHECK, like campaigns.campaign_type.
  category    varchar(50) not null default 'general',
  content     text not null,
  is_active   boolean not null default true,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create index if not exists ix_knowledge_documents_clinic_id on knowledge_documents(clinic_id);
create index if not exists ix_knowledge_documents_category on knowledge_documents(category);
create index if not exists ix_knowledge_documents_is_active on knowledge_documents(is_active);

drop trigger if exists trg_knowledge_documents_updated_at on knowledge_documents;
create trigger trg_knowledge_documents_updated_at
  before update on knowledge_documents
  for each row execute function set_updated_at();

-- vector(1536) matches Embeddings:Dimensions (default: OpenAI text-embedding-3-small at 1536).
-- If you switch to a model with a different dimension, this column must be changed to match.
create table if not exists knowledge_chunks (
  id                     uuid primary key default gen_random_uuid(),
  clinic_id              uuid not null references clinics(id) on delete cascade,
  knowledge_document_id  uuid not null references knowledge_documents(id) on delete cascade,
  chunk_index            integer not null,
  content                text not null,
  embedding              vector(1536) not null,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_knowledge_chunks_clinic_id on knowledge_chunks(clinic_id);
create index if not exists ix_knowledge_chunks_knowledge_document_id on knowledge_chunks(knowledge_document_id);
-- Deliberately no ANN (hnsw/ivfflat) index yet: every search is already narrowed to one clinic by
-- ix_knowledge_chunks_clinic_id first, and a clinic's Knowledge Base is hundreds of chunks, not
-- millions — an exact scan of that slice is fast and has perfect recall (an ANN index would apply the
-- clinic filter *after* its approximate scan and can drop results). Add
--   create index ... using hnsw (embedding vector_cosine_ops);
-- only if a single clinic's chunk count grows into the tens of thousands.

drop trigger if exists trg_knowledge_chunks_updated_at on knowledge_chunks;
create trigger trg_knowledge_chunks_updated_at
  before update on knowledge_chunks
  for each row execute function set_updated_at();


-- =====================================================================
-- Knowledge Base search settings — one row per clinic (unique clinic_id), created lazily by
-- IKnowledgeSettingsService with the system defaults. Staff edit only chunk size/overlap, top_k and
-- minimum_similarity; embedding_model, vector_dimension, similarity_method and vector_index_type are
-- persisted for transparency but read-only in the UI (changing them means re-embedding and/or a
-- migration of knowledge_chunks.embedding). Secrets such as the embeddings API key are NEVER stored
-- here — they stay in configuration.
-- =====================================================================
create table if not exists knowledge_search_settings (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,

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

create unique index if not exists ux_knowledge_search_settings_clinic_id on knowledge_search_settings(clinic_id);

drop trigger if exists trg_knowledge_search_settings_updated_at on knowledge_search_settings;
create trigger trg_knowledge_search_settings_updated_at
  before update on knowledge_search_settings
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
alter table channel_integrations add column if not exists telegram_bot_id varchar(50);
alter table channel_integrations add column if not exists telegram_bot_username varchar(100);
-- 'active' | 'pending' | 'error' | 'not_registered' — generic on purpose so other webhook-registered
-- channels could reuse it; WhatsApp/Facebook rows leave it null.
alter table channel_integrations add column if not exists webhook_status varchar(30);
alter table channel_integrations add column if not exists webhook_registered_at timestamptz;

alter table channel_integrations drop constraint if exists ck_channel_integrations_channel;
alter table channel_integrations add constraint ck_channel_integrations_channel
  check (channel in ('whatsapp','instagram','facebook','telegram'));

-- One bot can have only one webhook, so one bot may be connected to only one clinic at a time
-- (a disconnected row keeps its bot id for display but no longer blocks another clinic).
create unique index if not exists ux_channel_integrations_telegram_bot_id
  on channel_integrations(telegram_bot_id)
  where telegram_bot_id is not null and status = 'connected';

alter table conversations drop constraint if exists ck_conversations_channel;
alter table conversations add constraint ck_conversations_channel
  check (channel in ('whatsapp','instagram','website','facebook','sms','email','telegram'));

alter table messages drop constraint if exists ck_messages_origin;
alter table messages add constraint ck_messages_origin check (origin in (
  'whatsapp_customer','whatsapp_business_app','dashboard','ai','system','campaign','telegram_customer'
));

-- A Telegram chat maps to exactly one conversation per clinic+channel (external_thread_id = chat.id).
-- Partial: WhatsApp/other conversations leave external_thread_id null.
create unique index if not exists ux_conversations_clinic_channel_thread
  on conversations(clinic_id, channel, external_thread_id)
  where external_thread_id is not null;

-- ---------------------------------------------------------------------
-- Knowledge Base: uploaded documents (PDF / DOCX / TXT)
--
-- An uploaded file is turned into text server-side and stored in knowledge_documents.content — the
-- same column manual entries use — then chunked/embedded by the SAME pipeline. So re-saving or
-- re-indexing never needs the original file, and the binary is NOT stored (only these metadata
-- columns). source_type = 'manual' (typed in) | 'upload' (extracted from a file).
-- ---------------------------------------------------------------------
alter table knowledge_documents add column if not exists source_type varchar(20) not null default 'manual';
alter table knowledge_documents add column if not exists original_file_name varchar(255);
alter table knowledge_documents add column if not exists mime_type varchar(100);
alter table knowledge_documents add column if not exists file_size_bytes bigint;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'ck_knowledge_documents_source_type') then
    alter table knowledge_documents add constraint ck_knowledge_documents_source_type
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
  if not exists (select 1 from information_schema.columns where table_name = 'conversations' and column_name = 'last_read_at') then
    alter table conversations add column last_read_at timestamptz;
    update conversations set last_read_at = now();
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
alter table knowledge_documents add column if not exists source_url varchar(2000);
alter table knowledge_documents drop constraint if exists ck_knowledge_documents_source_type;
alter table knowledge_documents add constraint ck_knowledge_documents_source_type
  check (source_type in ('manual','upload','website'));

create table if not exists knowledge_website_sources (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,

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
create unique index if not exists ux_kws_clinic_start_url on knowledge_website_sources(clinic_id, normalized_start_url);
create index if not exists ix_kws_clinic_id on knowledge_website_sources(clinic_id);

drop trigger if exists trg_kws_updated_at on knowledge_website_sources;
create trigger trg_kws_updated_at before update on knowledge_website_sources
  for each row execute function set_updated_at();

create table if not exists knowledge_website_pages (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,
  website_source_id     uuid not null references knowledge_website_sources(id) on delete cascade,

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
  knowledge_document_id uuid references knowledge_documents(id) on delete set null,
  -- For status 'duplicate': the page whose identical content/canonical was kept instead.
  duplicate_of_page_id  uuid references knowledge_website_pages(id) on delete set null,

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
create unique index if not exists ux_kwp_source_normalized_url on knowledge_website_pages(website_source_id, normalized_url);
create index if not exists ix_kwp_clinic_id on knowledge_website_pages(clinic_id);
create index if not exists ix_kwp_source_status on knowledge_website_pages(website_source_id, status);
create index if not exists ix_kwp_document_id on knowledge_website_pages(knowledge_document_id) where knowledge_document_id is not null;
-- Deliberately NOT unique: identical text may legitimately exist on several pages / clinics.
create index if not exists ix_kwp_source_content_hash on knowledge_website_pages(website_source_id, content_hash) where content_hash is not null;

drop trigger if exists trg_kwp_updated_at on knowledge_website_pages;
create trigger trg_kwp_updated_at before update on knowledge_website_pages
  for each row execute function set_updated_at();

create table if not exists knowledge_website_scrape_runs (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,
  website_source_id     uuid not null references knowledge_website_sources(id) on delete cascade,

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
create index if not exists ix_kwr_source_created on knowledge_website_scrape_runs(website_source_id, created_at desc);
create index if not exists ix_kwr_clinic_id on knowledge_website_scrape_runs(clinic_id);

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
create table if not exists knowledge_retrieval_benchmark_cases (
  id                    uuid primary key default gen_random_uuid(),
  clinic_id             uuid not null references clinics(id) on delete cascade,

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
create index if not exists ix_kbc_clinic_id on knowledge_retrieval_benchmark_cases(clinic_id);
create index if not exists ix_kbc_clinic_chunk on knowledge_retrieval_benchmark_cases(clinic_id, expected_chunk_id);
-- The same question for the same chunk is never stored twice.
create unique index if not exists ux_kbc_clinic_chunk_question
  on knowledge_retrieval_benchmark_cases(clinic_id, expected_chunk_id, lower(question));

drop trigger if exists trg_kbc_updated_at on knowledge_retrieval_benchmark_cases;
create trigger trg_kbc_updated_at before update on knowledge_retrieval_benchmark_cases
  for each row execute function set_updated_at();

create table if not exists knowledge_retrieval_benchmark_runs (
  id                       uuid primary key default gen_random_uuid(),
  clinic_id                uuid not null references clinics(id) on delete cascade,

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
create index if not exists ix_kbr_clinic_created on knowledge_retrieval_benchmark_runs(clinic_id, created_at desc);

create table if not exists knowledge_retrieval_benchmark_results (
  id                          uuid primary key default gen_random_uuid(),
  clinic_id                   uuid not null references clinics(id) on delete cascade,
  benchmark_run_id            uuid not null references knowledge_retrieval_benchmark_runs(id) on delete cascade,
  -- set null (not cascade): deleting a case keeps the run's history intact via the snapshots below.
  benchmark_case_id           uuid references knowledge_retrieval_benchmark_cases(id) on delete set null,

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
create index if not exists ix_kbres_run on knowledge_retrieval_benchmark_results(benchmark_run_id);
create index if not exists ix_kbres_clinic_id on knowledge_retrieval_benchmark_results(clinic_id);
create index if not exists ix_kbres_case_created on knowledge_retrieval_benchmark_results(benchmark_case_id, created_at desc);

-- Retrieval Benchmark: which generation request created a generated case. The app generates a generation_id per
-- "Generate Test Cases" request, sends it to the n8n generator, requires it echoed back, and stores it on every case
-- that response produced (null for manual cases and for cases created before this column existed).
alter table knowledge_retrieval_benchmark_cases add column if not exists generation_id uuid;
create index if not exists ix_kbc_clinic_generation on knowledge_retrieval_benchmark_cases(clinic_id, generation_id) where generation_id is not null;

-- Retrieval Benchmark: ASYNC question generation. One row per "Generate Test Cases" request. `id` IS the generationId that is
-- sent to the n8n generator and that n8n's callback must carry (POST .../generations/{id}/questions). The row is created
-- (pending) BEFORE anything is sent, so even an instant callback finds it, and it remembers exactly which chunks were sent so
-- the reply can be validated against that set (and the clinic is taken from THIS row, never from the caller).
create table if not exists knowledge_retrieval_benchmark_generations (
  id                    uuid primary key,
  clinic_id             uuid not null references clinics(id) on delete cascade,

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
create index if not exists ix_kbg_clinic_created on knowledge_retrieval_benchmark_generations(clinic_id, created_at desc);

-- Results remember which generation their case came from, so scores can be reported per generation even after the case is
-- deleted (the case FK is ON DELETE SET NULL).
alter table knowledge_retrieval_benchmark_results add column if not exists generation_id uuid;
create index if not exists ix_kbres_generation on knowledge_retrieval_benchmark_results(clinic_id, generation_id) where generation_id is not null;

-- Backfill: generations created by the earlier synchronous version left only a tag on their cases.
insert into knowledge_retrieval_benchmark_generations
  (id, clinic_id, status, chunks_sent, questions_returned, cases_created, completed_at, created_at)
select generation_id, clinic_id, 'completed', count(*), count(*), count(*), min(created_at), min(created_at)
from knowledge_retrieval_benchmark_cases
where generation_id is not null
group by generation_id, clinic_id
on conflict (id) do nothing;

update knowledge_retrieval_benchmark_results r
set generation_id = c.generation_id
from knowledge_retrieval_benchmark_cases c
where r.benchmark_case_id = c.id and c.generation_id is not null and r.generation_id is null;
