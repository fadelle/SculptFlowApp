// Notification bell — loaded on every authenticated dashboard page (Pages/Shared/_Layout.cshtml).
// PostgreSQL (via /api/notifications) stays the source of truth; SignalR (the same clinic-scoped
// /hubs/inbox hub the Inbox uses) just tells an already-open page a new one arrived, so the badge
// re-fetches instead of trusting the pushed payload as authoritative — same principle as inbox.js.
(function () {
  var bell = document.getElementById('notif-bell');
  if (!bell) return; // not authenticated on this page render

  var badge = document.getElementById('notif-bell-badge');
  var dropdown = document.getElementById('notif-dropdown');
  var body = document.getElementById('notif-dropdown-body');
  var markAllBtn = document.getElementById('notif-mark-all');
  var open = false;

  function api(method, path) {
    return fetch(path, { method: method, headers: { 'Content-Type': 'application/json' } }).then(function (r) {
      if (!r.ok) throw new Error('Request failed: ' + r.status);
      return r.status === 204 ? null : r.json();
    });
  }

  function setBadge(count) {
    if (count > 0) {
      badge.textContent = count > 99 ? '99+' : String(count);
      badge.hidden = false;
    } else {
      badge.hidden = true;
    }
  }

  function refreshBadge() {
    api('GET', '/api/notifications/unread-count').then(function (r) { setBadge(r.count); }).catch(function () {});
  }

  var ICONS = {
    NEW_LEAD: '👤', HANDOFF: '🙋', APPOINTMENT_BOOKED: '📅', APPOINTMENT_RESCHEDULED: '🔁',
    APPOINTMENT_CANCELLED: '✖', CAMPAIGN_REPLY: '📣', OUTBOUND_MESSAGE_FAILED: '⚠', INTEGRATION_UNHEALTHY: '🔌'
  };

  function timeAgo(iso) {
    var diffMs = Date.now() - new Date(iso).getTime();
    var mins = Math.round(diffMs / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return mins + 'm ago';
    var hours = Math.round(mins / 60);
    if (hours < 24) return hours + 'h ago';
    return Math.round(hours / 24) + 'd ago';
  }

  function render(items) {
    body.innerHTML = '';
    if (!items.length) {
      body.appendChild(el('div', { class: 'notif-empty' }, ['No notifications yet.']));
      return;
    }
    items.forEach(function (n) {
      var row = el('div', { class: 'notif-row' + (n.isRead ? '' : ' unread') });
      row.appendChild(el('span', { class: 'notif-row-icon' }, [ICONS[n.type] || '🔔']));
      var main = el('div', { class: 'notif-row-main' }, [
        el('div', { class: 'notif-row-title' }, [n.title]),
        n.message ? el('div', { class: 'notif-row-message' }, [n.message]) : null,
        el('div', { class: 'notif-row-time' }, [timeAgo(n.createdAt)])
      ].filter(Boolean));
      row.appendChild(main);
      row.addEventListener('click', function () {
        if (!n.isRead) api('POST', '/api/notifications/' + n.id + '/read').then(refreshBadge).catch(function () {});
        if (n.link) window.location.href = n.link;
      });
      body.appendChild(row);
    });
  }

  function el(tag, attrs, kids) {
    var e = document.createElement(tag);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    (kids || []).forEach(function (k) { e.appendChild(typeof k === 'string' ? document.createTextNode(k) : k); });
    return e;
  }

  function loadList() {
    api('GET', '/api/notifications?take=20').then(function (r) {
      render(r.items);
      setBadge(r.unreadCount);
    }).catch(function () {
      body.innerHTML = '';
      body.appendChild(el('div', { class: 'notif-empty' }, ['Could not load notifications.']));
    });
  }

  function toggle() {
    open = !open;
    dropdown.hidden = !open;
    if (open) loadList();
  }

  bell.addEventListener('click', function (e) { e.stopPropagation(); toggle(); });
  document.addEventListener('click', function (e) {
    if (open && !dropdown.contains(e.target) && e.target !== bell) toggle();
  });
  markAllBtn.addEventListener('click', function (e) {
    e.stopPropagation();
    api('POST', '/api/notifications/read-all').then(loadList).catch(function () {});
  });

  refreshBadge();

  if (window.signalR) {
    var connection = new signalR.HubConnectionBuilder().withUrl('/hubs/inbox').withAutomaticReconnect().build();
    connection.on('NotificationCreated', function () {
      refreshBadge();
      if (open) loadList();
    });
    connection.onreconnected(refreshBadge);
    connection.start().catch(function (err) { console.error('[notifications] live updates unavailable', err); });
  }
})();
