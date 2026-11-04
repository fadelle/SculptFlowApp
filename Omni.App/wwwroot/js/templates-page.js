// WhatsApp Templates page (Pages/WhatsApp/Templates/Index.cshtml): list tabs, the side panel that shows one
// template as WhatsApp renders it, and the "New template" popup (name formatting, category cards, language,
// header type, "+ Insert blank", character counters, live preview), which also edits a draft, rejected or paused
// template ([data-edit-template]: same fields, handler Edit, name and language fixed). Presentation only — the form
// posts the same fields as before. Live status updates stay in whatsapp-templates.js.
(function () {
  function $(id) { return document.getElementById(id); }
  function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : String(s); return d.innerHTML; }
  // WhatsApp-style bubble: optional bold header, body with {{n}} blanks highlighted, optional grey footer.
  function bubbleHtml(header, body, footer) {
    var html = header ? '<div class="wa-header">' + esc(header) + '</div>' : '';
    html += esc(body).replace(/\{\{(\d+)\}\}/g, '<mark>{{$1}}</mark>');
    if (footer) html += '<div class="wa-footer">' + esc(footer) + '</div>';
    return html + '<span class="wa-time">09:00</span>';
  }

  // ---------------------------------------------------------------- list tabs
  var tabs = document.querySelectorAll('#template-tabs [data-tab]');
  var rows = document.querySelectorAll('#templates-tbody tr[data-template-id]');
  tabs.forEach(function (tab) {
    tab.addEventListener('click', function () {
      var group = tab.getAttribute('data-tab');
      var shown = 0;
      tabs.forEach(function (t) { t.classList.toggle('active', t === tab); });
      rows.forEach(function (r) {
        var show = group === 'all' || r.getAttribute('data-group') === group;
        r.hidden = !show;
        if (show) shown++;
      });
      $('template-tab-empty').hidden = shown > 0;
    });
  });

  // ---------------------------------------------------------------- side panel
  var side = $('tpl-side');
  function openSide(row) {
    rows.forEach(function (r) { r.classList.toggle('row-open', r === row); });
    var body = row.getAttribute('data-body') || '';
    var blanks = (body.match(/\{\{\d+\}\}/g) || []).filter(function (v, i, a) { return a.indexOf(v) === i; });
    var cells = row.children;
    $('tpl-side-name').textContent = row.getAttribute('data-name');
    $('tpl-side-meta').textContent = cells[1].innerText.replace(/\n+/g, ' · ');
    $('tpl-side-bubble').innerHTML = bubbleHtml(row.getAttribute('data-header'), body, row.getAttribute('data-footer'));
    $('tpl-side-status').innerHTML = row.querySelector('[data-role="status"]').outerHTML;
    $('tpl-side-quality').innerHTML = row.querySelector('[data-role="quality"]').innerHTML;
    $('tpl-side-blanks').textContent = blanks.length ? blanks.join(', ') : omniText.t('whatsAppHealth.tech.none');
    $('tpl-side-updated').textContent = row.querySelector('[data-role="updated"]').textContent;
    var actions = $('tpl-side-actions');
    actions.innerHTML = '';
    row.querySelectorAll('.row-actions > *').forEach(function (a) { actions.appendChild(a.cloneNode(true)); });
    side.hidden = false;
    $('tpl-split').classList.add('has-side');
  }
  function closeSide() {
    if (!side) return;
    side.hidden = true;
    $('tpl-split').classList.remove('has-side');
    rows.forEach(function (r) { r.classList.remove('row-open'); });
  }
  rows.forEach(function (row) {
    row.addEventListener('click', function (e) {
      if (e.target.closest('a, button, form')) return;
      openSide(row);
    });
  });
  if ($('tpl-side-close')) $('tpl-side-close').addEventListener('click', closeSide);

  // ---------------------------------------------------------------- new template popup
  var overlay = $('new-template-overlay');
  if (!overlay) return;
  var form = $('new-template-form');
  var nameEl = $('tpl-name'), bodyEl = $('tpl-body'), headerEl = $('tpl-header'), footerEl = $('tpl-footer');
  var langSelect = $('tpl-lang-select'), langEl = $('tpl-lang');

  var titleEl = $('new-template-title'), submitLabel = $('tpl-submit').querySelector('span:last-child');
  var createTitle = titleEl.textContent, createSubmit = submitLabel.textContent, createAction = form.getAttribute('action');

  // Create mode: an empty name and the Create handler. Edit mode: the row's values, name and language fixed.
  function setMode(row) {
    var editing = !!row;
    if (!editing && nameEl.readOnly) form.reset(); // back from editing: an empty new template
    $('tpl-id').value = editing ? row.getAttribute('data-template-id') : '';
    form.setAttribute('action', editing ? createAction.replace(/handler=Create/i, 'handler=Edit') : createAction);
    titleEl.textContent = editing ? overlay.getAttribute('data-edit-title') : createTitle;
    submitLabel.textContent = editing ? overlay.getAttribute('data-edit-submit') : createSubmit;
    nameEl.readOnly = editing;
    langSelect.disabled = editing;
    langEl.readOnly = editing;
    if (!editing) return;
    nameEl.value = row.getAttribute('data-name');
    langEl.value = row.getAttribute('data-lang');
    langSelect.value = Array.prototype.some.call(langSelect.options, function (o) { return o.value === langEl.value; }) ? langEl.value : '__other';
    langEl.hidden = langSelect.value !== '__other';
    bodyEl.value = row.getAttribute('data-body') || '';
    headerEl.value = row.getAttribute('data-header') || '';
    footerEl.value = row.getAttribute('data-footer') || '';
    [['Category', row.getAttribute('data-category')], ['HeaderType', row.getAttribute('data-header-type') || 'none']].forEach(function (pair) {
      var input = form.querySelector('input[name="' + pair[0] + '"][value="' + pair[1] + '"]');
      if (input) input.checked = true;
    });
  }
  function openModal(row) { setMode(row && row.nodeType === 1 ? row : null); overlay.hidden = false; (nameEl.readOnly ? bodyEl : nameEl).focus(); refresh(); }
  function closeModal() { overlay.hidden = true; }
  if ($('new-template-btn')) $('new-template-btn').addEventListener('click', function () { openModal(null); });
  document.querySelectorAll('[data-open-new-template]').forEach(function (b) { b.addEventListener('click', function () { openModal(null); }); });
  // Edit buttons live in the rows and in their copies in the side panel, so listen on the page.
  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-edit-template]');
    if (!btn) return;
    var row = document.querySelector('#templates-tbody tr[data-template-id="' + btn.getAttribute('data-edit-template') + '"]');
    if (row) openModal(row);
  });
  $('new-template-close').addEventListener('click', closeModal);
  $('new-template-cancel').addEventListener('click', closeModal);
  overlay.addEventListener('click', function (e) { if (e.target === overlay) closeModal(); });
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    if (!overlay.hidden) closeModal(); else closeSide();
  });

  // Meta template names: lowercase letters, digits and underscores only.
  nameEl.addEventListener('input', function () {
    var pos = nameEl.selectionStart;
    var cleaned = nameEl.value.toLowerCase().replace(/[\s-]+/g, '_').replace(/[^a-z0-9_]/g, '');
    if (cleaned !== nameEl.value) {
      nameEl.value = cleaned;
      try { nameEl.setSelectionRange(pos, pos); } catch (err) { /* ignore */ }
    }
  });

  // Language: common ones in a dropdown; "Other…" reveals the code box. The text box is the posted field.
  function syncLang() {
    var other = langSelect.value === '__other';
    langEl.hidden = !other;
    if (!other) langEl.value = langSelect.value;
  }
  langSelect.addEventListener('change', function () { syncLang(); if (!langEl.hidden) langEl.focus(); });

  function checkedValue(name) {
    var r = form.querySelector('input[name="' + name + '"]:checked');
    return r ? r.value : '';
  }

  function syncCards() {
    form.querySelectorAll('.cat-card, .seg-opt').forEach(function (c) { c.classList.toggle('on', c.querySelector('input').checked); });
    var isText = checkedValue('HeaderType') === 'text';
    headerEl.hidden = !isText;
    var counter = form.querySelector('[data-counter-for="tpl-header"]');
    if (counter) counter.hidden = !isText;
  }

  // "+ Insert blank": adds the next {{n}} at the cursor.
  $('tpl-insert-blank').addEventListener('click', function () {
    var used = (bodyEl.value.match(/\{\{(\d+)\}\}/g) || []).map(function (m) { return parseInt(m.replace(/\D/g, ''), 10); });
    var next = '{{' + ((used.length ? Math.max.apply(null, used) : 0) + 1) + '}}';
    var start = bodyEl.selectionStart != null ? bodyEl.selectionStart : bodyEl.value.length;
    var end = bodyEl.selectionEnd != null ? bodyEl.selectionEnd : start;
    bodyEl.value = bodyEl.value.slice(0, start) + next + bodyEl.value.slice(end);
    bodyEl.focus();
    bodyEl.setSelectionRange(start + next.length, start + next.length);
    refresh();
  });

  function refresh() {
    syncCards();
    form.querySelectorAll('[data-counter-for]').forEach(function (c) {
      var el = $(c.getAttribute('data-counter-for'));
      var text = omniText.t('common.counter', [el.value.length, c.getAttribute('data-max')]);
      if (el === bodyEl) {
        var n = (bodyEl.value.match(/\{\{\d+\}\}/g) || []).filter(function (v, i, a) { return a.indexOf(v) === i; }).length;
        if (n) text += ' · ' + omniText.plural('templates.form.blanks', n);
      }
      c.textContent = text;
    });
    var header = checkedValue('HeaderType') === 'text' ? headerEl.value : '';
    var preview = $('tpl-preview');
    preview.innerHTML = bodyEl.value || header || footerEl.value
      ? bubbleHtml(header, bodyEl.value, footerEl.value)
      : '<span class="text-subtle">' + esc(omniText.t('templates.form.previewEmpty')) + '</span>';
  }
  form.addEventListener('input', refresh);
  form.addEventListener('change', refresh);

  syncLang();
  refresh();
})();
