// Shows stored UTC moments in the VIEWER's own timezone (whatever the browser/OS is set to), and converts
// clinic-local wall-clock input to UTC. Loaded on every page by _Layout, before page scripts.
//
// Server side, Pages/Shared/LocalTimeHelper renders <time datetime="…Z" data-local="format">…UTC</time>
// (plus data-local-title for tooltips); this file rewrites the text/title in local time. The UTC text
// the server printed stays as the no-JS fallback.
(function () {
  var FORMATS = {
    'datetime': { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' },
    'datetime-year': { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' },
    'date': { month: 'short', day: 'numeric', year: 'numeric' },
    'month-day': { month: 'short', day: 'numeric' },
    'month-year': { month: 'long', year: 'numeric' },
    'time': { hour: '2-digit', minute: '2-digit' }
  };

  function format(iso, fmt) {
    if (!iso) return '';
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleString(undefined, FORMATS[fmt] || FORMATS['datetime']);
  }

  function render(root) {
    root = root || document;
    root.querySelectorAll('time[data-local]').forEach(function (el) {
      var text = format(el.getAttribute('datetime'), el.getAttribute('data-local'));
      if (text) el.textContent = text;
    });
    root.querySelectorAll('[data-local-title]').forEach(function (el) {
      var parts = el.getAttribute('data-local-title').split('|');
      var text = format(parts[0], parts[1]);
      if (text) el.title = text;
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

  /* Today's date ("yyyy-MM-dd") as seen in an IANA `timeZone` (the clinic's), not the viewer's. */
  function todayIn(timeZone) {
    var parts = new Intl.DateTimeFormat('en-CA', { timeZone: timeZone || 'UTC', year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(new Date());
    var get = function (t) { return parts.find(function (p) { return p.type === t; }).value; };
    return get('year') + '-' + get('month') + '-' + get('day');
  }

  window.SculptTime = { format: format, render: render, zonedToUtcIso: zonedToUtcIso, todayIn: todayIn };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { render(); });
  else render();
})();
