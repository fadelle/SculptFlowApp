// Clinic Info: unsaved-changes bar (details tab); day switches, copy hours, time checks, rules summary,
// timezone clock and the special-date form (opening hours tab).
(function () {
  // ---- Clinic details: save bar
  var form = document.querySelector('[data-dirty-form]');
  if (form) {
    var bar = form.querySelector('[data-save-bar]');
    var text = form.querySelector('[data-save-text]');
    var discard = form.querySelector('[data-discard]');
    var snapshot = function () { return new URLSearchParams(new FormData(form)).toString(); };
    var initial = snapshot();
    var dirty = false;
    var refresh = function () {
      dirty = snapshot() !== initial;
      bar.classList.toggle('is-idle', !dirty);
      text.textContent = dirty ? '● You have unsaved changes' : 'All changes saved';
      discard.hidden = !dirty;
    };
    form.addEventListener('input', refresh);
    form.addEventListener('reset', function () { setTimeout(function () { refresh(); syncLink(); }, 0); });
    form.addEventListener('submit', function () { dirty = false; });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    var site = document.getElementById('clinic-website');
    var open = document.getElementById('clinic-website-open');
    var syncLink = function () {
      var v = site.value.trim();
      open.hidden = !v;
      open.href = v && !/^https?:\/\//i.test(v) ? 'https://' + v : v;
    };
    site.addEventListener('input', syncLink);
    syncLink();
  }

  // ---- Opening hours
  var week = document.getElementById('avail-week');
  if (week) {
    var rows = Array.prototype.slice.call(week.querySelectorAll('.week-row'));
    var countEl = document.getElementById('avail-open-count');
    var checkRow = function (row) {
      var open = row.querySelector('[data-day-open]').checked;
      var s = row.querySelector('[data-day-start]'), e = row.querySelector('[data-day-end]');
      var bad = open && s.value && e.value && e.value <= s.value;
      [s, e].forEach(function (el) { el.classList.toggle('input-error', !!bad); el.setCustomValidity(bad ? 'Must end after it starts' : ''); });
      row.querySelector('[data-day-err]').hidden = !bad;
    };
    var syncRow = function (row) {
      var open = row.querySelector('[data-day-open]').checked;
      row.classList.toggle('is-closed', !open);
      var s = row.querySelector('[data-day-start]'), e = row.querySelector('[data-day-end]');
      s.required = e.required = open;
      if (open && !s.value && !e.value) { s.value = '09:00'; e.value = '17:00'; }
      checkRow(row);
    };
    var syncCount = function () {
      var n = rows.filter(function (r) { return r.querySelector('[data-day-open]').checked; }).length;
      countEl.textContent = n ? n + (n === 1 ? ' day open' : ' days open') : 'All days closed';
    };
    rows.forEach(function (row) {
      row.querySelector('[data-day-open]').addEventListener('change', function () { syncRow(row); syncCount(); });
      row.querySelectorAll('input[type=time]').forEach(function (t) { t.addEventListener('input', function () { checkRow(row); }); });
      syncRow(row);
    });
    syncCount();

    document.getElementById('avail-copy').addEventListener('click', function () {
      var mon = rows.filter(function (r) { return r.dataset.day === 'Monday'; })[0] || rows[0];
      var s = mon.querySelector('[data-day-start]').value, e = mon.querySelector('[data-day-end]').value;
      rows.forEach(function (row) {
        if (row === mon || !row.querySelector('[data-day-open]').checked) return;
        row.querySelector('[data-day-start]').value = s;
        row.querySelector('[data-day-end]').value = e;
        checkRow(row);
      });
    });

    // Rules summary
    var summary = document.getElementById('avail-summary');
    var val = function (k) { var el = document.querySelector('[data-rule=' + k + ']'); return el ? el.value : ''; };
    var plural = function (n, one, many) { return n + ' ' + (String(n) === '1' ? one : many); };
    var syncSummary = function () {
      var d = val('duration'), b = val('buffer'), n = val('notice'), a = val('advance');
      summary.textContent = 'Patients can book ' + (d || '?') + '-minute consultations, from '
        + (n === '0' ? 'right away' : plural(n || '?', 'hour', 'hours')) + ' to ' + plural(a || '?', 'day', 'days') + ' ahead'
        + (b && b !== '0' ? ', with ' + b + ' minutes between appointments.' : ', back to back.');
    };
    document.querySelectorAll('[data-rule]').forEach(function (el) { el.addEventListener('input', syncSummary); });
    syncSummary();

    // Current time in the chosen timezone
    var tz = document.getElementById('avail-tz');
    var now = document.getElementById('avail-tz-now');
    var syncNow = function () {
      try {
        now.textContent = 'Now ' + new Intl.DateTimeFormat('en-GB', { timeZone: tz.value, hour: '2-digit', minute: '2-digit', weekday: 'short' }).format(new Date()) + ' there';
      } catch (e) { now.textContent = ''; }
    };
    tz.addEventListener('change', syncNow);
    syncNow();
    setInterval(syncNow, 30000);
  }

  // ---- Special date form
  var exc = document.getElementById('exc-form');
  if (exc) {
    var closed = document.getElementById('exc-closed');
    var times = exc.querySelectorAll('[data-exc-time]');
    var start = document.getElementById('exc-start'), end = document.getElementById('exc-end');
    exc.querySelectorAll('#exc-mode button').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var isClosed = btn.dataset.closed === 'true';
        closed.value = String(isClosed);
        exc.querySelectorAll('#exc-mode button').forEach(function (b) { b.classList.toggle('on', b === btn); });
        times.forEach(function (t) { t.hidden = isClosed; });
        start.required = end.required = !isClosed;
        if (!isClosed && !start.value && !end.value) { start.value = '09:00'; end.value = '13:00'; }
      });
    });
    var checkExc = function () {
      var bad = closed.value === 'false' && start.value && end.value && end.value <= start.value;
      end.setCustomValidity(bad ? 'Must end after it starts' : '');
      end.classList.toggle('input-error', !!bad);
    };
    start.addEventListener('input', checkExc);
    end.addEventListener('input', checkExc);
  }
})();
