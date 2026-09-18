// Drives the "Who do you want to reach?" picker on /Campaigns/Create.
//
// Four clinic-facing radios (data-audience-choice) map onto the real posted fields — two hidden
// inputs, #audience-type-field (AudienceType) and #manual-selection-field (ManualSelection) — so
// the backend still only ever sees the three real audience types it already understands (see
// CampaignAudienceType); "Select leads manually" is audience_type=custom + ManualSelection=true
// under the hood, exactly as CampaignService.CreateAsync already expects.
//
// All match counts and the "Preview leads" lists come from GET /api/campaigns/audience-preview(/leads)
// — the same ICampaignAudienceService the backend uses to actually snapshot recipients — so nothing
// here re-implements the eligibility rules in JavaScript; this file only reads form fields, builds
// the filters JSON, and renders what the server already computed.
(function () {
  var CHOICE_TO_TYPE = {
    reactivation_no_consultation: 'reactivation_no_consultation',
    all_eligible: 'all_eligible',
    custom: 'custom',
    manual: 'custom'
  };

  var radios = document.querySelectorAll('[data-audience-choice]');
  var audienceTypeField = document.getElementById('audience-type-field');
  var manualSelectionField = document.getElementById('manual-selection-field');
  var panels = {
    reactivation_no_consultation: document.getElementById('reactivation-panel'),
    all_eligible: document.getElementById('all-eligible-panel'),
    custom: document.getElementById('custom-panel'),
    manual: document.getElementById('manual-recipients')
  };
  var advancedToggle = document.getElementById('advanced-filters-toggle');
  var advancedFilters = document.getElementById('advanced-filters');
  if (!radios.length || !audienceTypeField || !manualSelectionField) return;

  var debounceTimer = null;

  function selectedChoice() {
    for (var i = 0; i < radios.length; i++) {
      if (radios[i].checked) return radios[i].value;
    }
    return 'reactivation_no_consultation';
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
    if (val('reactivation-procedure')) filters.procedureId = val('reactivation-procedure');
    if (val('reactivation-source')) filters.sources = [val('reactivation-source')];
    return filters;
  }

  function customFilters() {
    var filters = {};
    if (val('custom-procedure')) filters.procedureId = val('custom-procedure');
    if (val('custom-inactive-days')) filters.inactiveDays = parseInt(val('custom-inactive-days'), 10);

    var leadStatuses = checkedValues('CustomLeadStatuses');
    if (leadStatuses.length) filters.leadStatuses = leadStatuses;
    var qualificationStatuses = checkedValues('CustomQualificationStatuses');
    if (qualificationStatuses.length) filters.qualificationStatuses = qualificationStatuses;
    var sources = checkedValues('CustomSources');
    if (sources.length) filters.sources = sources;
    var appointmentStatuses = checkedValues('CustomAppointmentStatuses');
    if (appointmentStatuses.length) filters.appointmentStatuses = appointmentStatuses;

    if (val('custom-created-after')) filters.createdAfter = val('custom-created-after');
    if (val('custom-created-before')) filters.createdBefore = val('custom-created-before');
    if (val('custom-last-contacted-after')) filters.lastContactedAfter = val('custom-last-contacted-after');
    if (val('custom-last-contacted-before')) filters.lastContactedBefore = val('custom-last-contacted-before');

    if (val('custom-country')) filters.countries = [val('custom-country')];
    if (val('custom-city')) filters.cities = [val('custom-city')];
    return filters;
  }

  function filtersFor(choice) {
    if (choice === 'reactivation_no_consultation') return reactivationFilters();
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
      if (panels[key]) panels[key].style.display = key === choice ? (key === 'manual' ? '' : 'block') : 'none';
    });
  }

  function countElFor(choice) {
    return choice === 'reactivation_no_consultation' ? 'reactivation-match-count'
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
      .then(function (data) { setText(countElId, data ? String(data.matchingLeads) : '—'); })
      .catch(function () { setText(countElId, '—'); });
  }

  function onAudienceChanged() {
    syncHiddenFields();
    updateVisibility();
    refreshCount();
  }

  function onFilterChanged() {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(refreshCount, 350);
  }

  radios.forEach(function (r) { r.addEventListener('change', onAudienceChanged); });

  if (advancedToggle && advancedFilters) {
    advancedToggle.addEventListener('click', function () {
      var expanded = advancedFilters.style.display !== 'none';
      advancedFilters.style.display = expanded ? 'none' : 'block';
      advancedToggle.textContent = expanded ? 'Advanced filters ▾' : 'Advanced filters ▴';
    });
  }

  // Any change inside the reactivation/custom panels (selects, dates, text inputs, or a checkbox
  // toggled via the multi-select chips widget) should refresh the count — delegate on the form so
  // this doesn't need per-field wiring as filters are added.
  var form = audienceTypeField.closest('form');
  if (form) {
    form.addEventListener('change', function (e) {
      var t = e.target;
      if (t.matches('#inactive-days, #reactivation-procedure, #reactivation-source, ' +
        '#custom-procedure, #custom-inactive-days, #custom-created-after, #custom-created-before, ' +
        '#custom-last-contacted-after, #custom-last-contacted-before, ' +
        'input[name="CustomLeadStatuses"], input[name="CustomQualificationStatuses"], ' +
        'input[name="CustomSources"], input[name="CustomAppointmentStatuses"]')) {
        onFilterChanged();
      }
    });
    form.addEventListener('input', function (e) {
      var t = e.target;
      if (t.matches('#custom-country, #custom-city')) onFilterChanged();
    });
  }

  // "Preview leads" — reuses the exact same audience resolution as the count, just also returning
  // names/phones for the first few matches (GET /api/campaigns/audience-preview/leads).
  document.querySelectorAll('[data-preview-btn]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var choice = btn.getAttribute('data-preview-audience');
      var target = document.getElementById(btn.getAttribute('data-preview-target'));
      if (!target) return;

      if (target.style.display !== 'none') { target.style.display = 'none'; return; }

      target.style.display = 'block';
      target.innerHTML = '<div class="text-subtle">Loading…</div>';

      var type = CHOICE_TO_TYPE[choice];
      var filters = filtersFor(choice);
      var url = '/api/campaigns/audience-preview/leads?audienceType=' + encodeURIComponent(type) + '&limit=10';
      if (filters) url += '&filters=' + encodeURIComponent(JSON.stringify(filters));

      fetch(url)
        .then(function (res) { return res.ok ? res.json() : null; })
        .then(function (data) {
          if (!data || data.leads.length === 0) { target.innerHTML = '<div class="text-subtle">No leads to show.</div>'; return; }
          var html = data.leads.map(function (l) {
            var name = l.fullName ? l.fullName.replace(/</g, '&lt;') : 'Unnamed lead';
            var phone = l.phone ? l.phone.replace(/</g, '&lt;') : '—';
            return '<div class="preview-lead-row"><span>' + name + '</span><span class="text-subtle">' + phone + '</span></div>';
          }).join('');
          if (data.matchingLeads > data.leads.length) {
            html += '<div class="preview-more">+' + (data.matchingLeads - data.leads.length) + ' more</div>';
          }
          target.innerHTML = html;
        })
        .catch(function () { target.innerHTML = '<div class="text-subtle">Couldn\'t load preview.</div>'; });
    });
  });

  syncHiddenFields();
  updateVisibility();
  refreshCount();
})();
