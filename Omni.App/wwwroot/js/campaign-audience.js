// Drives the "Who do you want to reach?" picker on /Campaigns/Create.
//
// Four client-facing radios (data-audience-choice) map onto the real posted fields — two hidden
// inputs, #audience-type-field (AudienceType) and #manual-selection-field (ManualSelection) — so
// the backend still only ever sees the three real audience types it already understands (see
// CampaignAudienceType); "Select contacts manually" is audience_type=custom + ManualSelection=true
// under the hood, exactly as CampaignService.CreateAsync already expects.
//
// All match counts and the "Preview contacts" lists come from GET /api/campaigns/audience-preview(/contacts)
// — the same ICampaignAudienceService the backend uses to actually snapshot recipients — so nothing
// here re-implements the eligibility rules in JavaScript; this file only reads form fields, builds
// the filters JSON, and renders what the server already computed.
(function () {
  var CHOICE_TO_TYPE = {
    reactivation_no_booking: 'reactivation_no_booking',
    all_eligible: 'all_eligible',
    custom: 'custom',
    manual: 'custom'
  };

  var radios = document.querySelectorAll('[data-audience-choice]');
  var audienceTypeField = document.getElementById('audience-type-field');
  var manualSelectionField = document.getElementById('manual-selection-field');
  var panels = {
    reactivation_no_booking: document.getElementById('reactivation-panel'),
    all_eligible: document.getElementById('all-eligible-panel'),
    custom: document.getElementById('custom-panel'),
    manual: document.getElementById('manual-recipients')
  };
  if (!radios.length || !audienceTypeField || !manualSelectionField) return;

  function localMidnightUtc(dateStr, addDays) {
    if (addDays) {
      var d = new Date(dateStr + 'T00:00:00Z');
      d.setUTCDate(d.getUTCDate() + addDays);
      dateStr = d.toISOString().slice(0, 10);
    }
    return new Date(dateStr + 'T00:00:00').toISOString(); // the browser reads this as its own local midnight
  }

  var debounceTimer = null;

  function selectedChoice() {
    for (var i = 0; i < radios.length; i++) {
      if (radios[i].checked) return radios[i].value;
    }
    return 'reactivation_no_booking';
  }

  function val(id) {
    var el = document.getElementById(id);
    return el ? el.value : '';
  }

  function checkedValues(name) {
    return Array.from(document.querySelectorAll('input[name="' + name + '"]:checked')).map(function (el) { return el.value; });
  }

  function reactivationFilters() {
    var filters = { inactiveDays: parseInt(val('inactive-days'), 10) };
    if (val('reactivation-service')) filters.serviceId = val('reactivation-service');
    if (val('reactivation-source')) filters.sources = [val('reactivation-source')];
    return filters;
  }

  function customFilters() {
    var filters = {};
    if (val('custom-service')) filters.serviceId = val('custom-service');
    if (val('custom-inactive-days')) filters.inactiveDays = parseInt(val('custom-inactive-days'), 10);

    var stageKeys = checkedValues('CustomStageKeys');
    if (stageKeys.length) filters.stageKeys = stageKeys;
    var qualifications = checkedValues('CustomQualifications');
    if (qualifications.length) filters.qualifications = qualifications;
    var sources = checkedValues('CustomSources');
    if (sources.length) filters.sources = sources;
    var bookingStatuses = checkedValues('CustomBookingStatuses');
    if (bookingStatuses.length) filters.bookingStatuses = bookingStatuses;

    // Date filters are whole days in the viewer's own timezone; send that midnight as UTC (same as the form post).
    // The "to" date is inclusive, so it becomes midnight at the start of the next day.
    if (val('custom-created-after')) filters.createdAfter = localMidnightUtc(val('custom-created-after'));
    if (val('custom-created-before')) filters.createdBefore = localMidnightUtc(val('custom-created-before'), 1);
    if (val('custom-last-contacted-after')) filters.lastContactedAfter = localMidnightUtc(val('custom-last-contacted-after'));
    if (val('custom-last-contacted-before')) filters.lastContactedBefore = localMidnightUtc(val('custom-last-contacted-before'), 1);

    if (val('custom-country')) filters.countries = [val('custom-country')];
    if (val('custom-city')) filters.cities = [val('custom-city')];
    return filters;
  }

  function filtersFor(choice) {
    if (choice === 'reactivation_no_booking') return reactivationFilters();
    if (choice === 'custom') return customFilters();
    return null; // all_eligible / manual — no filters to build
  }

  function syncHiddenFields() {
    var choice = selectedChoice();
    audienceTypeField.value = CHOICE_TO_TYPE[choice] || 'custom';
    manualSelectionField.value = choice === 'manual' ? 'true' : 'false';
  }

  function updateVisibility() {
    var choice = selectedChoice();
    Object.keys(panels).forEach(function (key) {
      if (panels[key]) panels[key].hidden = key !== choice;
    });
  }

  function countElFor(choice) {
    return choice === 'reactivation_no_booking' ? 'reactivation-match-count'
      : choice === 'all_eligible' ? 'all-eligible-match-count'
      : choice === 'custom' ? 'custom-match-count' : null;
  }

  function setText(elId, text) {
    var el = document.getElementById(elId);
    if (el) el.textContent = text;
  }

  function refreshCount() {
    var choice = selectedChoice();
    var countElId = countElFor(choice);
    if (!countElId) return; // manual — nothing to preview-count

    setText(countElId, '…');
    var type = CHOICE_TO_TYPE[choice];
    var filters = filtersFor(choice);
    var url = '/api/campaigns/audience-preview?audienceType=' + encodeURIComponent(type);
    if (filters) url += '&filters=' + encodeURIComponent(JSON.stringify(filters));

    fetch(url)
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (data) {
        setText(countElId, data ? String(data.matchingContacts) : '—');
        document.dispatchEvent(new CustomEvent('campaign:count', { detail: { count: data ? data.matchingContacts : null } }));
      })
      .catch(function () { setText(countElId, '—'); });
  }

  function onAudienceChanged() {
    syncHiddenFields();
    updateVisibility();
    refreshCount();
    document.dispatchEvent(new CustomEvent('campaign:audience-changed', { detail: { choice: selectedChoice() } }));
  }

  function onFilterChanged() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(refreshCount, 350);
  }

  radios.forEach(function (r) { r.addEventListener('change', onAudienceChanged); });

  // Any change inside the reactivation/custom panels (selects, dates, text inputs, or a checkbox
  // toggled via the multi-select chips widget) should refresh the count — delegate on the form so
  // this doesn't need per-field wiring as filters are added.
  var form = audienceTypeField.closest('form');
  if (form) {
    form.addEventListener('change', function (e) {
      var t = e.target;
      if (t.matches('#inactive-days, #reactivation-service, #reactivation-source, ' +
        '#custom-service, #custom-inactive-days, #custom-created-after, #custom-created-before, ' +
        '#custom-last-contacted-after, #custom-last-contacted-before, ' +
        'input[name="CustomStageKeys"], input[name="CustomQualifications"], ' +
        'input[name="CustomSources"], input[name="CustomBookingStatuses"]')) {
        onFilterChanged();
      }
    });
    form.addEventListener('input', function (e) {
      var t = e.target;
      if (t.matches('#custom-country, #custom-city')) onFilterChanged();
    });
  }

  // "Preview contacts" — reuses the exact same audience resolution as the count, just also returning
  // names/phones for the first few matches (GET /api/campaigns/audience-preview/contacts).
  document.querySelectorAll('[data-preview-btn]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var choice = btn.getAttribute('data-preview-audience');
      var target = document.getElementById(btn.getAttribute('data-preview-target'));
      if (!target) return;

      if (target.style.display !== 'none') { target.style.display = 'none'; return; }

      target.style.display = 'block';
      target.innerHTML = '<div class="text-subtle">' + omniText.t('common.loading') + '</div>';

      var type = CHOICE_TO_TYPE[choice];
      var filters = filtersFor(choice);
      var url = '/api/campaigns/audience-preview/contacts?audienceType=' + encodeURIComponent(type) + '&limit=10';
      if (filters) url += '&filters=' + encodeURIComponent(JSON.stringify(filters));

      fetch(url)
        .then(function (res) { return res.ok ? res.json() : null; })
        .then(function (data) {
          if (!data || data.contacts.length === 0) { target.innerHTML = '<div class="text-subtle">' + omniText.t('campaigns.create.previewNone') + '</div>'; return; }
          var html = data.contacts.map(function (l) {
            var name = l.fullName ? l.fullName.replace(/</g, '&lt;') : omniText.t('campaigns.create.unnamedContact');
            var phone = l.phone ? l.phone.replace(/</g, '&lt;') : '—';
            return '<div class="preview-contact-row"><span>' + name + '</span><span class="text-subtle">' + phone + '</span></div>';
          }).join('');
          if (data.matchingContacts > data.contacts.length) {
            html += '<div class="preview-more">' + omniText.t('bookings.calendar.more', [data.matchingContacts - data.contacts.length]) + '</div>';
          }
          target.innerHTML = html;
        })
        .catch(function () { target.innerHTML = '<div class="text-subtle">' + omniText.t('campaigns.create.previewFailed') + '</div>'; });
    });
  });

  syncHiddenFields();
  updateVisibility();
  refreshCount();
})();
