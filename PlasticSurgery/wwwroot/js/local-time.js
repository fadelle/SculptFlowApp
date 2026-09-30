// Shows stored UTC moments in the VIEWER's own timezone (whatever the browser/OS is set to). Loaded on every page
// by _Layout, before page scripts. Also saves that timezone in the sf-tz cookie so server-side day math (calendar
// grid, date ranges, typed dates/times — Pages/Shared/ViewerTimeZone) uses the viewer's clock too. Only Clinic
// Info → Availability is entered in the clinic's timezone.
//
// Appointment pages default to CLINIC time: elements carrying data-clinic-tz show in that zone. When the viewer's
// timezone differs from the clinic's, Pages/Shared/_TimeViewSwitch ("Clinic time | My time") appears; the choice is
// remembered per browser and announced as a window "sf-time-view" event so page scripts can reload.
//
// Server side, Pages/Shared/LocalTimeHelper renders <time datetime="…Z" data-local="format">…UTC</time>
// (plus data-local-title for tooltips); this file rewrites the text/title in local time. The UTC text
// the server printed stays as the no-JS fallback.
(function () {
  var FORMATS = {
    'datetime': { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' },
    'datetime-year': { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' },
    'weekday-datetime': { weekday: 'long', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' },
    'date': { month: 'short', day: 'numeric', year: 'numeric' },
    'month-day': { month: 'short', day: 'numeric' },
    'month-year': { month: 'long', year: 'numeric' },
    'time': { hour: '2-digit', minute: '2-digit' }
  };

  function format(iso, fmt, timeZone) {
    if (!iso) return '';
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    var opts = Object.assign({}, FORMATS[fmt] || FORMATS['datetime']);
    if (timeZone) opts.timeZone = timeZone;
    try { return d.toLocaleString(undefined, opts); }
    catch (e) { delete opts.timeZone; return d.toLocaleString(undefined, opts); } // unknown zone id
  }

  /* Server-written text (e.g. a notification) can carry a moment as [[time:ISO]]; this swaps each for local text. */
  function expand(text, fmt) {
    return String(text || '').replace(/\[\[time:([^\]]+)\]\]/g, function (_, iso) {
      return format(iso, fmt || 'weekday-datetime') || iso;
    });
  }

  function render(root) {
    root = root || document;
    root.querySelectorAll('[data-viewer-tz]').forEach(function (el) { if (viewerTimeZone) el.textContent = viewerTimeZone; });
    root.querySelectorAll('time[data-local]').forEach(function (el) {
      var clinicTz = el.getAttribute('data-clinic-tz');
      var text = format(el.getAttribute('datetime'), el.getAttribute('data-local'), clinicTz ? displayZone(clinicTz) : null);
      if (text) el.textContent = text;
    });
    root.querySelectorAll('[data-local-title]').forEach(function (el) {
      var parts = el.getAttribute('data-local-title').split('|');
      var text = format(parts[0], parts[1]);
      if (text) el.title = text;
    });
  }

  var viewerTimeZone = null;
  try { viewerTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || null; } catch (e) { }
  if (viewerTimeZone) document.cookie = 'sf-tz=' + encodeURIComponent(viewerTimeZone) + '; path=/; max-age=31536000; samesite=lax';

  // ---- Clinic time | My time ----
  var VIEW_KEY = 'sf-time-view';

  function storedView() {
    try { return localStorage.getItem(VIEW_KEY) === 'mine' ? 'mine' : 'clinic'; } catch (e) { return 'clinic'; }
  }

  /* Minutes `timeZone` sits ahead of UTC at `date`. */
  function offsetAt(timeZone, date) {
    var zoned = new Date(date.toLocaleString('en-US', { timeZone: timeZone }));
    var utc = new Date(date.toLocaleString('en-US', { timeZone: 'UTC' }));
    return Math.round((zoned - utc) / 60000);
  }

  /* True when the viewer's clock and the clinic's differ now or in half a year (daylight saving). */
  function zonesDiffer(clinicTz) {
    if (!viewerTimeZone || !clinicTz || viewerTimeZone === clinicTz) return false;
    var now = new Date(), later = new Date(now.getTime() + 182 * 86400000);
    try { return offsetAt(viewerTimeZone, now) !== offsetAt(clinicTz, now) || offsetAt(viewerTimeZone, later) !== offsetAt(clinicTz, later); }
    catch (e) { return false; }
  }

  /* 'mine' only when the zones differ and the viewer picked "My time"; otherwise 'clinic'. */
  function timeView(clinicTz) {
    return zonesDiffer(clinicTz) && storedView() === 'mine' ? 'mine' : 'clinic';
  }

  function displayZone(clinicTz) {
    return timeView(clinicTz) === 'mine' ? viewerTimeZone : clinicTz;
  }

  function initSwitches() {
    document.querySelectorAll('.time-view-switch').forEach(function (sw) {
      var clinicTz = sw.getAttribute('data-clinic-tz');
      sw.hidden = !zonesDiffer(clinicTz);
      if (sw.hidden) return;
      var view = storedView();
      sw.querySelectorAll('input[name="time-view"]').forEach(function (radio) {
        var opt = radio.closest('.seg-opt');
        radio.checked = radio.value === view;
        opt.classList.toggle('on', radio.checked);
        opt.title = radio.value === 'clinic' ? clinicTz : viewerTimeZone;
        if (radio.dataset.bound) return;
        radio.dataset.bound = '1';
        radio.addEventListener('change', function () {
          try { localStorage.setItem(VIEW_KEY, radio.value); } catch (e) { }
          initSwitches();
          render();
          window.dispatchEvent(new CustomEvent('sf-time-view', { detail: { view: radio.value } }));
        });
      });
    });
  }

  /* Converts a wall-clock date ("yyyy-MM-dd") + time ("HH:mm") in an IANA `timeZone` to a UTC ISO string.
     Asks Intl how far that zone sits from UTC at that moment, so daylight saving is handled. */
  function zonedToUtcIso(dateStr, timeStr, timeZone) {
    var naiveUtc = new Date(dateStr + 'T' + (timeStr || '00:00') + ':00Z');
    if (isNaN(naiveUtc.getTime())) return null;
    var asZoned = new Date(naiveUtc.toLocaleString('en-US', { timeZone: timeZone || 'UTC' }));
    var asUtc = new Date(naiveUtc.toLocaleString('en-US', { timeZone: 'UTC' }));
    return new Date(naiveUtc.getTime() - (asZoned.getTime() - asUtc.getTime())).toISOString();
  }

  /* Today's date ("yyyy-MM-dd") as seen in an IANA `timeZone`. */
  function todayIn(timeZone) {
    var parts = new Intl.DateTimeFormat('en-CA', { timeZone: timeZone || 'UTC', year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(new Date());
    var get = function (t) { return parts.find(function (p) { return p.type === t; }).value; };
    return get('year') + '-' + get('month') + '-' + get('day');
  }

  window.SculptTime = {
    format: format, expand: expand, render: render, zonedToUtcIso: zonedToUtcIso, todayIn: todayIn,
    viewerTimeZone: viewerTimeZone, timeView: timeView, zonesDiffer: zonesDiffer
  };

  function start() { initSwitches(); render(); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
