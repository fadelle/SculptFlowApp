// Drives live updates on /WhatsApp/Templates. Same principle as inbox.js: SignalR only says
// "something changed" — on WhatsAppTemplateUpdated this re-fetches the template list from the API
// (source of truth) and re-renders, rather than trusting the event payload as authoritative.
(function () {
  var cfg = window.PS_WHATSAPP_TEMPLATES_CONFIG || {};
  var tbody = document.getElementById('templates-tbody');
  if (!tbody || !window.signalR) return;

  var KNOWN_STATUSES = ['draft', 'pending', 'approved', 'rejected', 'paused', 'disabled'];

  function statusLabel(status) {
    if (!status) return 'Unknown';
    if (KNOWN_STATUSES.indexOf(status) !== -1) return status.charAt(0).toUpperCase() + status.slice(1);
    return 'Problem';
  }

  function statusBadgeClass(status) {
    if (status && KNOWN_STATUSES.indexOf(status) === -1) return 'badge-red';
    switch (status) {
      case 'approved': return 'badge-green';
      case 'pending':
      case 'paused': return 'badge-amber';
      case 'rejected': return 'badge-red';
      case 'draft':
      case 'disabled': return 'badge-gray';
      default: return 'badge-gray';
    }
  }

  function formatTime(iso) {
    try {
      return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    } catch (e) {
      return iso;
    }
  }

  function applyUpdate(payload) {
    var row = tbody.querySelector('tr[data-template-id="' + payload.templateId + '"]');
    if (!row) {
      // A template we don't have a row for yet (created after this page loaded, or created
      // directly in Business Manager) — simplest correct handling is a full reload of the list.
      window.location.reload();
      return;
    }

    var statusEl = row.querySelector('[data-role="status"]');
    if (statusEl) {
      statusEl.textContent = statusLabel(payload.status);
      statusEl.className = 'badge ' + statusBadgeClass(payload.status);
    }
    var reasonEl = row.querySelector('[data-role="reason"]');
    if (reasonEl) reasonEl.textContent = payload.rejectionReason || '—';
    var updatedEl = row.querySelector('[data-role="updated"]');
    if (updatedEl) updatedEl.textContent = formatTime(payload.updatedAt);

    // Quality isn't in the SignalR payload (kept small) — re-fetch that one template for it. The
    // API resolves clinicId server-side from the logged-in session now, not from the query string.
    fetch('/api/whatsapp/templates/' + payload.templateId)
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (t) {
        if (!t) return;
        var qualityEl = row.querySelector('[data-role="quality"]');
        if (qualityEl) qualityEl.textContent = t.qualityRating || '—';
      });

    // Flash the row so staff notice the change even without watching closely.
    row.style.transition = 'background-color 0.2s';
    row.style.backgroundColor = 'var(--surface-subtle)';
    setTimeout(function () { row.style.backgroundColor = ''; }, 1200);
  }

  var connection = new signalR.HubConnectionBuilder()
    .withUrl(cfg.hubUrl)
    .withAutomaticReconnect()
    .build();

  connection.on('WhatsAppTemplateUpdated', applyUpdate);
  connection.start().catch(function (err) {
    console.error('[whatsapp-templates] SignalR connection failed', err);
  });
})();
