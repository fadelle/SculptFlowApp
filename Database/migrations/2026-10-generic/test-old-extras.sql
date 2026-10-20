-- Test fixture for test-copy.ps1: runs in the OLD test database, after the OLD schema.sql, seed-config.sql, seed.sql,
-- seed-demo.sql and admin-schema.sql. It adds the cases the demo seeds do not cover so every mapping in
-- copy-from-old.sql is exercised. Old names on purpose (allowlisted folder). Never run it anywhere else.
-- Placeholder password for the test admins is the demo seed's: Demo-ChangeMe-2026 (the demo users' hash is reused).

begin;

-- Events with every old type/source that has a new name (the demo seed has some; these add the rest).
insert into activity.events (clinic_id, lead_id, event_type, source, metadata)
select l.clinic_id, l.id, x.event_type, x.source, x.metadata::jsonb
from crm.leads l
cross join (values
    ('lead_qualified',                  'ai',    '{"by":"ai"}'),
    ('customer_message_received',       'whatsapp', '{}'),
    ('staff_message_sent_dashboard',    'staff', '{}'),
    ('staff_message_sent_whatsapp_app', 'staff', '{}'),
    ('consultation_rescheduled',        'ai',    '{}'),
    ('consultation_cancelled',          'ai',    '{}')
) as x(event_type, source, metadata)
where l.id = 'de000000-0000-4000-8000-000000000100';

-- A contact in a booked stage with a medical question (qualification becomes needs_human, the stage stays).
update crm.leads set qualification_status = 'medical_question' where id = 'de000000-0000-4000-8000-000000000101';

-- Contact languages as old free text: names, Arabizi, a tag, a code that is no language and junk (the copy keeps codes only).
update crm.leads set preferred_language = 'Arabic'   where id = 'de000000-0000-4000-8000-000000000102';
update crm.leads set preferred_language = 'Français' where id = 'de000000-0000-4000-8000-000000000105';
update crm.leads set preferred_language = 'Arabizi'  where id = 'de000000-0000-4000-8000-000000000107';
update crm.leads set preferred_language = 'EN-us'    where id = 'de000000-0000-4000-8000-000000000108';
update crm.leads set preferred_language = 'xx'       where id = 'de000000-0000-4000-8000-000000000109';
update crm.leads set preferred_language = '??'       where id = 'de000000-0000-4000-8000-000000000110';

-- Campaign audience filters in the old JSON shape.
update marketing.campaigns
set audience_filters = '{"leadStatuses":["new","qualified"],"qualificationStatuses":["hot","medical_question"],"procedureId":"de000000-0000-4000-8000-0000000000f1","inactiveDays":30}'::jsonb
where id = 'de000000-0000-4000-8000-000000000702';

-- A knowledge website source in an old category (documents already have 'procedure' and 'consultation').
update knowledge.knowledge_website_sources set category = 'consultation';

-- Settings changed by hand under old key names, one unchanged key, and one key that no longer exists.
update config.settings set value = '7',  updated_by = 'admin@test' where section = 'Cache' and key = 'ClinicsMinutes';
update config.settings set value = '8',  updated_by = 'admin@test' where section = 'Cache' and key = 'ProceduresMinutes';
update config.settings set value = '9',  updated_by = 'admin@test' where section = 'Cache' and key = 'StaffLookupMinutes';
update config.settings set value = '30', updated_by = 'admin@test' where section = 'Dashboard' and key = 'LeadsPageSize';
update config.settings set value = '11', updated_by = 'admin@test' where section = 'WebScraping' and key = 'MaxSourcesPerClinic';
update config.settings set value = '12', updated_by = 'admin@test' where section = 'Billing' and key = 'GracePeriodDays';
insert into config.settings (section, key, value, updated_by) values ('Obsolete', 'GoneSetting', 'x', 'admin@test');

-- Error log, message trace and API timings (the demo seed has none).
insert into activity.app_logs (occurred_at, level, category, message, fingerprint, clinic_id)
values (now() - interval '1 hour', 'Error', 'PlasticSurgery.Business.Services.Sample', 'sample failure', 'fp-1', 'de000000-0000-4000-8000-000000000001'),
       (now() - interval '2 hours', 'Warning', 'Microsoft.AspNetCore.Hosting', 'sample warning', 'fp-2', null);
insert into activity.message_trace_steps (occurred_at, trace_id, step, outcome, clinic_id)
values (now() - interval '1 hour', 'trace-1', 'webhook.clinic_unresolved', 'skipped', null),
       (now() - interval '1 hour', 'trace-1', 'webhook.received', 'ok', 'de000000-0000-4000-8000-000000000001');
insert into activity.api_route_stats (bucket_start, method, route, request_count, total_ms, p95_ms, max_ms)
values (date_trunc('hour', now()), 'GET', '/api/leads', 5, 100, 40, 60);

-- A user claim that is not the full name (the full_name claims become users.full_name).
insert into identity.identity_user_claims (user_id, claim_type, claim_value)
values ('de000000-0000-4000-8000-0000000000a1', 'theme', 'dark');

-- Admins: one normal, one locked out after failed sign-ins, one inactive. Same placeholder password hash as the demo users.
insert into admin.admin_users (id, email, full_name, password_hash, is_active, failed_logins, locked_until, last_login_at)
select 'ad000000-0000-4000-8000-000000000001', 'admin@omni-test.test', 'Test Admin', u.password_hash, true, 0, null, now() - interval '1 day'
from identity.identity_users u where u.email = 'demo@aurora-demo.test';
insert into admin.admin_users (id, email, full_name, password_hash, is_active, failed_logins, locked_until)
select 'ad000000-0000-4000-8000-000000000002', 'locked@omni-test.test', 'Locked Admin', u.password_hash, true, 5, now() + interval '15 minutes'
from identity.identity_users u where u.email = 'demo@aurora-demo.test';
insert into admin.admin_users (id, email, full_name, password_hash, is_active)
select 'ad000000-0000-4000-8000-000000000003', 'inactive@omni-test.test', 'Inactive Admin', u.password_hash, false
from identity.identity_users u where u.email = 'demo@aurora-demo.test';

insert into admin.admin_audit_log (id, admin_user_id, admin_email, action, entity_type, entity_id, clinic_id, details)
values ('ae000000-0000-4000-8000-000000000001', 'ad000000-0000-4000-8000-000000000001', 'admin@omni-test.test', 'clinic.update', 'clinic', 'de000000-0000-4000-8000-000000000001', 'de000000-0000-4000-8000-000000000001', 'renamed'),
       ('ae000000-0000-4000-8000-000000000002', null, 'gone@omni-test.test', 'staff.deactivate', 'staff', null, null, 'admin since deleted');

commit;
