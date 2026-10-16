-- =====================================================================
-- Seed data for local/dev testing — 1 client, a few services,
-- several contacts spread across the pipeline, bookings, and one
-- completed order with revenue, so the dashboard has
-- something real to show. Safe to re-run: clears existing seed rows
-- for this client slug first.
-- =====================================================================

do $$
declare
  v_client_id uuid;
  v_rhino_id uuid;
  v_lipo_id uuid;
  v_facelift_id uuid;

  v_contact_sarah uuid;
  v_contact_omar uuid;
  v_contact_lina uuid;
  v_contact_karim uuid;
  v_contact_nour uuid;

  v_appt_sarah uuid;
begin
  -- clear any previous seed run for this client
  delete from core.clients where slug = 'demo-clinic'; -- leftover-ok: demo clinic data

  insert into core.clients (name, slug, phone, email, timezone, business_type_code)
  values ('Demo Aesthetic Clinic', 'demo-clinic', '+96170000000', 'hello@democlinic.test', 'Asia/Beirut', 'clinic') -- leftover-ok: demo clinic data
  returning id into v_client_id;

  insert into core.services (client_id, name, code, booking_duration)
  values (v_client_id, 'Rhinoplasty', 'RHINO', 45)
  returning id into v_rhino_id;

  insert into core.services (client_id, name, code, booking_duration)
  values (v_client_id, 'Liposuction', 'LIPO', 30)
  returning id into v_lipo_id;

  insert into core.services (client_id, name, code, booking_duration)
  values (v_client_id, 'Facelift', 'FACELIFT', 45)
  returning id into v_facelift_id;

  insert into core.services (client_id, name, code, booking_duration)
  values (v_client_id, 'Breast Augmentation', 'BREAST-AUG', 30);

  insert into core.services (client_id, name, code, booking_duration)
  values (v_client_id, 'Tummy Tuck', 'TUMMY-TUCK', 30);

  -- Contacts carry stage keys of the demo client's business type (crm.pipeline_stages data).
  -- Contact 1: Sarah — full journey through to a confirmed order + revenue
  insert into crm.contacts (client_id, service_id, full_name, first_name, last_name, phone, source,
                      external_contact_id, stage_key, qualification, city, created_at)
  values (v_client_id, v_rhino_id, 'Sarah Haddad', 'Sarah', 'Haddad', '+96171111111', 'instagram',
          'ig-contact-001', 'surgery_booked', 'hot', 'Beirut', now() - interval '20 days') -- leftover-ok: clinic stage key
  returning id into v_contact_sarah;

  insert into crm.conversations (client_id, contact_id, channel, status, last_message_at, last_message_direction)
  values (v_client_id, v_contact_sarah, 'whatsapp', 'active', now() - interval '2 days', 'inbound')
  returning id into v_appt_sarah; -- reused var, fine since scoped

  insert into crm.messages (client_id, conversation_id, contact_id, direction, sender_type, channel, content, is_ai_generated, sent_at, received_at)
  values
    (v_client_id, v_appt_sarah, v_contact_sarah, 'inbound', 'contact', 'whatsapp', 'Hi, I am interested in rhinoplasty', false, null, now() - interval '20 days'),
    (v_client_id, v_appt_sarah, v_contact_sarah, 'outbound', 'ai', 'whatsapp', 'Hi Sarah! Happy to help. Could you tell me your preferred timeline?', true, now() - interval '20 days', null),
    (v_client_id, v_appt_sarah, v_contact_sarah, 'inbound', 'contact', 'whatsapp', 'Within the next 2 months', false, null, now() - interval '19 days');

  insert into scheduling.bookings (client_id, contact_id, service_id, status, scheduled_start, scheduled_end, location_type)
  values (v_client_id, v_contact_sarah, v_rhino_id, 'attended', now() - interval '5 days', now() - interval '5 days' + interval '45 minutes', 'in_person');

  insert into crm.orders (client_id, contact_id, service_id, status, quoted_amount, deposit_amount, final_amount, currency_code, service_date)
  values (v_client_id, v_contact_sarah, v_rhino_id, 'completed', 5500, 1000, 5000, 'USD', now() - interval '1 day');

  insert into activity.events (client_id, contact_id, event_type, source, metadata)
  values
    (v_client_id, v_contact_sarah, 'contact_created', 'instagram', '{}'::jsonb),
    (v_client_id, v_contact_sarah, 'booking_attended', 'staff', '{}'::jsonb),
    (v_client_id, v_contact_sarah, 'order_confirmed', 'staff', '{"amount": 5000}'::jsonb);

  -- Contact 2: Omar — booked, upcoming booking
  insert into crm.contacts (client_id, service_id, full_name, first_name, last_name, phone, source,
                      external_contact_id, stage_key, qualification, city, created_at)
  values (v_client_id, v_lipo_id, 'Omar Khalil', 'Omar', 'Khalil', '+96172222222', 'facebook',
          'fb-contact-002', 'consultation_booked', 'warm', 'Jounieh', now() - interval '6 days') -- leftover-ok: clinic stage key
  returning id into v_contact_omar;

  insert into scheduling.bookings (client_id, contact_id, service_id, status, scheduled_start, location_type)
  values (v_client_id, v_contact_omar, v_lipo_id, 'booked', now() + interval '3 days', 'in_person');

  -- Contact 3: Lina — no-show, needs recovery follow-up
  insert into crm.contacts (client_id, service_id, full_name, first_name, last_name, phone, source,
                      stage_key, qualification, city, created_at)
  values (v_client_id, v_facelift_id, 'Lina Aoun', 'Lina', 'Aoun', '+96173333333', 'google',
          'no_show', 'warm', 'Beirut', now() - interval '10 days')
  returning id into v_contact_lina;

  insert into scheduling.bookings (client_id, contact_id, service_id, status, scheduled_start, location_type)
  values (v_client_id, v_contact_lina, v_facelift_id, 'no_show', now() - interval '2 days', 'in_person');

  -- Contact 4: Karim — brand new, just came in, not contacted yet
  insert into crm.contacts (client_id, full_name, first_name, last_name, phone, source, external_contact_id,
                      stage_key, qualification, city, created_at)
  values (v_client_id, 'Karim Fares', 'Karim', 'Fares', '+96174444444', 'website',
          'web-contact-004', 'new', 'unknown', 'Tripoli', now() - interval '2 hours')
  returning id into v_contact_karim;

  -- Contact 5: Nour — old contact, never converted, candidate for reactivation
  insert into crm.contacts (client_id, service_id, full_name, first_name, last_name, phone, source,
                      stage_key, qualification, city, created_at, last_contact_at)
  values (v_client_id, v_rhino_id, 'Nour Saad', 'Nour', 'Saad', '+96175555555', 'instagram',
          'not_interested', 'cold', 'Sidon', now() - interval '190 days', now() - interval '180 days')
  returning id into v_contact_nour;

  raise notice 'Seed complete for client %', v_client_id;
end $$;
