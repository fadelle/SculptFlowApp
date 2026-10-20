-- Assertions for test-copy.ps1: run against the NEW test database after copy-from-old.sql (data = the old demo seeds +
-- test-old-extras.sql). Every check prints one line; the first failure stops psql with a non-zero exit code.
-- Update this file together with copy-from-old.sql when a mapping changes.

\set ON_ERROR_STOP on
\set QUIET on

create function pg_temp.check(name text, ok boolean) returns void language plpgsql as $$
begin
    if ok is not true then raise exception 'FAILED: %', name; end if;
    raise notice 'ok: %', name;
end $$;

-- The link to the old project is gone, with its password.
select pg_temp.check('no foreign server left', (select count(*) from pg_foreign_server) = 0);
select pg_temp.check('no old_* schema left', (select count(*) from pg_namespace where nspname like 'old\_%') = 0);

-- Clients, services, contacts.
select pg_temp.check('every client is business type clinic, language en',
    (select count(*) from core.clients) = 2 and (select count(*) from core.clients where business_type_code = 'clinic' and default_language = 'en') = 2);
select pg_temp.check('booking_info and booking_duration carried over',
    (select booking_info from core.clients where name = 'Aurora Aesthetic Clinic') like 'Consultations last 30-45 minutes and cos%'
    and (select booking_duration from core.services where name = 'Botox & Fillers') = 20
    and (select booking_duration from core.services where name = 'Facelift' and client_id = 'de000000-0000-4000-8000-000000000001') = 45);
select pg_temp.check('medical_question lead in an open stage -> stage medical_question, qualification needs_human',
    exists (select 1 from crm.contacts where id = 'de000000-0000-4000-8000-000000000113' and stage_key = 'medical_question' and qualification = 'needs_human'));
select pg_temp.check('medical_question lead in a booked stage keeps its stage, qualification needs_human',
    exists (select 1 from crm.contacts where id = 'de000000-0000-4000-8000-000000000101' and stage_key = 'consultation_booked' and qualification = 'needs_human'));
select pg_temp.check('other leads keep status as stage_key and qualification',
    exists (select 1 from crm.contacts where id = 'de000000-0000-4000-8000-000000000100' and stage_key = 'surgery_booked' and qualification = 'hot')
    and exists (select 1 from crm.contacts where id = 'de000000-0000-4000-8000-000000000112' and stage_key = 'new' and qualification = 'spam'));
select pg_temp.check('contact languages become two-letter codes (names, Arabizi, tags), junk becomes null',
    (select string_agg(coalesce(preferred_language, '-'), ',' order by id) from crm.contacts
     where id between 'de000000-0000-4000-8000-000000000102' and 'de000000-0000-4000-8000-000000000111')
      = 'ar,ar,en,fr,es,ar,en,-,-,it'
    and (select count(*) from crm.contacts where preferred_language !~ '^[a-z]{2}$') = 0);
select pg_temp.check('contact external id, service and assigned_user carried over',
    exists (select 1 from crm.contacts where external_contact_id is not null)
    and (select count(*) from crm.contacts where service_id is not null) > 0
    and (select count(*) from crm.contacts where assigned_user_id is not null) = 0);

-- Bookings, orders.
select pg_temp.check('booking_type consultation -> standard',
    (select count(*) from scheduling.bookings where booking_type = 'consultation') = 0 and (select count(*) from scheduling.bookings where booking_type = 'standard') > 0);
select pg_temp.check('order status booked -> confirmed',
    (select count(*) from crm.orders where status = 'booked') = 0 and (select count(*) from crm.orders where status = 'confirmed') = 1
    and (select count(*) from crm.orders where service_date is not null) > 0);

-- Messages and events.
select pg_temp.check('message senders are contact/ai/member/system and members kept',
    (select count(*) from crm.messages where sender_type = 'member') > 0 and (select count(*) from crm.messages where sender_type = 'contact') > 0);
select pg_temp.check('message -> campaign recipient link restored', (select count(*) from crm.messages where campaign_recipient_id is not null) = 2);
select pg_temp.check('no old event types or sources remain',
    (select count(*) from activity.events where event_type ~ '^(lead_|consultation_|procedure_|customer_|staff_)' or source = 'staff') = 0);
select pg_temp.check('lead_qualified -> contact_stage_changed with metadata.to and the old metadata',
    exists (select 1 from activity.events where contact_id = 'de000000-0000-4000-8000-000000000100' and event_type = 'contact_stage_changed'
            and metadata ->> 'to' = 'qualified' and metadata ->> 'by' = 'ai'));
select pg_temp.check('lead_contacted -> contact_stage_changed with metadata.to = contacted',
    exists (select 1 from activity.events where event_type = 'contact_stage_changed' and metadata ->> 'to' = 'contacted'));
select pg_temp.check('every renamed event type exists',
    (select count(distinct event_type) from activity.events where event_type in
        ('contact_created', 'contact_message_received', 'member_message_sent_dashboard', 'member_message_sent_whatsapp_app',
         'booking_created', 'booking_attended', 'booking_no_show', 'booking_rescheduled', 'booking_canceled', 'order_confirmed')) = 10);
select pg_temp.check('event source staff -> member', (select count(*) from activity.events where source = 'member') > 0);

-- Notifications, campaigns, knowledge, billing.
select pg_temp.check('notification types renamed',
    (select count(distinct type) from activity.notifications where type in ('NEW_CONTACT', 'BOOKING_CREATED', 'BOOKING_RESCHEDULED', 'BOOKING_CANCELED')) = 4);
select pg_temp.check('notification links renamed, text_args default',
    (select count(*) from activity.notifications where link like '%/appointments/%' or link like '%/leads/%') = 0
    and (select count(*) from activity.notifications where link like '/dashboard/bookings/%' or link like '/dashboard/contacts/%') > 0
    and (select count(*) from activity.notifications where text_args <> '{}') = 0);
select pg_temp.check('campaign audience_type renamed',
    exists (select 1 from marketing.campaigns where id = 'de000000-0000-4000-8000-000000000701' and audience_type = 'reactivation_no_booking'));
select pg_temp.check('campaign audience filter keys renamed',
    (select audience_filters from marketing.campaigns where id = 'de000000-0000-4000-8000-000000000702') =
    '{"stageKeys":["new","qualified"],"qualifications":["hot","needs_human"],"serviceId":"de000000-0000-4000-8000-0000000000f1","inactiveDays":30}'::jsonb);
select pg_temp.check('campaign filters without old keys unchanged',
    (select audience_filters from marketing.campaigns where id = 'de000000-0000-4000-8000-000000000701') = '{"minDaysSinceLastContact": 90}'::jsonb);
select pg_temp.check('knowledge categories procedure/consultation -> service/booking',
    (select count(*) from knowledge.knowledge_documents where category in ('procedure', 'consultation')) = 0
    and (select count(*) from knowledge.knowledge_documents where category = 'service') > 0
    and (select count(*) from knowledge.knowledge_documents where category = 'booking') > 0
    and (select count(*) from knowledge.knowledge_website_sources where category = 'booking') = 1);
select pg_temp.check('plan entitlement keys renamed',
    (select count(*) from billing.plan_entitlements where entitlement_key in ('max_agents', 'ai_agent')) = 0
    and (select count(*) from billing.plan_entitlements where entitlement_key in ('max_users', 'ai_assistant')) = 2);
select pg_temp.check('usage rate_source clinic -> client',
    (select count(*) from billing.usage_records where rate_source = 'clinic') = 0 and (select count(*) from billing.usage_records where rate_source = 'client') = 4);
select pg_temp.check('ledger rows and sequence keep their values',
    (select count(*) from billing.ledger_entries) = 7 and (select min(seq) from billing.ledger_entries) is not null);

-- Logs, trace, timings.
select pg_temp.check('error-log category prefix renamed',
    exists (select 1 from activity.app_logs where category = 'Omni.App.Business.Services.Sample')
    and exists (select 1 from activity.app_logs where category = 'Microsoft.AspNetCore.Hosting')
    and (select count(*) from activity.app_logs where category like 'PlasticSurgery.%') = 0);
select pg_temp.check('trace step renamed',
    exists (select 1 from activity.message_trace_steps where step = 'webhook.client_unresolved')
    and exists (select 1 from activity.message_trace_steps where step = 'webhook.received')
    and not exists (select 1 from activity.message_trace_steps where step = 'webhook.clinic_unresolved'));
select pg_temp.check('api timings copied with p95', exists (select 1 from activity.api_route_stats where route = '/api/leads' and p95_ms = 40));

-- Settings: hand-changed ones under new keys, defaults untouched, vanished keys skipped.
select pg_temp.check('changed settings copied under the new keys',
    (select value from config.settings where section = 'Cache' and key = 'ClientsMinutes') = '7'
    and (select value from config.settings where section = 'Cache' and key = 'ServicesMinutes') = '8'
    and (select value from config.settings where section = 'Cache' and key = 'MembershipsMinutes') = '9'
    and (select value from config.settings where section = 'Dashboard' and key = 'ContactsPageSize') = '30'
    and (select value from config.settings where section = 'WebScraping' and key = 'MaxSourcesPerClient') = '11'
    and (select value from config.settings where section = 'Billing' and key = 'GracePeriodDays') = '12'
    and (select updated_by from config.settings where section = 'Billing' and key = 'GracePeriodDays') = 'admin@test');
select pg_temp.check('unchanged defaults keep the new seed values, vanished key not created',
    (select updated_by from config.settings where section = 'Billing' and key = 'Enabled') = 'seed'
    and (select value from config.settings where section = 'WebScraping' and key = 'UserAgent') like 'OmniBot/%'
    and not exists (select 1 from config.settings where section = 'Obsolete'));

-- Business types come from schema.sql, not from the old project.
select pg_temp.check('business types and stages untouched', (select count(*) from core.business_types) = 2 and (select count(*) from crm.pipeline_stages) = 21);

-- Users.
select pg_temp.check('owner is client, other members are members',
    (select role from identity.users where email = 'demo@aurora-demo.test') = 'client'
    and (select count(*) from identity.users where email in ('nurse@aurora-demo.test', 'reception@aurora-demo.test') and role = 'member') = 2);
select pg_temp.check('full_name claim became users.full_name, claims table keeps only the others',
    (select count(*) from identity.users where role <> 'admin' and full_name is not null) = 3
    and (select count(*) from identity.user_claims) = 1
    and exists (select 1 from identity.user_claims where claim_type = 'theme' and claim_value = 'dark')
    and not exists (select 1 from identity.user_claims where claim_type = 'full_name'));
select pg_temp.check('client users keep their password hash, names, active membership',
    (select count(*) from identity.users u join identity.client_users cu on cu.user_id = u.id where u.password_hash is not null and cu.is_active) = 3
    and (select count(*) from identity.users where role <> 'admin' and is_active and lockout_enabled) = 3);
select pg_temp.check('three admins with the old id as text, normalized names, new stamps',
    (select count(*) from identity.users where role = 'admin') = 3
    and (select count(*) from identity.users where role = 'admin' and id like 'ad000000-0000-4000-8000-00000000000_' and normalized_user_name = upper(email)
            and normalized_email = upper(email) and user_name = email and email_confirmed and security_stamp is not null and concurrency_stamp is not null
            and password_hash is not null) = 3);
select pg_temp.check('admin lockout and active flag carried over',
    (select access_failed_count from identity.users where email = 'locked@omni-test.test') = 5
    and (select lockout_end > now() from identity.users where email = 'locked@omni-test.test')
    and not (select is_active from identity.users where email = 'inactive@omni-test.test')
    and (select is_active from identity.users where email = 'admin@omni-test.test'));
select pg_temp.check('admin audit log: user_id text and client_id',
    (select count(*) from admin.admin_audit_log) = 2
    and exists (select 1 from admin.admin_audit_log where user_id = 'ad000000-0000-4000-8000-000000000001' and client_id = 'de000000-0000-4000-8000-000000000001' and action = 'clinic.update')
    and exists (select 1 from admin.admin_audit_log where user_id is null and admin_email = 'gone@omni-test.test'));

-- Identity columns continue after the copied ids.
do $$
declare r record; nxt bigint; mx bigint;
begin
    for r in select * from (values ('identity.user_claims', 'id'), ('billing.ledger_entries', 'seq'), ('activity.api_route_stats', 'id'),
                                    ('activity.app_logs', 'id'), ('activity.message_trace_steps', 'id')) v(t, c) loop
        execute format('select max(%s) from %s', r.c, r.t) into mx;
        execute format('select nextval(pg_get_serial_sequence(%L, %L))', r.t, r.c) into nxt;
        if nxt <= mx then raise exception 'FAILED: sequence of %.% is behind (next %, max %)', r.t, r.c, nxt, mx; end if;
    end loop;
    raise notice 'ok: identity sequences continue after the copied ids';
end $$;
