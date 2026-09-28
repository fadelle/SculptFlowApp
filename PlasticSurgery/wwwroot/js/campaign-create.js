// New Campaign page (Pages/Campaigns/Create.cshtml) — everything except the audience count/preview,
// which stays in campaign-audience.js:
//  - picking a template reloads the page with it (keeping the name typed so far)
//  - "Fill in the blanks": each {{n}} dropdown writes the real Variables[n] hidden field; live WhatsApp preview
//  - blanks only work with "Pick people manually" -> warning + Create disabled for the other audiences
//  - audience cards highlight, custom-filter summary chips + "Clear all"
//  - manual pick: live filter, select all, selected count, Enter = server search over all leads
//  - Send now / Schedule toggle, and the summary bar ("template to N leads · now")
(function () {
  var form = document.getElementById('campaign-form');
  if (!form) return;

  function $(id) { return document.getElementById(id); }
  function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : String(s); return d.innerHTML; }

  // ---------------------------------------------------------------- template pick
  var templateSelect = $('template-select');
  if (templateSelect) {
    templateSelect.addEventListener('change', function () {
      var params = new URLSearchParams();
      if (templateSelect.value) params.set('WhatsAppTemplateId', templateSelect.value);
      var name = $('campaign-name').value.trim();
      if (name) params.set('Name', name);
      window.location.href = '/Campaigns/Create' + (params.toString() ? '?' + params.toString() : '');
    });
  }

  // ---------------------------------------------------------------- blanks + preview
  var bubble = $('wa-bubble');
  var SAMPLE = { '{LeadFirstName}': "lead's first name", '{LeadFullName}': "lead's full name", '{LeadPhone}': "lead's phone" };
  var varRows = document.querySelectorAll('.var-row');

  function syncVar(row) {
    var mode = row.querySelector('.var-mode');
    var text = row.querySelector('.var-text');
    var value = row.querySelector('.var-value');
    var isText = mode.value === '__text';
    text.hidden = !isText;
    value.value = isText ? text.value : mode.value;
  }

  function renderPreview() {
    if (!bubble) return;
    var html = esc(bubble.getAttribute('data-body'));
    varRows.forEach(function (row) {
      var n = row.getAttribute('data-var');
      var v = row.querySelector('.var-value').value;
      var shown = SAMPLE[v] ? '[' + SAMPLE[v] + ']' : (v || '{{' + n + '}}');
      html = html.split('{{' + n + '}}').join('<mark>' + esc(shown) + '</mark>');
    });
    bubble.innerHTML = html + '<span class="wa-time">09:00</span>';
  }

  varRows.forEach(function (row) {
    row.querySelector('.var-mode').addEventListener('change', function () { syncVar(row); renderPreview(); });
    row.querySelector('.var-text').addEventListener('input', function () { syncVar(row); renderPreview(); });
    syncVar(row);
  });
  renderPreview();

  // ---------------------------------------------------------------- audience
  var countFor = { reactivation_no_consultation: 'reactivation-match-count', all_eligible: 'all-eligible-match-count', custom: 'custom-match-count' };

  function choice() {
    var r = form.querySelector('[data-audience-choice]:checked');
    return r ? r.value : 'reactivation_no_consultation';
  }

  function syncCards() {
    form.querySelectorAll('.aud-card').forEach(function (card) {
      card.classList.toggle('on', card.querySelector('input').checked);
    });
  }

  // Blanks can only be filled per person for a hand-picked list (see CreateModel.OnPostAsync).
  function blanksBlocked() { return varRows.length > 0 && choice() !== 'manual'; }

  function syncBlanks() {
    var blocked = blanksBlocked();
    if ($('blanks-warning')) $('blanks-warning').hidden = !blocked;
    if ($('blanks-fields')) $('blanks-fields').hidden = blocked;
    // Blank values must not be posted for an audience campaign (the server rejects them).
    varRows.forEach(function (row) { row.querySelector('.var-value').disabled = blocked; });
  }

  // Custom audience: one line of chips summarising the active filters, plus "Clear all".
  var customPanel = $('custom-panel');
  function customSummary() {
    var box = $('custom-active-filters');
    if (!box || !customPanel) return;
    var chips = [];
    customPanel.querySelectorAll('select').forEach(function (s) {
      if (s.value) chips.push(s.options[s.selectedIndex].text);
    });
    customPanel.querySelectorAll('.ms').forEach(function (ms) {
      var picked = Array.from(ms.querySelectorAll('input[type=checkbox]:checked')).map(function (cb) {
        return cb.nextElementSibling ? cb.nextElementSibling.textContent : cb.value;
      });
      if (picked.length) chips.push(picked.join(', '));
    });
    customPanel.querySelectorAll('input[type=date], input[type=text]').forEach(function (i) {
      if (i.value && !i.closest('.ms')) chips.push((i.getAttribute('aria-label') || i.name.replace('Custom', '')) + ': ' + i.value);
    });
    box.hidden = chips.length === 0;
    box.innerHTML = 'Filters: ' + chips.map(function (c) { return '<span class="ms-chip">' + esc(c) + '</span>'; }).join('') +
      ' <button type="button" class="link-btn" id="custom-clear-all">Clear all</button>';
    $('custom-clear-all').addEventListener('click', function () {
      customPanel.querySelectorAll('select').forEach(function (s) { s.selectedIndex = 0; });
      customPanel.querySelectorAll('input[type=date], input[type=text]:not(.ms-search)').forEach(function (i) { i.value = ''; });
      customPanel.querySelectorAll('.ms input[type=checkbox]:checked').forEach(function (cb) {
        cb.checked = false;
        cb.dispatchEvent(new Event('change', { bubbles: true }));
      });
      customPanel.querySelector('select').dispatchEvent(new Event('change', { bubbles: true }));
      customSummary();
    });
  }
  if (customPanel) {
    customPanel.addEventListener('change', customSummary);
    customPanel.addEventListener('input', customSummary);
  }

  // ---------------------------------------------------------------- manual pick
  var manualRows = Array.from(document.querySelectorAll('#manual-recipients tr[data-search]'));
  var searchInput = $('lead-search-input');
  var selectAll = $('manual-select-all');

  function selectedCount() { return form.querySelectorAll('input[name="LeadIds"]:checked').length; }

  function syncManual() {
    manualRows.forEach(function (r) { r.classList.toggle('row-selected', r.querySelector('input').checked); });
    var n = selectedCount();
    if ($('manual-selected-count')) $('manual-selected-count').textContent = n + ' selected';
    var visible = manualRows.filter(function (r) { return !r.hidden; });
    if (selectAll) selectAll.checked = visible.length > 0 && visible.every(function (r) { return r.querySelector('input').checked; });
    updateSummary();
  }

  if (searchInput) {
    searchInput.addEventListener('input', function () {
      var q = searchInput.value.trim().toLowerCase();
      var shown = 0;
      manualRows.forEach(function (r) {
        var show = !q || r.getAttribute('data-search').indexOf(q) !== -1;
        r.hidden = !show;
        if (show) shown++;
      });
      if ($('manual-empty')) $('manual-empty').hidden = shown > 0;
      syncManual();
    });
    // Enter searches ALL leads on the server (the list only holds the first 200).
    searchInput.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      var params = new URLSearchParams(window.location.search);
      params.set('Search', searchInput.value.trim());
      var name = $('campaign-name').value.trim();
      if (name) params.set('Name', name);
      window.location.href = '/Campaigns/Create?' + params.toString();
    });
  }
  if (selectAll) {
    selectAll.addEventListener('change', function () {
      manualRows.forEach(function (r) { if (!r.hidden) r.querySelector('input').checked = selectAll.checked; });
      syncManual();
    });
  }
  manualRows.forEach(function (r) { r.querySelector('input').addEventListener('change', syncManual); });

  // ---------------------------------------------------------------- when
  function syncWhen() {
    var later = form.querySelector('input[name="SendOption"]:checked');
    later = later && later.value === 'later';
    if ($('schedule-fields')) $('schedule-fields').hidden = !later;
    if ($('send-now-hint')) $('send-now-hint').hidden = later;
    form.querySelectorAll('.seg-opt').forEach(function (o) { o.classList.toggle('on', o.querySelector('input').checked); });
    updateSummary();
  }
  form.querySelectorAll('input[name="SendOption"]').forEach(function (r) { r.addEventListener('change', syncWhen); });
  form.addEventListener('change', function (e) { if (e.target.name === 'ScheduleDate' || e.target.name === 'ScheduleTime') updateSummary(); });

  // ---------------------------------------------------------------- summary bar
  function audienceCount() {
    var c = choice();
    if (c === 'manual') return selectedCount();
    var el = $(countFor[c]);
    var n = el ? parseInt(el.textContent, 10) : NaN;
    return isNaN(n) ? null : n;
  }

  function updateSummary() {
    var box = $('summary-text');
    var btn = $('create-btn');
    if (!box || !btn) return;
    var n = audienceCount();
    var later = form.querySelector('input[name="SendOption"]:checked');
    later = later && later.value === 'later';
    var date = form.querySelector('input[name="ScheduleDate"]');
    var time = form.querySelector('input[name="ScheduleTime"]');
    var when = later ? (date && date.value ? 'on ' + date.value + (time && time.value ? ' at ' + time.value : '') : 'at a time you pick') : 'now';
    var template = templateSelect && templateSelect.selectedIndex > 0 ? templateSelect.options[templateSelect.selectedIndex].text : '';

    var problem = blanksBlocked() ? 'This template has blanks: pick people manually, or choose another template.'
      : n === 0 ? (choice() === 'manual' ? 'Tick at least one person.' : 'This audience matches nobody right now.')
      : null;
    btn.disabled = !!problem;
    box.innerHTML = problem
      ? '<span class="summary-problem">' + esc(problem) + '</span>'
      : '<strong>Ready:</strong> ' + esc(template) + ' to <strong>' + (n == null ? '…' : n) + (n === 1 ? ' lead' : ' leads') + '</strong> · ' + esc(when);
  }

  document.addEventListener('campaign:count', updateSummary);
  document.addEventListener('campaign:audience-changed', function () { syncCards(); syncBlanks(); updateSummary(); });

  syncCards();
  syncBlanks();
  customSummary();
  syncManual();
  syncWhen();
})();
