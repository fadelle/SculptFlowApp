// Knowledge Base list (search, category filter, clickable rows, "How it works") and the Add/Edit form
// (choice cards, Q&A template, character count, upload drop zone, website mode cards).
(function () {
  document.querySelectorAll('[data-toggle-target]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var box = document.getElementById(btn.getAttribute('data-toggle-target'));
      box.hidden = !box.hidden;
      btn.setAttribute('aria-expanded', String(!box.hidden));
    });
  });

  // ---- list
  var table = document.getElementById('kb-table');
  if (table) {
    var search = document.getElementById('kb-search');
    var cat = document.getElementById('kb-cat');
    var none = document.getElementById('kb-no-match');
    var apply = function () {
      var q = search.value.trim().toLowerCase(), c = cat.value, shown = 0;
      table.querySelectorAll('tbody tr[data-search]').forEach(function (tr) {
        var hit = (!q || tr.dataset.search.indexOf(q) !== -1) && (!c || tr.dataset.cat === c);
        tr.hidden = !hit;
        if (hit) shown++;
      });
      none.hidden = shown > 0;
    };
    search.addEventListener('input', apply);
    cat.addEventListener('change', apply);
    table.addEventListener('click', function (e) {
      if (e.target.closest('a, button, form, input')) return;
      var tr = e.target.closest('tr[data-href]');
      if (tr) window.location.href = tr.dataset.href;
    });
  }

  // ---- add: choice cards switch the panels
  var panels = { manual: 'kb-panel-manual', upload: 'kb-panel-upload', website: 'kb-panel-website' };
  document.querySelectorAll('[data-kb-tab]').forEach(function (radio) {
    radio.addEventListener('change', function () {
      var which = radio.getAttribute('data-kb-tab');
      Object.keys(panels).forEach(function (k) {
        var el = document.getElementById(panels[k]);
        if (el) el.hidden = k !== which;
      });
      document.querySelectorAll('.kb-choice').forEach(function (c) { c.classList.toggle('on', c.contains(radio)); });
    });
  });

  // Radio cards (website mode): highlight the chosen one.
  document.querySelectorAll('.seg-cards').forEach(function (group) {
    group.addEventListener('change', function () {
      group.querySelectorAll('.cat-card').forEach(function (c) { c.classList.toggle('on', c.querySelector('input').checked); });
    });
  });

  // ---- written entry: Q&A template + character count
  var body = document.getElementById('kb-body');
  var count = document.getElementById('kb-body-count');
  if (body && count) {
    var update = function () { count.textContent = body.value.length.toLocaleString() + ' characters'; };
    body.addEventListener('input', update);
    update();
    var qa = document.getElementById('kb-add-qa');
    if (qa) {
      qa.addEventListener('click', function () {
        var prefix = body.value && !/\n\n$/.test(body.value) ? (/\n$/.test(body.value) ? '\n' : '\n\n') : '';
        body.value += prefix + 'Q: \nA: ';
        var caret = body.value.length - 4; // right after "Q: "
        body.focus();
        body.setSelectionRange(caret, caret);
        update();
      });
    }
  }

  // ---- upload drop zone
  var zone = document.getElementById('kb-dropzone');
  var file = document.getElementById('kb-file');
  if (zone && file) {
    var max = parseInt(zone.dataset.maxBytes, 10) || 0;
    var title = document.getElementById('kb-drop-title');
    var sub = document.getElementById('kb-drop-sub');
    var icon = document.getElementById('kb-drop-icon');
    var defaults = { title: title.textContent, sub: sub.textContent };
    var size = function (b) { return b < 1024 * 1024 ? Math.max(1, Math.round(b / 1024)) + ' KB' : (b / 1024 / 1024).toFixed(1) + ' MB'; };
    var show = function () {
      var f = file.files && file.files[0];
      zone.classList.toggle('has-file', !!f);
      zone.classList.remove('is-error');
      file.setCustomValidity('');
      if (!f) { title.textContent = defaults.title; sub.textContent = defaults.sub; icon.textContent = '⇪'; return; }
      icon.textContent = '📄';
      title.textContent = f.name;
      sub.textContent = size(f.size) + ' · ' + (f.name.split('.').pop() || '').toUpperCase() + ' · click to change';
      if (!/\.(pdf|docx|txt)$/i.test(f.name)) {
        zone.classList.add('is-error');
        sub.textContent = 'Only PDF, DOCX or TXT files can be used.';
        file.setCustomValidity(sub.textContent);
      } else if (max && f.size > max) {
        zone.classList.add('is-error');
        sub.textContent = 'This file is ' + size(f.size) + '. The limit is ' + size(max) + '.';
        file.setCustomValidity(sub.textContent);
      }
    };
    file.addEventListener('change', show);
    ['dragenter', 'dragover'].forEach(function (t) { zone.addEventListener(t, function (e) { e.preventDefault(); zone.classList.add('is-drag'); }); });
    ['dragleave', 'drop'].forEach(function (t) { zone.addEventListener(t, function () { zone.classList.remove('is-drag'); }); });
    zone.addEventListener('drop', function (e) {
      e.preventDefault();
      if (e.dataTransfer && e.dataTransfer.files.length) { file.files = e.dataTransfer.files; show(); }
    });
  }
})();
