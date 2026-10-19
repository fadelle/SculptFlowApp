-- =====================================================================================================
-- Omni: copy the data of the OLD SculptFlow project (PlasticSurgery schema) into a NEW Omni project.
--
-- Written for Milestone A (session S10). Keep it in step with Database/schema.sql: any change to a table the
-- script copies must change this file and run test-copy.ps1 (same folder). The old table and column names appear
-- here on purpose (this folder is allowlisted in the leftover-names test).
--
-- WHAT YOU NEED
--   * A NEW Supabase project whose empty database already ran, in this order:
--       Database/schema.sql, Database/seed-config.sql
--     (seed.sql and seed-demo.sql are demo data: do NOT run them in the new project).
--   * The OLD project's database connection details (Settings > Database > "Direct connection", NOT the pooler).
--
-- HOW TO RUN (psql, against the NEW database; the old password is only ever typed on your machine)
--   psql "<NEW project connection string>" ^
--        -v old_host=db.<old-ref>.supabase.co -v old_dbname=postgres -v old_user=postgres ^
--        -v old_password=<old database password> -f copy-from-old.sql
--   Optional: -v old_port=5432 (default), -v old_sslmode=require (default; the local test uses disable),
--             -v force=1 (EMPTIES every table this script fills first, then copies; without it the script refuses
--             to run when any of them has rows). Never put a real host or password into this file or git.
--
-- WHAT IT DOES (all in ONE transaction: any error, or any row-count mismatch, saves nothing)
--   1. postgres_fdw: a foreign server for the old project, every old schema imported as old_<schema>.
--   2. Checks (old stage keys exist, no sign-in name clashes, targets empty).
--   3. One insert ... select per table in foreign-key order, with the renames of Milestone A:
--        clinics -> clients (business type 'clinic', default language 'en'), procedures -> services,
--        leads -> contacts (status -> stage_key, qualification_status -> qualification; a 'medical_question' lead in an
--        open stage moves to the stage 'medical_question'), appointments -> bookings (type 'consultation' -> 'standard'),
--        procedure_bookings -> crm.orders (status 'booked' -> 'confirmed'), clinic_* scheduling tables, every
--        clinic_id/lead_id/procedure_id/appointment_id column, message senders lead/staff -> contact/member, event types,
--        notification types and links, campaign audience type and filter keys, knowledge categories, plan entitlements
--        max_agents/ai_agent -> max_users/ai_assistant, usage rate source clinic -> client, a trace step, error-log
--        categories, and the config settings that were changed by hand (renamed keys; unchanged defaults stay as seeded).
--   4. Users: identity.identity_users -> identity.users. Role rule: the EARLIEST membership of each clinic is its owner
--      (role client), every other user is a member. The report lists them so you can check. The 'full_name' claim
--      becomes users.full_name. admin.admin_users -> identity.users with role admin (same id as text, same password hash,
--      new security stamp), admin.admin_audit_log -> same table with user_id (text) and client_id.
--   5. Identity columns continue after the copied ids; a row-count report (old vs new per table) ends with ok/mismatch.
--   Not copied: core.business_types and crm.pipeline_stages (they come from schema.sql), old AI placeholder profiles.
--   Row counts differ from the old project only where the report says so: users (+ admins), user_claims (the full_name
--   claims became a column) and config.settings (only changed settings).
--
-- After it: run `analyze;`, sign in as a migrated client user (their old password still works), then rotate the
-- old project's keys when you retire it.
-- =====================================================================================================

\set ON_ERROR_STOP on
\set QUIET on

\if :{?old_host}
\else
  \echo 'Missing -v old_host=... (the old project database host). See the header of this file.'
  \quit
\endif
\if :{?old_password}
\else
  \echo 'Missing -v old_password=... (the old project database password). See the header of this file.'
  \quit
\endif
\if :{?old_user}
\else
  \set old_user postgres
\endif
\if :{?old_dbname}
\else
  \set old_dbname postgres
\endif
\if :{?old_port}
\else
  \set old_port 5432
\endif
\if :{?old_sslmode}
\else
  \set old_sslmode require
\endif

begin;

-- ===================================================================================
-- 1. Link to the old project
-- ===================================================================================
create extension if not exists postgres_fdw;

create server old_project foreign data wrapper postgres_fdw
    options (host :'old_host', port :'old_port', dbname :'old_dbname', sslmode :'old_sslmode', fetch_size '1000');
create user mapping for current_user server old_project
    options (user :'old_user', password :'old_password');

create schema old_core;       import foreign schema core       from server old_project into old_core;
create schema old_identity;   import foreign schema identity   from server old_project into old_identity;
create schema old_crm;        import foreign schema crm        from server old_project into old_crm;
create schema old_scheduling; import foreign schema scheduling from server old_project into old_scheduling;
create schema old_channels;   import foreign schema channels   from server old_project into old_channels;
create schema old_marketing;  import foreign schema marketing  from server old_project into old_marketing;
create schema old_knowledge;  import foreign schema knowledge  from server old_project into old_knowledge;
create schema old_activity;   import foreign schema activity   from server old_project into old_activity;
create schema old_billing;    import foreign schema billing    from server old_project into old_billing;
create schema old_config;     import foreign schema config     from server old_project into old_config;
create schema old_admin;      import foreign schema admin      from server old_project into old_admin;

-- Campaign audience filters (JSON): lead/procedure words -> contact/service words.
create function pg_temp.map_audience_filters(f jsonb) returns jsonb language sql immutable as $$
    select case when f is null or jsonb_typeof(f) <> 'object' then f else
        (f - 'leadStatuses' - 'qualificationStatuses' - 'procedureId')
        || case when f ? 'leadStatuses' then jsonb_build_object('stageKeys', f -> 'leadStatuses') else '{}'::jsonb end
        || case when f ? 'procedureId' then jsonb_build_object('serviceId', f -> 'procedureId') else '{}'::jsonb end
        || case when jsonb_typeof(f -> 'qualificationStatuses') = 'array' then jsonb_build_object('qualifications',
               (select coalesce(jsonb_agg(case when q = '"medical_question"'::jsonb then '"needs_human"'::jsonb else q end), '[]'::jsonb)
                from jsonb_array_elements(f -> 'qualificationStatuses') q))
           when f ? 'qualificationStatuses' then jsonb_build_object('qualifications', f -> 'qualificationStatuses')
           else '{}'::jsonb end
    end
$$;

-- ===================================================================================
-- 2. Checks
-- ===================================================================================
do $$
declare missing text;
begin
    if not exists (select 1 from core.business_types where code = 'clinic') then
        raise exception 'The business type "clinic" is missing: run Database/schema.sql in this project first.';
    end if;
    if not exists (select 1 from config.settings) then
        raise exception 'config.settings is empty: run Database/seed-config.sql in this project first.';
    end if;

    select string_agg(distinct l.status, ', ') into missing from old_crm.leads l
    where not exists (select 1 from crm.pipeline_stages ps where ps.business_type_code = 'clinic' and ps.key = l.status);
    if missing is not null then
        raise exception 'Old lead statuses with no stage in the clinic pipeline: %', missing;
    end if;

    select string_agg(distinct upper(a.email), ', ') into missing from old_admin.admin_users a
    join old_identity.identity_users u on u.normalized_user_name = upper(a.email);
    if missing is not null then
        raise exception 'These admin emails are also client user names (one users table needs unique sign-in names): %', missing;
    end if;

    if exists (select 1 from information_schema.columns where table_schema = 'admin' and table_name = 'admin_audit_log' and column_name = 'admin_user_id') then
        raise exception 'admin.admin_audit_log in this database still has the old shape (admin_user_id). Use a fresh project.';
    end if;
end $$;

-- The audit log lives in the admin portal's schema; fresh projects get the final shape (portal repo, admin-schema.sql).
create schema if not exists admin;
create table if not exists admin.admin_audit_log (
    id              uuid primary key default gen_random_uuid(),
    user_id         text null references identity.users(id) on delete set null,
    admin_email     text not null,
    action          text not null,
    entity_type     text not null,
    entity_id       text null,
    client_id       uuid null,
    details         text null,
    created_at      timestamptz not null default now()
);
create index if not exists ix_admin_audit_log_created on admin.admin_audit_log (created_at desc);
create index if not exists ix_admin_audit_log_client on admin.admin_audit_log (client_id, created_at desc);
alter table admin.admin_audit_log enable row level security;

-- Every table this script fills (config.settings is seeded already and only updated).
create temp table copy_targets (tbl text primary key);
insert into copy_targets values ('activity.api_route_stats'),
    ('activity.app_logs'),
    ('activity.events'),
    ('activity.message_trace_steps'),
    ('activity.notifications'),
    ('admin.admin_audit_log'),
    ('billing.accounts'),
    ('billing.channel_account_settings'),
    ('billing.ledger_entries'),
    ('billing.plan_entitlements'),
    ('billing.plans'),
    ('billing.rate_cards'),
    ('billing.rates'),
    ('billing.subscriptions'),
    ('billing.usage_records'),
    ('channels.channel_integrations'),
    ('channels.tiktok_integrations'),
    ('channels.whatsapp_health_events'),
    ('channels.whatsapp_templates'),
    ('core.clients'),
    ('core.services'),
    ('crm.contacts'),
    ('crm.conversations'),
    ('crm.inbox_settings'),
    ('crm.messages'),
    ('crm.orders'),
    ('identity.client_users'),
    ('identity.user_claims'),
    ('identity.user_logins'),
    ('identity.user_tokens'),
    ('identity.users'),
    ('knowledge.knowledge_chunks'),
    ('knowledge.knowledge_documents'),
    ('knowledge.knowledge_retrieval_benchmark_cases'),
    ('knowledge.knowledge_retrieval_benchmark_generations'),
    ('knowledge.knowledge_retrieval_benchmark_results'),
    ('knowledge.knowledge_retrieval_benchmark_runs'),
    ('knowledge.knowledge_search_settings'),
    ('knowledge.knowledge_website_pages'),
    ('knowledge.knowledge_website_scrape_runs'),
    ('knowledge.knowledge_website_sources'),
    ('marketing.campaign_recipients'),
    ('marketing.campaigns'),
    ('scheduling.availability_exceptions'),
    ('scheduling.availability_rules'),
    ('scheduling.booking_calendar_syncs'),
    ('scheduling.booking_settings'),
    ('scheduling.bookings'),
    ('scheduling.calendar_integration_calendars'),
    ('scheduling.calendar_integrations');

\if :{?force}
  \echo 'force is set: emptying every table this script fills.'
  do $$
  declare t text;
  begin
      for t in select tbl from copy_targets loop
          execute format('truncate table %s cascade', t);
      end loop;
  end $$;
\endif

do $$
declare t text; n bigint; filled text := '';
begin
    for t in select tbl from copy_targets order by tbl loop
        execute format('select count(*) from %s', t) into n;
        if n > 0 then filled := filled || ' ' || t || '(' || n || ')'; end if;
    end loop;
    if filled <> '' then
        raise exception 'These tables already have rows:%. This script runs once into an empty project. Use a fresh project, or -v force=1 to empty them first.', filled;
    end if;
end $$;


-- ===================================================================================
-- 3. Copy, parents first
-- ===================================================================================

-- activity.api_route_stats -> activity.api_route_stats
insert into activity.api_route_stats (
    id, bucket_start, method, route, request_count, error_count, client_error_count, slow_count, total_ms,
    p95_ms, max_ms, created_at
)
overriding system value
select
    o.id, o.bucket_start, o.method, o.route, o.request_count, o.error_count, o.client_error_count, o.slow_count,
    o.total_ms, o.p95_ms, o.max_ms, o.created_at
from old_activity.api_route_stats o;

-- activity.app_logs -> activity.app_logs
insert into activity.app_logs (
    id, occurred_at, level, category, message, message_template, exception_type, exception_message, stack_trace,
    fingerprint, repeat_count, trace_id, client_id, conversation_id, message_id, method, route, instance,
    properties
)
overriding system value
select
    o.id, o.occurred_at, o.level,
    case when o.category like 'PlasticSurgery.%' then 'Omni.App.' || substr(o.category, 16) else o.category end,
    o.message, o.message_template, o.exception_type, o.exception_message, o.stack_trace, o.fingerprint,
    o.repeat_count, o.trace_id, o.clinic_id, o.conversation_id, o.message_id, o.method, o.route, o.instance,
    o.properties
from old_activity.app_logs o;

-- core.clinics -> core.clients
insert into core.clients (
    id, name, slug, phone, email, website, country_code, timezone, address, operating_hours, booking_info,
    business_type_code, default_language, is_active, created_at, updated_at
)
select
    o.id, o.name, o.slug, o.phone, o.email, o.website, o.country_code, o.timezone, o.address, o.operating_hours,
    o.consultation_info, 'clinic', 'en', o.is_active, o.created_at, o.updated_at
from old_core.clinics o;

-- core.procedures -> core.services
insert into core.services (
    id, client_id, name, code, description, booking_duration, is_active, created_at, updated_at
)
select
    o.id, o.clinic_id, o.name, o.code, o.description, o.consultation_duration, o.is_active, o.created_at,
    o.updated_at
from old_core.procedures o;

-- crm.leads -> crm.contacts
insert into crm.contacts (
    id, client_id, service_id, full_name, first_name, last_name, phone, email, source, source_detail,
    campaign_name, external_contact_id, stage_key, qualification, preferred_language, country_code, city,
    desired_timeline, notes, assigned_user_id, marketing_opt_in, opted_out_at, last_contact_at, next_followup_at,
    created_at, updated_at
)
select
    o.id, o.clinic_id, o.procedure_id, o.full_name, o.first_name, o.last_name, o.phone, o.email, o.source,
    o.source_detail, o.campaign_name, o.external_lead_id,
    case when o.qualification_status = 'medical_question'
             and exists (select 1 from crm.pipeline_stages ps where ps.business_type_code = 'clinic' and ps.key = o.status and ps.category = 'open')
        then 'medical_question' else o.status end,
    case o.qualification_status when 'medical_question' then 'needs_human' else o.qualification_status end,
    o.preferred_language, o.country_code, o.city, o.desired_timeline, o.notes, null, o.marketing_opt_in,
    o.opted_out_at, o.last_contact_at, o.next_followup_at, o.created_at, o.updated_at
from old_crm.leads o;

-- crm.conversations -> crm.conversations
insert into crm.conversations (
    id, client_id, contact_id, channel, external_thread_id, status, ai_enabled, human_takeover, last_message_at,
    last_message_direction, created_at, updated_at, mode, last_customer_message_at, service_window_expires_at,
    last_read_at
)
select
    o.id, o.clinic_id, o.lead_id, o.channel, o.external_thread_id, o.status, o.ai_enabled, o.human_takeover,
    o.last_message_at, o.last_message_direction, o.created_at, o.updated_at, o.mode, o.last_customer_message_at,
    o.service_window_expires_at, o.last_read_at
from old_crm.conversations o;

-- scheduling.appointments -> scheduling.bookings
insert into scheduling.bookings (
    id, client_id, contact_id, service_id, booking_type, status, scheduled_start, scheduled_end, location_type,
    location_name, external_calendar_id, external_event_id, assigned_user_id, notes, created_at, updated_at
)
select
    o.id, o.clinic_id, o.lead_id, o.procedure_id,
    case o.appointment_type when 'consultation' then 'standard' else o.appointment_type end,
    o.status, o.scheduled_start, o.scheduled_end, o.location_type, o.location_name, o.external_calendar_id,
    o.external_event_id, null, o.notes, o.created_at, o.updated_at
from old_scheduling.appointments o;

-- activity.events -> activity.events
insert into activity.events (
    id, client_id, contact_id, conversation_id, booking_id, event_type, source, metadata, created_at
)
select
    o.id, o.clinic_id, o.lead_id, o.conversation_id, o.appointment_id,
    case o.event_type
        when 'lead_created' then 'contact_created'
        when 'lead_contacted' then 'contact_stage_changed'
        when 'lead_qualified' then 'contact_stage_changed'
        when 'consultation_booked' then 'booking_created'
        when 'consultation_attended' then 'booking_attended'
        when 'consultation_no_show' then 'booking_no_show'
        when 'consultation_rescheduled' then 'booking_rescheduled'
        when 'consultation_cancelled' then 'booking_canceled'
        when 'procedure_booked' then 'order_confirmed'
        when 'customer_message_received' then 'contact_message_received'
        when 'staff_message_sent_dashboard' then 'member_message_sent_dashboard'
        when 'staff_message_sent_whatsapp_app' then 'member_message_sent_whatsapp_app'
        else o.event_type end,
    case o.source when 'staff' then 'member' else o.source end,
    case o.event_type
        when 'lead_contacted' then jsonb_build_object('to', 'contacted') || coalesce(o.metadata, '{}'::jsonb)
        when 'lead_qualified' then jsonb_build_object('to', 'qualified') || coalesce(o.metadata, '{}'::jsonb)
        else o.metadata end,
    o.created_at
from old_activity.events o;

-- activity.message_trace_steps -> activity.message_trace_steps
insert into activity.message_trace_steps (
    id, trace_id, occurred_at, step, outcome, duration_ms, client_id, conversation_id, message_id,
    external_message_id, channel, n8n_execution_id, detail, instance
)
overriding system value
select
    o.id, o.trace_id, o.occurred_at,
    case o.step when 'webhook.clinic_unresolved' then 'webhook.client_unresolved' else o.step end,
    o.outcome, o.duration_ms, o.clinic_id, o.conversation_id, o.message_id, o.external_message_id, o.channel,
    o.n8n_execution_id, o.detail, o.instance
from old_activity.message_trace_steps o;

-- channels.channel_integrations -> channels.channel_integrations
insert into channels.channel_integrations (
    id, client_id, channel, status, display_name, phone_number_id, whatsapp_business_id, page_id,
    instagram_business_id, access_token, webhook_verify_token, pin, last_verified_at, last_error, created_at,
    updated_at, meta_business_id, verified_name, account_status, account_review_status, phone_quality_rating,
    phone_status, name_status, is_healthy, health_level, last_problem_code, last_problem_message,
    last_webhook_at, last_health_event_at, telegram_bot_id, telegram_bot_username, webhook_status,
    webhook_registered_at
)
select
    o.id, o.clinic_id, o.channel, o.status, o.display_name, o.phone_number_id, o.whatsapp_business_id, o.page_id,
    o.instagram_business_id, o.access_token, o.webhook_verify_token, o.pin, o.last_verified_at, o.last_error,
    o.created_at, o.updated_at, o.meta_business_id, o.verified_name, o.account_status, o.account_review_status,
    o.phone_quality_rating, o.phone_status, o.name_status, o.is_healthy, o.health_level, o.last_problem_code,
    o.last_problem_message, o.last_webhook_at, o.last_health_event_at, o.telegram_bot_id,
    o.telegram_bot_username, o.webhook_status, o.webhook_registered_at
from old_channels.channel_integrations o;

-- activity.notifications -> activity.notifications
insert into activity.notifications (
    id, client_id, type, title, message, contact_id, conversation_id, booking_id, channel_integration_id, link,
    is_read, read_at, created_at
)
select
    o.id, o.clinic_id,
    case o.type when 'NEW_LEAD' then 'NEW_CONTACT' when 'APPOINTMENT_BOOKED' then 'BOOKING_CREATED'
        when 'APPOINTMENT_RESCHEDULED' then 'BOOKING_RESCHEDULED' when 'APPOINTMENT_CANCELLED' then 'BOOKING_CANCELED' else o.type end,
    o.title, o.message, o.lead_id, o.conversation_id, o.appointment_id, o.channel_integration_id,
    replace(replace(o.link, '/dashboard/appointments/', '/dashboard/bookings/'), '/dashboard/leads/', '/dashboard/contacts/'),
    o.is_read, o.read_at, o.created_at
from old_activity.notifications o;

-- billing.accounts -> billing.accounts
insert into billing.accounts (
    id, client_id, currency, wallet_balance, included_credit_balance, reserved_amount, created_at, updated_at
)
select
    o.id, o.clinic_id, o.currency, o.wallet_balance, o.included_credit_balance, o.reserved_amount, o.created_at,
    o.updated_at
from old_billing.accounts o;

-- billing.channel_account_settings -> billing.channel_account_settings
insert into billing.channel_account_settings (
    channel_integration_id, client_id, provider_billing, omni_usage_billing, reason, updated_by, created_at,
    updated_at
)
select
    o.channel_integration_id, o.clinic_id, o.provider_billing, o.omni_usage_billing, o.reason, o.updated_by,
    o.created_at, o.updated_at
from old_billing.channel_account_settings o;

-- billing.rate_cards -> billing.rate_cards
insert into billing.rate_cards (
    id, code, name, description, client_id, is_default, is_active, created_at, updated_at
)
select
    o.id, o.code, o.name, o.description, o.clinic_id, o.is_default, o.is_active, o.created_at, o.updated_at
from old_billing.rate_cards o;

-- billing.plans -> billing.plans
insert into billing.plans (
    id, code, name, description, price, currency, billing_period, included_usage_credit, rate_card_id, is_active,
    sort_order, created_at, updated_at
)
select
    o.id, o.code, o.name, o.description, o.price, o.currency, o.billing_period, o.included_usage_credit,
    o.rate_card_id, o.is_active, o.sort_order, o.created_at, o.updated_at
from old_billing.plans o;

-- billing.subscriptions -> billing.subscriptions
insert into billing.subscriptions (
    id, client_id, plan_id, status, current_period_start, current_period_end, cancel_at_period_end,
    past_due_since, ended_at, created_at, updated_at
)
select
    o.id, o.clinic_id, o.plan_id, o.status, o.current_period_start, o.current_period_end, o.cancel_at_period_end,
    o.past_due_since, o.ended_at, o.created_at, o.updated_at
from old_billing.subscriptions o;

-- billing.rates -> billing.rates
insert into billing.rates (
    id, rate_card_id, event_type, country_code, operator, provider, provider_billing, unit, provider_cost,
    client_rate, currency, effective_from, effective_to, notes, created_by, created_at
)
select
    o.id, o.rate_card_id, o.event_type, o.country_code, o.operator, o.provider, o.provider_billing, o.unit,
    o.provider_cost, o.client_rate, o.currency, o.effective_from, o.effective_to, o.notes, o.created_by,
    o.created_at
from old_billing.rates o;

-- billing.usage_records -> billing.usage_records
insert into billing.usage_records (
    id, client_id, billing_account_id, idempotency_key, event_type, channel, channel_integration_id, quantity,
    unit, country_code, operator, provider, provider_billing, rate_id, rate_card_id, rate_source,
    unit_provider_cost, unit_price, provider_cost, amount, reserved_amount, credit_amount, wallet_amount,
    refunded_amount, currency, charge_status, provider_outcome, failure_reason, release_reason, message_id,
    conversation_id, campaign_id, source, actor, occurred_at, reserved_at, settled_at, released_at, refunded_at,
    created_at, updated_at
)
select
    o.id, o.clinic_id, o.billing_account_id, o.idempotency_key, o.event_type, o.channel,
    o.channel_integration_id, o.quantity, o.unit, o.country_code, o.operator, o.provider, o.provider_billing,
    o.rate_id, o.rate_card_id,
    case o.rate_source when 'clinic' then 'client' else o.rate_source end,
    o.unit_provider_cost, o.unit_price, o.provider_cost, o.amount, o.reserved_amount, o.credit_amount,
    o.wallet_amount, o.refunded_amount, o.currency, o.charge_status, o.provider_outcome, o.failure_reason,
    o.release_reason, o.message_id, o.conversation_id, o.campaign_id, o.source, o.actor, o.occurred_at,
    o.reserved_at, o.settled_at, o.released_at, o.refunded_at, o.created_at, o.updated_at
from old_billing.usage_records o;

-- billing.ledger_entries -> billing.ledger_entries
insert into billing.ledger_entries (
    id, seq, client_id, billing_account_id, entry_type, balance_type, amount, balance_after, currency,
    idempotency_key, usage_record_id, subscription_id, plan_id, source, actor, reason, reference, correlation_id,
    created_at
)
select
    o.id, o.seq, o.clinic_id, o.billing_account_id, o.entry_type, o.balance_type, o.amount, o.balance_after,
    o.currency, o.idempotency_key, o.usage_record_id, o.subscription_id, o.plan_id, o.source, o.actor, o.reason,
    o.reference, o.correlation_id, o.created_at
from old_billing.ledger_entries o;

-- billing.plan_entitlements -> billing.plan_entitlements
insert into billing.plan_entitlements (
    id, plan_id, entitlement_key, value, created_at, updated_at
)
select
    o.id, o.plan_id,
    case o.entitlement_key when 'max_agents' then 'max_users' when 'ai_agent' then 'ai_assistant' else o.entitlement_key end,
    o.value, o.created_at, o.updated_at
from old_billing.plan_entitlements o;

-- channels.tiktok_integrations -> channels.tiktok_integrations
insert into channels.tiktok_integrations (
    id, client_id, status, open_id, union_id, display_name, avatar_url, access_token, refresh_token,
    token_expires_at, refresh_token_expires_at, is_healthy, last_problem_message, created_at, updated_at
)
select
    o.id, o.clinic_id, o.status, o.open_id, o.union_id, o.display_name, o.avatar_url, o.access_token,
    o.refresh_token, o.token_expires_at, o.refresh_token_expires_at, o.is_healthy, o.last_problem_message,
    o.created_at, o.updated_at
from old_channels.tiktok_integrations o;

-- channels.whatsapp_health_events -> channels.whatsapp_health_events
insert into channels.whatsapp_health_events (
    id, client_id, channel_integration_id, event_type, severity, status, code, message, raw_metadata,
    occurred_at, created_at
)
select
    o.id, o.clinic_id, o.channel_integration_id, o.event_type, o.severity, o.status, o.code, o.message,
    o.raw_metadata, o.occurred_at, o.created_at
from old_channels.whatsapp_health_events o;

-- channels.whatsapp_templates -> channels.whatsapp_templates
insert into channels.whatsapp_templates (
    id, client_id, meta_template_id, name, category, language, status, header_type, header_content, body, footer,
    buttons_json, variables_json, rejection_reason, created_at, updated_at, channel_integration_id,
    quality_rating, previous_category, current_category, components, last_meta_event_at
)
select
    o.id, o.clinic_id, o.meta_template_id, o.name, o.category, o.language, o.status, o.header_type,
    o.header_content, o.body, o.footer, o.buttons_json, o.variables_json, o.rejection_reason, o.created_at,
    o.updated_at, o.channel_integration_id, o.quality_rating, o.previous_category, o.current_category,
    o.components, o.last_meta_event_at
from old_channels.whatsapp_templates o;


-- config.settings: only settings changed by hand (updated_by is not 'seed'), under their new key names; they overwrite the
-- seeded default. Settings whose key no longer exists are listed and skipped.
create temp table settings_mapped as
select s.section, s.key as old_key,
       case when s.section = 'Cache' and s.key = 'ClinicsMinutes' then 'ClientsMinutes'
            when s.section = 'Cache' and s.key = 'ProceduresMinutes' then 'ServicesMinutes'
            when s.section = 'Cache' and s.key = 'StaffLookupMinutes' then 'MembershipsMinutes'
            when s.section = 'Dashboard' and s.key = 'LeadsPageSize' then 'ContactsPageSize'
            when s.section = 'WebScraping' and s.key = 'MaxSourcesPerClinic' then 'MaxSourcesPerClient'
            else s.key end as new_key,
       s.value, s.description, s.updated_by
from old_config.settings s
where s.updated_by is distinct from 'seed';

create temp table settings_to_copy as
select m.* from settings_mapped m
where exists (select 1 from config.settings t where t.section = m.section and t.key = m.new_key);

update config.settings t
set value = c.value, description = coalesce(c.description, t.description), updated_by = c.updated_by
from settings_to_copy c
where t.section = c.section and t.key = c.new_key;

\echo
\echo === Settings changed in the old project but no longer existing (not copied) ===
select m.section, m.old_key as key, m.value from settings_mapped m
where not exists (select 1 from settings_to_copy c where c.section = m.section and c.old_key = m.old_key)
order by 1, 2;

-- crm.inbox_settings -> crm.inbox_settings
insert into crm.inbox_settings (
    id, client_id, channel_integration_id, ai_bot_new_conversations, ai_bot_reopened_conversations, created_at,
    updated_at
)
select
    o.id, o.clinic_id, o.channel_integration_id, o.ai_bot_new_conversations, o.ai_bot_reopened_conversations,
    o.created_at, o.updated_at
from old_crm.inbox_settings o;

-- marketing.campaigns -> marketing.campaigns
insert into marketing.campaigns (
    id, client_id, name, whatsapp_template_id, status, scheduled_at, started_at, completed_at,
    created_by_user_id, created_at, updated_at, campaign_type, channel, audience_type, audience_filters
)
select
    o.id, o.clinic_id, o.name, o.whatsapp_template_id, o.status, o.scheduled_at, o.started_at, o.completed_at,
    o.created_by_user_id, o.created_at, o.updated_at, o.campaign_type, o.channel,
    case o.audience_type when 'reactivation_no_consultation' then 'reactivation_no_booking' else o.audience_type end,
    pg_temp.map_audience_filters(o.audience_filters)
from old_marketing.campaigns o;

-- crm.messages -> crm.messages
insert into crm.messages (
    id, client_id, conversation_id, contact_id, direction, sender_type, channel, message_type, content,
    external_message_id, delivery_status, is_ai_generated, sent_at, received_at, created_at, origin,
    whatsapp_template_id, campaign_id, campaign_recipient_id, delivered_at, read_at, failed_at, deleted_at,
    failure_code, failure_reason, metadata_json, trace_id
)
select
    o.id, o.clinic_id, o.conversation_id, o.lead_id, o.direction,
    case o.sender_type when 'lead' then 'contact' when 'staff' then 'member' else o.sender_type end,
    o.channel, o.message_type, o.content, o.external_message_id, o.delivery_status, o.is_ai_generated, o.sent_at,
    o.received_at, o.created_at, o.origin, o.whatsapp_template_id, o.campaign_id, null, o.delivered_at,
    o.read_at, o.failed_at, o.deleted_at, o.failure_code, o.failure_reason, o.metadata_json, o.trace_id
from old_crm.messages o;

-- scheduling.procedure_bookings -> crm.orders
insert into crm.orders (
    id, client_id, contact_id, service_id, booking_id, status, quoted_amount, deposit_amount, final_amount,
    currency_code, service_date, notes, created_at, updated_at
)
select
    o.id, o.clinic_id, o.lead_id, o.procedure_id, o.appointment_id,
    case o.status when 'booked' then 'confirmed' else o.status end,
    o.quoted_amount, o.deposit_amount, o.final_amount, o.currency_code, o.procedure_date, o.notes, o.created_at,
    o.updated_at
from old_scheduling.procedure_bookings o;


-- identity.identity_users -> identity.users, then the admins. Owner rule: the earliest membership of a clinic is its owner.
with owners as (
    select distinct on (cu.clinic_id) cu.clinic_id, cu.user_id
    from old_identity.clinic_users cu
    order by cu.clinic_id, cu.created_at, cu.id
)
insert into identity.users (
    id, role, full_name, language, is_active, last_login_at, user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp, phone_number, phone_number_confirmed,
    two_factor_enabled, lockout_end, lockout_enabled, access_failed_count, created_at, updated_at
) select
    u.id,
    case when exists (select 1 from owners ow where ow.user_id = u.id) then 'client' else 'member' end,
    (select c.claim_value from old_identity.identity_user_claims c where c.user_id = u.id and c.claim_type = 'full_name' order by c.id limit 1),
    null,
    true,
    null,
    u.user_name, u.normalized_user_name, u.email, u.normalized_email,
    u.email_confirmed, u.password_hash, u.security_stamp, u.concurrency_stamp, u.phone_number, u.phone_number_confirmed,
    u.two_factor_enabled, u.lockout_end, u.lockout_enabled, u.access_failed_count,
    coalesce((select min(cu.created_at) from old_identity.clinic_users cu where cu.user_id = u.id), now()),
    coalesce((select min(cu.created_at) from old_identity.clinic_users cu where cu.user_id = u.id), now())
from old_identity.identity_users u;

-- admin.admin_users -> identity.users, role admin: the same id (as text) and password hash, new stamps.
insert into identity.users (
    id, role, full_name, language, is_active, last_login_at, user_name, normalized_user_name, email, normalized_email,
    email_confirmed, password_hash, security_stamp, concurrency_stamp, phone_number, phone_number_confirmed,
    two_factor_enabled, lockout_end, lockout_enabled, access_failed_count, created_at, updated_at
) select
    a.id::text, 'admin', a.full_name, null, a.is_active, a.last_login_at, a.email, upper(a.email), a.email, upper(a.email),
    true, a.password_hash, gen_random_uuid()::text, gen_random_uuid()::text, null, false,
    false, a.locked_until, true, a.failed_logins, a.created_at, a.updated_at
from old_admin.admin_users a;

-- identity.clinic_users -> identity.client_users
insert into identity.client_users (
    id, client_id, user_id, is_active, created_at, updated_at
)
select
    o.id, o.clinic_id, o.user_id, o.is_active, o.created_at, o.updated_at
from old_identity.clinic_users o;


-- identity.identity_user_claims -> identity.user_claims (the 'full_name' claim became identity.users.full_name)
insert into identity.user_claims (id, user_id, claim_type, claim_value)
overriding system value
select c.id, c.user_id, c.claim_type, c.claim_value
from old_identity.identity_user_claims c
where c.claim_type <> 'full_name';

-- identity.identity_user_logins -> identity.user_logins
insert into identity.user_logins (
    login_provider, provider_key, provider_display_name, user_id
)
select
    o.login_provider, o.provider_key, o.provider_display_name, o.user_id
from old_identity.identity_user_logins o;

-- identity.identity_user_tokens -> identity.user_tokens
insert into identity.user_tokens (
    user_id, login_provider, name, value
)
select
    o.user_id, o.login_provider, o.name, o.value
from old_identity.identity_user_tokens o;

-- knowledge.knowledge_documents -> knowledge.knowledge_documents
insert into knowledge.knowledge_documents (
    id, client_id, title, category, content, is_active, created_at, updated_at, source_type, original_file_name,
    mime_type, file_size_bytes, source_url
)
select
    o.id, o.clinic_id, o.title,
    case o.category when 'procedure' then 'service' when 'consultation' then 'booking' else o.category end,
    o.content, o.is_active, o.created_at, o.updated_at, o.source_type, o.original_file_name, o.mime_type,
    o.file_size_bytes, o.source_url
from old_knowledge.knowledge_documents o;

-- knowledge.knowledge_chunks -> knowledge.knowledge_chunks
insert into knowledge.knowledge_chunks (
    id, client_id, knowledge_document_id, chunk_index, content, embedding, created_at, updated_at
)
select
    o.id, o.clinic_id, o.knowledge_document_id, o.chunk_index, o.content, o.embedding, o.created_at,
    o.updated_at
from old_knowledge.knowledge_chunks o;

-- knowledge.knowledge_retrieval_benchmark_cases -> knowledge.knowledge_retrieval_benchmark_cases
insert into knowledge.knowledge_retrieval_benchmark_cases (
    id, client_id, question, expected_document_id, expected_chunk_id, case_type, is_reviewed, reviewed_at,
    source_chunk_hash, source_chunk_preview, source_document_title, is_stale, stale_reason, created_at,
    updated_at, generation_id
)
select
    o.id, o.clinic_id, o.question, o.expected_document_id, o.expected_chunk_id, o.case_type, o.is_reviewed,
    o.reviewed_at, o.source_chunk_hash, o.source_chunk_preview, o.source_document_title, o.is_stale,
    o.stale_reason, o.created_at, o.updated_at, o.generation_id
from old_knowledge.knowledge_retrieval_benchmark_cases o;

-- knowledge.knowledge_retrieval_benchmark_generations -> knowledge.knowledge_retrieval_benchmark_generations
insert into knowledge.knowledge_retrieval_benchmark_generations (
    id, client_id, status, chunks_sent, sent_chunks, questions_returned, cases_created, rejected_count,
    rejected_json, raw_response, error_message, completed_at, created_at
)
select
    o.id, o.clinic_id, o.status, o.chunks_sent, o.sent_chunks, o.questions_returned, o.cases_created,
    o.rejected_count, o.rejected_json, o.raw_response, o.error_message, o.completed_at, o.created_at
from old_knowledge.knowledge_retrieval_benchmark_generations o;

-- knowledge.knowledge_retrieval_benchmark_runs -> knowledge.knowledge_retrieval_benchmark_runs
insert into knowledge.knowledge_retrieval_benchmark_runs (
    id, client_id, case_scope, status, started_at, completed_at, total_cases, processed_cases, scored_cases,
    stale_cases, error_cases, chunk_top1_accuracy, chunk_top3_accuracy, chunk_top5_accuracy,
    document_top1_accuracy, document_top3_accuracy, document_top5_accuracy, chunk_mrr, document_mrr,
    average_latency_ms, embedding_model, vector_dimension, chunk_size_tokens, chunk_overlap_tokens,
    similarity_method, top_k, minimum_similarity, indexed_chunk_count, avg_chunk_chars, error_summary,
    created_at, generation_id
)
select
    o.id, o.clinic_id, o.case_scope, o.status, o.started_at, o.completed_at, o.total_cases, o.processed_cases,
    o.scored_cases, o.stale_cases, o.error_cases, o.chunk_top1_accuracy, o.chunk_top3_accuracy,
    o.chunk_top5_accuracy, o.document_top1_accuracy, o.document_top3_accuracy, o.document_top5_accuracy,
    o.chunk_mrr, o.document_mrr, o.average_latency_ms, o.embedding_model, o.vector_dimension,
    o.chunk_size_tokens, o.chunk_overlap_tokens, o.similarity_method, o.top_k, o.minimum_similarity,
    o.indexed_chunk_count, o.avg_chunk_chars, o.error_summary, o.created_at, o.generation_id
from old_knowledge.knowledge_retrieval_benchmark_runs o;

-- knowledge.knowledge_retrieval_benchmark_results -> knowledge.knowledge_retrieval_benchmark_results
insert into knowledge.knowledge_retrieval_benchmark_results (
    id, client_id, benchmark_run_id, benchmark_case_id, question, expected_document_id, expected_chunk_id,
    expected_document_title, expected_chunk_preview, expected_chunk_rank, expected_document_best_rank,
    expected_chunk_score, expected_below_threshold, chunk_top1_pass, chunk_top3_pass, chunk_top5_pass,
    document_top1_pass, document_top3_pass, document_top5_pass, result_classification, stale_reason,
    error_message, returned_count, retrieved_json, latency_ms, created_at, generation_id
)
select
    o.id, o.clinic_id, o.benchmark_run_id, o.benchmark_case_id, o.question, o.expected_document_id,
    o.expected_chunk_id, o.expected_document_title, o.expected_chunk_preview, o.expected_chunk_rank,
    o.expected_document_best_rank, o.expected_chunk_score, o.expected_below_threshold, o.chunk_top1_pass,
    o.chunk_top3_pass, o.chunk_top5_pass, o.document_top1_pass, o.document_top3_pass, o.document_top5_pass,
    o.result_classification, o.stale_reason, o.error_message, o.returned_count, o.retrieved_json, o.latency_ms,
    o.created_at, o.generation_id
from old_knowledge.knowledge_retrieval_benchmark_results o;

-- knowledge.knowledge_search_settings -> knowledge.knowledge_search_settings
insert into knowledge.knowledge_search_settings (
    id, client_id, embedding_model, vector_dimension, similarity_method, vector_index_type, chunk_size_tokens,
    chunk_overlap_tokens, top_k, minimum_similarity, created_at, updated_at
)
select
    o.id, o.clinic_id, o.embedding_model, o.vector_dimension, o.similarity_method, o.vector_index_type,
    o.chunk_size_tokens, o.chunk_overlap_tokens, o.top_k, o.minimum_similarity, o.created_at, o.updated_at
from old_knowledge.knowledge_search_settings o;

-- knowledge.knowledge_website_sources -> knowledge.knowledge_website_sources
insert into knowledge.knowledge_website_sources (
    id, client_id, start_url, normalized_start_url, host, crawl_mode, category, is_active, status,
    last_scraped_at, boilerplate_block_hashes, created_at, updated_at
)
select
    o.id, o.clinic_id, o.start_url, o.normalized_start_url, o.host, o.crawl_mode,
    case o.category when 'procedure' then 'service' when 'consultation' then 'booking' else o.category end,
    o.is_active, o.status, o.last_scraped_at, o.boilerplate_block_hashes, o.created_at, o.updated_at
from old_knowledge.knowledge_website_sources o;

-- knowledge.knowledge_website_pages -> knowledge.knowledge_website_pages
insert into knowledge.knowledge_website_pages (
    id, client_id, website_source_id, url, normalized_url, canonical_url, title, http_status, content_type,
    content_hash, etag, last_modified_header, status, failure_reason, depth, knowledge_document_id,
    duplicate_of_page_id, links, missing_count, removed_at, first_discovered_at, last_seen_at, last_scraped_at,
    created_at, updated_at
)
select
    o.id, o.clinic_id, o.website_source_id, o.url, o.normalized_url, o.canonical_url, o.title, o.http_status,
    o.content_type, o.content_hash, o.etag, o.last_modified_header, o.status, o.failure_reason, o.depth,
    o.knowledge_document_id, o.duplicate_of_page_id, o.links, o.missing_count, o.removed_at,
    o.first_discovered_at, o.last_seen_at, o.last_scraped_at, o.created_at, o.updated_at
from old_knowledge.knowledge_website_pages o;

-- knowledge.knowledge_website_scrape_runs -> knowledge.knowledge_website_scrape_runs
insert into knowledge.knowledge_website_scrape_runs (
    id, client_id, website_source_id, status, started_at, completed_at, pages_discovered, pages_processed,
    pages_indexed, pages_new, pages_changed, pages_unchanged, pages_skipped, pages_duplicate, pages_failed,
    pages_removed, error_summary, created_at
)
select
    o.id, o.clinic_id, o.website_source_id, o.status, o.started_at, o.completed_at, o.pages_discovered,
    o.pages_processed, o.pages_indexed, o.pages_new, o.pages_changed, o.pages_unchanged, o.pages_skipped,
    o.pages_duplicate, o.pages_failed, o.pages_removed, o.error_summary, o.created_at
from old_knowledge.knowledge_website_scrape_runs o;

-- marketing.campaign_recipients -> marketing.campaign_recipients
insert into marketing.campaign_recipients (
    id, client_id, campaign_id, contact_id, conversation_id, message_id, phone_number, variables_json, status,
    failure_reason, queued_at, sent_at, delivered_at, read_at, failed_at, created_at, updated_at,
    external_message_id, booking_id, skip_reason, failure_code, replied_at, booked_at
)
select
    o.id, o.clinic_id, o.campaign_id, o.lead_id, o.conversation_id, o.message_id, o.phone_number,
    o.variables_json, o.status, o.failure_reason, o.queued_at, o.sent_at, o.delivered_at, o.read_at, o.failed_at,
    o.created_at, o.updated_at, o.external_message_id, o.appointment_id, o.skip_reason, o.failure_code,
    o.replied_at, o.booked_at
from old_marketing.campaign_recipients o;

-- crm.messages.campaign_recipient_id (cycle with campaign_recipients.message_id): set once both tables exist
update crm.messages m set campaign_recipient_id = o.campaign_recipient_id
from old_crm.messages o where o.id = m.id and o.campaign_recipient_id is not null;

-- scheduling.clinic_availability_exceptions -> scheduling.availability_exceptions
insert into scheduling.availability_exceptions (
    id, client_id, date, is_closed, start_time, end_time, reason, created_at, updated_at
)
select
    o.id, o.clinic_id, o.date, o.is_closed, o.start_time, o.end_time, o.reason, o.created_at, o.updated_at
from old_scheduling.clinic_availability_exceptions o;

-- scheduling.clinic_availability_rules -> scheduling.availability_rules
insert into scheduling.availability_rules (
    id, client_id, day_of_week, is_open, start_time, end_time, created_at, updated_at
)
select
    o.id, o.clinic_id, o.day_of_week, o.is_open, o.start_time, o.end_time, o.created_at, o.updated_at
from old_scheduling.clinic_availability_rules o;

-- scheduling.calendar_integrations -> scheduling.calendar_integrations
insert into scheduling.calendar_integrations (
    id, client_id, provider, status, external_connection_ref, account_display_name, selected_calendar_id,
    selected_calendar_name, sync_enabled, is_healthy, last_problem_message, last_synced_at, created_at,
    updated_at, access_token, refresh_token, token_expires_at
)
select
    o.id, o.clinic_id, o.provider, o.status, o.external_connection_ref, o.account_display_name,
    o.selected_calendar_id, o.selected_calendar_name, o.sync_enabled, o.is_healthy, o.last_problem_message,
    o.last_synced_at, o.created_at, o.updated_at, o.access_token, o.refresh_token, o.token_expires_at
from old_scheduling.calendar_integrations o;

-- scheduling.appointment_calendar_syncs -> scheduling.booking_calendar_syncs
insert into scheduling.booking_calendar_syncs (
    id, booking_id, calendar_integration_id, external_event_id, status, last_request_id, last_error, created_at,
    updated_at, last_operation
)
select
    o.id, o.appointment_id, o.calendar_integration_id, o.external_event_id, o.status, o.last_request_id,
    o.last_error, o.created_at, o.updated_at, o.last_operation
from old_scheduling.appointment_calendar_syncs o;

-- scheduling.clinic_booking_settings -> scheduling.booking_settings
insert into scheduling.booking_settings (
    id, client_id, default_booking_duration_minutes, buffer_minutes, minimum_booking_notice_minutes,
    maximum_advance_booking_days, created_at, updated_at
)
select
    o.id, o.clinic_id, o.default_consultation_duration_minutes, o.buffer_minutes,
    o.minimum_booking_notice_minutes, o.maximum_advance_booking_days, o.created_at, o.updated_at
from old_scheduling.clinic_booking_settings o;

-- scheduling.calendar_integration_calendars -> scheduling.calendar_integration_calendars
insert into scheduling.calendar_integration_calendars (
    id, calendar_integration_id, external_calendar_id, name, is_primary, created_at
)
select
    o.id, o.calendar_integration_id, o.external_calendar_id, o.name, o.is_primary, o.created_at
from old_scheduling.calendar_integration_calendars o;


-- admin.admin_audit_log: admin_user_id (uuid) -> user_id (text), clinic_id -> client_id
insert into admin.admin_audit_log (id, user_id, admin_email, action, entity_type, entity_id, client_id, details, created_at)
select l.id, l.admin_user_id::text, l.admin_email, l.action, l.entity_type, l.entity_id, l.clinic_id, l.details, l.created_at
from old_admin.admin_audit_log l;


-- ===================================================================================
-- 4. Identity columns continue after the copied ids
-- ===================================================================================

do $$
begin
    perform setval(pg_get_serial_sequence('activity.api_route_stats', 'id'), coalesce((select max(id) from activity.api_route_stats), 1), (select count(*) > 0 from activity.api_route_stats));
    perform setval(pg_get_serial_sequence('activity.app_logs', 'id'), coalesce((select max(id) from activity.app_logs), 1), (select count(*) > 0 from activity.app_logs));
    perform setval(pg_get_serial_sequence('activity.message_trace_steps', 'id'), coalesce((select max(id) from activity.message_trace_steps), 1), (select count(*) > 0 from activity.message_trace_steps));
    perform setval(pg_get_serial_sequence('billing.ledger_entries', 'seq'), coalesce((select max(seq) from billing.ledger_entries), 1), (select count(*) > 0 from billing.ledger_entries));
    perform setval(pg_get_serial_sequence('identity.user_claims', 'id'), coalesce((select max(id) from identity.user_claims), 1), (select count(*) > 0 from identity.user_claims));
end $$;

-- ===================================================================================
-- 5. Row-count report (old vs new) and the verdict
-- ===================================================================================

create temp table copy_report (tbl text primary key, old_rows bigint not null, expected_rows bigint not null, new_rows bigint not null);

insert into copy_report values ('activity.api_route_stats', (select count(*) from old_activity.api_route_stats), (select count(*) from old_activity.api_route_stats), (select count(*) from activity.api_route_stats));
insert into copy_report values ('activity.app_logs', (select count(*) from old_activity.app_logs), (select count(*) from old_activity.app_logs), (select count(*) from activity.app_logs));
insert into copy_report values ('core.clients', (select count(*) from old_core.clinics), (select count(*) from old_core.clinics), (select count(*) from core.clients));
insert into copy_report values ('core.services', (select count(*) from old_core.procedures), (select count(*) from old_core.procedures), (select count(*) from core.services));
insert into copy_report values ('crm.contacts', (select count(*) from old_crm.leads), (select count(*) from old_crm.leads), (select count(*) from crm.contacts));
insert into copy_report values ('crm.conversations', (select count(*) from old_crm.conversations), (select count(*) from old_crm.conversations), (select count(*) from crm.conversations));
insert into copy_report values ('scheduling.bookings', (select count(*) from old_scheduling.appointments), (select count(*) from old_scheduling.appointments), (select count(*) from scheduling.bookings));
insert into copy_report values ('activity.events', (select count(*) from old_activity.events), (select count(*) from old_activity.events), (select count(*) from activity.events));
insert into copy_report values ('activity.message_trace_steps', (select count(*) from old_activity.message_trace_steps), (select count(*) from old_activity.message_trace_steps), (select count(*) from activity.message_trace_steps));
insert into copy_report values ('channels.channel_integrations', (select count(*) from old_channels.channel_integrations), (select count(*) from old_channels.channel_integrations), (select count(*) from channels.channel_integrations));
insert into copy_report values ('activity.notifications', (select count(*) from old_activity.notifications), (select count(*) from old_activity.notifications), (select count(*) from activity.notifications));
insert into copy_report values ('billing.accounts', (select count(*) from old_billing.accounts), (select count(*) from old_billing.accounts), (select count(*) from billing.accounts));
insert into copy_report values ('billing.channel_account_settings', (select count(*) from old_billing.channel_account_settings), (select count(*) from old_billing.channel_account_settings), (select count(*) from billing.channel_account_settings));
insert into copy_report values ('billing.rate_cards', (select count(*) from old_billing.rate_cards), (select count(*) from old_billing.rate_cards), (select count(*) from billing.rate_cards));
insert into copy_report values ('billing.plans', (select count(*) from old_billing.plans), (select count(*) from old_billing.plans), (select count(*) from billing.plans));
insert into copy_report values ('billing.subscriptions', (select count(*) from old_billing.subscriptions), (select count(*) from old_billing.subscriptions), (select count(*) from billing.subscriptions));
insert into copy_report values ('billing.rates', (select count(*) from old_billing.rates), (select count(*) from old_billing.rates), (select count(*) from billing.rates));
insert into copy_report values ('billing.usage_records', (select count(*) from old_billing.usage_records), (select count(*) from old_billing.usage_records), (select count(*) from billing.usage_records));
insert into copy_report values ('billing.ledger_entries', (select count(*) from old_billing.ledger_entries), (select count(*) from old_billing.ledger_entries), (select count(*) from billing.ledger_entries));
insert into copy_report values ('billing.plan_entitlements', (select count(*) from old_billing.plan_entitlements), (select count(*) from old_billing.plan_entitlements), (select count(*) from billing.plan_entitlements));
insert into copy_report values ('channels.tiktok_integrations', (select count(*) from old_channels.tiktok_integrations), (select count(*) from old_channels.tiktok_integrations), (select count(*) from channels.tiktok_integrations));
insert into copy_report values ('channels.whatsapp_health_events', (select count(*) from old_channels.whatsapp_health_events), (select count(*) from old_channels.whatsapp_health_events), (select count(*) from channels.whatsapp_health_events));
insert into copy_report values ('channels.whatsapp_templates', (select count(*) from old_channels.whatsapp_templates), (select count(*) from old_channels.whatsapp_templates), (select count(*) from channels.whatsapp_templates));
insert into copy_report values ('config.settings', (select count(*) from old_config.settings), (select count(*) from pg_temp.settings_to_copy), (select count(*) from config.settings s join pg_temp.settings_to_copy c on c.section = s.section and c.new_key = s.key and s.value = c.value));
insert into copy_report values ('crm.inbox_settings', (select count(*) from old_crm.inbox_settings), (select count(*) from old_crm.inbox_settings), (select count(*) from crm.inbox_settings));
insert into copy_report values ('marketing.campaigns', (select count(*) from old_marketing.campaigns), (select count(*) from old_marketing.campaigns), (select count(*) from marketing.campaigns));
insert into copy_report values ('crm.messages', (select count(*) from old_crm.messages), (select count(*) from old_crm.messages), (select count(*) from crm.messages));
insert into copy_report values ('crm.orders', (select count(*) from old_scheduling.procedure_bookings), (select count(*) from old_scheduling.procedure_bookings), (select count(*) from crm.orders));
insert into copy_report values ('identity.users', (select count(*) from old_identity.identity_users), (select count(*) from old_identity.identity_users) + (select count(*) from old_admin.admin_users), (select count(*) from identity.users));
insert into copy_report values ('identity.client_users', (select count(*) from old_identity.clinic_users), (select count(*) from old_identity.clinic_users), (select count(*) from identity.client_users));
insert into copy_report values ('identity.user_claims', (select count(*) from old_identity.identity_user_claims), (select count(*) from old_identity.identity_user_claims where claim_type <> 'full_name'), (select count(*) from identity.user_claims));
insert into copy_report values ('identity.user_logins', (select count(*) from old_identity.identity_user_logins), (select count(*) from old_identity.identity_user_logins), (select count(*) from identity.user_logins));
insert into copy_report values ('identity.user_tokens', (select count(*) from old_identity.identity_user_tokens), (select count(*) from old_identity.identity_user_tokens), (select count(*) from identity.user_tokens));
insert into copy_report values ('knowledge.knowledge_documents', (select count(*) from old_knowledge.knowledge_documents), (select count(*) from old_knowledge.knowledge_documents), (select count(*) from knowledge.knowledge_documents));
insert into copy_report values ('knowledge.knowledge_chunks', (select count(*) from old_knowledge.knowledge_chunks), (select count(*) from old_knowledge.knowledge_chunks), (select count(*) from knowledge.knowledge_chunks));
insert into copy_report values ('knowledge.knowledge_retrieval_benchmark_cases', (select count(*) from old_knowledge.knowledge_retrieval_benchmark_cases), (select count(*) from old_knowledge.knowledge_retrieval_benchmark_cases), (select count(*) from knowledge.knowledge_retrieval_benchmark_cases));
insert into copy_report values ('knowledge.knowledge_retrieval_benchmark_generations', (select count(*) from old_knowledge.knowledge_retrieval_benchmark_generations), (select count(*) from old_knowledge.knowledge_retrieval_benchmark_generations), (select count(*) from knowledge.knowledge_retrieval_benchmark_generations));
insert into copy_report values ('knowledge.knowledge_retrieval_benchmark_runs', (select count(*) from old_knowledge.knowledge_retrieval_benchmark_runs), (select count(*) from old_knowledge.knowledge_retrieval_benchmark_runs), (select count(*) from knowledge.knowledge_retrieval_benchmark_runs));
insert into copy_report values ('knowledge.knowledge_retrieval_benchmark_results', (select count(*) from old_knowledge.knowledge_retrieval_benchmark_results), (select count(*) from old_knowledge.knowledge_retrieval_benchmark_results), (select count(*) from knowledge.knowledge_retrieval_benchmark_results));
insert into copy_report values ('knowledge.knowledge_search_settings', (select count(*) from old_knowledge.knowledge_search_settings), (select count(*) from old_knowledge.knowledge_search_settings), (select count(*) from knowledge.knowledge_search_settings));
insert into copy_report values ('knowledge.knowledge_website_sources', (select count(*) from old_knowledge.knowledge_website_sources), (select count(*) from old_knowledge.knowledge_website_sources), (select count(*) from knowledge.knowledge_website_sources));
insert into copy_report values ('knowledge.knowledge_website_pages', (select count(*) from old_knowledge.knowledge_website_pages), (select count(*) from old_knowledge.knowledge_website_pages), (select count(*) from knowledge.knowledge_website_pages));
insert into copy_report values ('knowledge.knowledge_website_scrape_runs', (select count(*) from old_knowledge.knowledge_website_scrape_runs), (select count(*) from old_knowledge.knowledge_website_scrape_runs), (select count(*) from knowledge.knowledge_website_scrape_runs));
insert into copy_report values ('marketing.campaign_recipients', (select count(*) from old_marketing.campaign_recipients), (select count(*) from old_marketing.campaign_recipients), (select count(*) from marketing.campaign_recipients));
insert into copy_report values ('scheduling.availability_exceptions', (select count(*) from old_scheduling.clinic_availability_exceptions), (select count(*) from old_scheduling.clinic_availability_exceptions), (select count(*) from scheduling.availability_exceptions));
insert into copy_report values ('scheduling.availability_rules', (select count(*) from old_scheduling.clinic_availability_rules), (select count(*) from old_scheduling.clinic_availability_rules), (select count(*) from scheduling.availability_rules));
insert into copy_report values ('scheduling.calendar_integrations', (select count(*) from old_scheduling.calendar_integrations), (select count(*) from old_scheduling.calendar_integrations), (select count(*) from scheduling.calendar_integrations));
insert into copy_report values ('scheduling.booking_calendar_syncs', (select count(*) from old_scheduling.appointment_calendar_syncs), (select count(*) from old_scheduling.appointment_calendar_syncs), (select count(*) from scheduling.booking_calendar_syncs));
insert into copy_report values ('scheduling.booking_settings', (select count(*) from old_scheduling.clinic_booking_settings), (select count(*) from old_scheduling.clinic_booking_settings), (select count(*) from scheduling.booking_settings));
insert into copy_report values ('scheduling.calendar_integration_calendars', (select count(*) from old_scheduling.calendar_integration_calendars), (select count(*) from old_scheduling.calendar_integration_calendars), (select count(*) from scheduling.calendar_integration_calendars));
insert into copy_report values ('admin.admin_audit_log', (select count(*) from old_admin.admin_audit_log), (select count(*) from old_admin.admin_audit_log), (select count(*) from admin.admin_audit_log));

\echo
\echo === Row counts: old project vs new project (new may differ from old only where listed in the script header) ===
select tbl as "table", old_rows as old, expected_rows as expected, new_rows as new,
       case when expected_rows = new_rows then 'ok' else 'MISMATCH' end as check
from copy_report order by tbl;

\echo
\echo === Owners and members (rule: the earliest membership of each client is the owner; check this list) ===
select c.name as client, u.email, u.role, cu.created_at as joined, cu.is_active as membership_active
from identity.client_users cu
join core.clients c on c.id = cu.client_id
join identity.users u on u.id = cu.user_id
order by c.name, cu.created_at, u.email;

\echo
\echo === Users without any client membership (cannot sign in; kept as role member) ===
select u.email from identity.users u
where u.role = 'member' and not exists (select 1 from identity.client_users cu where cu.user_id = u.id)
order by u.email;

select case when exists (select 1 from copy_report where expected_rows <> new_rows) then 'mismatch' else 'ok' end as result \gset
\echo
\echo Result: :result
do $$
begin
    if exists (select 1 from copy_report where expected_rows <> new_rows) then
        raise exception 'Row counts do not match: nothing was copied (the whole run is rolled back). See the MISMATCH rows above.';
    end if;
end $$;

-- Remove the link to the old project (and the password stored in the user mapping) before saving.
set client_min_messages = warning;
drop schema old_core, old_identity, old_crm, old_scheduling, old_channels, old_marketing, old_knowledge,
            old_activity, old_billing, old_config, old_admin cascade;
drop server old_project cascade;

commit;
\echo
\echo Copy finished and committed. Next: run ANALYZE, sign in as a migrated user, then rotate the old project keys.
