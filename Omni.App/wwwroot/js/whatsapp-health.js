// Drives live updates on /WhatsApp/Health and (via the same SignalR event) the dashboard's health
// indicator. Same principle as inbox.js/whatsapp-templates.js: the event just says "health
// changed" — this updates the visible fields from the payload (small enough to trust directly for
// display) and, on the full Health page, still links out to a fresh GET on reload for anything not
// in the payload (event history).
(function () {
  var cfg = window.PS_WHATSAPP_HEALTH_CONFIG || window.PS_DASHBOARD_CONFIG;
  if (!cfg || !window.signalR) return;

  function badgeClass(level) {
    switch (level) {
      case 'healthy': return 'badge-green';
      case 'warning': return 'badge-amber';
      case 'problem': return 'badge-red';
      case 'disconnected': return 'badge-gray';
      default: return 'badge-gray';
    }
  }

  function capitalize(s) {
    return s ? s.charAt(0).toUpperCase() + s.slice(1) : s;
  }

  // Full Health page: the banner, cards and activity are worded server-side, so on a change we note WHAT
  // changed ("quality changed from High to Medium"), reload to get the fresh page, and show the note there.
  var NOTE_KEY = 'sf-health-note';
  var QUALITY = { GREEN: 'High', YELLOW: 'Medium', RED: 'Low', HIGH: 'High', MEDIUM: 'Medium', LOW: 'Low' };
  var LEVEL = { healthy: 'All good', warning: 'Needs attention', problem: 'Sending may be limited', disconnected: 'Not connected' };
  function q(v) { return v ? (QUALITY[String(v).toUpperCase()] || v) : 'none'; }

  function describeChange(card, payload) {
    var changes = [];
    if ((card.dataset.quality || '').toUpperCase() !== (payload.phoneQualityRating || '').toUpperCase()) {
      changes.push('quality changed from ' + q(card.dataset.quality) + ' to ' + q(payload.phoneQualityRating));
    }
    if ((card.dataset.account || '').toLowerCase() !== (payload.accountStatus || '').toLowerCase()) {
      changes.push('account is now ' + (payload.accountStatus || 'unknown').toLowerCase());
    }
    if ((card.dataset.review || '').toLowerCase() !== (payload.accountReviewStatus || '').toLowerCase()) {
      changes.push('Meta review is now ' + (payload.accountReviewStatus || 'unknown').toLowerCase());
    }
    if (!changes.length && card.dataset.level !== payload.healthLevel) {
      changes.push('status is now "' + (LEVEL[payload.healthLevel] || capitalize(payload.healthLevel)) + '"');
    }
    if (!changes.length && payload.problemMessage) changes.push(payload.problemMessage);
    return changes.length ? changes.join('; ') : 'new information from Meta';
  }

  function applyToFullPage(payload) {
    var card = document.getElementById('whatsapp-health-card');
    if (!card) return;
    try { sessionStorage.setItem(NOTE_KEY, describeChange(card, payload)); } catch (e) { /* private mode: no note */ }
    window.location.reload();
  }

  (function showNoteAfterReload() {
    var box = document.getElementById('health-live-note');
    if (!box) return;
    var note = null;
    try { note = sessionStorage.getItem(NOTE_KEY); sessionStorage.removeItem(NOTE_KEY); } catch (e) { /* ignore */ }
    if (!note) return;
    box.innerHTML = '<strong>Updated just now:</strong> ';
    box.appendChild(document.createTextNode(note.charAt(0).toUpperCase() + note.slice(1) + '.'));
    box.hidden = false;
  })();

  function applyToIndicator(payload) {
    var dot = document.getElementById('dashboard-whatsapp-health-dot');
    var label = document.getElementById('dashboard-whatsapp-health-label');
    if (dot) dot.className = 'health-dot ' + badgeClass(payload.healthLevel);
    if (label) label.textContent = payload.healthLevel === 'healthy' ? 'Healthy' : 'Attention Required';
  }

  var connection = new signalR.HubConnectionBuilder()
    .withUrl(cfg.hubUrl)
    .withAutomaticReconnect()
    .build();

  connection.on('WhatsAppHealthUpdated', function (payload) {
    applyToFullPage(payload);
    applyToIndicator(payload);
  });

  connection.start().catch(function (err) {
    console.error('[whatsapp-health] SignalR connection failed', err);
  });
})();
