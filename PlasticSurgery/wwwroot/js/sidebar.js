// Sidebar extras, loaded on every authenticated page (Pages/Shared/_Layout.cshtml):
//  - the user menu under the avatar (theme toggle, API docs, sign out)
//  - the unread count next to "Inbox"
// The count comes from the same GET /api/conversations the Inbox uses (summing unreadCount), and
// SignalR only says "something changed, re-fetch" — same principle as inbox.js and notifications.js.
(function () {
  // ---------------------------------------------------------------------
  // User menu
  // ---------------------------------------------------------------------
  var menuBtn = document.getElementById('user-menu-btn');
  var menu = document.getElementById('user-menu');
  if (menuBtn && menu) {
    var setMenu = function (open) {
      menu.hidden = !open;
      menuBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    };
    menuBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      setMenu(menu.hidden);
    });
    document.addEventListener('click', function (e) {
      if (!menu.hidden && !menu.contains(e.target)) setMenu(false);
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !menu.hidden) { setMenu(false); menuBtn.focus(); }
    });
  }

  // ---------------------------------------------------------------------
  // Inbox unread count
  // ---------------------------------------------------------------------
  var badge = document.getElementById('sidebar-inbox-badge');
  if (!badge) return;

  function setCount(n) {
    badge.textContent = n > 99 ? '99+' : String(n);
    badge.hidden = !(n > 0);
  }

  // Also callable by inbox.js, which already has the fresh list whenever it re-renders.
  window.SF_setInboxUnread = setCount;

  function refresh() {
    fetch('/api/conversations?take=200')
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (result) {
        setCount((result.items || []).reduce(function (sum, c) { return sum + (c.unreadCount || 0); }, 0));
      })
      .catch(function () { /* the badge is a hint; leave it as it was */ });
  }

  // On the Inbox page itself, inbox.js keeps the count current (it knows which conversation is open).
  if (window.PS_INBOX_CONFIG) {
    if (typeof window.SF_inboxUnread === 'number') setCount(window.SF_inboxUnread);
    return;
  }

  refresh();
  if (window.signalR) {
    var connection = new signalR.HubConnectionBuilder().withUrl('/hubs/inbox').withAutomaticReconnect().build();
    connection.on('NewMessage', refresh);
    connection.on('ConversationUpdated', refresh);
    connection.onreconnected(refresh);
    connection.start().catch(function (err) { console.error('[sidebar] live unread count unavailable', err); });
  }
})();
