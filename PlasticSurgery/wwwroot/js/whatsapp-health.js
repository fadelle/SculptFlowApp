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

  function applyToFullPage(payload) {
    var badge = document.getElementById('health-status-badge');
    if (badge) {
      badge.textContent = capitalize(payload.healthLevel);
      badge.className = 'badge ' + badgeClass(payload.healthLevel);
    }
    var setText = function (id, value) {
      var el = document.getElementById(id);
      if (el) el.textContent = value || '—';
    };
    setText('health-quality', payload.phoneQualityRating);
    setText('health-account', payload.accountStatus);
    setText('health-review', payload.accountReviewStatus);
    setText('health-last-problem', payload.problemMessage || 'None');

    var row = document.getElementById('whatsapp-health-card');
    if (row) {
      row.style.transition = 'background-color 0.2s';
      row.style.backgroundColor = 'var(--surface-subtle)';
      setTimeout(function () { row.style.backgroundColor = ''; }, 1200);
    }
  }

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
