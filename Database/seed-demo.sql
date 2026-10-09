-- =====================================================================
-- DEMO CLINIC SEED — a fully populated clinic for demos and screenshots.
--
--   Clinic     "Aurora Aesthetic Clinic" (slug demo-sculptflow, Miami, America/New_York)
--   Sign in    demo@aurora-demo.test   /   Demo-ChangeMe-2026      <-- PLACEHOLDER PASSWORD: CHANGE IT
--              (two more staff: nurse@ / reception@aurora-demo.test, same placeholder password)
--
-- Everything is FICTIONAL: people, 555-01xx phone numbers, .test e-mail domains, and every
-- token / id / calendar link below is a dummy value that works against no real service.
-- No real secret is stored here. The password column holds a one-way ASP.NET Identity hash of the
-- placeholder password above, not the password itself.
--
-- Covers: dashboard (leads across the pipeline, revenue, appointments), inbox (WhatsApp, Telegram,
-- Instagram, Facebook conversations: AI / human / approval modes, unread, service window), leads,
-- procedure bookings, appointments (+ calendar sync), staff, procedures, knowledge base (manual,
-- upload and website entries), WhatsApp templates, campaigns with recipients, notifications,
-- activity events, clinic info, availability + booking rules + date exceptions, channel
-- connections (+ health history), TikTok, billing (plan, subscription, wallet, usage, ledger).
--
-- Run after Database/schema.sql (and optionally seed-config.sql):
--   psql "<connection string of a NON-PRODUCTION database>" -f Database/seed-demo.sql
--
-- Re-runnable: it first removes the demo clinic (slug demo-sculptflow, which cascades to everything
-- of the clinic) and its three staff users, then recreates it all with fixed GUIDs, so it never
-- duplicates and the relative dates (days/hours before or after "now") are refreshed to look live.
-- It touches nothing else except the shared billing plan 'demo-growth' (upserted).
-- Does not touch the other seed (Database/seed.sql, slug demo-clinic).
--
-- Knowledge chunks carry a PLACEHOLDER embedding (constant vector), so the entries show as indexed
-- but semantic search over them is meaningless: use "re-index" in the Knowledge page (with a real
-- embeddings key) if you want real retrieval.
-- Billing note: this writes billing rows directly (balances equal the ledger sums); that is fine
-- for a demo clinic but is not how real money moves (BillingLedger.PostAsync).
-- =====================================================================

-- ---- helpers (session-local, vanish at disconnect) -------------------
-- Fixed, recognisable GUID for demo object n.
create or replace function pg_temp.did(n integer) returns uuid language sql immutable as
$$ select ('de000000-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid $$;

-- Clinic-local wall-clock moment: day offset from today (clinic time) at hh:mm, as timestamptz.
create or replace function pg_temp.at(day_offset integer, t time) returns timestamptz language sql stable as
$$ select ((date_trunc('day', now() at time zone 'America/New_York') + make_interval(days => day_offset) + t)
          at time zone 'America/New_York') $$;

-- Placeholder (constant) 1536-dim embedding text for the vector column.
create or replace function pg_temp.dummy_embedding() returns text language sql immutable as
$$ select '[' || array_to_string(array_fill(0.0255::real, array[1536]), ',') || ']' $$;

begin;

do $$
declare
  c uuid := pg_temp.did(1);                       -- the clinic
  -- placeholder password hash (ASP.NET Identity v3, PBKDF2) for 'Demo-ChangeMe-2026'
  pwd_hash constant text := 'AQAAAAEAAYagAAAAEIQ2jaZC5yJN9fYBnHdWfnpVgfoGC5Nt7tJvhk3HWJQZypOiEoLHKESvr0QdACNZkg==';
  v_plan uuid;
begin
  -- ---------------------------------------------------------------
  -- 0. Clean previous run (clinic cascades to all its data)
  -- ---------------------------------------------------------------
  delete from core.clinics where slug = 'demo-sculptflow';
  delete from identity.identity_users where id in ('de000000-0000-4000-8000-0000000000a1',
    'de000000-0000-4000-8000-0000000000a2', 'de000000-0000-4000-8000-0000000000a3');

  -- ---------------------------------------------------------------
  -- 1. Clinic + info the AI quotes
  -- ---------------------------------------------------------------
  insert into core.clinics (id, name, slug, phone, email, website, country_code, timezone,
                            address, operating_hours, consultation_info, is_active, created_at)
  values (c, 'Aurora Aesthetic Clinic', 'demo-sculptflow', '+1 305 555 0100', 'hello@aurora-demo.test',
          'https://aurora-demo.test', 'US', 'America/New_York',
          '500 Example Avenue, Suite 12, Miami, FL 33101 (fictional address)',
          'Mon-Fri 9:00-17:00, Sat 10:00-14:00, closed Sunday',
          'Consultations last 30-45 minutes and cost $75, credited toward your procedure. Bring a photo ID and a list of current medications. Free parking in the building garage.',
          true, now() - interval '120 days');

  -- ---------------------------------------------------------------
  -- 2. Staff (identity users + clinic memberships)
  -- ---------------------------------------------------------------
  insert into identity.identity_users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                                       password_hash, security_stamp, concurrency_stamp, lockout_enabled)
  values
    ('de000000-0000-4000-8000-0000000000a1', 'demo@aurora-demo.test', 'DEMO@AURORA-DEMO.TEST', 'demo@aurora-demo.test', 'DEMO@AURORA-DEMO.TEST', true, pwd_hash, 'DEMOSTAMP0000000000000000000000A1', 'de000000-0000-4000-8000-0000000000b1', true),
    ('de000000-0000-4000-8000-0000000000a2', 'nurse@aurora-demo.test', 'NURSE@AURORA-DEMO.TEST', 'nurse@aurora-demo.test', 'NURSE@AURORA-DEMO.TEST', true, pwd_hash, 'DEMOSTAMP0000000000000000000000A2', 'de000000-0000-4000-8000-0000000000b2', true),
    ('de000000-0000-4000-8000-0000000000a3', 'reception@aurora-demo.test', 'RECEPTION@AURORA-DEMO.TEST', 'reception@aurora-demo.test', 'RECEPTION@AURORA-DEMO.TEST', true, pwd_hash, 'DEMOSTAMP0000000000000000000000A3', 'de000000-0000-4000-8000-0000000000b3', true);

  insert into identity.identity_user_claims (user_id, claim_type, claim_value) values
    ('de000000-0000-4000-8000-0000000000a1', 'full_name', 'Dr. Amelia Hart'),
    ('de000000-0000-4000-8000-0000000000a2', 'full_name', 'Jordan Blake, RN'),
    ('de000000-0000-4000-8000-0000000000a3', 'full_name', 'Priya Shah');

  insert into identity.clinic_users (id, clinic_id, user_id, is_active, created_at) values
    (pg_temp.did(2), c, 'de000000-0000-4000-8000-0000000000a1', true, now() - interval '120 days'),
    (pg_temp.did(3), c, 'de000000-0000-4000-8000-0000000000a2', true, now() - interval '90 days'),
    (pg_temp.did(4), c, 'de000000-0000-4000-8000-0000000000a3', true, now() - interval '60 days');

  -- ---------------------------------------------------------------
  -- 3. Procedures
  -- ---------------------------------------------------------------
  insert into core.procedures (id, clinic_id, name, code, description, consultation_duration, is_active) values
    (pg_temp.did(10), c, 'Rhinoplasty',          'RHINO',      'Reshaping of the nose for function or appearance.', 45, true),
    (pg_temp.did(11), c, 'Liposuction',          'LIPO',       'Targeted removal of stubborn fat deposits.', 30, true),
    (pg_temp.did(12), c, 'Facelift',             'FACELIFT',   'Lifts and tightens the face and neck.', 45, true),
    (pg_temp.did(13), c, 'Breast Augmentation',  'BREAST-AUG', 'Implant-based breast enlargement.', 30, true),
    (pg_temp.did(14), c, 'Tummy Tuck',           'TUMMY-TUCK', 'Removes excess skin and tightens the abdomen.', 30, true),
    (pg_temp.did(15), c, 'Botox & Fillers',      'INJECTABLES','Non-surgical wrinkle and volume treatments.', 20, true),
    (pg_temp.did(16), c, 'Laser Skin Resurfacing','LASER',     'Currently paused while the laser is serviced.', 20, false);

  -- ---------------------------------------------------------------
  -- 4. Leads (spread across the whole pipeline)
  -- ---------------------------------------------------------------
  insert into crm.leads (id, clinic_id, procedure_id, full_name, first_name, last_name, phone, email, source, source_detail,
                         campaign_name, external_lead_id, status, qualification_status, preferred_language, country_code,
                         city, desired_timeline, notes, marketing_opt_in, opted_out_at, last_contact_at, next_followup_at, created_at) values
    (pg_temp.did(100), c, pg_temp.did(10), 'Sarah Mitchell',  'Sarah',  'Mitchell', '+1 305 555 0111', 'sarah.mitchell@example.test', 'instagram', 'Reel: nose reshaping FAQ', 'Spring Consultation Offer', 'demo-ig-001', 'surgery_booked', 'hot',  'en', 'US', 'Miami',       'Within 2 months', 'Wants a natural result. Deposit paid.', true, null, now() - interval '2 days', null, now() - interval '25 days'),
    (pg_temp.did(101), c, pg_temp.did(11), 'Daniel Brooks',   'Daniel', 'Brooks',   '+1 305 555 0112', 'daniel.brooks@example.test',  'facebook',  'Lead form', null, 'demo-fb-002', 'consultation_booked', 'warm', 'en', 'US', 'Coral Gables', '3 months', null, true, null, now() - interval '1 day', null, now() - interval '8 days'),
    (pg_temp.did(102), c, pg_temp.did(12), 'Emily Carter',    'Emily',  'Carter',   '+1 305 555 0113', 'emily.carter@example.test',   'google',    'Search: facelift miami', null, null, 'no_show', 'warm', 'en', 'US', 'Miami Beach', 'ASAP', 'Missed consultation, needs a recovery message.', true, null, now() - interval '2 days', now() + interval '1 day', now() - interval '12 days'),
    (pg_temp.did(103), c, null,            'Karim Nasser',    'Karim',  'Nasser',   '+1 305 555 0114', 'karim.nasser@example.test',   'website',   'Contact form', null, 'demo-web-004', 'new', 'unknown', 'ar', 'US', 'Hialeah', null, null, true, null, null, null, now() - interval '2 hours'),
    (pg_temp.did(104), c, pg_temp.did(13), 'Olivia Reed',     'Olivia', 'Reed',     '+1 305 555 0115', 'olivia.reed@example.test',    'instagram', 'DM', null, 'demo-ig-005', 'needs_human', 'needs_human', 'en', 'US', 'Miami', '1 month', 'Asked about financing and recovery time; AI handed off.', true, null, now() - interval '35 minutes', now() + interval '3 hours', now() - interval '3 days'),
    (pg_temp.did(105), c, pg_temp.did(14), 'Noah Bennett',    'Noah',   'Bennett',  '+1 305 555 0116', 'noah.bennett@example.test',   'website',   'Chat widget', null, 'demo-web-006', 'qualified', 'hot', 'en', 'US', 'Doral', 'Within 6 weeks', null, true, null, now() - interval '3 hours', null, now() - interval '5 days'),
    (pg_temp.did(106), c, pg_temp.did(15), 'Mia Torres',      'Mia',    'Torres',   '+1 305 555 0117', 'mia.torres@example.test',     'instagram', 'Story reply', 'Botox Event Invite', 'demo-ig-007', 'contacted', 'warm', 'es', 'US', 'Miami', 'This month', null, true, null, now() - interval '1 day', now() + interval '2 days', now() - interval '4 days'),
    (pg_temp.did(107), c, pg_temp.did(10), 'Liam Foster',     'Liam',   'Foster',   '+1 305 555 0118', 'liam.foster@example.test',    'facebook',  'Lead form', null, 'demo-fb-008', 'not_interested', 'cold', 'en', 'US', 'Orlando', null, 'Said price was too high in spring.', true, null, now() - interval '170 days', null, now() - interval '190 days'),
    (pg_temp.did(108), c, pg_temp.did(12), 'Ava Collins',     'Ava',    'Collins',  '+1 305 555 0119', 'ava.collins@example.test',    'google',    'Search', null, 'demo-g-009', 'lost', 'cold', 'en', 'US', 'Fort Lauderdale', null, null, true, null, now() - interval '120 days', null, now() - interval '140 days'),
    (pg_temp.did(109), c, pg_temp.did(13), 'Sophia Nguyen',   'Sophia', 'Nguyen',   '+1 305 555 0120', 'sophia.nguyen@example.test',  'referral',  'Referred by Lucas P.', null, null, 'consultation_attended', 'hot', 'en', 'US', 'Miami', '2 months', 'Quote sent, waiting for her decision.', true, null, now() - interval '3 days', now() + interval '1 day', now() - interval '14 days'),
    (pg_temp.did(110), c, pg_temp.did(11), 'Lucas Perry',     'Lucas',  'Perry',    '+1 305 555 0121', 'lucas.perry@example.test',    'instagram', 'Ad: summer body', null, 'demo-ig-010', 'surgery_booked', 'hot', 'en', 'US', 'Miami', null, 'Procedure completed, great reviews.', true, null, now() - interval '6 days', null, now() - interval '60 days'),
    (pg_temp.did(111), c, pg_temp.did(12), 'Isabella Rossi',  'Isabella','Rossi',   '+1 305 555 0122', 'isabella.rossi@example.test', 'website',   'Booking page', null, null, 'consultation_booked', 'warm', 'it', 'US', 'Key Biscayne', null, null, true, null, now() - interval '1 day', null, now() - interval '6 days'),
    (pg_temp.did(112), c, null,            'Ethan Wright',    'Ethan',  'Wright',   '+1 305 555 0123', null,                          'website',   'Contact form', null, 'demo-web-012', 'new', 'spam', 'en', null, null, null, 'Marked as spam.', true, null, null, null, now() - interval '9 days'),
    (pg_temp.did(113), c, pg_temp.did(15), 'Grace Kim',       'Grace',  'Kim',      '+1 305 555 0124', 'grace.kim@example.test',      'facebook',  'Messenger', null, 'demo-fb-013', 'contacted', 'medical_question', 'en', 'US', 'Miami', null, 'Asked a medical question; opted out of marketing.', false, now() - interval '10 days', now() - interval '10 days', null, now() - interval '30 days');

  -- ---------------------------------------------------------------
  -- 5. Channel connections (all dummy values)
  -- ---------------------------------------------------------------
  insert into channels.channel_integrations (id, clinic_id, channel, status, display_name, phone_number_id, whatsapp_business_id,
        page_id, instagram_business_id, access_token, webhook_verify_token, pin, last_verified_at, last_error,
        meta_business_id, verified_name, account_status, account_review_status, phone_quality_rating, phone_status, name_status,
        is_healthy, health_level, last_problem_code, last_problem_message, last_webhook_at, last_health_event_at,
        telegram_bot_id, telegram_bot_username, webhook_status, webhook_registered_at, provider, provider_sender_id, created_at) values
    (pg_temp.did(20), c, 'whatsapp', 'connected', '+1 305 555 0100', 'demo-sculptflow-phone-0001', 'demo-waba-0001',
        null, null, 'DEMO-NOT-A-REAL-TOKEN', 'demo-verify-token-whatsapp', null, now() - interval '1 hour', null,
        'demo-meta-business-0001', 'Aurora Aesthetic Clinic', 'APPROVED', 'APPROVED', 'GREEN', 'CONNECTED', 'APPROVED',
        true, 'healthy', null, null, now() - interval '5 minutes', now() - interval '3 days',
        null, null, null, null, null, null, now() - interval '100 days'),
    (pg_temp.did(21), c, 'instagram', 'connected', '@aurora.aesthetic.demo', null, null,
        'demo-page-0001', 'demo-ig-business-0001', 'DEMO-NOT-A-REAL-TOKEN', 'demo-verify-token-instagram', null, now() - interval '1 day', null,
        null, null, null, null, null, null, null, true, 'healthy', null, null, now() - interval '2 hours', null,
        null, null, null, null, null, null, now() - interval '95 days'),
    (pg_temp.did(22), c, 'facebook', 'error', 'Aurora Aesthetic (Facebook page)', null, null,
        'demo-page-0002', null, 'DEMO-NOT-A-REAL-TOKEN', 'demo-verify-token-facebook', null, now() - interval '9 days',
        'The access token expired. Reconnect the page.', null, null, null, null, null, null, null,
        false, 'error', 'TOKEN_EXPIRED', 'The access token expired. Reconnect the page.', now() - interval '9 days', now() - interval '9 days',
        null, null, null, null, null, null, now() - interval '95 days'),
    (pg_temp.did(23), c, 'telegram', 'connected', '@aurora_demo_bot', null, null,
        null, null, 'DEMO-NOT-A-REAL-TOKEN', 'demo-secret-token-telegram', null, now() - interval '2 days', null,
        null, null, null, null, null, null, null, true, 'healthy', null, null, now() - interval '20 minutes', null,
        '9000000001', 'aurora_demo_bot', 'active', now() - interval '80 days', null, null, now() - interval '80 days');

  insert into channels.whatsapp_health_events (id, clinic_id, channel_integration_id, event_type, severity, status, code, message, raw_metadata, occurred_at) values
    (pg_temp.did(30), c, pg_temp.did(20), 'phone_number_quality_update', 'warning', 'YELLOW', 'QUALITY_MEDIUM', 'Quality rating dropped to medium.', '{"demo": true}', now() - interval '12 days'),
    (pg_temp.did(31), c, pg_temp.did(20), 'phone_number_quality_update', 'info',    'GREEN',  'QUALITY_HIGH',   'Quality rating recovered to high.', '{"demo": true}', now() - interval '3 days'),
    (pg_temp.did(32), c, pg_temp.did(20), 'account_review_update',        'info',    'APPROVED', 'ACCOUNT_APPROVED', 'WhatsApp Business account review approved.', '{"demo": true}', now() - interval '98 days');

  insert into channels.tiktok_integrations (id, clinic_id, status, open_id, display_name, avatar_url, access_token, refresh_token,
                                            token_expires_at, refresh_token_expires_at, is_healthy)
  values (pg_temp.did(24), c, 'connected', 'demo-tiktok-open-id', '@aurora.aesthetic.demo', null, 'DEMO-NOT-A-REAL-TOKEN', 'DEMO-NOT-A-REAL-TOKEN',
          now() + interval '20 hours', now() + interval '300 days', true);

  -- ---------------------------------------------------------------
  -- 6. WhatsApp templates
  -- ---------------------------------------------------------------
  insert into channels.whatsapp_templates (id, clinic_id, channel_integration_id, meta_template_id, name, category, language, status,
        header_type, header_content, body, footer, buttons_json, variables_json, quality_rating, rejection_reason, last_meta_event_at, created_at) values
    (pg_temp.did(40), c, pg_temp.did(20), 'demo-tpl-0001', 'appointment_reminder', 'utility', 'en_US', 'approved', 'none', null,
        'Hi {{1}}, this is a reminder of your consultation at Aurora Aesthetic Clinic on {{2}} at {{3}}. Reply 1 to confirm or 2 to reschedule.',
        'Aurora Aesthetic Clinic', null, '["first_name","date","time"]', 'GREEN', null, now() - interval '60 days', now() - interval '70 days'),
    (pg_temp.did(41), c, pg_temp.did(20), 'demo-tpl-0002', 'spring_consultation_offer', 'marketing', 'en_US', 'approved', 'text', 'Spring offer',
        'Hi {{1}}, book a consultation this month and the $75 fee is credited toward any procedure. Reply BOOK and we will find you a time.',
        'Reply STOP to opt out', '[{"type":"quick_reply","text":"BOOK"},{"type":"quick_reply","text":"STOP"}]', '["first_name"]', 'GREEN', null, now() - interval '40 days', now() - interval '45 days'),
    (pg_temp.did(42), c, pg_temp.did(20), 'demo-tpl-0003', 'we_miss_you_checkin', 'marketing', 'en_US', 'approved', 'none', null,
        'Hi {{1}}, it has been a while! Still thinking about {{2}}? We would love to answer any questions and offer a complimentary follow-up chat.',
        'Reply STOP to opt out', '[{"type":"quick_reply","text":"Yes, tell me more"},{"type":"quick_reply","text":"STOP"}]', '["first_name","procedure"]', 'YELLOW', null, now() - interval '14 days', now() - interval '20 days'),
    (pg_temp.did(43), c, pg_temp.did(20), 'demo-tpl-0004', 'post_consultation_followup', 'utility', 'en_US', 'pending', 'none', null,
        'Hi {{1}}, thank you for visiting Aurora Aesthetic Clinic. Your personalised quote is ready. Reply here with any questions.',
        null, null, '["first_name"]', null, null, now() - interval '1 day', now() - interval '1 day'),
    (pg_temp.did(44), c, pg_temp.did(20), 'demo-tpl-0005', 'botox_event_invite', 'marketing', 'en_US', 'draft', 'image', null,
        'Hi {{1}}, you are invited to our Botox & Fillers evening on {{2}}. Limited spots, reply YES to reserve yours.',
        'Reply STOP to opt out', null, '["first_name","date"]', null, null, null, now() - interval '2 days'),
    (pg_temp.did(45), c, pg_temp.did(20), 'demo-tpl-0006', 'cheap_surgery_deals', 'marketing', 'en_US', 'rejected', 'none', null,
        'CHEAP surgery deals this week only!!! Call now!!!', null, null, null, null,
        'Rejected: the message makes promotional claims that violate the WhatsApp Business policy.', now() - interval '30 days', now() - interval '31 days');

  -- ---------------------------------------------------------------
  -- 7. Conversations
  --    (id, lead, channel, thread id, status, mode, minutes since last message, last direction, minutes since last customer message, unread?)
  -- ---------------------------------------------------------------
  insert into crm.conversations (id, clinic_id, lead_id, channel, external_thread_id, status, mode, ai_enabled, human_takeover,
        last_message_at, last_message_direction, last_customer_message_at, service_window_expires_at, last_read_at, created_at) values
    (pg_temp.did(200), c, pg_temp.did(100), 'whatsapp',  null,          'active',   'ai',       true,  false, now() - interval '2 days',    'inbound',  now() - interval '2 days',    now() - interval '1 day',  now() - interval '2 days', now() - interval '25 days'),
    (pg_temp.did(201), c, pg_temp.did(101), 'whatsapp',  null,          'active',   'ai',       true,  false, now() - interval '3 hours',   'outbound', now() - interval '4 hours',   now() + interval '20 hours', now() - interval '3 hours', now() - interval '8 days'),
    (pg_temp.did(202), c, pg_temp.did(102), 'whatsapp',  null,          'active',   'ai',       true,  false, now() - interval '2 days',    'outbound', now() - interval '5 days',    now() - interval '4 days',  now() - interval '2 days', now() - interval '12 days'),
    (pg_temp.did(203), c, pg_temp.did(103), 'telegram',  '7000000001',  'active',   'ai',       true,  false, now() - interval '2 hours',   'inbound',  now() - interval '2 hours',   now() + interval '22 hours', null,                    now() - interval '2 hours'),
    (pg_temp.did(204), c, pg_temp.did(104), 'instagram', 'demo-ig-thread-5', 'active', 'human',  false, true,  now() - interval '35 minutes','inbound',  now() - interval '35 minutes',now() + interval '23 hours', null,                    now() - interval '3 days'),
    (pg_temp.did(205), c, pg_temp.did(105), 'telegram',  '7000000002',  'active',   'approval', true,  false, now() - interval '3 hours',   'inbound',  now() - interval '3 hours',   now() + interval '21 hours', null,                    now() - interval '5 days'),
    (pg_temp.did(206), c, pg_temp.did(106), 'whatsapp',  null,          'active',   'ai',       true,  false, now() - interval '1 day',     'outbound', now() - interval '1 day',     now() - interval '0 hours', now() - interval '1 day',  now() - interval '4 days'),
    (pg_temp.did(207), c, pg_temp.did(107), 'whatsapp',  null,          'archived', 'ai',       true,  false, now() - interval '170 days',  'outbound', now() - interval '171 days',  now() - interval '170 days', now() - interval '170 days', now() - interval '190 days'),
    (pg_temp.did(208), c, pg_temp.did(108), 'whatsapp',  null,          'closed',   'ai',       true,  false, now() - interval '120 days',  'outbound', now() - interval '121 days',  now() - interval '120 days', now() - interval '120 days', now() - interval '140 days'),
    (pg_temp.did(209), c, pg_temp.did(109), 'whatsapp',  null,          'active',   'ai',       true,  false, now() - interval '3 days',    'outbound', now() - interval '3 days',    now() - interval '2 days',  now() - interval '3 days', now() - interval '14 days'),
    (pg_temp.did(210), c, pg_temp.did(111), 'facebook',  'demo-fb-thread-11','active','ai',      true,  false, now() - interval '1 day',     'outbound', now() - interval '1 day',     now() - interval '0 hours', now() - interval '1 day',  now() - interval '6 days'),
    (pg_temp.did(211), c, pg_temp.did(113), 'facebook',  'demo-fb-thread-13','closed','human',   false, true,  now() - interval '10 days',   'inbound',  now() - interval '10 days',   now() - interval '9 days',  now() - interval '10 days', now() - interval '30 days');

  -- ---------------------------------------------------------------
  -- 8. Messages  (conversation, lead, direction, sender, channel, origin, minutes ago, status, text)
  -- ---------------------------------------------------------------
  create temp table _m (n int, conv uuid, lead uuid, dir text, sender text, channel text, origin text,
                        mins_ago int, status text, body text, ai boolean) on commit drop;
  insert into _m values
   -- Sarah (WhatsApp)
   (1, pg_temp.did(200), pg_temp.did(100), 'inbound',  'lead',  'whatsapp', 'whatsapp_customer', 36000, null, 'Hi! I saw your reel about rhinoplasty. How much does it cost?', false),
   (2, pg_temp.did(200), pg_temp.did(100), 'outbound', 'ai',    'whatsapp', 'ai',               35995, 'read', 'Hi Sarah, thanks for reaching out! Rhinoplasty at Aurora starts from $6,000 and the final quote depends on your goals. A $75 consultation, credited toward the procedure, is the best first step. When are you hoping to have it done?', true),
   (3, pg_temp.did(200), pg_temp.did(100), 'inbound',  'lead',  'whatsapp', 'whatsapp_customer', 35980, null, 'Within the next 2 months, ideally.', false),
   (4, pg_temp.did(200), pg_temp.did(100), 'outbound', 'ai',    'whatsapp', 'ai',               35975, 'read', 'Great, we have openings this week. Would Thursday at 10:00 or Friday at 14:00 suit you?', true),
   (5, pg_temp.did(200), pg_temp.did(100), 'inbound',  'lead',  'whatsapp', 'whatsapp_customer', 35960, null, 'Thursday at 10 works.', false),
   (6, pg_temp.did(200), pg_temp.did(100), 'outbound', 'staff', 'whatsapp', 'dashboard',        2900,  'read', 'Hi Sarah, Dr. Hart here. Your surgery date is confirmed. Please read the preparation guide we sent and tell me if anything is unclear.', false),
   (7, pg_temp.did(200), pg_temp.did(100), 'inbound',  'lead',  'whatsapp', 'whatsapp_customer', 2880,  null, 'Thank you doctor, all clear. See you then!', false),
   -- Daniel (WhatsApp)
   (10, pg_temp.did(201), pg_temp.did(101), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 11500, null, 'Hello, I would like information about liposuction on the abdomen.', false),
   (11, pg_temp.did(201), pg_temp.did(101), 'outbound', 'ai',   'whatsapp', 'ai',                11495, 'read', 'Hello Daniel! Abdominal liposuction removes stubborn fat for a more contoured shape. Recovery is typically 1-2 weeks of rest. Would you like to book a 30-minute consultation?', true),
   (12, pg_temp.did(201), pg_temp.did(101), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 11480, null, 'Yes please, sometime tomorrow afternoon?', false),
   (13, pg_temp.did(201), pg_temp.did(101), 'outbound', 'ai',   'whatsapp', 'ai',                11475, 'read', 'Booked: tomorrow at 15:00 with Dr. Hart. I will send a reminder the day before.', true),
   (14, pg_temp.did(201), pg_temp.did(101), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 240,   null, 'Could you remind me where to park?', false),
   (15, pg_temp.did(201), pg_temp.did(101), 'outbound', 'ai',   'whatsapp', 'ai',                180,   'delivered', 'There is free parking in the building garage, level P1. Take the elevator to Suite 12.', true),
   -- Emily (WhatsApp, no show)
   (20, pg_temp.did(202), pg_temp.did(102), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 7500, null, 'Hi, I want to know about facelift options.', false),
   (21, pg_temp.did(202), pg_temp.did(102), 'outbound', 'ai',   'whatsapp', 'ai',                7495, 'read', 'Hi Emily! We offer several facelift techniques. A consultation will let Dr. Hart recommend the right one. Shall I book you in?', true),
   (22, pg_temp.did(202), pg_temp.did(102), 'outbound', 'system','whatsapp','campaign',           3000, 'delivered', 'Hi Emily, this is a reminder of your consultation at Aurora Aesthetic Clinic.', false),
   (23, pg_temp.did(202), pg_temp.did(102), 'outbound', 'ai',   'whatsapp', 'ai',                2850, 'sent', 'Hi Emily, we missed you today. Would you like to pick another time? We have openings this week.', true),
   -- Karim (Telegram, new, unread)
   (30, pg_temp.did(203), pg_temp.did(103), 'inbound',  'lead', 'telegram', 'telegram_customer', 125, null, 'Marhaba, do you do consultations in Arabic?', false),
   (31, pg_temp.did(203), pg_temp.did(103), 'inbound',  'lead', 'telegram', 'telegram_customer', 120, null, 'I am interested in a hair transplant but also nose surgery.', false),
   -- Olivia (Instagram, human handoff, unread)
   (40, pg_temp.did(204), pg_temp.did(104), 'inbound',  'lead', 'instagram','whatsapp_customer', 4300, null, 'Hi, how long is recovery after breast augmentation?', false),
   (41, pg_temp.did(204), pg_temp.did(104), 'outbound', 'ai',   'instagram','ai',                4295, 'delivered', 'Most patients return to desk work within a week and resume exercise after 4-6 weeks. Dr. Hart will personalise this for you.', true),
   (42, pg_temp.did(204), pg_temp.did(104), 'inbound',  'lead', 'instagram','whatsapp_customer', 40, null, 'Do you offer financing? And can I speak to someone about my medical history?', false),
   (43, pg_temp.did(204), pg_temp.did(104), 'outbound', 'system','instagram','system',            38, 'sent', 'Conversation handed to the team: the patient asked about financing and medical history.', false),
   (44, pg_temp.did(204), pg_temp.did(104), 'inbound',  'lead', 'instagram','whatsapp_customer', 35, null, 'Hello? Is someone there?', false),
   -- Noah (Telegram, approval mode)
   (50, pg_temp.did(205), pg_temp.did(105), 'inbound',  'lead', 'telegram', 'telegram_customer', 7200, null, 'What is the price range for a tummy tuck?', false),
   (51, pg_temp.did(205), pg_temp.did(105), 'outbound', 'staff','telegram', 'dashboard',         7100, 'delivered', 'Hi Noah, tummy tucks at Aurora typically range from $7,500 to $11,000 depending on the extent. A consultation gives you an exact quote.', false),
   (52, pg_temp.did(205), pg_temp.did(105), 'inbound',  'lead', 'telegram', 'telegram_customer', 180, null, 'Ok. Can I come this Saturday? And what should I bring?', false),
   -- Mia (WhatsApp, Spanish)
   (60, pg_temp.did(206), pg_temp.did(106), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 5800, null, 'Hola, cuanto cuesta el botox?', false),
   (61, pg_temp.did(206), pg_temp.did(106), 'outbound', 'ai',   'whatsapp', 'ai',                5795, 'read', 'Hola Mia! El botox comienza en $12 por unidad; la mayoria de las zonas requieren 20-40 unidades. Te gustaria agendar una cita de 20 minutos?', true),
   (62, pg_temp.did(206), pg_temp.did(106), 'outbound', 'ai',   'whatsapp', 'ai',                1500, 'failed', 'Hola Mia, te recordamos nuestra noche de Botox & Fillers.', true),
   -- Liam / Ava (old leads, campaign)
   (70, pg_temp.did(207), pg_temp.did(107), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 246000, null, 'Too expensive for me right now, sorry.', false),
   (71, pg_temp.did(208), pg_temp.did(108), 'outbound', 'ai',   'whatsapp', 'ai',                172800, 'read', 'Hi Ava, just checking in about your facelift enquiry.', true),
   -- Sophia (WhatsApp)
   (80, pg_temp.did(209), pg_temp.did(109), 'inbound',  'lead', 'whatsapp', 'whatsapp_customer', 20100, null, 'Hi! Lucas told me about you. I want to ask about augmentation.', false),
   (81, pg_temp.did(209), pg_temp.did(109), 'outbound', 'ai',   'whatsapp', 'ai',                20095, 'read', 'Welcome Sophia! Happy to help. Shall we schedule a consultation?', true),
   (82, pg_temp.did(209), pg_temp.did(109), 'outbound', 'staff','whatsapp', 'whatsapp_business_app', 4400, 'read', 'Hi Sophia, your quote is ready. Let me know once you have decided and I will hold a surgery date.', false),
   -- Isabella (Facebook)
   (90, pg_temp.did(210), pg_temp.did(111), 'inbound',  'lead', 'facebook', 'whatsapp_customer', 8600, null, 'Buongiorno, vorrei prenotare una consulenza per un lifting.', false),
   (91, pg_temp.did(210), pg_temp.did(111), 'outbound', 'ai',   'facebook', 'ai',                8595, 'read', 'Buongiorno Isabella! Ho prenotato una consulenza per voi tra 4 giorni alle 11:00.', true),
   -- Grace (Facebook, closed)
   (95, pg_temp.did(211), pg_temp.did(113), 'inbound',  'lead', 'facebook', 'whatsapp_customer', 14400, null, 'I take blood thinners. Is Botox safe for me? Also please stop sending promotions.', false),
   (96, pg_temp.did(211), pg_temp.did(113), 'outbound', 'staff','facebook', 'dashboard',         14390, 'read', 'Hi Grace, Dr. Hart will review your medication before any treatment, and we have removed you from promotions.', false);

  insert into crm.messages (id, clinic_id, conversation_id, lead_id, direction, sender_type, channel, origin, message_type, content,
                            delivery_status, is_ai_generated, sent_at, received_at, delivered_at, read_at, failed_at,
                            failure_code, failure_reason, created_at)
  select pg_temp.did(1000 + n), c, conv, lead, dir, sender, channel, origin, 'text', body, status, ai,
         case when dir = 'outbound' then now() - make_interval(mins => mins_ago) end,
         case when dir = 'inbound'  then now() - make_interval(mins => mins_ago) end,
         case when status in ('delivered','read') then now() - make_interval(mins => mins_ago) + interval '5 seconds' end,
         case when status = 'read' then now() - make_interval(mins => mins_ago) + interval '2 minutes' end,
         case when status = 'failed' then now() - make_interval(mins => mins_ago) + interval '3 seconds' end,
         case when status = 'failed' then '131047' end,
         case when status = 'failed' then 'Re-engagement message: more than 24 hours since the customer last replied.' end,
         now() - make_interval(mins => mins_ago)
  from _m;

  -- ---------------------------------------------------------------
  -- 9. Appointments + procedure bookings
  -- ---------------------------------------------------------------
  insert into scheduling.appointments (id, clinic_id, lead_id, procedure_id, appointment_type, status, scheduled_start, scheduled_end,
                                       location_type, location_name, notes, created_at) values
    (pg_temp.did(300), c, pg_temp.did(100), pg_temp.did(10), 'consultation', 'attended',  pg_temp.at(-18, '10:00'), pg_temp.at(-18, '10:45'), 'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Discussed goals; quote prepared.', now() - interval '25 days'),
    (pg_temp.did(301), c, pg_temp.did(100), pg_temp.did(10), 'surgery',      'booked',    pg_temp.at(21, '08:00'),  pg_temp.at(21, '11:00'),  'in_person', 'Aurora Surgical Suite', 'Pre-op bloodwork required.', now() - interval '10 days'),
    (pg_temp.did(302), c, pg_temp.did(101), pg_temp.did(11), 'consultation', 'confirmed', pg_temp.at(1, '15:00'),   pg_temp.at(1, '15:30'),   'in_person', 'Aurora Aesthetic Clinic, Suite 12', null, now() - interval '7 days'),
    (pg_temp.did(303), c, pg_temp.did(102), pg_temp.did(12), 'consultation', 'no_show',   pg_temp.at(-2, '11:00'),  pg_temp.at(-2, '11:45'),  'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Did not arrive, no call.', now() - interval '9 days'),
    (pg_temp.did(304), c, pg_temp.did(109), pg_temp.did(13), 'consultation', 'attended',  pg_temp.at(-4, '14:00'),  pg_temp.at(-4, '14:30'),  'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Wants 350cc implants. Quote sent.', now() - interval '12 days'),
    (pg_temp.did(305), c, pg_temp.did(110), pg_temp.did(11), 'consultation', 'attended',  pg_temp.at(-40, '09:30'), pg_temp.at(-40, '10:00'), 'in_person', 'Aurora Aesthetic Clinic, Suite 12', null, now() - interval '55 days'),
    (pg_temp.did(306), c, pg_temp.did(110), pg_temp.did(11), 'surgery',      'attended',  pg_temp.at(-8, '08:00'),  pg_temp.at(-8, '10:30'),  'in_person', 'Aurora Surgical Suite', 'Uneventful. Follow-up in 2 weeks.', now() - interval '35 days'),
    (pg_temp.did(307), c, pg_temp.did(111), pg_temp.did(12), 'consultation', 'booked',    pg_temp.at(4, '11:00'),   pg_temp.at(4, '11:45'),   'in_person', 'Aurora Aesthetic Clinic, Suite 12', null, now() - interval '1 day'),
    (pg_temp.did(308), c, pg_temp.did(106), pg_temp.did(15), 'consultation', 'rescheduled',pg_temp.at(-1, '16:00'), pg_temp.at(-1, '16:20'),  'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Moved to next week at patient request.', now() - interval '3 days'),
    (pg_temp.did(309), c, pg_temp.did(106), pg_temp.did(15), 'consultation', 'booked',    pg_temp.at(6, '16:00'),   pg_temp.at(6, '16:20'),   'in_person', 'Aurora Aesthetic Clinic, Suite 12', null, now() - interval '1 day'),
    (pg_temp.did(310), c, pg_temp.did(105), pg_temp.did(14), 'consultation', 'booked',    pg_temp.at(2, '10:00'),   pg_temp.at(2, '10:30'),   'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Saturday slot requested.', now() - interval '2 hours'),
    (pg_temp.did(311), c, pg_temp.did(108), pg_temp.did(12), 'consultation', 'canceled',  pg_temp.at(-100, '10:00'),pg_temp.at(-100, '10:45'), 'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Patient canceled, found another clinic.', now() - interval '130 days'),
    (pg_temp.did(312), c, pg_temp.did(104), pg_temp.did(13), 'consultation', 'booked',    pg_temp.at(3, '13:00'),   pg_temp.at(3, '13:30'),   'in_person', 'Aurora Aesthetic Clinic, Suite 12', 'Wants to discuss financing.', now() - interval '20 minutes');

  insert into scheduling.procedure_bookings (id, clinic_id, lead_id, procedure_id, appointment_id, status, quoted_amount, deposit_amount,
                                             final_amount, currency_code, procedure_date, notes, created_at) values
    (pg_temp.did(320), c, pg_temp.did(100), pg_temp.did(10), pg_temp.did(301), 'booked',       6500, 1500, null, 'USD', pg_temp.at(21, '08:00'), 'Deposit received.', now() - interval '10 days'),
    (pg_temp.did(321), c, pg_temp.did(109), pg_temp.did(13), pg_temp.did(304), 'quoted',       7800, null, null, 'USD', null, 'Awaiting decision.', now() - interval '4 days'),
    (pg_temp.did(322), c, pg_temp.did(110), pg_temp.did(11), pg_temp.did(306), 'completed',    5200, 1000, 5000, 'USD', pg_temp.at(-8, '08:00'), 'Final price after loyalty discount.', now() - interval '35 days'),
    (pg_temp.did(323), c, pg_temp.did(101), pg_temp.did(11), pg_temp.did(302), 'considering',  null, null, null, 'USD', null, null, now() - interval '7 days'),
    (pg_temp.did(324), c, pg_temp.did(111), pg_temp.did(12), pg_temp.did(307), 'deposit_paid', 14500, 2000, null, 'USD', null, 'Deposit paid ahead of consultation.', now() - interval '1 day'),
    (pg_temp.did(325), c, pg_temp.did(108), pg_temp.did(12), pg_temp.did(311), 'lost',         14000, null, null, 'USD', null, 'Went elsewhere.', now() - interval '130 days'),
    (pg_temp.did(326), c, pg_temp.did(107), pg_temp.did(10), null,             'canceled',     6000, null, null, 'USD', null, 'Declined due to price.', now() - interval '185 days'),
    (pg_temp.did(327), c, pg_temp.did(105), pg_temp.did(14), pg_temp.did(310), 'quoted',       9500, null, null, 'USD', null, null, now() - interval '2 hours');

  -- ---------------------------------------------------------------
  -- 10. Availability, booking rules, exceptions
  -- ---------------------------------------------------------------
  insert into scheduling.clinic_availability_rules (id, clinic_id, day_of_week, is_open, start_time, end_time) values
    (pg_temp.did(400), c, 0, false, '09:00', '17:00'),
    (pg_temp.did(401), c, 1, true,  '09:00', '17:00'),
    (pg_temp.did(402), c, 2, true,  '09:00', '17:00'),
    (pg_temp.did(403), c, 3, true,  '09:00', '17:00'),
    (pg_temp.did(404), c, 4, true,  '09:00', '17:00'),
    (pg_temp.did(405), c, 5, true,  '09:00', '17:00'),
    (pg_temp.did(406), c, 6, true,  '10:00', '14:00');

  insert into scheduling.clinic_booking_settings (id, clinic_id, default_consultation_duration_minutes, buffer_minutes,
                                                  minimum_booking_notice_minutes, maximum_advance_booking_days)
  values (pg_temp.did(410), c, 30, 10, 240, 60);

  insert into scheduling.clinic_availability_exceptions (id, clinic_id, date, is_closed, start_time, end_time, reason) values
    (pg_temp.did(420), c, (pg_temp.at(12, '12:00') at time zone 'America/New_York')::date, true,  null,    null,    'Staff training day'),
    (pg_temp.did(421), c, (pg_temp.at(19, '12:00') at time zone 'America/New_York')::date, false, '12:00', '16:00', 'Short day: surgeon at a conference'),
    (pg_temp.did(422), c, (pg_temp.at(26, '12:00') at time zone 'America/New_York')::date, true,  null,    null,    'Public holiday');

  -- ---------------------------------------------------------------
  -- 11. Calendar integrations (dummy references; no real tokens)
  -- ---------------------------------------------------------------
  insert into scheduling.calendar_integrations (id, clinic_id, provider, status, external_connection_ref, account_display_name,
        selected_calendar_id, selected_calendar_name, sync_enabled, is_healthy, last_problem_message, last_synced_at,
        access_token, refresh_token, token_expires_at) values
    (pg_temp.did(430), c, 'google',  'connected',    'demo-connection-ref-google', 'frontdesk@aurora-demo.test',
        'demo-calendar-primary', 'Aurora Consultations', true, true, null, now() - interval '10 minutes',
        'DEMO-NOT-A-REAL-TOKEN', 'DEMO-NOT-A-REAL-TOKEN', now() + interval '50 minutes'),
    (pg_temp.did(431), c, 'outlook', 'disconnected', null, null, null, null, false, true, null, null, null, null, null);

  insert into scheduling.calendar_integration_calendars (id, calendar_integration_id, external_calendar_id, name, is_primary) values
    (pg_temp.did(432), pg_temp.did(430), 'demo-calendar-primary', 'Aurora Consultations', true),
    (pg_temp.did(433), pg_temp.did(430), 'demo-calendar-surgery', 'Aurora Surgeries', false);

  insert into scheduling.appointment_calendar_syncs (id, appointment_id, calendar_integration_id, external_event_id, status, last_operation) values
    (pg_temp.did(434), pg_temp.did(302), pg_temp.did(430), 'demo-event-0001', 'synced', 'create'),
    (pg_temp.did(435), pg_temp.did(307), pg_temp.did(430), 'demo-event-0002', 'synced', 'create'),
    (pg_temp.did(436), pg_temp.did(311), pg_temp.did(430), 'demo-event-0003', 'canceled', 'cancel'),
    (pg_temp.did(437), pg_temp.did(310), pg_temp.did(430), null, 'pending', 'create');

  update scheduling.appointments set external_calendar_id = 'demo-calendar-primary', external_event_id = 'demo-event-0001' where id = pg_temp.did(302);

  -- ---------------------------------------------------------------
  -- 12. Knowledge base
  -- ---------------------------------------------------------------
  insert into knowledge.knowledge_documents (id, clinic_id, title, category, content, is_active, source_type,
                                             original_file_name, mime_type, file_size_bytes, source_url, created_at) values
    (pg_temp.did(500), c, 'About Aurora Aesthetic Clinic', 'general',
      'Aurora Aesthetic Clinic is a boutique plastic surgery practice led by Dr. Amelia Hart, board-certified, with over 15 years of experience. We offer surgical and non-surgical aesthetic treatments in a private, comfortable setting.', true, 'manual', null, null, null, null, now() - interval '100 days'),
    (pg_temp.did(501), c, 'Consultation: what to expect', 'consultation',
      'A consultation lasts 30-45 minutes and costs $75, which is credited toward your procedure. Dr. Hart reviews your goals and medical history, examines the area, explains options and risks, and gives a personalised quote. Please bring a photo ID and a list of medications.', true, 'manual', null, null, null, null, now() - interval '100 days'),
    (pg_temp.did(502), c, 'Pricing guide', 'pricing',
      'Typical starting prices: Rhinoplasty from $6,000; Liposuction from $4,500; Facelift from $12,000; Breast augmentation from $6,500; Tummy tuck from $7,500; Botox from $12 per unit. Final quotes depend on the extent of the procedure and are given after a consultation.', true, 'manual', null, null, null, null, now() - interval '90 days'),
    (pg_temp.did(503), c, 'Financing and payment', 'payment',
      'We accept cards, bank transfer and cash. Third-party medical financing with 6-24 month plans is available subject to approval. A deposit holds your surgery date and is applied to the final price.', true, 'manual', null, null, null, null, now() - interval '90 days'),
    (pg_temp.did(504), c, 'Cancellation policy', 'policy',
      'Please give at least 24 hours notice to cancel or reschedule a consultation. Surgery deposits are refundable up to 14 days before the procedure date; later cancellations may forfeit the deposit.', true, 'manual', null, null, null, null, now() - interval '80 days'),
    (pg_temp.did(505), c, 'Frequently asked questions', 'faq',
      'Q: How long is recovery? A: It depends on the procedure; most patients return to desk work within 1-2 weeks. Q: Is surgery painful? A: Discomfort is managed with medication. Q: Do you offer virtual consultations? A: Yes, by video for initial enquiries.', true, 'manual', null, null, null, null, now() - interval '70 days'),
    (pg_temp.did(506), c, 'Pre-operative instructions', 'preparation',
      'Stop smoking and blood-thinning supplements two weeks before surgery. Do not eat or drink after midnight the night before. Arrange for someone to drive you home and stay with you for the first night.', true, 'upload', 'pre-op-instructions.pdf', 'application/pdf', 184320, null, now() - interval '50 days'),
    (pg_temp.did(507), c, 'Rhinoplasty (from website)', 'procedure',
      'Rhinoplasty reshapes the nose to improve harmony with your face or to correct breathing issues. Procedures take 2-3 hours under general anaesthesia. Swelling subsides over several weeks and the final result appears after about a year.', true, 'website', null, null, null, 'https://aurora-demo.test/procedures/rhinoplasty', now() - interval '30 days'),
    (pg_temp.did(508), c, 'Botox and fillers (from website)', 'procedure',
      'Botox and dermal fillers smooth wrinkles and restore volume with minimal downtime. Results appear within days and last 3-6 months for Botox and 6-18 months for fillers. Treatments take about 20 minutes.', true, 'website', null, null, null, 'https://aurora-demo.test/procedures/injectables', now() - interval '30 days'),
    (pg_temp.did(509), c, 'Old winter promotion (archived)', 'pricing',
      'Winter promotion: 10% off facelifts booked before January. This offer has ended.', false, 'manual', null, null, null, null, now() - interval '200 days');

  -- placeholder embeddings (see header note)
  insert into knowledge.knowledge_chunks (id, clinic_id, knowledge_document_id, chunk_index, content, embedding)
  select pg_temp.did(600 + (id_n - 500)), c, pg_temp.did(id_n), 0, content, pg_temp.dummy_embedding()::vector
  from (select (right(id::text, 12)::int) as id_n, content from knowledge.knowledge_documents
        where clinic_id = c and is_active) d;

  insert into knowledge.knowledge_search_settings (id, clinic_id, embedding_model, vector_dimension, similarity_method, vector_index_type,
                                                   chunk_size_tokens, chunk_overlap_tokens, top_k, minimum_similarity)
  values (pg_temp.did(520), c, 'text-embedding-3-small', 1536, 'cosine', 'none', 400, 50, 5, 0.30);

  insert into knowledge.knowledge_website_sources (id, clinic_id, start_url, normalized_start_url, host, crawl_mode, category,
                                                   is_active, status, last_scraped_at) values
    (pg_temp.did(530), c, 'https://aurora-demo.test/', 'https://aurora-demo.test/', 'aurora-demo.test', 'crawl_site', 'procedure',
     true, 'completed', now() - interval '30 days');

  insert into knowledge.knowledge_website_pages (id, clinic_id, website_source_id, url, normalized_url, title, http_status, content_type,
                                                 content_hash, status, depth, knowledge_document_id, first_discovered_at, last_seen_at, last_scraped_at) values
    (pg_temp.did(531), c, pg_temp.did(530), 'https://aurora-demo.test/procedures/rhinoplasty', 'https://aurora-demo.test/procedures/rhinoplasty',
        'Rhinoplasty | Aurora Aesthetic Clinic', 200, 'text/html', repeat('a', 64), 'indexed', 1, pg_temp.did(507), now() - interval '30 days', now() - interval '30 days', now() - interval '30 days'),
    (pg_temp.did(532), c, pg_temp.did(530), 'https://aurora-demo.test/procedures/injectables', 'https://aurora-demo.test/procedures/injectables',
        'Botox & Fillers | Aurora Aesthetic Clinic', 200, 'text/html', repeat('b', 64), 'indexed', 1, pg_temp.did(508), now() - interval '30 days', now() - interval '30 days', now() - interval '30 days'),
    (pg_temp.did(533), c, pg_temp.did(530), 'https://aurora-demo.test/gallery', 'https://aurora-demo.test/gallery',
        'Gallery', 200, 'text/html', null, 'skipped', 1, null, now() - interval '30 days', now() - interval '30 days', now() - interval '30 days');

  insert into knowledge.knowledge_website_scrape_runs (id, clinic_id, website_source_id, status, started_at, completed_at,
        pages_discovered, pages_processed, pages_indexed, pages_new, pages_changed, pages_unchanged, pages_skipped, created_at) values
    (pg_temp.did(534), c, pg_temp.did(530), 'completed', now() - interval '30 days', now() - interval '30 days' + interval '45 seconds',
        3, 3, 2, 2, 0, 0, 1, now() - interval '30 days');

  -- ---------------------------------------------------------------
  -- 13. Campaigns + recipients
  -- ---------------------------------------------------------------
  insert into marketing.campaigns (id, clinic_id, name, whatsapp_template_id, status, campaign_type, channel, audience_type, audience_filters,
        scheduled_at, started_at, completed_at, created_by_user_id, created_at) values
    (pg_temp.did(700), c, 'Spring Consultation Offer', pg_temp.did(41), 'completed', 'promotion', 'whatsapp', 'all_eligible', null,
        now() - interval '20 days', now() - interval '20 days', now() - interval '20 days' + interval '15 minutes', null, now() - interval '22 days'),
    (pg_temp.did(701), c, 'Old Lead Reactivation', pg_temp.did(42), 'running', 'reactivation', 'whatsapp', 'reactivation_no_consultation', '{"minDaysSinceLastContact": 90}',
        now() - interval '2 hours', now() - interval '2 hours', null, null, now() - interval '1 day'),
    (pg_temp.did(702), c, 'Botox Event Invite', pg_temp.did(44), 'draft', 'event', 'whatsapp', 'custom', '{"procedure": "INJECTABLES"}',
        null, null, null, null, now() - interval '2 days'),
    (pg_temp.did(703), c, 'Summer Skin Refresh', pg_temp.did(41), 'scheduled', 'promotion', 'whatsapp', 'all_eligible', null,
        now() + interval '5 days', null, null, null, now() - interval '1 day');

  insert into marketing.campaign_recipients (id, clinic_id, campaign_id, lead_id, conversation_id, phone_number, variables_json, status,
        skip_reason, failure_code, failure_reason, queued_at, sent_at, delivered_at, read_at, replied_at, booked_at, failed_at) values
    -- completed spring offer
    (pg_temp.did(710), c, pg_temp.did(700), pg_temp.did(100), pg_temp.did(200), '+1 305 555 0111', '{"first_name":"Sarah"}', 'booked',  null, null, null, now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '19 days 20 hours', now() - interval '19 days', null),
    (pg_temp.did(711), c, pg_temp.did(700), pg_temp.did(101), pg_temp.did(201), '+1 305 555 0112', '{"first_name":"Daniel"}', 'replied', null, null, null, now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '19 days 22 hours', null, null),
    (pg_temp.did(712), c, pg_temp.did(700), pg_temp.did(102), pg_temp.did(202), '+1 305 555 0113', '{"first_name":"Emily"}', 'read',    null, null, null, now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '19 days', null, null, null),
    (pg_temp.did(713), c, pg_temp.did(700), pg_temp.did(106), pg_temp.did(206), '+1 305 555 0117', '{"first_name":"Mia"}',   'delivered',null, null, null, now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', null, null, null, null),
    (pg_temp.did(714), c, pg_temp.did(700), pg_temp.did(109), pg_temp.did(209), '+1 305 555 0120', '{"first_name":"Sophia"}','sent',    null, null, null, now() - interval '20 days', now() - interval '20 days', null, null, null, null, null),
    (pg_temp.did(715), c, pg_temp.did(700), pg_temp.did(112), null,            '+1 305 555 0123', '{"first_name":"Ethan"}', 'failed',  null, '131026', 'Message undeliverable: number is not on WhatsApp.', now() - interval '20 days', null, null, null, null, null, now() - interval '20 days'),
    (pg_temp.did(716), c, pg_temp.did(700), pg_temp.did(113), pg_temp.did(211), '+1 305 555 0124', '{"first_name":"Grace"}', 'skipped', 'opted_out', null, null, null, null, null, null, null, null, null),
    -- running reactivation
    (pg_temp.did(720), c, pg_temp.did(701), pg_temp.did(107), pg_temp.did(207), '+1 305 555 0118', '{"first_name":"Liam","procedure":"Rhinoplasty"}', 'delivered', null, null, null, now() - interval '2 hours', now() - interval '2 hours', now() - interval '2 hours', null, null, null, null),
    (pg_temp.did(721), c, pg_temp.did(701), pg_temp.did(108), pg_temp.did(208), '+1 305 555 0119', '{"first_name":"Ava","procedure":"Facelift"}',     'read',      null, null, null, now() - interval '2 hours', now() - interval '2 hours', now() - interval '2 hours', now() - interval '1 hour', null, null, null),
    (pg_temp.did(722), c, pg_temp.did(701), pg_temp.did(102), pg_temp.did(202), '+1 305 555 0113', '{"first_name":"Emily","procedure":"Facelift"}',   'queued',    null, null, null, now() - interval '2 hours', null, null, null, null, null, null),
    (pg_temp.did(723), c, pg_temp.did(701), pg_temp.did(103), pg_temp.did(203), '+1 305 555 0114', '{"first_name":"Karim","procedure":"your enquiry"}','pending', null, null, null, null, null, null, null, null, null, null);

  -- a few campaign messages in the matching conversations, linked back to template + campaign + recipient
  insert into crm.messages (id, clinic_id, conversation_id, lead_id, direction, sender_type, channel, origin, message_type, content,
        delivery_status, sent_at, delivered_at, read_at, whatsapp_template_id, campaign_id, campaign_recipient_id, created_at) values
    (pg_temp.did(1100), c, pg_temp.did(200), pg_temp.did(100), 'outbound', 'system', 'whatsapp', 'campaign', 'text',
        'Hi Sarah, book a consultation this month and the $75 fee is credited toward any procedure. Reply BOOK and we will find you a time.',
        'read', now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', pg_temp.did(41), pg_temp.did(700), pg_temp.did(710), now() - interval '20 days'),
    (pg_temp.did(1101), c, pg_temp.did(208), pg_temp.did(108), 'outbound', 'system', 'whatsapp', 'campaign', 'text',
        'Hi Ava, it has been a while! Still thinking about Facelift? We would love to answer any questions and offer a complimentary follow-up chat.',
        'read', now() - interval '2 hours', now() - interval '2 hours', now() - interval '1 hour', pg_temp.did(42), pg_temp.did(701), pg_temp.did(721), now() - interval '2 hours');
  update marketing.campaign_recipients set message_id = pg_temp.did(1100) where id = pg_temp.did(710);
  update marketing.campaign_recipients set message_id = pg_temp.did(1101) where id = pg_temp.did(721);
  update marketing.campaign_recipients set appointment_id = pg_temp.did(300) where id = pg_temp.did(710);

  -- ---------------------------------------------------------------
  -- 14. Notifications (bell)
  -- ---------------------------------------------------------------
  insert into activity.notifications (id, clinic_id, type, title, message, lead_id, conversation_id, appointment_id, channel_integration_id,
        link, is_read, read_at, created_at) values
    (pg_temp.did(800), c, 'NEW_LEAD',               'New lead: Karim Nasser', 'Karim wrote in on Telegram about a consultation in Arabic.', pg_temp.did(103), pg_temp.did(203), null, null, '/inbox?conversationId=' || pg_temp.did(203), false, null, now() - interval '2 hours'),
    (pg_temp.did(801), c, 'HANDOFF',                'Olivia Reed needs a human', 'Asked about financing and medical history on Instagram.', pg_temp.did(104), pg_temp.did(204), null, null, '/inbox?conversationId=' || pg_temp.did(204), false, null, now() - interval '38 minutes'),
    (pg_temp.did(802), c, 'APPOINTMENT_BOOKED',     'Consultation booked: Noah Bennett', 'Saturday at 10:00.', pg_temp.did(105), null, pg_temp.did(310), null, '/dashboard/appointments/' || pg_temp.did(310), false, null, now() - interval '2 hours'),
    (pg_temp.did(803), c, 'APPOINTMENT_BOOKED',     'Consultation booked: Isabella Rossi', 'Booked by the AI assistant via Facebook.', pg_temp.did(111), pg_temp.did(210), pg_temp.did(307), null, '/dashboard/appointments/' || pg_temp.did(307), true, now() - interval '20 hours', now() - interval '1 day'),
    (pg_temp.did(804), c, 'APPOINTMENT_RESCHEDULED','Consultation rescheduled: Mia Torres', 'Moved to next week at the patient''s request.', pg_temp.did(106), pg_temp.did(206), pg_temp.did(309), null, '/dashboard/appointments/' || pg_temp.did(309), true, now() - interval '20 hours', now() - interval '1 day'),
    (pg_temp.did(805), c, 'APPOINTMENT_CANCELLED',  'Consultation canceled: Ava Collins', 'Cancelled by the patient.', pg_temp.did(108), null, pg_temp.did(311), null, '/dashboard/appointments/' || pg_temp.did(311), true, now() - interval '99 days', now() - interval '100 days'),
    (pg_temp.did(806), c, 'CAMPAIGN_REPLY',         'Campaign reply: Daniel Brooks', 'Replied to "Spring Consultation Offer".', pg_temp.did(101), pg_temp.did(201), null, null, '/inbox?conversationId=' || pg_temp.did(201), true, now() - interval '19 days', now() - interval '19 days 22 hours'),
    (pg_temp.did(807), c, 'OUTBOUND_MESSAGE_FAILED','Message not delivered: Mia Torres', 'The re-engagement message could not be sent (24-hour window closed).', pg_temp.did(106), pg_temp.did(206), null, pg_temp.did(20), '/inbox?conversationId=' || pg_temp.did(206), false, null, now() - interval '25 hours'),
    (pg_temp.did(808), c, 'INTEGRATION_UNHEALTHY',  'Facebook connection needs attention', 'The access token expired. Reconnect the page.', null, null, null, pg_temp.did(22), '/settings/channels', false, null, now() - interval '9 days');

  -- ---------------------------------------------------------------
  -- 15. Activity events (feed + analytics)
  -- ---------------------------------------------------------------
  insert into activity.events (id, clinic_id, lead_id, conversation_id, appointment_id, event_type, source, metadata, created_at) values
    (pg_temp.did(900), c, pg_temp.did(100), pg_temp.did(200), null,             'lead_created',           'instagram', '{}', now() - interval '25 days'),
    (pg_temp.did(901), c, pg_temp.did(100), pg_temp.did(200), pg_temp.did(300), 'consultation_booked',    'ai',        '{}', now() - interval '25 days' + interval '10 minutes'),
    (pg_temp.did(902), c, pg_temp.did(100), null,             pg_temp.did(300), 'consultation_attended',  'staff',     '{}', now() - interval '18 days'),
    (pg_temp.did(903), c, pg_temp.did(100), null,             pg_temp.did(301), 'procedure_booked',       'staff',     '{"amount": 6500, "deposit": 1500}', now() - interval '10 days'),
    (pg_temp.did(904), c, pg_temp.did(101), pg_temp.did(201), pg_temp.did(302), 'consultation_booked',    'ai',        '{}', now() - interval '7 days'),
    (pg_temp.did(905), c, pg_temp.did(102), null,             pg_temp.did(303), 'consultation_no_show',   'staff',     '{}', now() - interval '2 days'),
    (pg_temp.did(906), c, pg_temp.did(103), pg_temp.did(203), null,             'lead_created',           'website',   '{}', now() - interval '2 hours'),
    (pg_temp.did(907), c, pg_temp.did(104), pg_temp.did(204), null,             'human_handoff',          'ai',        '{"reason": "financing and medical history"}', now() - interval '38 minutes'),
    (pg_temp.did(908), c, pg_temp.did(105), pg_temp.did(205), pg_temp.did(310), 'consultation_booked',    'staff',     '{}', now() - interval '2 hours'),
    (pg_temp.did(909), c, pg_temp.did(106), pg_temp.did(206), null,             'message_failed',         'whatsapp',  '{"code": "131047"}', now() - interval '25 hours'),
    (pg_temp.did(910), c, pg_temp.did(109), null,             pg_temp.did(304), 'consultation_attended',  'staff',     '{}', now() - interval '4 days'),
    (pg_temp.did(911), c, pg_temp.did(110), null,             pg_temp.did(306), 'procedure_booked',       'staff',     '{"amount": 5000}', now() - interval '35 days'),
    (pg_temp.did(912), c, pg_temp.did(108), null,             null,             'lead_contacted',         'campaign',  '{"campaign": "Old Lead Reactivation"}', now() - interval '2 hours'),
    (pg_temp.did(913), c, pg_temp.did(111), pg_temp.did(210), pg_temp.did(307), 'consultation_booked',    'ai',        '{}', now() - interval '1 day'),
    (pg_temp.did(914), c, pg_temp.did(113), pg_temp.did(211), null,             'conversation_closed',    'staff',     '{}', now() - interval '10 days');

  -- ---------------------------------------------------------------
  -- 16. Billing: plan, subscription, wallet, usage, ledger
  --     (balances below equal the ledger sums: wallet 151.00, included credit 19.50, reserved 0.50)
  -- ---------------------------------------------------------------
  insert into billing.plans (code, name, description, price, currency, billing_period, included_usage_credit, is_active, sort_order)
  values ('demo-growth', 'Growth (demo)', 'Demo plan: campaigns, AI assistant and reporting with $25 monthly usage credit.', 99, 'USD', 'month', 25, true, 90)
  on conflict (code) do update set name = excluded.name, description = excluded.description, price = excluded.price,
        included_usage_credit = excluded.included_usage_credit
  returning id into v_plan;

  insert into billing.plan_entitlements (plan_id, entitlement_key, value) values
    (v_plan, 'campaigns', 'true'), (v_plan, 'ai_agent', 'true'), (v_plan, 'advanced_reporting', 'true'),
    (v_plan, 'api_access', 'false'), (v_plan, 'max_agents', '3'), (v_plan, 'max_whatsapp_numbers', '2'),
    (v_plan, 'max_channel_connections', '6')
  on conflict (plan_id, entitlement_key) do update set value = excluded.value;

  insert into billing.rate_cards (id, code, name, description, clinic_id, is_default, is_active)
  values (pg_temp.did(1200), 'demo-sculptflow-card', 'Aurora custom pricing (demo)', 'Demo clinic price list.', c, false, true);

  insert into billing.rates (id, rate_card_id, event_type, provider, provider_billing, unit, provider_cost, client_rate, currency, effective_from, notes, created_by) values
    (pg_temp.did(1201), pg_temp.did(1200), 'whatsapp_marketing_message', null, null, 'message', 0.0250, 0.0500, 'USD', now() - interval '200 days', 'Demo rate', 'seed-demo'),
    (pg_temp.did(1202), pg_temp.did(1200), 'whatsapp_utility_message',   null, null, 'message', 0.0100, 0.0200, 'USD', now() - interval '200 days', 'Demo rate', 'seed-demo'),
    (pg_temp.did(1203), pg_temp.did(1200), 'whatsapp_service_message',   null, null, 'message', 0.0000, 0.0000, 'USD', now() - interval '200 days', 'Demo rate', 'seed-demo');

  insert into billing.accounts (id, clinic_id, currency, wallet_balance, included_credit_balance, reserved_amount)
  values (pg_temp.did(1210), c, 'USD', 151.000000, 19.500000, 0.500000)
  on conflict (clinic_id) do update set wallet_balance = excluded.wallet_balance,
        included_credit_balance = excluded.included_credit_balance, reserved_amount = excluded.reserved_amount;

  insert into billing.subscriptions (id, clinic_id, plan_id, status, current_period_start, current_period_end, cancel_at_period_end)
  values (pg_temp.did(1211), c, v_plan, 'active', now() - interval '12 days', now() + interval '18 days', false);

  insert into billing.channel_account_settings (channel_integration_id, clinic_id, provider_billing, omni_usage_billing, applies_to_provider, reason, updated_by)
  values (pg_temp.did(20), c, 'platform_funded', true, null, 'Demo default', 'seed-demo');

  insert into billing.usage_records (id, clinic_id, billing_account_id, idempotency_key, event_type, channel, channel_integration_id,
        quantity, unit, country_code, provider_billing, rate_id, rate_card_id, rate_source, unit_provider_cost, unit_price, provider_cost,
        amount, reserved_amount, credit_amount, wallet_amount, currency, charge_status, provider_outcome, failure_reason, campaign_id,
        source, actor, occurred_at, reserved_at, settled_at) values
    (pg_temp.did(1220), c, pg_temp.did(1210), 'demo-usage-1', 'whatsapp_marketing_message', 'whatsapp', pg_temp.did(20), 40, 'message', 'US', 'platform_funded',
        pg_temp.did(1201), pg_temp.did(1200), 'clinic', 0.0250, 0.0500, 1.00, 2.00, 0, 2.00, 0, 'USD', 'settled', 'billable', null, pg_temp.did(700),
        'campaign', 'seed-demo', now() - interval '12 days', now() - interval '12 days', now() - interval '12 days'),
    (pg_temp.did(1221), c, pg_temp.did(1210), 'demo-usage-2', 'whatsapp_utility_message', 'whatsapp', pg_temp.did(20), 25, 'message', 'US', 'platform_funded',
        pg_temp.did(1202), pg_temp.did(1200), 'clinic', 0.0100, 0.0200, 0.25, 0.50, 0, 0.50, 0, 'USD', 'settled', 'billable', null, null,
        'channel', 'seed-demo', now() - interval '8 days', now() - interval '8 days', now() - interval '8 days'),
    (pg_temp.did(1222), c, pg_temp.did(1210), 'demo-usage-3', 'whatsapp_marketing_message', 'whatsapp', pg_temp.did(20), 60, 'message', 'US', 'platform_funded',
        pg_temp.did(1201), pg_temp.did(1200), 'clinic', 0.0250, 0.0500, 1.50, 3.00, 0, 3.00, 0, 'USD', 'settled', 'billable', null, null,
        'campaign', 'seed-demo', now() - interval '4 days', now() - interval '4 days', now() - interval '4 days'),
    (pg_temp.did(1223), c, pg_temp.did(1210), 'demo-usage-4', 'telegram_message', 'telegram', pg_temp.did(23), 30, 'message', null, 'no_provider_usage_fee',
        null, null, null, null, null, null, 0, 0, 0, 0, 'USD', 'not_charged', 'billable', null, null,
        'channel', 'seed-demo', now() - interval '3 days', null, null),
    (pg_temp.did(1224), c, pg_temp.did(1210), 'demo-usage-5', 'whatsapp_marketing_message', 'whatsapp', pg_temp.did(20), 10, 'message', 'US', 'platform_funded',
        pg_temp.did(1201), pg_temp.did(1200), 'clinic', 0.0250, 0.0500, 0.25, null, 0.50, 0, 0, 'USD', 'reserved', 'pending', null, pg_temp.did(701),
        'campaign', 'seed-demo', now() - interval '2 hours', now() - interval '2 hours', null),
    (pg_temp.did(1225), c, pg_temp.did(1210), 'demo-usage-6', 'whatsapp_utility_message', 'whatsapp', pg_temp.did(20), 1, 'message', 'US', 'platform_funded',
        null, null, null, null, null, null, null, 0, 0, 0, 'USD', 'failed', 'not_billable', 'no_rate', null,
        'channel', 'seed-demo', now() - interval '1 day', null, null);

  insert into billing.ledger_entries (clinic_id, billing_account_id, entry_type, balance_type, amount, balance_after, currency, idempotency_key,
        usage_record_id, subscription_id, plan_id, source, actor, reason, created_at) values
    (c, pg_temp.did(1210), 'wallet_top_up',                'wallet',          200.00, 200.00, 'USD', 'demo-ledger-1', null, null, null, 'admin', 'seed-demo', 'Initial wallet top-up (demo)', now() - interval '30 days'),
    (c, pg_temp.did(1210), 'subscription_charge',          'wallet',          -99.00, 101.00, 'USD', 'demo-ledger-2', null, pg_temp.did(1211), v_plan, 'subscription', 'seed-demo', 'Growth (demo) monthly charge', now() - interval '12 days'),
    (c, pg_temp.did(1210), 'included_credit_grant',        'included_credit',  25.00,  25.00, 'USD', 'demo-ledger-3', null, pg_temp.did(1211), v_plan, 'subscription', 'seed-demo', 'Monthly included credit', now() - interval '12 days'),
    (c, pg_temp.did(1210), 'included_credit_consumption',  'included_credit',  -2.00,  23.00, 'USD', 'demo-ledger-4', pg_temp.did(1220), null, null, 'campaign', 'seed-demo', null, now() - interval '12 days'),
    (c, pg_temp.did(1210), 'included_credit_consumption',  'included_credit',  -0.50,  22.50, 'USD', 'demo-ledger-5', pg_temp.did(1221), null, null, 'channel',  'seed-demo', null, now() - interval '8 days'),
    (c, pg_temp.did(1210), 'wallet_top_up',                'wallet',           50.00, 151.00, 'USD', 'demo-ledger-7', null, null, null, 'admin', 'seed-demo', 'Wallet top-up (demo)', now() - interval '5 days'),
    (c, pg_temp.did(1210), 'included_credit_consumption',  'included_credit',  -3.00,  19.50, 'USD', 'demo-ledger-6', pg_temp.did(1222), null, null, 'campaign', 'seed-demo', null, now() - interval '4 days');

  raise notice 'Demo clinic seeded: Aurora Aesthetic Clinic (%). Sign in as demo@aurora-demo.test with the placeholder password in the file header and CHANGE IT.', c;
end $$;

commit;
