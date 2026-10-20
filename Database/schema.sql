-- =====================================================================
-- Omni platform — MVP schema
-- =====================================================================
-- Run this directly against your Postgres/Supabase database
-- (psql, or the Supabase SQL Editor). This file is the source of
-- truth for the schema; the EF Core entity classes in
-- Omni.App/Entities/Models are mapped to match it exactly via
-- Fluent API rather than owning migrations themselves — see
-- Omni.App/Persistence/Contexts/ApplicationDbContext.cs.
--
-- Idempotent: safe to re-run (uses IF NOT EXISTS / OR REPLACE).
--
-- Tables live in one Postgres schema per area (never in public):
--   core        business types, clients, services
--   identity    ASP.NET Identity users/claims/logins/tokens, client_users
--   crm         pipeline stages, contacts, orders, conversations, messages, inbox settings
--   scheduling  bookings, availability, calendar sync
--   channels    channel integrations, WhatsApp templates/health, TikTok
--   marketing   campaigns, campaign recipients
--   knowledge   the knowledge base, website scraping, retrieval benchmarks
--   activity    events, notifications, API timings (api_route_stats)
--   billing     plans, rate cards, subscriptions, wallets, usage, ledger
--   config      settings (configuration overrides)
-- public keeps only the shared set_updated_at() function and extensions.
-- The admin portal's own tables live in admin.* (Omni.Admin repo).
-- Always schema-qualify table names (core.clients, crm.contacts, ...).
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
    ('clients', 'core'),
    ('services', 'core'),
    ('users', 'identity'),
    ('user_claims', 'identity'),
    ('user_logins', 'identity'),
    ('user_tokens', 'identity'),
    ('client_users', 'identity'),
    ('contacts', 'crm'),
    ('conversations', 'crm'),
    ('messages', 'crm'),
    ('bookings', 'scheduling'),
    ('orders', 'crm'),
    ('availability_rules', 'scheduling'),
    ('availability_exceptions', 'scheduling'),
    ('booking_settings', 'scheduling'),
    ('calendar_integrations', 'scheduling'),
    ('calendar_integration_calendars', 'scheduling'),
    ('booking_calendar_syncs', 'scheduling'),
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
-- 0. business types and their pipeline stages
-- Every client has one business type (clients.business_type_code). The type
-- supplies the words the dashboard uses (terminology: contact, member,
-- service, booking, order, client, singular and plural, per language), the
-- AI assistant's profile, and the stages a contact moves through. Code, reports
-- and the AI work with a stage's category; people see its label. Edited in the
-- admin portal (/api/platform-admin/business-types).
--   name         {"en": "Salon", "ar": "...", "fr": "..."}
--   terminology  {"en": {"contact": {"one": "contact", "other": "contacts"}, ...}, "ar": {...}}
--   labels       {"en": "Booked", "ar": "...", "fr": "..."}
-- English is required everywhere; other languages fall back to it.
-- ---------------------------------------------------------------------
create table if not exists core.business_types (
  code         varchar(50) primary key,
  name         jsonb not null default '{}'::jsonb,
  terminology  jsonb not null default '{}'::jsonb,
  ai_profile   text not null default '',
  is_active    boolean not null default true,
  sort_order   int not null default 0,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  constraint ck_business_types_code check (code ~ '^[a-z][a-z0-9_]{1,49}$')
);

drop trigger if exists trg_business_types_updated_at on core.business_types;
create trigger trg_business_types_updated_at
  before update on core.business_types
  for each row execute function set_updated_at();

create table if not exists crm.pipeline_stages (
  business_type_code  varchar(50) not null references core.business_types(code) on delete cascade,
  key                 varchar(50) not null,
  category            varchar(30) not null,
  labels              jsonb not null default '{}'::jsonb,
  sort_order          int not null default 0,
  created_at          timestamptz not null default now(),
  updated_at          timestamptz not null default now(),
  constraint pk_pipeline_stages primary key (business_type_code, key),
  constraint ck_pipeline_stages_key check (key ~ '^[a-z][a-z0-9_]{0,49}$'),
  constraint ck_pipeline_stages_category check (category in (
    'open','booked','attended','no_show','converted','closed_lost','needs_human'
  ))
);

drop trigger if exists trg_pipeline_stages_updated_at on crm.pipeline_stages;
create trigger trg_pipeline_stages_updated_at
  before update on crm.pipeline_stages
  for each row execute function set_updated_at();

-- The two built-in types, seeded once: a type that already exists is left exactly as the admin portal left it
-- (re-running this file never brings back a stage or a word someone changed). The ai_profile texts are filled just below
-- (from a placeholder, so an edited profile is never replaced), then the Arabic and French words, only where missing.
-- leftover-ok-begin: the built-in clinic business type (its stage keys, labels and words are clinic data)
do $$
begin
  if not exists (select 1 from core.business_types where code = 'clinic') then
    insert into core.business_types (code, name, terminology, ai_profile, sort_order) values (
      'clinic',
      '{"en": "Clinic"}',
      '{"en": {
         "contact": {"one": "patient", "other": "patients"},
         "member":  {"one": "team member", "other": "team members"},
         "service": {"one": "procedure", "other": "procedures"},
         "booking": {"one": "consultation", "other": "consultations"},
         "order":   {"one": "procedure booking", "other": "procedure bookings"},
         "client":  {"one": "clinic", "other": "clinics"}}}',
      'Placeholder: filled by the update below.',
      10);
    insert into crm.pipeline_stages (business_type_code, key, category, labels, sort_order) values
      ('clinic', 'new',                   'open',        '{"en": "New"}',                    10),
      ('clinic', 'contacted',             'open',        '{"en": "Contacted"}',              20),
      ('clinic', 'qualified',             'open',        '{"en": "Qualified"}',              30),
      ('clinic', 'consultation_booked',   'booked',      '{"en": "Consultation booked"}',    40),
      ('clinic', 'consultation_attended', 'attended',    '{"en": "Consultation attended"}',  50),
      ('clinic', 'no_show',               'no_show',     '{"en": "No-show"}',                60),
      ('clinic', 'surgery_booked',        'converted',   '{"en": "Procedure booked"}',       70),
      ('clinic', 'not_interested',        'closed_lost', '{"en": "Not interested"}',         80),
      ('clinic', 'lost',                  'closed_lost', '{"en": "Lost"}',                   90),
      ('clinic', 'needs_human',           'needs_human', '{"en": "Needs human"}',           100),
      ('clinic', 'medical_question',      'needs_human', '{"en": "Medical question"}',      110);
  end if;
end $$;
-- leftover-ok-end

do $$
begin
  if not exists (select 1 from core.business_types where code = 'general') then
    insert into core.business_types (code, name, terminology, ai_profile, sort_order) values (
      'general',
      '{"en": "General business"}',
      '{"en": {
         "contact": {"one": "contact", "other": "contacts"},
         "member":  {"one": "member", "other": "members"},
         "service": {"one": "service", "other": "services"},
         "booking": {"one": "booking", "other": "bookings"},
         "order":   {"one": "order", "other": "orders"},
         "client":  {"one": "business", "other": "businesses"}}}',
      'Placeholder: filled by the update below.',
      20);
    insert into crm.pipeline_stages (business_type_code, key, category, labels, sort_order) values
      ('general', 'new',            'open',        '{"en": "New"}',            10),
      ('general', 'contacted',      'open',        '{"en": "Contacted"}',      20),
      ('general', 'qualified',      'open',        '{"en": "Qualified"}',      30),
      ('general', 'booked',         'booked',      '{"en": "Booked"}',         40),
      ('general', 'attended',       'attended',    '{"en": "Attended"}',       50),
      ('general', 'no_show',        'no_show',     '{"en": "No-show"}',        60),
      ('general', 'customer',       'converted',   '{"en": "Customer"}',       70),
      ('general', 'not_interested', 'closed_lost', '{"en": "Not interested"}', 80),
      ('general', 'lost',           'closed_lost', '{"en": "Lost"}',           90),
      ('general', 'needs_human',    'needs_human', '{"en": "Needs human"}',   100);
  end if;
end $$;

-- The AI profile of each built-in type: the business-specific part of the n8n AI assistant's system prompt (the
-- workflow's own prompt is generic and reads this from the AI trigger, see docs/system-design.html §7). Written only
-- over the placeholder, so a profile edited in the admin portal stays as it is.
-- leftover-ok-begin: the built-in clinic business type's AI profile (clinic data, moved from the n8n prompt)
update core.business_types set ai_profile =
'You are the virtual receptionist of a plastic surgery and aesthetic clinic. The people who write to you are patients or prospective patients asking about procedures, consultations, prices, financing, preparation, recovery and the clinic itself.

Clinic information:
- Clinic facts (hours, address, parking, policies, prices, financing, doctors, procedure details, preparation, recovery and FAQs) come only from search_knowledge. Never invent clinic information.
- Keep the qualifiers the clinic uses, such as "approximately", "starting from", "estimated", "typically" or "depending on complexity".

Medical safety:
- Do not diagnose, prescribe, determine suitability, change medication, override surgeon instructions, or give personalized clinical judgment.
- Personalized clinical questions (for example about swelling, pain or whether a procedure suits the patient) use action=handoff, with a reason saying it is a medical question.
- For apparent emergencies, tell the patient to seek urgent medical attention or local emergency services immediately.'
where code = 'clinic' and ai_profile like 'Placeholder:%';
-- leftover-ok-end

update core.business_types set ai_profile =
'You are the virtual assistant of this business. The people who write to you are customers or prospective customers asking about its services, prices, opening hours and bookings.

Business information:
- Facts about the business come only from search_knowledge. Never invent them.
- Keep qualifiers such as "approximately", "starting from" or "depending on the case".

Hand off to a team member (action=handoff) when someone asks for a person, makes a complaint, asks for a refund or an exception to a policy, or needs advice that depends on their personal situation.'
where code = 'general' and ai_profile like 'Placeholder:%';

-- Arabic and French words of the built-in types (Milestone B; the vocabulary is in docs/i18n/glossary.md). Merged so that
-- whatever is already there wins: a name, a term or a stage label edited in the admin portal stays as it is when this
-- file runs again; only a language (or a term) that is missing is filled in.
-- leftover-ok-begin: the built-in clinic business type's words and stage keys (clinic data)
with words(code, name, terms) as (values
  ('clinic',
   '{"ar": "عيادة", "fr": "Clinique"}'::jsonb,
   '{"ar": {
       "contact": {"one": "مريض", "other": "مرضى"},
       "member":  {"one": "عضو الفريق", "other": "أعضاء الفريق"},
       "service": {"one": "إجراء", "other": "إجراءات"},
       "booking": {"one": "استشارة", "other": "استشارات"},
       "order":   {"one": "حجز إجراء", "other": "حجوزات إجراءات"},
       "client":  {"one": "عيادة", "other": "عيادات"}},
     "fr": {
       "contact": {"one": "patient", "other": "patients"},
       "member":  {"one": "membre de l’équipe", "other": "membres de l’équipe"},
       "service": {"one": "intervention", "other": "interventions"},
       "booking": {"one": "consultation", "other": "consultations"},
       "order":   {"one": "réservation d’intervention", "other": "réservations d’intervention"},
       "client":  {"one": "clinique", "other": "cliniques"}}}'::jsonb),
  ('general',
   '{"ar": "نشاط تجاري عام", "fr": "Activité générale"}'::jsonb,
   '{"ar": {
       "contact": {"one": "جهة اتصال", "other": "جهات اتصال"},
       "member":  {"one": "عضو", "other": "أعضاء"},
       "service": {"one": "خدمة", "other": "خدمات"},
       "booking": {"one": "حجز", "other": "حجوزات"},
       "order":   {"one": "طلب", "other": "طلبات"},
       "client":  {"one": "نشاط تجاري", "other": "أنشطة تجارية"}},
     "fr": {
       "contact": {"one": "contact", "other": "contacts"},
       "member":  {"one": "membre", "other": "membres"},
       "service": {"one": "service", "other": "services"},
       "booking": {"one": "réservation", "other": "réservations"},
       "order":   {"one": "commande", "other": "commandes"},
       "client":  {"one": "entreprise", "other": "entreprises"}}}'::jsonb)
),
merged as (
  select t.code,
         w.name || t.name as name,
         t.terminology || jsonb_build_object(
           'ar', (w.terms -> 'ar') || coalesce(t.terminology -> 'ar', '{}'::jsonb),
           'fr', (w.terms -> 'fr') || coalesce(t.terminology -> 'fr', '{}'::jsonb)) as terminology
  from core.business_types t
  join words w on w.code = t.code
)
update core.business_types t set name = m.name, terminology = m.terminology
from merged m
where t.code = m.code and (t.name <> m.name or t.terminology <> m.terminology);

with words(business_type_code, key, labels) as (values
  ('clinic',  'new',                   '{"ar": "جديد", "fr": "Nouveau"}'::jsonb),
  ('clinic',  'contacted',             '{"ar": "تم التواصل", "fr": "Contacté"}'::jsonb),
  ('clinic',  'qualified',             '{"ar": "مؤهل", "fr": "Qualifié"}'::jsonb),
  ('clinic',  'consultation_booked',   '{"ar": "استشارة محجوزة", "fr": "Consultation réservée"}'::jsonb),
  ('clinic',  'consultation_attended', '{"ar": "حضر الاستشارة", "fr": "Consultation effectuée"}'::jsonb),
  ('clinic',  'no_show',               '{"ar": "لم يحضر", "fr": "Absent"}'::jsonb),
  ('clinic',  'surgery_booked',        '{"ar": "إجراء محجوز", "fr": "Intervention réservée"}'::jsonb),
  ('clinic',  'not_interested',        '{"ar": "غير مهتم", "fr": "Pas intéressé"}'::jsonb),
  ('clinic',  'lost',                  '{"ar": "مفقود", "fr": "Perdu"}'::jsonb),
  ('clinic',  'needs_human',           '{"ar": "يحتاج إلى موظف", "fr": "Humain requis"}'::jsonb),
  ('clinic',  'medical_question',      '{"ar": "سؤال طبي", "fr": "Question médicale"}'::jsonb),
  ('general', 'new',                   '{"ar": "جديد", "fr": "Nouveau"}'::jsonb),
  ('general', 'contacted',             '{"ar": "تم التواصل", "fr": "Contacté"}'::jsonb),
  ('general', 'qualified',             '{"ar": "مؤهل", "fr": "Qualifié"}'::jsonb),
  ('general', 'booked',                '{"ar": "محجوز", "fr": "Réservé"}'::jsonb),
  ('general', 'attended',              '{"ar": "حضر", "fr": "Présent"}'::jsonb),
  ('general', 'no_show',               '{"ar": "لم يحضر", "fr": "Absent"}'::jsonb),
  ('general', 'customer',              '{"ar": "عميل", "fr": "Client"}'::jsonb),
  ('general', 'not_interested',        '{"ar": "غير مهتم", "fr": "Pas intéressé"}'::jsonb),
  ('general', 'lost',                  '{"ar": "مفقود", "fr": "Perdu"}'::jsonb),
  ('general', 'needs_human',           '{"ar": "يحتاج إلى موظف", "fr": "Humain requis"}'::jsonb)
)
update crm.pipeline_stages s set labels = w.labels || s.labels
from words w
where s.business_type_code = w.business_type_code and s.key = w.key and w.labels || s.labels <> s.labels;
-- leftover-ok-end

-- ---------------------------------------------------------------------
-- 1. clients
-- ---------------------------------------------------------------------
create table if not exists core.clients (
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
  booking_info  text,
  business_type_code varchar(50) not null constraint fk_clients_business_type references core.business_types(code),
  default_language   varchar(10) not null default 'en',
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  constraint ck_clients_default_language check (default_language in ('en','ar','fr'))
);

drop trigger if exists trg_clients_updated_at on core.clients;
create trigger trg_clients_updated_at
  before update on core.clients
  for each row execute function set_updated_at();

-- Clients created before business types existed get the first built-in type. The default only fills those rows; it is dropped
-- right after, so every new client names its type (Register, Google sign-up).
-- leftover-ok-begin: existing clients become the built-in clinic type
alter table core.clients add column if not exists business_type_code varchar(50) not null default 'clinic'
  constraint fk_clients_business_type references core.business_types(code);
-- leftover-ok-end
alter table core.clients alter column business_type_code drop default;
alter table core.clients add column if not exists default_language varchar(10) not null default 'en';
do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'ck_clients_default_language') then
    alter table core.clients add constraint ck_clients_default_language check (default_language in ('en','ar','fr'));
  end if;
end $$;
create index if not exists ix_clients_business_type_code on core.clients(business_type_code);

-- ---------------------------------------------------------------------
-- 2. services  (catalog of services a client offers)
-- ---------------------------------------------------------------------
create table if not exists core.services (
  id                     uuid primary key default gen_random_uuid(),
  client_id              uuid not null references core.clients(id) on delete cascade,
  name                   varchar(200) not null,
  code                   varchar(100),
  description            text,
  booking_duration  integer,
  is_active              boolean not null default true,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_services_client_id on core.services(client_id);

drop trigger if exists trg_services_updated_at on core.services;
create trigger trg_services_updated_at
  before update on core.services
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 3. contacts
-- ---------------------------------------------------------------------
create table if not exists crm.contacts (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,
  service_id            uuid references core.services(id) on delete set null,

  full_name             varchar(200),
  first_name            varchar(100),
  last_name             varchar(100),
  phone                 varchar(50),   -- store normalized E.164 where possible
  email                 varchar(200),

  source                varchar(100),
  source_detail         varchar(200),
  campaign_name         varchar(200),
  external_contact_id   varchar(200),  -- id from Meta/Instagram/website form, used for dedup

  -- A stage key of the client's business type (crm.pipeline_stages). Types are edited in the admin portal, so the
  -- allowed keys are checked in code (new contacts start at the type's first open stage), not by a check constraint.
  stage_key             varchar(50) not null,
  qualification         varchar(50) not null default 'unknown',

  preferred_language    varchar(20),   -- two-letter ISO 639-1 code (ck_contacts_preferred_language below)
  country_code          varchar(10),
  city                  varchar(100),
  desired_timeline      varchar(100),
  notes                 text,

  assigned_user_id     text,             -- identity.users.id (not assigned by any feature yet)

  -- consent / opt-out (Development Principle 9: build this in early)
  marketing_opt_in      boolean not null default true,
  opted_out_at          timestamptz,

  last_contact_at       timestamptz,
  next_followup_at      timestamptz,

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_contacts_qualification check (qualification in (
    'unknown','hot','warm','cold','needs_human','spam'
  ))
);

-- Dedup: the same external contact (e.g. the same Meta form entry) should not be inserted twice per client.
create unique index if not exists ux_contacts_client_external_contact_id
  on crm.contacts(client_id, external_contact_id)
  where external_contact_id is not null;

create index if not exists ix_contacts_client_id on crm.contacts(client_id);
create index if not exists ix_contacts_client_phone on crm.contacts(client_id, phone);
create index if not exists ix_contacts_client_email on crm.contacts(client_id, email);
create index if not exists ix_contacts_client_stage_key on crm.contacts(client_id, stage_key);
create index if not exists ix_contacts_service_id on crm.contacts(service_id);
create index if not exists ix_contacts_created_at on crm.contacts(created_at);
create index if not exists ix_contacts_next_followup_at on crm.contacts(next_followup_at) where next_followup_at is not null;

drop trigger if exists trg_contacts_updated_at on crm.contacts;
create trigger trg_contacts_updated_at
  before update on crm.contacts
  for each row execute function set_updated_at();

-- The language a contact writes in: a two-letter ISO 639-1 code or null (Milestone B). crm.normalize_language applies the
-- rules of Common/Helpers/ContactLanguage.cs (names such as "Arabic", "Arabizi" or "français", three-letter codes, tags
-- such as "fr-CA"); ContactLanguageTests checks that both give the same answer. Existing free text is normalized once,
-- before the check is added.
create or replace function crm.normalize_language(raw text) returns varchar(2)
language sql immutable as $$
  with t as (
    select lower(regexp_replace(regexp_replace(replace(coalesce(raw, ''), '_', '-'), '\s+', ' ', 'g'), '^ | $', '', 'g')) as s
  )
  select coalesce(
    (select n.code from (values
      ('arabic', 'ar'), ('arabe', 'ar'), ('ara', 'ar'), ('arabizi', 'ar'), ('arabeezi', 'ar'), ('franco', 'ar'),
      ('franco-arabic', 'ar'), ('franco arabic', 'ar'), ('lebanese', 'ar'), ('lebanese arabic', 'ar'),
      ('levantine', 'ar'), ('عربي', 'ar'), ('عربية', 'ar'), ('العربية', 'ar'), ('لبناني', 'ar'), ('english', 'en'),
      ('anglais', 'en'), ('eng', 'en'), ('انجليزي', 'en'), ('إنجليزي', 'en'), ('الانجليزية', 'en'),
      ('الإنجليزية', 'en'), ('french', 'fr'), ('français', 'fr'), ('francais', 'fr'), ('fra', 'fr'), ('fre', 'fr'),
      ('فرنسي', 'fr'), ('الفرنسية', 'fr'), ('spanish', 'es'), ('español', 'es'), ('espanol', 'es'), ('spa', 'es'),
      ('german', 'de'), ('deutsch', 'de'), ('deu', 'de'), ('ger', 'de'), ('italian', 'it'), ('italiano', 'it'),
      ('ita', 'it'), ('portuguese', 'pt'), ('português', 'pt'), ('portugues', 'pt'), ('por', 'pt'),
      ('turkish', 'tr'), ('türkçe', 'tr'), ('turkce', 'tr'), ('tur', 'tr'), ('russian', 'ru'), ('русский', 'ru'),
      ('rus', 'ru'), ('armenian', 'hy'), ('hye', 'hy'), ('arm', 'hy'), ('persian', 'fa'), ('farsi', 'fa'),
      ('fas', 'fa'), ('per', 'fa'), ('kurdish', 'ku'), ('kur', 'ku'), ('hebrew', 'he'), ('heb', 'he'),
      ('greek', 'el'), ('ell', 'el'), ('gre', 'el'), ('dutch', 'nl'), ('nld', 'nl'), ('dut', 'nl'), ('urdu', 'ur'),
      ('urd', 'ur'), ('hindi', 'hi'), ('hin', 'hi'), ('chinese', 'zh'), ('mandarin', 'zh'), ('zho', 'zh'),
      ('chi', 'zh')
    ) as n(name, code) where n.name = t.s),
    case when t.s ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
          and split_part(t.s, '-', 1) = any (regexp_split_to_array(
    'aa ab ae af ak am an ar as av ay az ba be bg bh bi bm bn bo br bs ca ce ch co cr cs cu cv cy da de dv dz ee
    el en eo es et eu fa ff fi fj fo fr fy ga gd gl gn gu gv ha he hi ho hr ht hu hy hz ia id ie ig ii ik io is
    it iu ja jv ka kg ki kj kk kl km kn ko kr ks ku kv kw ky la lb lg li ln lo lt lu lv mg mh mi mk ml mn mr ms
    mt my na nb nd ne ng nl nn no nr nv ny oc oj om or os pa pi pl ps pt qu rm rn ro ru rw sa sc sd se sg si sk
    sl sm sn so sq sr ss st su sv sw ta te tg th ti tk tl tn to tr ts tt tw ty ug uk ur uz ve vi vo wa wo xh yi
    yo za zh zu', '\s+'))
      then split_part(t.s, '-', 1) end)::varchar(2)
  from t
$$;

do $$
begin
  if not exists (select 1 from pg_constraint where conname = 'ck_contacts_preferred_language') then
    update crm.contacts set preferred_language = crm.normalize_language(preferred_language)
    where preferred_language is distinct from crm.normalize_language(preferred_language);
    alter table crm.contacts add constraint ck_contacts_preferred_language check (preferred_language ~ '^[a-z]{2}$');
  end if;
end $$;

-- ---------------------------------------------------------------------
-- 4. conversations
-- ---------------------------------------------------------------------
create table if not exists crm.conversations (
  id                      uuid primary key default gen_random_uuid(),
  client_id               uuid not null references core.clients(id) on delete cascade,
  contact_id                 uuid not null references crm.contacts(id) on delete cascade,
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

create index if not exists ix_conversations_client_id on crm.conversations(client_id);
create index if not exists ix_conversations_contact_id on crm.conversations(contact_id);

drop trigger if exists trg_conversations_updated_at on crm.conversations;
create trigger trg_conversations_updated_at
  before update on crm.conversations
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 5. messages
-- ---------------------------------------------------------------------
create table if not exists crm.messages (
  id                   uuid primary key default gen_random_uuid(),
  client_id            uuid not null references core.clients(id) on delete cascade,
  conversation_id      uuid not null references crm.conversations(id) on delete cascade,
  contact_id              uuid not null references crm.contacts(id) on delete cascade,

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
  constraint ck_messages_sender_type check (sender_type in ('contact','ai','member','system'))
);

create index if not exists ix_messages_client_id on crm.messages(client_id);
create index if not exists ix_messages_conversation_id on crm.messages(conversation_id);
create index if not exists ix_messages_contact_id on crm.messages(contact_id);
create index if not exists ix_messages_created_at on crm.messages(created_at);

-- ---------------------------------------------------------------------
-- 6. bookings
-- ---------------------------------------------------------------------
create table if not exists scheduling.bookings (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,
  contact_id               uuid not null references crm.contacts(id) on delete cascade,
  service_id            uuid references core.services(id) on delete set null,

  booking_type      varchar(50) not null default 'standard',
  status                varchar(50) not null default 'booked',
  scheduled_start       timestamptz not null,
  scheduled_end         timestamptz,
  location_type         varchar(30),
  location_name         varchar(200),
  external_calendar_id  varchar(200),
  external_event_id     varchar(200),
  assigned_user_id     text,             -- identity.users.id (not assigned by any feature yet)
  notes                 text,

  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),

  constraint ck_bookings_status check (status in (
    'booked','confirmed','attended','no_show','canceled','rescheduled'
  ))
);

create index if not exists ix_bookings_client_id on scheduling.bookings(client_id);
create index if not exists ix_bookings_contact_id on scheduling.bookings(contact_id);
create index if not exists ix_bookings_scheduled_start on scheduling.bookings(scheduled_start);
create index if not exists ix_bookings_status on scheduling.bookings(status);

drop trigger if exists trg_bookings_updated_at on scheduling.bookings;
create trigger trg_bookings_updated_at
  before update on scheduling.bookings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 7. orders  (a contact buying a service: quoted, deposit paid, confirmed, completed)
-- ---------------------------------------------------------------------
create table if not exists crm.orders (
  id                uuid primary key default gen_random_uuid(),
  client_id         uuid not null references core.clients(id) on delete cascade,
  contact_id           uuid not null references crm.contacts(id) on delete cascade,
  service_id        uuid not null references core.services(id) on delete restrict,
  booking_id    uuid references scheduling.bookings(id) on delete set null,

  status            varchar(50) not null default 'considering',
  quoted_amount     numeric(12,2),
  deposit_amount    numeric(12,2),
  final_amount      numeric(12,2),
  currency_code     varchar(3),
  service_date      timestamptz,
  notes             text,

  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),

  constraint ck_orders_status check (status in (
    'considering','quoted','deposit_paid','confirmed','completed','canceled','lost'
  ))
);

create index if not exists ix_orders_client_id on crm.orders(client_id);
create index if not exists ix_orders_contact_id on crm.orders(contact_id);
create index if not exists ix_orders_status on crm.orders(status);

drop trigger if exists trg_orders_updated_at on crm.orders;
create trigger trg_orders_updated_at
  before update on crm.orders
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- 8. events  (audit / analytics event log)
-- ---------------------------------------------------------------------
create table if not exists activity.events (
  id               uuid primary key default gen_random_uuid(),
  client_id        uuid not null references core.clients(id) on delete cascade,
  contact_id          uuid references crm.contacts(id) on delete set null,
  conversation_id  uuid references crm.conversations(id) on delete set null,
  booking_id   uuid references scheduling.bookings(id) on delete set null,
  event_type       varchar(100) not null,
  source            varchar(50),
  metadata         jsonb not null default '{}'::jsonb,
  created_at       timestamptz not null default now()
);

create index if not exists ix_events_client_id on activity.events(client_id);
create index if not exists ix_events_event_type on activity.events(event_type);
create index if not exists ix_events_created_at on activity.events(created_at);

-- ---------------------------------------------------------------------
-- 9. channel_integrations  (per-client WhatsApp / Instagram / Facebook connection config —
--    Settings → Channels & Integrations in the dashboard)
-- ---------------------------------------------------------------------
create table if not exists channels.channel_integrations (
  id                     uuid primary key default gen_random_uuid(),
  client_id              uuid not null references core.clients(id) on delete cascade,

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
  -- encrypted column / secrets manager before connecting a real app or real contact data.
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

create unique index if not exists ux_channel_integrations_client_channel
  on channels.channel_integrations(client_id, channel);

drop trigger if exists trg_channel_integrations_updated_at on channels.channel_integrations;
create trigger trg_channel_integrations_updated_at
  before update on channels.channel_integrations
  for each row execute function set_updated_at();

-- =====================================================================
-- End of MVP schema (9 tables). LATER tables (members, campaigns,
-- followups, payments, client_settings, knowledge_documents,
-- automation_runs, audit_logs) are intentionally not created yet —
-- see the plan's "LATER TABLES" section.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Inbox: message origin + conversation mode
--
-- origin tells the Inbox UI (and the AI-collision guard) which system actually produced a
-- message — a "members" sender could be replying from the dashboard or directly from the WhatsApp
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
-- two rows. Scoped by client + channel since external IDs are only unique within one provider.
create unique index if not exists ux_messages_client_channel_external_message_id
  on crm.messages(client_id, channel, external_message_id)
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
-- updated ONLY when a genuine inbound whatsapp_customer message is ingested (never by members, AI,
-- or campaign sends — see MessageService.IngestAsync) — see Conversation.IsServiceWindowOpen().
-- ---------------------------------------------------------------------

alter table crm.conversations add column if not exists last_customer_message_at timestamptz;
alter table crm.conversations add column if not exists service_window_expires_at timestamptz;
create index if not exists ix_conversations_service_window on crm.conversations(service_window_expires_at);

-- messages: allow the new 'campaign' origin, and link a message back to whichever
-- template/campaign produced it (all nullable — most messages have none of these).
-- Lists every origin added later too ('telegram_customer'), so a re-run over existing rows passes.
alter table crm.messages drop constraint if exists ck_messages_origin;
alter table crm.messages add constraint ck_messages_origin check (origin in (
  'whatsapp_customer','whatsapp_business_app','dashboard','ai','system','campaign','telegram_customer'
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
  client_id          uuid not null references core.clients(id) on delete cascade,

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

create index if not exists ix_whatsapp_templates_client_id on channels.whatsapp_templates(client_id);
-- Meta's own uniqueness model: one template name is one thing per language per WABA (client, here).
create unique index if not exists ux_whatsapp_templates_client_name_language
  on channels.whatsapp_templates(client_id, name, language);

drop trigger if exists trg_whatsapp_templates_updated_at on channels.whatsapp_templates;
create trigger trg_whatsapp_templates_updated_at
  before update on channels.whatsapp_templates
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaigns
-- ---------------------------------------------------------------------
create table if not exists marketing.campaigns (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,
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

create index if not exists ix_campaigns_client_id on marketing.campaigns(client_id);
create index if not exists ix_campaigns_status on marketing.campaigns(status);
create index if not exists ix_campaigns_scheduled_at on marketing.campaigns(scheduled_at) where scheduled_at is not null;
create index if not exists ix_campaigns_whatsapp_template_id on marketing.campaigns(whatsapp_template_id);

drop trigger if exists trg_campaigns_updated_at on marketing.campaigns;
create trigger trg_campaigns_updated_at
  before update on marketing.campaigns
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- campaign_recipients — one row per target contact. Every actual send still produces a normal row in
-- messages (message_id here points to it) — this table is reporting/lifecycle state, not a second
-- message store.
-- ---------------------------------------------------------------------
create table if not exists marketing.campaign_recipients (
  id                uuid primary key default gen_random_uuid(),
  client_id         uuid not null references core.clients(id) on delete cascade,
  campaign_id       uuid not null references marketing.campaigns(id) on delete cascade,
  contact_id           uuid not null references crm.contacts(id) on delete cascade,
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

create index if not exists ix_campaign_recipients_client_id on marketing.campaign_recipients(client_id);
create index if not exists ix_campaign_recipients_campaign_id on marketing.campaign_recipients(campaign_id);
create index if not exists ix_campaign_recipients_contact_id on marketing.campaign_recipients(contact_id);
create index if not exists ix_campaign_recipients_status on marketing.campaign_recipients(status);
-- Prevent the same contact being inserted into the same campaign twice.
create unique index if not exists ux_campaign_recipients_campaign_contact
  on marketing.campaign_recipients(campaign_id, contact_id);

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
-- AI assistant tool surface (Controllers/AiController.cs) — clients gains the free-text fields
-- get_client_info reads. No new tables: booking reschedule reuses scheduled_start/end,
-- and cancel/handoff reuse existing status/mode columns.
-- ---------------------------------------------------------------------
alter table core.clients add column if not exists address text;
alter table core.clients add column if not exists operating_hours text;
alter table core.clients add column if not exists booking_info text;

-- WhatsApp phone number registration (Cloud API requires this after Embedded Signup — see
-- ChannelIntegrationService.ConnectWhatsAppAsync / MetaGraphClient.RegisterPhoneNumberAsync).
alter table channels.channel_integrations add column if not exists pin varchar(10);

-- =====================================================================
-- WhatsApp webhook event routing: Inbox delivery states, Templates, Health
-- (n8n forwards normalized Meta webhook events here — see Controllers/AiController.cs's sibling
-- Controllers/WhatsAppIntegrationEventsController.cs). PostgreSQL stays authoritative; SignalR only
-- notifies. channel_integrations plays the role of "whatsapp_connections" (already one row per
-- client per channel) rather than a separate table duplicating the same WABA/phone_number_id.
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
-- than one client (webhook routing: phone_number_id -> channel_integration -> client_id depends on
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
-- Meta template ids aren't globally unique across clients by themselves, but scoped by client they
-- are — this is the upsert key ApplyMetaEventAsync uses (not unique, since Meta sends null template
-- ids for pure category-limit updates that still need to land somewhere findable by name+language).
create index if not exists ix_whatsapp_templates_client_meta_template_id
  on channels.whatsapp_templates(client_id, meta_template_id) where meta_template_id is not null;

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
  client_id                uuid not null references core.clients(id) on delete cascade,
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

create index if not exists ix_whatsapp_health_events_client_id on channels.whatsapp_health_events(client_id);
create index if not exists ix_whatsapp_health_events_channel_integration_id on channels.whatsapp_health_events(channel_integration_id);
create index if not exists ix_whatsapp_health_events_occurred_at on channels.whatsapp_health_events(occurred_at);
-- Practical idempotency for webhook retries: Meta doesn't hand these a stable event id, so dedupe
-- on (connection, event type, occurred_at) — see WhatsAppHealthService.ApplyHealthEventAsync.
create unique index if not exists ux_whatsapp_health_events_connection_type_occurred
  on channels.whatsapp_health_events(channel_integration_id, event_type, occurred_at);

-- =====================================================================
-- Users (ASP.NET Core Identity) + client_users
--
-- One users table for everyone who signs in. users.role says which app a user may use:
--   admin   the admin portal only (it signs in through POST /api/platform-admin/auth/login); no client
--   client  the main app; the person who signed the client up, manages its members
--   member  the main app; added by a client user, uses everything except member management
-- The main app checks the role, users.is_active and an active client_users row on every request
-- (cookie validation), not only at login. Contacts never sign in.
--
-- The tables keep ASP.NET Core Identity's IdentityUserContext shape (no role tables: the role is a
-- column), in snake_case (ApplicationDbContext maps them onto AppUser). users.id is a string (a GUID
-- the app generates), so client_users.user_id is text. Lockout uses Identity's own fields
-- (lockout_end, access_failed_count): 5 failed sign-ins lock an account for 15 minutes.
-- =====================================================================

create table if not exists identity.users (
  id                          text primary key,
  role                        varchar(20) not null,
  full_name                   text,
  language                    varchar(5),
  is_active                   boolean not null default true,
  last_login_at               timestamptz,
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
  access_failed_count         integer not null default 0,
  created_at                  timestamptz not null default now(),
  updated_at                  timestamptz not null default now(),

  constraint ck_users_role check (role in ('admin','client','member')),
  constraint ck_users_language check (language is null or language in ('en','ar','fr'))
);

create unique index if not exists ux_users_normalized_user_name on identity.users(normalized_user_name);
create index if not exists ix_users_normalized_email on identity.users(normalized_email);
create index if not exists ix_users_role on identity.users(role);

drop trigger if exists trg_users_updated_at on identity.users;
create trigger trg_users_updated_at
  before update on identity.users
  for each row execute function set_updated_at();

create table if not exists identity.user_claims (
  id            integer generated always as identity primary key,
  user_id       text not null references identity.users(id) on delete cascade,
  claim_type    text,
  claim_value   text
);

create index if not exists ix_user_claims_user_id on identity.user_claims(user_id);

create table if not exists identity.user_logins (
  login_provider          varchar(128) not null,
  provider_key             varchar(128) not null,
  provider_display_name   text,
  user_id                  text not null references identity.users(id) on delete cascade,

  primary key (login_provider, provider_key)
);

create index if not exists ix_user_logins_user_id on identity.user_logins(user_id);

create table if not exists identity.user_tokens (
  user_id          text not null references identity.users(id) on delete cascade,
  login_provider   varchar(128) not null,
  name             varchar(128) not null,
  value            text,

  primary key (user_id, login_provider, name)
);

-- ---------------------------------------------------------------------
-- client_users: links a client or member user to their client (admins have none). A user has at
-- most one ACTIVE membership; that is a rule in code (MemberService and the platform-admin members
-- API check it before activating one), not a unique index, so a user may later belong to several
-- clients (agencies). The current client is the user's active membership.
-- ---------------------------------------------------------------------
create table if not exists identity.client_users (
  id            uuid primary key default gen_random_uuid(),
  client_id     uuid not null references core.clients(id) on delete cascade,
  user_id       text not null references identity.users(id) on delete cascade,
  is_active     boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create unique index if not exists ux_client_users_client_user on identity.client_users(client_id, user_id);
drop index if exists identity.ux_client_users_user_active;
create index if not exists ix_client_users_user_id on identity.client_users(user_id);

drop trigger if exists trg_client_users_updated_at on identity.client_users;
create trigger trg_client_users_updated_at
  before update on identity.client_users
  for each row execute function set_updated_at();

-- =====================================================================
-- Campaigns + Old Contact Reactivation — data model
-- campaigns/campaign_recipients already existed (WhatsApp Templates & Campaigns, above). This adds
-- what "old contact reactivation" needs: how a campaign's audience was chosen (audience_type /
-- audience_filters, so "all eligible" / "reactivation, never booked" / "custom filter" are
-- first-class instead of always requiring an explicit contact list) and reply/booking attribution on
-- campaign_recipients. See Services/ICampaignAudienceService.cs for how eligibility is computed —
-- deliberately NOT a stored contact.is_old_contact / incomplete_booking flag; derived live from
-- contacts/conversations/bookings every time a campaign is created.
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
    alter table marketing.campaigns add constraint ck_campaigns_audience_type check (audience_type in ('all_eligible','reactivation_no_booking','custom'));
  end if;
end $$;

-- campaign_type is deliberately NOT constrained (like contacts.source) — new campaign types can be
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
alter table marketing.campaign_recipients add column if not exists booking_id uuid;
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
  if not exists (select 1 from pg_constraint where conname = 'fk_campaign_recipients_booking_id') then
    alter table marketing.campaign_recipients add constraint fk_campaign_recipients_booking_id
      foreign key (booking_id) references scheduling.bookings(id) on delete set null;
  end if;
end $$;

alter table marketing.campaign_recipients drop constraint if exists ck_campaign_recipients_status;
alter table marketing.campaign_recipients add constraint ck_campaign_recipients_status
  check (status in ('pending','queued','sent','delivered','read','replied','booked','failed','skipped'));

create index if not exists ix_campaign_recipients_external_message_id
  on marketing.campaign_recipients(external_message_id) where external_message_id is not null;
create index if not exists ix_campaign_recipients_booking_id
  on marketing.campaign_recipients(booking_id) where booking_id is not null;
-- ux_campaign_recipients_campaign_contact (UNIQUE(campaign_id, contact_id)) already exists from the
-- original campaign_recipients table above — no change needed for the "no duplicate contact in the
-- same campaign" requirement.


-- =====================================================================
-- Client Knowledge Base — knowledge_documents (what members type in) + knowledge_chunks (the
-- embedded slices the AI assistant searches semantically). Everything is scoped by client_id; every
-- search filters on it. See Services/IKnowledgeService.cs and IKnowledgeSearchService.cs.
-- =====================================================================
create extension if not exists vector;

create table if not exists knowledge.knowledge_documents (
  id          uuid primary key default gen_random_uuid(),
  client_id   uuid not null references core.clients(id) on delete cascade,
  title       varchar(200) not null,
  -- Extensible string (general, faq, policy, doctor, service, pricing, payment, booking,
  -- preparation, recovery, ...) — deliberately no CHECK, like campaigns.campaign_type.
  category    varchar(50) not null default 'general',
  content     text not null,
  is_active   boolean not null default true,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create index if not exists ix_knowledge_documents_client_id on knowledge.knowledge_documents(client_id);
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
  client_id              uuid not null references core.clients(id) on delete cascade,
  knowledge_document_id  uuid not null references knowledge.knowledge_documents(id) on delete cascade,
  chunk_index            integer not null,
  content                text not null,
  embedding              vector(1536) not null,
  created_at             timestamptz not null default now(),
  updated_at             timestamptz not null default now()
);

create index if not exists ix_knowledge_chunks_client_id on knowledge.knowledge_chunks(client_id);
create index if not exists ix_knowledge_chunks_knowledge_document_id on knowledge.knowledge_chunks(knowledge_document_id);
-- Deliberately no ANN (hnsw/ivfflat) index yet: every search is already narrowed to one client by
-- ix_knowledge_chunks_client_id first, and a client's Knowledge Base is hundreds of chunks, not
-- millions — an exact scan of that slice is fast and has perfect recall (an ANN index would apply the
-- client filter *after* its approximate scan and can drop results). Add
--   create index ... using hnsw (embedding vector_cosine_ops);
-- only if a single client's chunk count grows into the tens of thousands.

drop trigger if exists trg_knowledge_chunks_updated_at on knowledge.knowledge_chunks;
create trigger trg_knowledge_chunks_updated_at
  before update on knowledge.knowledge_chunks
  for each row execute function set_updated_at();


-- =====================================================================
-- Knowledge Base search settings — one row per client (unique client_id), created lazily by
-- IKnowledgeSettingsService with the system defaults. Members edit only chunk size/overlap, top_k and
-- minimum_similarity; embedding_model, vector_dimension, similarity_method and vector_index_type are
-- persisted for transparency but read-only in the UI (changing them means re-embedding and/or a
-- migration of knowledge_chunks.embedding). Secrets such as the embeddings API key are NEVER stored
-- here — they stay in configuration.
-- =====================================================================
create table if not exists knowledge.knowledge_search_settings (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,

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

create unique index if not exists ux_knowledge_search_settings_client_id on knowledge.knowledge_search_settings(client_id);

drop trigger if exists trg_knowledge_search_settings_updated_at on knowledge.knowledge_search_settings;
create trigger trg_knowledge_search_settings_updated_at
  before update on knowledge.knowledge_search_settings
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------------
-- Telegram (direct Bot API channel)
--
-- Reuses channel_integrations (one row per client+channel, unchanged):
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

-- One bot can have only one webhook, so one bot may be connected to only one client at a time
-- (a disconnected row keeps its bot id for display but no longer blocks another client).
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

-- A Telegram chat maps to exactly one conversation per client+channel (external_thread_id = chat.id).
-- Partial: WhatsApp/other conversations leave external_thread_id null.
create unique index if not exists ux_conversations_client_channel_thread
  on crm.conversations(client_id, channel, external_thread_id)
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
-- last_read_at = when members last opened the conversation (shared across the client's members). Unread =
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
--   knowledge_website_sources      one website configured as a KB source (per client)
--   knowledge_website_pages        every URL the crawler knows about, with its crawl state
--   knowledge_website_scrape_runs  one row per crawl (history / progress / debugging)
--
-- The crawler OWNS crawling, URL identity, fetching, text extraction, page state, runs and change
-- detection. The existing Knowledge Base owns content, chunking, embeddings and search: a successfully
-- extracted page becomes (or updates) a knowledge_documents row (source_type = 'website') and flows through
-- the SAME chunk -> embed -> knowledge_chunks pipeline as manual entries and uploads. NO vectors and no
-- extracted text live in the scraping tables (the text is knowledge_documents.content).
-- Everything is scoped by client_id; nothing is ever deduplicated across clients.
-- =====================================================================

-- knowledge_documents: 'website' as a source type + the page URL it came from.
alter table knowledge.knowledge_documents add column if not exists source_url varchar(2000);
alter table knowledge.knowledge_documents drop constraint if exists ck_knowledge_documents_source_type;
alter table knowledge.knowledge_documents add constraint ck_knowledge_documents_source_type
  check (source_type in ('manual','upload','website'));

create table if not exists knowledge.knowledge_website_sources (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,

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
-- The same website cannot be added twice for one client; two clients may each add it.
create unique index if not exists ux_kws_client_start_url on knowledge.knowledge_website_sources(client_id, normalized_start_url);
create index if not exists ix_kws_client_id on knowledge.knowledge_website_sources(client_id);

drop trigger if exists trg_kws_updated_at on knowledge.knowledge_website_sources;
create trigger trg_kws_updated_at before update on knowledge.knowledge_website_sources
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_website_pages (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,
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
create index if not exists ix_kwp_client_id on knowledge.knowledge_website_pages(client_id);
create index if not exists ix_kwp_source_status on knowledge.knowledge_website_pages(website_source_id, status);
create index if not exists ix_kwp_document_id on knowledge.knowledge_website_pages(knowledge_document_id) where knowledge_document_id is not null;
-- Deliberately NOT unique: identical text may legitimately exist on several pages / clients.
create index if not exists ix_kwp_source_content_hash on knowledge.knowledge_website_pages(website_source_id, content_hash) where content_hash is not null;

drop trigger if exists trg_kwp_updated_at on knowledge.knowledge_website_pages;
create trigger trg_kwp_updated_at before update on knowledge.knowledge_website_pages
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_website_scrape_runs (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,
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
create index if not exists ix_kwr_client_id on knowledge.knowledge_website_scrape_runs(client_id);

-- =====================================================================
-- Knowledge Base: RETRIEVAL BENCHMARK (standalone diagnostic feature)
--
--   knowledge_retrieval_benchmark_cases    one realistic contact question + the ONE chunk that answers it
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
-- Everything is scoped by client_id.
-- =====================================================================
create table if not exists knowledge.knowledge_retrieval_benchmark_cases (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null references core.clients(id) on delete cascade,

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
create index if not exists ix_kbc_client_id on knowledge.knowledge_retrieval_benchmark_cases(client_id);
create index if not exists ix_kbc_client_chunk on knowledge.knowledge_retrieval_benchmark_cases(client_id, expected_chunk_id);
-- The same question for the same chunk is never stored twice.
create unique index if not exists ux_kbc_client_chunk_question
  on knowledge.knowledge_retrieval_benchmark_cases(client_id, expected_chunk_id, lower(question));

drop trigger if exists trg_kbc_updated_at on knowledge.knowledge_retrieval_benchmark_cases;
create trigger trg_kbc_updated_at before update on knowledge.knowledge_retrieval_benchmark_cases
  for each row execute function set_updated_at();

create table if not exists knowledge.knowledge_retrieval_benchmark_runs (
  id                       uuid primary key default gen_random_uuid(),
  client_id                uuid not null references core.clients(id) on delete cascade,

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
create index if not exists ix_kbr_client_created on knowledge.knowledge_retrieval_benchmark_runs(client_id, created_at desc);

create table if not exists knowledge.knowledge_retrieval_benchmark_results (
  id                          uuid primary key default gen_random_uuid(),
  client_id                   uuid not null references core.clients(id) on delete cascade,
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
  -- The expected chunk was in the top-K by similarity but scored under the client's minimum similarity, so
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
create index if not exists ix_kbres_client_id on knowledge.knowledge_retrieval_benchmark_results(client_id);
create index if not exists ix_kbres_case_created on knowledge.knowledge_retrieval_benchmark_results(benchmark_case_id, created_at desc);

-- Retrieval Benchmark: which generation request created a generated case. The app generates a generation_id per
-- "Generate Test Cases" request, sends it to the n8n generator, requires it echoed back, and stores it on every case
-- that response produced (null for manual cases and for cases created before this column existed).
alter table knowledge.knowledge_retrieval_benchmark_cases add column if not exists generation_id uuid;
create index if not exists ix_kbc_client_generation on knowledge.knowledge_retrieval_benchmark_cases(client_id, generation_id) where generation_id is not null;

-- Retrieval Benchmark: ASYNC question generation. One row per "Generate Test Cases" request. `id` IS the generationId that is
-- sent to the n8n generator and that n8n's callback must carry (POST .../generations/{id}/questions). The row is created
-- (pending) BEFORE anything is sent, so even an instant callback finds it, and it remembers exactly which chunks were sent so
-- the reply can be validated against that set (and the client is taken from THIS row, never from the caller).
create table if not exists knowledge.knowledge_retrieval_benchmark_generations (
  id                    uuid primary key,
  client_id             uuid not null references core.clients(id) on delete cascade,

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
create index if not exists ix_kbg_client_created on knowledge.knowledge_retrieval_benchmark_generations(client_id, created_at desc);

-- Results remember which generation their case came from, so scores can be reported per generation even after the case is
-- deleted (the case FK is ON DELETE SET NULL).
alter table knowledge.knowledge_retrieval_benchmark_results add column if not exists generation_id uuid;
create index if not exists ix_kbres_generation on knowledge.knowledge_retrieval_benchmark_results(client_id, generation_id) where generation_id is not null;

-- Backfill: generations created by the earlier synchronous version left only a tag on their cases.
insert into knowledge.knowledge_retrieval_benchmark_generations
  (id, client_id, status, chunks_sent, questions_returned, cases_created, completed_at, created_at)
select generation_id, client_id, 'completed', count(*), count(*), count(*), min(created_at), min(created_at)
from knowledge.knowledge_retrieval_benchmark_cases
where generation_id is not null
group by generation_id, client_id
on conflict (id) do nothing;

update knowledge.knowledge_retrieval_benchmark_results r
set generation_id = c.generation_id
from knowledge.knowledge_retrieval_benchmark_cases c
where r.benchmark_case_id = c.id and c.generation_id is not null and r.generation_id is null;

-- Retrieval Benchmark: a pending generation can be stopped by a member ("Stop generating"). Status 'cancelled' frees Generate at once
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
create index if not exists ix_kbr_generation on knowledge.knowledge_retrieval_benchmark_runs(client_id, generation_id) where generation_id is not null;

-- =====================================================================
-- Structured client availability (the booking source of truth; clients.operating_hours stays free text for the AI to quote)
-- ---------------------------------------------------------------------
-- Weekly schedule: one row per (client, weekday). day_of_week follows .NET DayOfWeek: 0 = Sunday ... 6 = Saturday.
-- Times are the client's LOCAL wall-clock (clients.timezone). To allow several windows per day later, drop
-- ux_availability_rules_day - the slot service already iterates every rule row for a day.
create table if not exists scheduling.availability_rules (
  id           uuid primary key default gen_random_uuid(),
  client_id    uuid not null references core.clients(id) on delete cascade,
  day_of_week  smallint not null check (day_of_week between 0 and 6),
  is_open      boolean not null default true,
  start_time   time not null,
  end_time     time not null,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  constraint ck_availability_rules_range check (end_time > start_time)
);
create unique index if not exists ux_availability_rules_day on scheduling.availability_rules(client_id, day_of_week);
drop trigger if exists trg_availability_rules_updated_at on scheduling.availability_rules;
create trigger trg_availability_rules_updated_at before update on scheduling.availability_rules
  for each row execute function set_updated_at();

-- Booking rules: one row per client. No row yet = defaults are shown in the UI (nothing is offered until a weekly rule is open).
create table if not exists scheduling.booking_settings (
  id                                    uuid primary key default gen_random_uuid(),
  client_id                             uuid not null unique references core.clients(id) on delete cascade,
  default_booking_duration_minutes integer not null default 30 check (default_booking_duration_minutes > 0),
  buffer_minutes                        integer not null default 0 check (buffer_minutes >= 0),
  minimum_booking_notice_minutes        integer not null default 240 check (minimum_booking_notice_minutes >= 0),
  maximum_advance_booking_days          integer not null default 60 check (maximum_advance_booking_days > 0),
  created_at                            timestamptz not null default now(),
  updated_at                            timestamptz not null default now()
);
drop trigger if exists trg_booking_settings_updated_at on scheduling.booking_settings;
create trigger trg_booking_settings_updated_at before update on scheduling.booking_settings
  for each row execute function set_updated_at();

-- Date exceptions override the weekly schedule for that local date: closed all day, or a custom window (which may also
-- open a normally-closed day).
create table if not exists scheduling.availability_exceptions (
  id          uuid primary key default gen_random_uuid(),
  client_id   uuid not null references core.clients(id) on delete cascade,
  date        date not null,
  is_closed   boolean not null default true,
  start_time  time,
  end_time    time,
  reason      varchar(200),
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  constraint ck_availability_exceptions_window
    check (is_closed or (start_time is not null and end_time is not null and end_time > start_time))
);
create unique index if not exists ux_availability_exceptions_date on scheduling.availability_exceptions(client_id, date);
drop trigger if exists trg_availability_exceptions_updated_at on scheduling.availability_exceptions;
create trigger trg_availability_exceptions_updated_at before update on scheduling.availability_exceptions
  for each row execute function set_updated_at();

-- =====================================================================
-- Notifications — a simple per-client notification center for members (bell/list). Created ONLY from confirmed
-- backend events (a message actually sent, a booking actually booked...), never from an AI intention alone.
-- ---------------------------------------------------------------------
create table if not exists activity.notifications (
  id                      uuid primary key default gen_random_uuid(),
  client_id               uuid not null references core.clients(id) on delete cascade,
  type                    varchar(40) not null
    check (type in ('NEW_CONTACT','HANDOFF','BOOKING_CREATED','BOOKING_RESCHEDULED','BOOKING_CANCELED',
                     'CAMPAIGN_REPLY','OUTBOUND_MESSAGE_FAILED','INTEGRATION_UNHEALTHY')),
  -- title/message hold strings-file keys (Resources/i18n/en.json); text_args their arguments, so each viewer reads the
  -- notification in their own language and business-type words. Rows written before keys hold plain text.
  title                   varchar(200) not null,
  message                 text,
  text_args               text[] not null default '{}',
  contact_id                 uuid references crm.contacts(id) on delete set null,
  conversation_id         uuid references crm.conversations(id) on delete set null,
  booking_id          uuid references scheduling.bookings(id) on delete set null,
  channel_integration_id  uuid references channels.channel_integrations(id) on delete set null,
  -- Precomputed relative URL (e.g. "/inbox?conversationId=..." or "/dashboard/bookings/{id}") so the
  -- bell UI never has to know which entity type maps to which page.
  link                    varchar(300),
  is_read                 boolean not null default false,
  read_at                 timestamptz,
  created_at              timestamptz not null default now()
);
alter table activity.notifications add column if not exists text_args text[] not null default '{}';
create index if not exists ix_notifications_client_created on activity.notifications(client_id, created_at desc);
create index if not exists ix_notifications_client_unread on activity.notifications(client_id) where is_read = false;

-- =====================================================================
-- Calendar Integrations — one-way (Omni -> external) booking sync via a dedicated n8n workflow.
-- Omni never talks to Google/Microsoft directly and never stores their OAuth tokens: n8n owns the
-- actual provider connection (see Services/ICalendarSyncNotifier.cs), Omni only stores the opaque
-- reference n8n gives back plus what the member chose (which calendar, sync on/off).
-- ---------------------------------------------------------------------
create table if not exists scheduling.calendar_integrations (
  id                       uuid primary key default gen_random_uuid(),
  client_id                uuid not null references core.clients(id) on delete cascade,
  provider                 varchar(20) not null check (provider in ('google','outlook')),
  status                   varchar(20) not null default 'disconnected'
    check (status in ('disconnected','pending','connected','error')),

  -- Opaque handle n8n returns on a successful connect — n8n looks THIS up to find the stored OAuth
  -- tokens for this client+provider. Never a raw access/refresh token; Omni cannot use it itself.
  external_connection_ref  varchar(200),
  account_display_name     varchar(200),  -- e.g. the connected Google/Outlook account's email, for display only

  selected_calendar_id     varchar(200),  -- external calendar id (opaque to Omni)
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
create unique index if not exists ux_calendar_integrations_client_provider on scheduling.calendar_integrations(client_id, provider);
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

-- One row per (booking, connected calendar) — the dedup/consistency mechanism: the SAME row is
-- reused across create -> reschedule -> cancel, so a reschedule updates the stored external_event_id's
-- event instead of ever creating a second one. last_request_id correlates an outstanding n8n call with
-- its callback.
create table if not exists scheduling.booking_calendar_syncs (
  id                       uuid primary key default gen_random_uuid(),
  booking_id           uuid not null references scheduling.bookings(id) on delete cascade,
  calendar_integration_id  uuid not null references scheduling.calendar_integrations(id) on delete cascade,
  external_event_id        varchar(200),
  status                   varchar(20) not null default 'pending'
    check (status in ('pending','synced','failed','canceled')),
  last_request_id          uuid,
  last_error               text,
  created_at               timestamptz not null default now(),
  updated_at               timestamptz not null default now()
);
create unique index if not exists ux_booking_calendar_syncs_booking_integration
  on scheduling.booking_calendar_syncs(booking_id, calendar_integration_id);
create index if not exists ix_booking_calendar_syncs_integration on scheduling.booking_calendar_syncs(calendar_integration_id);
drop trigger if exists trg_booking_calendar_syncs_updated_at on scheduling.booking_calendar_syncs;
create trigger trg_booking_calendar_syncs_updated_at before update on scheduling.booking_calendar_syncs
  for each row execute function set_updated_at();

-- booking_calendar_syncs: the sync row didn't know which operation (create/update/cancel) its last request
-- was for, so a successful cancel callback couldn't be told apart from a successful create/update one.
alter table scheduling.booking_calendar_syncs add column if not exists last_operation varchar(10);

-- calendar_integrations: connect/list-calendars/disconnect now happen directly against Google/Outlook from
-- Omni (no longer via n8n) — the client's own OAuth tokens are stored here so Omni can refresh them
-- and hand a fresh access token to n8n's sync webhook for the one thing n8n still does (the actual create/update/
-- cancel API call). MVP NOTE (same as channel_integrations.access_token): plain text — move to an encrypted
-- column/secrets manager before this handles real contact data at scale.
alter table scheduling.calendar_integrations add column if not exists access_token text;
alter table scheduling.calendar_integrations add column if not exists refresh_token text;
alter table scheduling.calendar_integrations add column if not exists token_expires_at timestamptz;

-- =====================================================================
-- TikTok Login Kit — account connection only (NOT a messaging channel in this phase, so it's its own table
-- rather than a row in channel_integrations — see Data/Entities/TikTokIntegration.cs). Omni owns the
-- whole OAuth2 relationship directly: connect, callback, token refresh, disconnect — no n8n involvement.
-- ---------------------------------------------------------------------
create table if not exists channels.tiktok_integrations (
  id                        uuid primary key default gen_random_uuid(),
  client_id                 uuid not null references core.clients(id) on delete cascade,
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
create unique index if not exists ux_tiktok_integrations_client on channels.tiktok_integrations(client_id);
drop trigger if exists trg_tiktok_integrations_updated_at on channels.tiktok_integrations;
create trigger trg_tiktok_integrations_updated_at before update on channels.tiktok_integrations
  for each row execute function set_updated_at();

-- ---------------------------------------------------------------
-- Infobip removed (2026-10-12): WhatsApp is Meta Cloud API only, one provider per channel. Drops what the
-- Infobip period added. Before dropping, Infobip-connected WhatsApp rows are disconnected (they have no Meta
-- credentials; the client reconnects with Facebook sign-in) and templates approved through Infobip go back to
-- draft so members can resubmit them to Meta (Retry submit). Re-runnable: does nothing once the columns are gone.
-- ---------------------------------------------------------------
do $$
begin
  if exists (select 1 from information_schema.columns
             where table_schema = 'channels' and table_name = 'channel_integrations' and column_name = 'provider') then
    update channels.channel_integrations
       set status = 'disconnected', webhook_verify_token = null, display_name = null, updated_at = now()
     where provider = 'infobip';
  end if;
  if exists (select 1 from information_schema.columns
             where table_schema = 'channels' and table_name = 'whatsapp_templates' and column_name = 'provider') then
    update channels.whatsapp_templates
       set status = 'draft', meta_template_id = null, rejection_reason = null, updated_at = now()
     where provider = 'infobip';
  end if;
end $$;
drop index if exists channels.ux_channel_integrations_provider_sender;
alter table channels.channel_integrations drop column if exists provider;
alter table channels.channel_integrations drop column if exists provider_sender_id;
alter table channels.whatsapp_templates drop column if exists provider;

-- =====================================================================
-- SUBSCRIPTIONS & USAGE BILLING — schema "billing"  (module: Omni.App/Billing, guide: docs/billing.md)
-- ---------------------------------------------------------------------
-- Every billing table lives in its own Postgres schema, billing.*, like the product tables live in core.*, crm.* and so on.
-- Three separate questions, never collapsed into one:
--   1. Subscription: what the client pays Omni for access (a plan: price, period, entitlements, and an
--      optional monetary included credit per period).
--   2. Provider billing responsibility: who pays the upstream provider (Meta, an SMS operator...) for a connected
--      channel account: customer_direct | platform_funded | external_provider_direct | no_provider_usage_fee.
--   3. Omni usage billing: whether Omni itself charges the client for that usage (by default only when
--      Omni pays the provider).
-- Usage is a BILLABLE EVENT. Every event can be recorded (usage analytics, provider cost where known), but only
-- events Omni charges are rated, reserved, settled and paid (included credit first, then the prepaid wallet).
-- Money is numeric(18,6) in the account currency (one currency for now: Billing:Currency, default USD).
-- Every balance change is one append-only row in billing.ledger_entries; billing.accounts caches the balances
-- and must always equal the ledger sums (BillingQueryService.ReconcileAsync checks it).
-- Concurrency: every money operation locks the client's billing.accounts row (select ... for update) inside one
-- transaction. Idempotency: usage records and ledger entries carry an idempotency key unique per client.
-- Nothing here is enforced until Billing:Enabled = true.
-- =====================================================================
create schema if not exists billing;
create extension if not exists btree_gist;   -- for the "no overlapping rate versions" exclusion constraint

-- Plans: what a client subscribes to. Price changes apply from the next renewal (history is in the ledger).
create table if not exists billing.plans (
  id                     uuid primary key default gen_random_uuid(),
  code                   varchar(50) not null unique check (code ~ '^[a-z0-9_-]+$'),
  name                   varchar(100) not null,
  description            text,
  price                  numeric(18,2) not null default 0 check (price >= 0),
  currency               char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  billing_period         varchar(10) not null default 'month' check (billing_period in ('month','year')),
  -- Monetary credit granted each period, usable for any usage Omni charges. Unused credit expires at renewal.
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

-- Rate cards: named price lists. Lookup order for a billable event: the client's own card (client_id set,
-- custom pricing) -> the plan's card -> the default card. The first card with a matching rate wins.
create table if not exists billing.rate_cards (
  id           uuid primary key default gen_random_uuid(),
  code         varchar(50) not null unique check (code ~ '^[a-z0-9_-]+$'),
  name         varchar(100) not null,
  description  text,
  client_id    uuid references core.clients(id) on delete cascade,   -- set = this client's custom-pricing card
  is_default   boolean not null default false,
  is_active    boolean not null default true,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  constraint ck_rate_cards_default_not_client check (not (is_default and client_id is not null))
);
create unique index if not exists ux_rate_cards_default on billing.rate_cards(is_default) where is_default;
create unique index if not exists ux_rate_cards_client on billing.rate_cards(client_id) where client_id is not null;
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
-- client_rate = what Omni charges the client per unit. They're independent: a customer-direct "service fee"
-- rate can have client_rate 0.01 and the provider cost only for reporting. Rates with provider_billing = null apply
-- only to platform_funded charging (and to cost reporting for any usage): Omni never charges a customer-paid
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

-- One billing account (wallet) per client. spendable = wallet_balance + included_credit_balance - reserved_amount.
-- The wallet only finances what the client owes Omni. wallet_balance can only go below zero when a settlement
-- costs more than was reserved (usage that already happened); reservations, subscription charges and manual
-- debits never take it below zero.
create table if not exists billing.accounts (
  id                       uuid primary key default gen_random_uuid(),
  client_id                uuid not null unique references core.clients(id) on delete cascade,
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

-- One subscription per client (changing plan updates this row; history is in events + the ledger).
-- active: normal. past_due: renewal couldn't be paid, still usable during the grace period (Billing:GracePeriodDays).
-- expired: grace ran out. cancelled: ended on request. Expired/cancelled clients can still sign in and see their
-- data, but can't send messages, run campaigns or use the AI until a plan is started again.
create table if not exists billing.subscriptions (
  id                    uuid primary key default gen_random_uuid(),
  client_id             uuid not null unique references core.clients(id) on delete cascade,
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
-- provider, and whether Omni charges usage on it. No row (or null values) = the defaults (channel/provider
-- defaults in code + Billing:ProviderBilling:Defaults).
create table if not exists billing.channel_account_settings (
  channel_integration_id  uuid primary key references channels.channel_integrations(id) on delete cascade,
  client_id               uuid not null references core.clients(id) on delete cascade,
  provider_billing        varchar(30) check (provider_billing in
                            ('customer_direct','platform_funded','external_provider_direct','no_provider_usage_fee')),
  omni_usage_billing      boolean,
  reason                  text,
  updated_by              varchar(200),
  created_at              timestamptz not null default now(),
  updated_at              timestamptz not null default now()
);
create index if not exists ix_channel_account_settings_client on billing.channel_account_settings(client_id);
-- applies_to_provider (override held only while on the same WhatsApp provider) went with Infobip (2026-10-12).
alter table billing.channel_account_settings drop column if exists applies_to_provider;
drop trigger if exists trg_channel_account_settings_updated_at on billing.channel_account_settings;
create trigger trg_channel_account_settings_updated_at before update on billing.channel_account_settings
  for each row execute function public.set_updated_at();

-- Usage records (CDRs): one row per billable event, whether or not Omni charges it.
--   charge_status    = Omni's money: reserved -> settled | released; failed = refused (no rate / not enough
--                      balance, nothing charged); not_charged = Omni doesn't bill this usage (customer-paid
--                      provider, no provider fee...) — never touches the wallet, included credit or ledger.
--   provider_outcome = what happened at the provider: pending -> billable (e.g. delivered) | not_billable (failed).
--   provider_billing = who paid the provider for it (snapshot of the account's responsibility at the time).
-- Prices are snapshotted at rating time, so later rate or setting changes never alter history.
-- message/conversation/campaign/channel_integration ids are plain columns (no FK) on purpose: financial history
-- must outlive deleted rows.
create table if not exists billing.usage_records (
  id                      uuid primary key default gen_random_uuid(),
  client_id               uuid not null references core.clients(id) on delete cascade,
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
create unique index if not exists ux_usage_records_key on billing.usage_records(client_id, idempotency_key);
create index if not exists ix_usage_records_client_occurred on billing.usage_records(client_id, occurred_at desc);
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

-- Financial ledger: only real Omni money movements (never uncharged usage). Append-only (updates are rejected
-- by a trigger). balance_after = that balance right after this entry. Signs: credits to a balance are positive,
-- debits negative. A subscription_charge of 0 is allowed so every started period (trial, free plan) has exactly one
-- charge row.
create table if not exists billing.ledger_entries (
  id                  uuid primary key default gen_random_uuid(),
  seq                 bigint generated by default as identity,   -- posting order (one operation's rows share created_at)
  client_id           uuid not null references core.clients(id) on delete cascade,
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
create unique index if not exists ux_ledger_entries_key on billing.ledger_entries(client_id, idempotency_key);
create index if not exists ix_ledger_entries_client_seq on billing.ledger_entries(client_id, seq desc);
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

-- Every existing client gets an (empty) billing account; new clients get one at registration (and lazily anyway).
insert into billing.accounts (client_id, currency)
select id, 'USD' from core.clients
on conflict (client_id) do nothing;

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

-- =====================================================================
-- API timings: activity.api_route_stats
--
-- How long each route takes in production. RequestTimingMiddleware records every request in memory (no database
-- write per request); ApiTimingFlushJob writes one row per (method, route template) per minute and deletes rows older
-- than Monitoring:RetentionDays. Several app instances each write their own rows; readers sum them. total_ms (not an
-- average) is stored so any period's average is sum(total_ms) / sum(request_count). p95_ms is the 95th percentile of
-- that minute only. Read by GET /api/platform-admin/api-timings (the admin portal's API timings page).
-- =====================================================================
create table if not exists activity.api_route_stats (
  id                  bigint generated always as identity primary key,
  bucket_start        timestamptz not null,
  method              varchar(10) not null,
  route               varchar(300) not null,
  request_count       int not null,
  error_count         int not null default 0,
  client_error_count  int not null default 0,
  slow_count          int not null default 0,
  total_ms            double precision not null,
  p95_ms              double precision not null,
  max_ms              double precision not null,
  created_at          timestamptz not null default now()
);

create index if not exists ix_api_route_stats_bucket_start on activity.api_route_stats(bucket_start);

-- =====================================================================
-- Error log: activity.app_logs
--
-- Every warning, error and critical the app logs (any ILogger call, including the framework's "unhandled exception"
-- log). DatabaseLoggerProvider queues each entry in memory (AppLogCollector, no database write on the caller's path);
-- AppLogFlushJob writes them in batches every few seconds and deletes warnings older than ErrorLog:WarningRetentionDays
-- and errors/criticals older than ErrorLog:ErrorRetentionDays. fingerprint (hash of category + message template +
-- exception type + top stack frame) groups one problem across its values. At most ErrorLog:MaxPerFingerprintPerMinute
-- rows per fingerprint per minute are written; the rest of that minute becomes one extra row whose repeat_count says how
-- many occurrences it stands for. trace_id is the request's W3C trace id. No foreign keys: a log row must outlive the
-- client, conversation or message it mentions, and inserts stay cheap. Read by GET /api/platform-admin/logs.
-- =====================================================================
create table if not exists activity.app_logs (
  id                 bigint generated always as identity primary key,
  occurred_at        timestamptz not null,
  level              varchar(12) not null,
  category           varchar(300) not null,
  message            text not null,
  message_template   varchar(1000),
  exception_type     varchar(300),
  exception_message  text,
  stack_trace        text,
  fingerprint        varchar(16) not null,
  repeat_count       int not null default 1,
  trace_id           varchar(32),
  client_id          uuid,
  conversation_id    uuid,
  message_id         uuid,
  method             varchar(10),
  route              varchar(300),
  instance           varchar(100),
  properties         jsonb not null default '{}'::jsonb,
  constraint ck_app_logs_level check (level in ('Warning','Error','Critical'))
);

create index if not exists ix_app_logs_occurred_at on activity.app_logs(occurred_at);
create index if not exists ix_app_logs_fingerprint on activity.app_logs(fingerprint, occurred_at desc);
create index if not exists ix_app_logs_trace_id on activity.app_logs(trace_id) where trace_id is not null;
create index if not exists ix_app_logs_client on activity.app_logs(client_id, occurred_at desc) where client_id is not null;

-- =====================================================================
-- Message trace: activity.message_trace_steps (+ crm.messages.trace_id)
--
-- One row per step a message takes through the app that nothing else records: webhook received/rejected, client not
-- found, duplicate delivery, saved, inbox notified, conversation reopened / moved to members, bot skipped (and why),
-- bot triggered or failed (n8n), the bot's tool calls, send requested / blocked / rejected, provider accepted or failed.
-- trace_id is the W3C trace id of the request (n8n sends it back as traceparent, so the bot's callbacks join the same
-- trace). Delivery statuses, billing and business events are not copied here: the trace view reads them from
-- crm.messages, billing.usage_records and activity.events. MessageTracer queues steps in memory; MessageTraceFlushJob
-- writes them every few seconds and deletes steps older than MessageTrace:RetentionDays, keeping traces with a failed
-- step until MessageTrace:FailedRetentionDays. No foreign keys (steps outlive the rows they mention; inserts stay cheap).
-- crm.messages.trace_id links a message to the trace it was created in, so its later delivery statuses and the AI
-- reply it caused show in the same timeline. Read by /api/platform-admin/message-traces.
-- =====================================================================
create table if not exists activity.message_trace_steps (
  id                   bigint generated always as identity primary key,
  trace_id             varchar(32) not null,
  occurred_at          timestamptz not null,
  step                 varchar(60) not null,
  outcome              varchar(12) not null,
  duration_ms          double precision,
  client_id            uuid,
  conversation_id      uuid,
  message_id           uuid,
  external_message_id  varchar(200),
  channel              varchar(50),
  n8n_execution_id     varchar(100),
  detail               jsonb not null default '{}'::jsonb,
  instance             varchar(100),
  constraint ck_message_trace_steps_outcome check (outcome in ('ok','skipped','failed'))
);

create index if not exists ix_trace_steps_trace on activity.message_trace_steps(trace_id, occurred_at);
create index if not exists ix_trace_steps_message on activity.message_trace_steps(message_id) where message_id is not null;
create index if not exists ix_trace_steps_conversation on activity.message_trace_steps(conversation_id, occurred_at desc) where conversation_id is not null;
create index if not exists ix_trace_steps_failed on activity.message_trace_steps(occurred_at desc) where outcome = 'failed';
create index if not exists ix_trace_steps_occurred_at on activity.message_trace_steps(occurred_at);

alter table crm.messages add column if not exists trace_id varchar(32);
create index if not exists ix_messages_trace_id on crm.messages(trace_id) where trace_id is not null;

-- =====================================================================
-- Inbox settings: crm.inbox_settings
--
-- How a client's Inbox handles conversations (Business/Services/Inbox/InboxSettingsService). One column per setting, so
-- a new setting is a new column here. Today: whether the AI bot answers new conversations and conversations a contact
-- reopens (off = they start with / move to members, human mode); they only take effect while the plan includes the
-- ai_assistant entitlement. A client without a row uses the column defaults, so existing clients keep the bot on.
-- channel_integration_id null = the client-wide row (the only kind used today); a row per channel connection can be
-- added later to override the client-wide one for that channel.
-- =====================================================================
create table if not exists crm.inbox_settings (
  id                             uuid primary key default gen_random_uuid(),
  client_id                      uuid not null references core.clients(id) on delete cascade,
  channel_integration_id         uuid references channels.channel_integrations(id) on delete cascade,
  ai_bot_new_conversations       boolean not null default true,
  ai_bot_reopened_conversations  boolean not null default true,
  created_at                     timestamptz not null default now(),
  updated_at                     timestamptz not null default now()
);

create unique index if not exists ux_inbox_settings_client_wide on crm.inbox_settings(client_id)
  where channel_integration_id is null;
create unique index if not exists ux_inbox_settings_channel on crm.inbox_settings(channel_integration_id)
  where channel_integration_id is not null;

drop trigger if exists trg_inbox_settings_updated_at on crm.inbox_settings;
create trigger trg_inbox_settings_updated_at
  before update on crm.inbox_settings
  for each row execute function public.set_updated_at();

-- UTC guard: every moment in time is stored as timestamptz (UTC); only the client-local availability columns
-- (availability_rules/exceptions start_time/end_time/date, read together with clients.timezone) are
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
