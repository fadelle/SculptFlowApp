# Glossary: fixed words in Arabic and French

One vocabulary for the dashboard's translations (`Omni.App/Resources/i18n/ar.json`, `fr.json`), the built-in business
types (`Database/schema.sql`) and the native reviewers. Drafts until the native review (Milestone B, plan D6); a reviewer's
correction changes this file and every string that uses the word.

Style: **Modern Standard Arabic**, Gregorian months (يناير، فبراير…), Latin digits 0-9. French: *vous*, no-break space
before `: ; ! ?` and inside « », typographic apostrophe (’).

<!-- leftover-ok-begin: the built-in clinic business type's words, stage keys and translation examples -->
## Rules for translators

- **Placeholders stay as they are**: `{0}`, `{1}`, `{contact}`, `{Contacts}` and the rest. A translation may move them,
  never add one its English lacks (`Guards/UiTextKeysTests`).
- **Business-type words come from the client's type** (`{contact}`, `{service}`, `{booking}`, `{order}`, `{member}`,
  `{client}`, capitalized forms the same in Arabic). Write sentences that work whatever the word is:
  - Arabic: use a verbal noun for actions so no verb agrees with the word's gender: «إضافة {contact}», «حذف {service}».
    For counts, put the word as a label: «{contacts}: {0}», since Arabic nouns change after numbers (3-10 plural,
    11+ singular).
  - Arabic: the words arrive without «ال» and some are two words («جهة اتصال», «نشاط تجاري»), so never write «ال{contact}»,
    a definite adjective after a word («{orders} المكتملة») or «هؤلاء {contacts}». Use an indefinite form («{orders}
    مكتملة»), a label, or leave the word out where the sentence is about people in general («من يتواصل معك», «ما تقدمه»).
    Avoid adjectives and verbs that must agree with the word's gender («{contact} جديد»); a verbal noun or a passive
    («تمّ إنشاء {booking}», «لم يُعثر على {contact}») works for every type.
  - French: the word's gender is unknown (une consultation, un patient), so avoid articles and adjectives that agree
    with it: « Ajouter : {contact} » or « Nouveau {contact} » only where both genders read well; prefer a colon or a
    verb in the infinitive.
- **Plurals**: Arabic may add `.zero`, `.two`, `.few`, `.many` to a plural family; French uses `.one` for 0 and 1.
- **Never translate**: Omni, WhatsApp, Instagram, Facebook, Messenger, Telegram, TikTok, Google, Meta, n8n, codes
  (E.164, ISO), file types (PDF, CSV). List one in `Guards/i18n-same-as-english.txt` when the translation equals the
  English.

## Business-type words

The defaults (`terms.*` in the strings files) are the `general` type's words.

| Term | English (clinic) | Arabic (clinic) | French (clinic) | English (general) | Arabic (general) | French (general) |
|---|---|---|---|---|---|---|
| contact | patient / patients | مريض / مرضى | patient / patients | contact / contacts | جهة اتصال / جهات اتصال | contact / contacts |
| member | team member / team members | عضو الفريق / أعضاء الفريق | membre de l’équipe / membres de l’équipe | member / members | عضو / أعضاء | membre / membres |
| service | procedure / procedures | إجراء / إجراءات | intervention / interventions | service / services | خدمة / خدمات | service / services |
| booking | consultation / consultations | استشارة / استشارات | consultation / consultations | booking / bookings | حجز / حجوزات | réservation / réservations |
| order | procedure booking / procedure bookings | حجز إجراء / حجوزات إجراءات | réservation d’intervention / réservations d’intervention | order / orders | طلب / طلبات | commande / commandes |
| client | clinic / clinics | عيادة / عيادات | clinique / cliniques | business / businesses | نشاط تجاري / أنشطة تجارية | entreprise / entreprises |
| type name | Clinic | عيادة | Clinique | General business | نشاط تجاري عام | Activité générale |

## Stage labels

| Stage key | English | Arabic | French |
|---|---|---|---|
| new | New | جديد | Nouveau |
| contacted | Contacted | تم التواصل | Contacté |
| qualified | Qualified | مؤهل | Qualifié |
| consultation_booked (clinic) | Consultation booked | استشارة محجوزة | Consultation réservée |
| consultation_attended (clinic) | Consultation attended | حضر الاستشارة | Consultation effectuée |
| booked (general) | Booked | محجوز | Réservé |
| attended (general) | Attended | حضر | Présent |
| no_show | No-show | لم يحضر | Absent |
| surgery_booked (clinic) | Procedure booked | إجراء محجوز | Intervention réservée |
| customer (general) | Customer | عميل | Client |
| not_interested | Not interested | غير مهتم | Pas intéressé |
| lost | Lost | مفقود | Perdu |
| needs_human | Needs human | يحتاج إلى موظف | Humain requis |
| medical_question (clinic) | Medical question | سؤال طبي | Question médicale |
<!-- leftover-ok-end -->

## Dashboard words

| English | Arabic | French |
|---|---|---|
| Dashboard | لوحة التحكم | Tableau de bord |
| Inbox | صندوق الوارد | Boîte de réception |
| Campaigns | الحملات | Campagnes |
| Knowledge Base | قاعدة المعرفة | Base de connaissances |
| Settings | الإعدادات | Paramètres |
| Billing | الفوترة | Facturation |
| Messaging Integrations | تكامل المراسلة | Intégrations de messagerie |
| Calendar Integrations | تكامل التقويم | Intégrations d’agenda |
| WhatsApp Templates | قوالب WhatsApp | Modèles WhatsApp |
| Save / Save changes | حفظ / حفظ التغييرات | Enregistrer / Enregistrer les modifications |
| Cancel | إلغاء | Annuler |
| Delete / Remove | حذف / إزالة | Supprimer / Retirer |
| Edit | تعديل | Modifier |
| Search | بحث | Rechercher |
| Close | إغلاق | Fermer |
| Loading… | جارٍ التحميل… | Chargement… |
| Optional | اختياري | Facultatif |
| Conversation | محادثة | Conversation |
| Message | رسالة | Message |
| Template | قالب | Modèle |
| Take over (from the AI) | تولّي المحادثة | Prendre la main |
| Hand back to the AI | إعادة إلى الذكاء الاصطناعي | Rendre la main à l’IA |
| AI assistant | المساعد الذكي | Assistant IA |
| Language | اللغة | Langue |
| Business default (language) | لغة النشاط الافتراضية | Langue par défaut de l’entreprise |

## Status words (`values.*`)

| English | Arabic | French |
|---|---|---|
| Hot / Warm / Cold | ساخن / دافئ / بارد | Chaud / Tiède / Froid |
| Needs human review | يحتاج إلى مراجعة موظف | À vérifier par l’équipe |
| Spam | رسائل مزعجة | Spam |
| Booked / Confirmed | محجوز / مؤكد | Réservé / Confirmé |
| Attended / No-show | حضر / لم يحضر | Présent / Absent |
| Canceled / Rescheduled | ملغى / أعيدت جدولته | Annulé / Reprogrammé |
| Scheduled / Running / Completed | مجدول / قيد التنفيذ / مكتمل | Planifié / En cours / Terminé |
| Queued / Pending / Failed | في الانتظار / معلّق / فشل | En file / En attente / Échec |
| Sent / Delivered / Read / Replied | أُرسلت / وصلت / قُرئت / تم الرد | Envoyé / Remis / Lu / Répondu |
| Draft / Approved / Rejected / Paused | مسودة / معتمد / مرفوض / متوقف مؤقتًا | Brouillon / Approuvé / Refusé / En pause |

## Languages (contact language, `contactDetail.language.*`)

| Code | English | Arabic | French |
|---|---|---|---|
| en | English | الإنجليزية | Anglais |
| ar | Arabic | العربية | Arabe |
| fr | French | الفرنسية | Français |

The language pickers always show each dashboard language in its own name (English, العربية, Français), never translated.
