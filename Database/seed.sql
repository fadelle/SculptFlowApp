-- =====================================================================
-- Seed data for local/dev testing — 1 clinic, a few procedures,
-- several leads spread across the pipeline, appointments, and one
-- completed procedure booking with revenue, so the dashboard has
-- something real to show. Safe to re-run: clears existing seed rows
-- for this clinic slug first.
-- =====================================================================

do $$
declare
  v_clinic_id uuid;
  v_rhino_id uuid;
  v_lipo_id uuid;
  v_facelift_id uuid;

  v_lead_sarah uuid;
  v_lead_omar uuid;
  v_lead_lina uuid;
  v_lead_karim uuid;
  v_lead_nour uuid;

  v_appt_sarah uuid;
begin
  -- clear any previous seed run for this clinic
  delete from clinics where slug = 'demo-clinic';

  insert into clinics (name, slug, phone, email, timezone)
  values ('Demo Aesthetic Clinic', 'demo-clinic', '+96170000000', 'hello@democlinic.test', 'Asia/Beirut')
  returning id into v_clinic_id;

  insert into procedures (clinic_id, name, code, consultation_duration)
  values (v_clinic_id, 'Rhinoplasty', 'RHINO', 45)
  returning id into v_rhino_id;

  insert into procedures (clinic_id, name, code, consultation_duration)
  values (v_clinic_id, 'Liposuction', 'LIPO', 30)
  returning id into v_lipo_id;

  insert into procedures (clinic_id, name, code, consultation_duration)
  values (v_clinic_id, 'Facelift', 'FACELIFT', 45)
  returning id into v_facelift_id;

  insert into procedures (clinic_id, name, code, consultation_duration)
  values (v_clinic_id, 'Breast Augmentation', 'BREAST-AUG', 30);

  insert into procedures (clinic_id, name, code, consultation_duration)
  values (v_clinic_id, 'Tummy Tuck', 'TUMMY-TUCK', 30);

  -- Lead 1: Sarah — full journey through to surgery booked + revenue
  insert into leads (clinic_id, procedure_id, full_name, first_name, last_name, phone, source,
                      external_lead_id, status, qualification_status, city, created_at)
  values (v_clinic_id, v_rhino_id, 'Sarah Haddad', 'Sarah', 'Haddad', '+96171111111', 'instagram',
          'ig-lead-001', 'surgery_booked', 'hot', 'Beirut', now() - interval '20 days')
  returning id into v_lead_sarah;

  insert into conversations (clinic_id, lead_id, channel, status, last_message_at, last_message_direction)
  values (v_clinic_id, v_lead_sarah, 'whatsapp', 'active', now() - interval '2 days', 'inbound')
  returning id into v_appt_sarah; -- reused var, fine since scoped

  insert into messages (clinic_id, conversation_id, lead_id, direction, sender_type, channel, content, is_ai_generated, sent_at, received_at)
  values
    (v_clinic_id, v_appt_sarah, v_lead_sarah, 'inbound', 'lead', 'whatsapp', 'Hi, I am interested in rhinoplasty', false, null, now() - interval '20 days'),
    (v_clinic_id, v_appt_sarah, v_lead_sarah, 'outbound', 'ai', 'whatsapp', 'Hi Sarah! Happy to help. Could you tell me your preferred timeline?', true, now() - interval '20 days', null),
    (v_clinic_id, v_appt_sarah, v_lead_sarah, 'inbound', 'lead', 'whatsapp', 'Within the next 2 months', false, null, now() - interval '19 days');

  insert into appointments (clinic_id, lead_id, procedure_id, status, scheduled_start, scheduled_end, location_type)
  values (v_clinic_id, v_lead_sarah, v_rhino_id, 'attended', now() - interval '5 days', now() - interval '5 days' + interval '45 minutes', 'in_person');

  insert into procedure_bookings (clinic_id, lead_id, procedure_id, status, quoted_amount, deposit_amount, final_amount, currency_code, procedure_date)
  values (v_clinic_id, v_lead_sarah, v_rhino_id, 'completed', 5500, 1000, 5000, 'USD', now() - interval '1 day');

  insert into events (clinic_id, lead_id, event_type, source, metadata)
  values
    (v_clinic_id, v_lead_sarah, 'lead_created', 'instagram', '{}'::jsonb),
    (v_clinic_id, v_lead_sarah, 'consultation_attended', 'staff', '{}'::jsonb),
    (v_clinic_id, v_lead_sarah, 'procedure_booked', 'staff', '{"amount": 5000}'::jsonb);

  -- Lead 2: Omar — booked, upcoming consultation
  insert into leads (clinic_id, procedure_id, full_name, first_name, last_name, phone, source,
                      external_lead_id, status, qualification_status, city, created_at)
  values (v_clinic_id, v_lipo_id, 'Omar Khalil', 'Omar', 'Khalil', '+96172222222', 'facebook',
          'fb-lead-002', 'consultation_booked', 'warm', 'Jounieh', now() - interval '6 days')
  returning id into v_lead_omar;

  insert into appointments (clinic_id, lead_id, procedure_id, status, scheduled_start, location_type)
  values (v_clinic_id, v_lead_omar, v_lipo_id, 'booked', now() + interval '3 days', 'in_person');

  -- Lead 3: Lina — no-show, needs recovery follow-up
  insert into leads (clinic_id, procedure_id, full_name, first_name, last_name, phone, source,
                      status, qualification_status, city, created_at)
  values (v_clinic_id, v_facelift_id, 'Lina Aoun', 'Lina', 'Aoun', '+96173333333', 'google',
          'no_show', 'warm', 'Beirut', now() - interval '10 days')
  returning id into v_lead_lina;

  insert into appointments (clinic_id, lead_id, procedure_id, status, scheduled_start, location_type)
  values (v_clinic_id, v_lead_lina, v_facelift_id, 'no_show', now() - interval '2 days', 'in_person');

  -- Lead 4: Karim — brand new, just came in, not contacted yet
  insert into leads (clinic_id, full_name, first_name, last_name, phone, source, external_lead_id,
                      status, qualification_status, city, created_at)
  values (v_clinic_id, 'Karim Fares', 'Karim', 'Fares', '+96174444444', 'website',
          'web-lead-004', 'new', 'unknown', 'Tripoli', now() - interval '2 hours')
  returning id into v_lead_karim;

  -- Lead 5: Nour — old lead, never converted, candidate for reactivation
  insert into leads (clinic_id, procedure_id, full_name, first_name, last_name, phone, source,
                      status, qualification_status, city, created_at, last_contact_at)
  values (v_clinic_id, v_rhino_id, 'Nour Saad', 'Nour', 'Saad', '+96175555555', 'instagram',
          'not_interested', 'cold', 'Sidon', now() - interval '190 days', now() - interval '180 days')
  returning id into v_lead_nour;

  raise notice 'Seed complete for clinic %', v_clinic_id;
end $$;
