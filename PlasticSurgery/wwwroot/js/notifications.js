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
  // Per-tab cache of the last-fetched list (not persisted — just a JS variable) so opening the bell
  // renders instantly instead of waiting on a round trip every time. Filled on page load and refreshed
  // in the background on every SignalR push; the pushed event itself is never trusted as the data,
  // only as a signal to go re-fetch — same principle as inbox.js.
  var cachedItems = null;

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
        if (!n.isRead) {
          api('POST', '/api/notifications/' + n.id + '/read').then(refreshBadge).catch(function () {});
          n.isRead = true; // update our own cache after a successful mutation — not the same as trusting a push payload
          row.classList.remove('unread');
        }
        if (n.link) window.location.href = n.link;
      });
      body.appendChild(row);
    });
  }

  function renderLoading() {
    body.innerHTML = '';
    body.appendChild(el('div', { class: 'notif-empty' }, ['Loading…']));
  }

  function el(tag, attrs, kids) {
    var e = document.createElement(tag);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    (kids || []).forEach(function (k) { e.appendChild(typeof k === 'string' ? document.createTextNode(k) : k); });
    return e;
  }

  // Always fetches fresh from the server and updates the cache; re-renders too if the dropdown is
  // currently open. Called on page load, on every SignalR push, and after our own mutations
  // (mark all read) — never gated behind the dropdown being open, so the cache stays current even
  // while closed and the NEXT open is instant.
  function refreshList() {
    return api('GET', '/api/notifications?take=20').then(function (r) {
      cachedItems = r.items;
      setBadge(r.unreadCount);
      if (open) render(cachedItems);
    }).catch(function () {
      if (open && !cachedItems) {
        body.innerHTML = '';
        body.appendChild(el('div', { class: 'notif-empty' }, ['Could not load notifications.']));
      }
    });
  }

  function toggle() {
    open = !open;
    dropdown.hidden = !open;
    if (!open) return;

    if (cachedItems) {
      render(cachedItems); // instant — no wait on a network round trip
    } else {
      renderLoading(); // only the very first open before the page-load fetch has landed
    }
    // Stale-while-revalidate: quietly confirm/update in the background even when we rendered from cache.
    refreshList();
  }

  bell.addEventListener('click', function (e) { e.stopPropagation(); toggle(); });
  document.addEventListener('click', function (e) {
    if (open && !dropdown.contains(e.target) && e.target !== bell) toggle();
  });
  markAllBtn.addEventListener('click', function (e) {
    e.stopPropagation();
    api('POST', '/api/notifications/read-all').then(refreshList).catch(function () {});
  });

  // Populate the cache in the background as soon as the page loads — not waiting for a click — so the
  // first time the bell is opened is already instant, not just the second time onward.
  refreshList();

  if (window.signalR) {
    var connection = new signalR.HubConnectionBuilder().withUrl('/hubs/inbox').withAutomaticReconnect().build();
    connection.on('NotificationCreated', refreshList);
    connection.onreconnected(refreshList); // events missed while disconnected — resync the cache, not just the badge
    connection.start().catch(function (err) { console.error('[notifications] live updates unavailable', err); });
  }
})();
