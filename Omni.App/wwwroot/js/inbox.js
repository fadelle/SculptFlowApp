// Drives the /inbox page: conversation list, message thread, composer, and the mode controls
// (Take Over / Return to AI / Close). PostgreSQL (via the REST API) is the only source of truth —
// SignalR only tells us *that* something changed; every handler below reacts by re-fetching from
// the API rather than trusting the SignalR payload's own fields as authoritative. If the socket
// disconnects or a page load happens after missing events entirely, nothing is lost: reloading
// re-reads current state from the database the normal way.
(function () {
  var cfg = window.PS_INBOX_CONFIG || {};
  var currentConversationId = null;
  var currentContactId = null;
  var currentWindowOpen = true;
  var contactInfoCache = {}; // contactId -> ContactResponse, avoids refetching every toggle
  var approvedTemplates = null; // WhatsAppTemplateResponse[], lazy-loaded on first "Send Template" click

  var listEl = document.getElementById('inbox-list');
  var emptyEl = document.getElementById('inbox-empty');
  var threadEl = document.getElementById('inbox-thread-inner');
  var nameEl = document.getElementById('inbox-thread-name');
  var serviceEl = document.getElementById('inbox-thread-service');
  var modeEl = document.getElementById('inbox-thread-mode');
  var messagesEl = document.getElementById('inbox-messages');
  var composerEl = document.getElementById('inbox-composer');
  var composerInputEl = document.getElementById('inbox-composer-input');
  var composerSendEl = document.getElementById('inbox-composer-send');
  var btnTakeover = document.getElementById('inbox-btn-takeover');
  var btnReturnAi = document.getElementById('inbox-btn-return-ai');
  var btnReopen = document.getElementById('inbox-btn-reopen');
  var btnClose = document.getElementById('inbox-btn-close');
  var btnMore = document.getElementById('inbox-btn-more');
  var moreMenuEl = document.getElementById('inbox-more-menu');
  var btnInfo = document.getElementById('inbox-btn-info');
  var contactInfoEl = document.getElementById('inbox-contact-info');
  var windowClosedNoticeEl = document.getElementById('inbox-window-closed-notice');
  var btnSendTemplate = document.getElementById('inbox-btn-send-template');
  var templatePickerEl = document.getElementById('inbox-template-picker');
  var templateSelectEl = document.getElementById('inbox-template-select');
  var templateVariablesEl = document.getElementById('inbox-template-variables');
  var templatePreviewEl = document.getElementById('inbox-template-preview');
  var btnTemplateCancel = document.getElementById('inbox-template-cancel');
  var shellEl = document.getElementById('inbox-shell');
  var threadAvatarEl = document.getElementById('inbox-thread-avatar');
  var searchEl = document.getElementById('inbox-search');
  var btnBack = document.getElementById('inbox-btn-back');
  var btnContactClose = document.getElementById('inbox-contact-close');
  var listFilter = 'all'; // all | needs | ai | closed — the server filters (GET /api/conversations?filter=)
  var listSearch = '';     // the server searches name, phone and service (?search=)
  var listItems = [];      // the rows loaded so far, newest activity first
  var listTotal = 0;       // how many match the search and tab
  var listRequest = 0;     // only the newest list request may render
  var PAGE_SIZE = 50;

  // ---------------------------------------------------------------------
  // API helpers
  // ---------------------------------------------------------------------
  // A refused request's message: the API's own ({ error }) when it answered; a 403 that isn't the API's (a page from the edge
  // firewall in front of the app, QA t3-inbox-06) says the network blocked it, so the member can rephrase instead of guessing.
  function failure(res) {
    var isJson = (res.headers.get('content-type') || '').indexOf('json') >= 0;
    if (!isJson && res.status === 403) return Promise.resolve(new Error(omniText.t('common.errors.blockedByFirewall')));
    return (isJson ? res.json() : Promise.resolve({})).catch(function () { return {}; }).then(function (problem) {
      return new Error(problem.error || problem.detail || omniText.t('common.errors.requestFailed', [res.status]));
    });
  }

  function apiGet(path) {
    return fetch(path).then(function (res) {
      if (!res.ok) return failure(res).then(function (err) { throw err; });
      return res.json();
    });
  }

  // A background refresh (SignalR, start-up) that fails says so once, not on every event, until one works again (QA t3-inbox-15:
  // failures were silent and the Inbox just stopped updating).
  var refreshFailing = false;
  function refreshFailed(err) {
    console.error('[inbox] refresh failed', err);
    if (refreshFailing) return;
    refreshFailing = true;
    showError(omniText.t('inbox.errors.refreshFailed', [err && err.message ? err.message : '']));
  }
  function refreshWorked() { refreshFailing = false; }

  // A member's action (take over, return to AI, reopen, close) that fails says why.
  function actionFailed(err) {
    showError(omniText.t('inbox.errors.actionFailed', [err && err.message ? err.message : '']));
  }

  // A failed send shows as an error toast (the Aurora kit, aurora.js); a plain alert if the kit is missing.
  // Ask before a destructive action: the Aurora kit's styled dialog (aurora.js), or the browser's own if the kit is
  // missing. Resolves true when confirmed.
  function ask(title, message, confirmLabel) {
    if (window.Aurora && window.Aurora.confirm) {
      return window.Aurora.confirm({ title: title, message: message, confirmLabel: confirmLabel, tone: 'danger' });
    }
    return Promise.resolve(window.confirm(message));
  }

  function showError(message) {
    if (window.Aurora && window.Aurora.toast) window.Aurora.toast.error(message);
    else alert(message);
  }

  function apiPost(path, body) {
    return fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body || {})
    }).then(function (res) {
      if (!res.ok) return failure(res).then(function (err) { throw err; });
      return res.json().catch(function () { return {}; });
    });
  }

  // The API resolves clientId server-side from the logged-in session (CurrentClientContext) now —
  // this used to append ?clientId=... to every URL; kept as a no-op pass-through so every call site
  // below doesn't need touching.
  function withClient(path) {
    return path;
  }

  // ---------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------
  var ORIGIN_LABELS = {
    whatsapp_customer: omniText.t('inbox.origin.contact'),
    telegram_customer: omniText.t('inbox.origin.contact'),
    whatsapp_business_app: omniText.t('inbox.origin.businessApp'),
    dashboard: omniText.t('inbox.mode.human'),
    ai: omniText.t('inbox.mode.ai'),
    system: omniText.t('inbox.origin.system')
  };

  function originLabel(origin) {
    return ORIGIN_LABELS[origin] || origin;
  }

  function originBubbleClass(origin, direction) {
    if (origin === 'whatsapp_customer' || origin === 'telegram_customer') return 'inbox-msg-customer';
    if (origin === 'ai') return 'inbox-msg-ai';
    if (origin === 'dashboard') return 'inbox-msg-member';
    if (origin === 'whatsapp_business_app') return 'inbox-msg-member-app';
    return 'inbox-msg-system';
  }

  function formatTime(iso) {
    try {
      var d = new Date(iso);
      return d.toLocaleString(omniText.locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    } catch (e) {
      return iso;
    }
  }

  // Short relative time for the conversation list: "now", "5m", "3h", "Yesterday", "Sep 21".
  function formatListTime(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    var mins = Math.floor((Date.now() - d.getTime()) / 60000);
    if (mins < 1) return omniText.t('inbox.time.now');
    if (mins < 60) return omniText.t('inbox.time.minutes', [mins]);
    if (mins < 60 * 24 && d.getDate() === new Date().getDate()) return omniText.t('inbox.time.hours', [Math.floor(mins / 60)]);
    var yesterday = new Date(); yesterday.setDate(yesterday.getDate() - 1);
    if (d.toDateString() === yesterday.toDateString()) return omniText.t('inbox.time.yesterday');
    return d.toLocaleDateString(omniText.locale, { month: 'short', day: 'numeric' });
  }

  function formatDate(iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    return isNaN(d.getTime()) ? '—' : d.toLocaleDateString(omniText.locale, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  function renderTimestamps() {
    document.querySelectorAll('.inbox-conv-time[data-timestamp]').forEach(function (el) {
      el.textContent = formatListTime(el.getAttribute('data-timestamp'));
      el.title = formatTime(el.getAttribute('data-timestamp'));
    });
  }

  // "Layla Haddad" -> "LH". Mirrors Initials() in Pages/Inbox/Index.cshtml.
  function initials(name) {
    // Letters and digits only (QA t3-inbox-20: "[QA] TG 0106" showed "[0").
    var parts = String(name || '').trim().split(/\s+/).map(function (p) { return p.replace(/[^\p{L}\p{N}]/gu, ''); }).filter(Boolean);
    if (parts.length === 0) return '?';
    return (parts[0].charAt(0) + (parts.length > 1 ? parts[parts.length - 1].charAt(0) : '')).toUpperCase();
  }

  function modeLabel(mode) {
    return { ai: omniText.t('inbox.mode.ai'), human: omniText.t('inbox.mode.human'), approval: omniText.t('inbox.mode.approval') }[mode] || mode;
  }

  // ---------------------------------------------------------------------
  // List search + filter tabs: done by the server over every conversation (QA t3-inbox-04: only the newest 50 used to
  // be reachable), a page at a time with "Show more".
  // ---------------------------------------------------------------------
  function listUrl(skip, take) {
    var query = new URLSearchParams({ skip: String(skip), take: String(take), filter: listFilter });
    if (listSearch) query.set('search', listSearch);
    return withClient('/api/conversations?' + query.toString());
  }

  // A refresh (SignalR, after an action) re-reads as many rows as are on screen, so a scrolled list keeps its place.
  function loadConversationList() {
    var token = ++listRequest;
    var take = Math.min(Math.max(listItems.length, PAGE_SIZE), 200);
    return apiGet(listUrl(0, take)).then(function (page) {
      refreshWorked();
      if (token !== listRequest) return;
      listItems = page.items || [];
      listTotal = page.totalCount || 0;
      renderConversationList(page);
    });
  }

  function loadMoreConversations() {
    var token = ++listRequest;
    return apiGet(listUrl(listItems.length, PAGE_SIZE)).then(function (page) {
      if (token !== listRequest) return;
      var known = {};
      listItems.forEach(function (c) { known[c.id] = true; });
      (page.items || []).forEach(function (c) { if (!known[c.id]) listItems.push(c); });
      listTotal = page.totalCount || 0;
      renderConversationList(page);
    });
  }

  // A new search or tab starts from the first page.
  function reloadConversationList() {
    listItems = [];
    return loadConversationList();
  }

  var searchTimer = null;
  if (searchEl) searchEl.addEventListener('input', function () {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(function () {
      listSearch = searchEl.value.trim();
      reloadConversationList().catch(refreshFailed);
    }, 300);
  });
  function selectTab(btn) {
    listFilter = btn.getAttribute('data-filter');
    document.querySelectorAll('.inbox-filter').forEach(function (b) { b.classList.toggle('active', b === btn); });
  }
  document.querySelectorAll('.inbox-filter').forEach(function (btn) {
    btn.addEventListener('click', function () {
      selectTab(btn);
      reloadConversationList().catch(refreshFailed);
    });
  });

  // Browser-tab title shows how many customer messages are waiting, e.g. "(3) Inbox - Omni",
  // so members working in another tab notice new messages too.
  var baseTitle = document.title;
  function updateTabTitle(totalUnread) {
    document.title = totalUnread > 0 ? '(' + totalUnread + ') ' + baseTitle : baseTitle;
    // Sidebar badge (sidebar.js). Also stored, in case sidebar.js hasn't loaded yet — it picks this up.
    window.SF_inboxUnread = totalUnread;
    if (window.SF_setInboxUnread) window.SF_setInboxUnread(totalUnread);
  }

  // The tabs' numbers and the unread total come from the server, over every conversation; the conversation being viewed
  // counts as read (it is marked read as soon as it's opened).
  function renderCounts(page) {
    var counts = page.counts || {};
    document.querySelectorAll('[data-count]').forEach(function (el) {
      var n = counts[el.getAttribute('data-count')] || 0;
      el.textContent = n > 0 ? n : '';
    });
    var open = listItems.filter(function (c) { return c.id === currentConversationId; })[0];
    updateTabTitle(Math.max(0, (page.unreadTotal || 0) - (open ? (open.unreadCount || 0) : 0)));
  }

  function renderConversationList(page) {
    var items = listItems;
    listEl.innerHTML = '';
    renderCounts(page);
    if (!items || items.length === 0) {
      var message = listSearch || listFilter !== 'all' ? omniText.t('inbox.list.noMatch') : omniText.t('inbox.list.empty');
      listEl.innerHTML = '<div class="inbox-list-message">' + escapeHtml(message) + '</div>';
      return;
    }
    items.forEach(function (c) {
      var div = document.createElement('div');
      // Unread = customer messages members haven't opened yet. The conversation being viewed right now
      // counts as read (it is marked read on the server as soon as it's opened / a message arrives in it).
      var unreadCount = c.id === currentConversationId ? 0 : (c.unreadCount || 0);
      var closed = c.status === 'closed' || c.status === 'archived';
      div.className = 'inbox-conv-item' + (c.id === currentConversationId ? ' active' : '') + (unreadCount > 0 ? ' unread' : '') + (closed ? ' closed' : '');
      div.setAttribute('data-conversation-id', c.id);
      div.setAttribute('data-contact-name', c.contactFullName || omniText.t('inbox.unknownName'));
      div.setAttribute('data-service-name', c.serviceName || '');
      div.setAttribute('data-channel', c.channel || '');
      div.setAttribute('data-mode', c.mode || '');
      div.setAttribute('data-closed', closed ? 'true' : 'false');

      // "Needs Human" means AI is paused, the customer spoke last, AND members haven't opened it yet
      // — not just "mode is human". Members sending from the dashboard also sets mode to human, but
      // that's a conversation actively being handled, and once members open a conversation they're on it.
      var needsHuman = c.mode === 'human' && c.lastMessageDirection === 'inbound' && unreadCount > 0;
      // The "Needs you" tab is broader: anything AI isn't handling on its own (human mode or awaiting approval).
      div.setAttribute('data-needs', (c.mode === 'human' || c.mode === 'approval') ? 'true' : 'false');

      var modePill = closed
        ? '<span class="inbox-mode inbox-mode-closed">' + escapeHtml(omniText.t('inbox.filter.closed')) + '</span>'
        : needsHuman
          ? '<span class="inbox-mode inbox-mode-needs">' + escapeHtml(omniText.t('inbox.filter.needs')) + '</span>'
          : '<span class="inbox-mode inbox-mode-' + escapeHtml(c.mode) + '">' + escapeHtml(modeLabel(c.mode)) + '</span>';

      div.innerHTML =
        '<span class="inbox-avatar">' + escapeHtml(initials(c.contactFullName)) +
          '<span class="inbox-channel-icon" style="background:' + channelColor(c.channel) + '" title="' + escapeHtml(c.channel) + '">' + (channelIcon(c.channel) || channelInitials(c.channel)) + '</span>' +
        '</span>' +
        '<div class="inbox-conv-main">' +
          '<div class="inbox-conv-top">' +
            '<span class="inbox-conv-name" dir="auto">' + escapeHtml(c.contactFullName || omniText.t('inbox.unknownName')) + '</span>' +
            (unreadCount > 0 ? '<span class="inbox-unread-badge" title="' + escapeHtml(omniText.t('inbox.list.unread', [unreadCount])) + '">' + (unreadCount > 99 ? '99+' : unreadCount) + '</span>' : '') +
            '<span class="inbox-conv-time" data-timestamp="' + (c.lastMessageAt || c.createdAt) + '"></span>' +
          '</div>' +
          (c.serviceName ? '<div class="inbox-conv-service">' + escapeHtml(c.serviceName) + '</div>' : '') +
          '<div class="inbox-conv-bottom">' +
            '<span class="inbox-conv-preview" dir="auto">' + escapeHtml((window.InboxMessageView ? window.InboxMessageView.previewText(c.lastMessagePreview) : c.lastMessagePreview) || omniText.t('inbox.list.noMessages')) + '</span>' +
            modePill +
          '</div>' +
        '</div>';

      div.addEventListener('click', function () { selectConversation(c.id).catch(actionFailed); });
      listEl.appendChild(div);
    });
    if (items.length < listTotal) {
      var more = document.createElement('button');
      more.type = 'button';
      more.className = 'au-btn au-btn--text inbox-load-more';
      more.textContent = omniText.plural('inbox.list.showMore', listTotal - items.length);
      more.addEventListener('click', function () { more.disabled = true; loadMoreConversations().catch(function (err) { more.disabled = false; actionFailed(err); }); });
      listEl.appendChild(more);
    }
    renderTimestamps();
  }

  // Tells the server members have seen this conversation (clears its unread count). The server then
  // notifies every open Inbox, which re-fetches the list — so this can't loop back into itself.
  function markRead(id) {
    return apiPost(withClient('/api/conversations/' + id + '/read')).catch(function () { /* non-critical */ });
  }

  // Mirrors Common/Helpers/ChannelIconHelper.cs — keep both in sync if a channel is added.
  var CHANNEL_ICONS = {
    whatsapp: { initials: 'WA', hex: '#25D366' },
    instagram: { initials: 'IG', hex: '#C13584' },
    facebook: { initials: 'FB', hex: '#1877F2' },
    telegram: { initials: 'TG', hex: '#229ED9' },
    website: { initials: 'WEB', hex: '#6b7280' },
    sms: { initials: 'SMS', hex: '#6b7280' },
    email: { initials: 'MAIL', hex: '#6b7280' }
  };

  function channelInitials(channel) { return (CHANNEL_ICONS[channel] || {}).initials || '?'; }
  function channelLabel(channel) {
    return { whatsapp: 'WhatsApp', instagram: 'Instagram', facebook: 'Facebook', telegram: 'Telegram', website: omniText.t('knowledge.source.website'), sms: 'SMS', email: omniText.t('inbox.contact.email') }[channel] || channel || '';
  }
  function channelColor(channel) { return (CHANNEL_ICONS[channel] || {}).hex || '#9ca3af'; }

  // Mirrors ChannelIconHelper.Svg() in C# — recognizable brand glyph instead of plain initials.
  var WHATSAPP_SVG = '<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 0 1-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 0 1-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 0 1 2.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0 0 12.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 0 0 5.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 0 0-3.48-8.413"/></svg>';
  var TELEGRAM_SVG = '<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.3 3.64 12c-.88-.25-.89-.86.2-1.3l15.97-6.16c.73-.33 1.43.18 1.15 1.3l-2.72 12.81c-.19.91-.74 1.13-1.5.71L12.6 16.3l-1.99 1.93c-.23.23-.42.42-.83.42z"/></svg>';
  function channelIcon(channel) { return channel === 'whatsapp' ? WHATSAPP_SVG : (channel === 'telegram' ? TELEGRAM_SVG : null); }

  // The client's pipeline stages ({key, category, label}); badge colours by category mirror Common/Helpers/StageDisplay.cs,
  // and the journey shows how far along the categories in cfg.journeyCategories a contact is (0 = off that path).
  var STAGES = {};
  (cfg.stages || []).forEach(function (s) { STAGES[s.key] = s; });
  var CATEGORY_BADGES = {
    open: 'badge-blue', booked: 'badge-amber', attended: 'badge-green', no_show: 'badge-red',
    converted: 'badge-teal', closed_lost: 'badge-gray', needs_human: 'badge-red'
  };
  function journeyStep(category) { return (cfg.journeyCategories || []).indexOf(category) + 1; }

  // A mode marker in the reader's language from its key (QA t7-07); a marker without one shows its stored text.
  var MARKER_TEXT = {
    handed_to_team: function () { return omniText.t('inbox.marker.handedToTeam'); },
    returned_to_ai: function () { return omniText.t('inbox.marker.returnedToAi'); }
  };
  function markerText(m) {
    var match = /"marker"\s*:\s*"([a-z_]+)"/.exec(m.metadataJson || '');
    return match && MARKER_TEXT[match[1]] ? MARKER_TEXT[match[1]]() : (m.content || '');
  }

  function escapeHtml(s) {
    var div = document.createElement('div');
    div.textContent = s == null ? '' : String(s);
    return div.innerHTML;
  }

  // WhatsApp-style delivery ticks for outbound messages — sent (single gray check), delivered
  // (double gray check), read (double blue check), failed (red mark). Deliberately not shown for
  // inbound messages (a customer's message never has a "delivery status" from our side).
  function deliveryStatusIcon(status) {
    switch (status) {
      case 'sent': return '<span class="inbox-msg-tick" title="' + escapeHtml(omniText.t('values.sent')) + '">&#10003;</span>';
      case 'delivered': return '<span class="inbox-msg-tick" title="' + escapeHtml(omniText.t('values.delivered')) + '">&#10003;&#10003;</span>';
      case 'read': return '<span class="inbox-msg-tick inbox-msg-tick-read" title="' + escapeHtml(omniText.t('values.read')) + '">&#10003;&#10003;</span>';
      case 'failed': return '<span class="inbox-msg-tick inbox-msg-tick-failed" title="' + escapeHtml(omniText.t('values.failed')) + '">&#9888;</span>';
      case 'deleted': return '<span class="inbox-msg-tick" title="' + escapeHtml(omniText.t('values.deleted')) + '">&#128465;</span>';
      default: return '';
    }
  }

  // "09:40" — the day is shown once by the day divider above, not on every bubble.
  function formatClock(iso) {
    var d = new Date(iso);
    return isNaN(d.getTime()) ? '' : d.toLocaleTimeString(omniText.locale, { hour: '2-digit', minute: '2-digit' });
  }

  // Day divider text: "Today", "Yesterday", or e.g. "Mon, Sep 22".
  function formatDay(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    var today = new Date();
    var yesterday = new Date(); yesterday.setDate(today.getDate() - 1);
    if (d.toDateString() === today.toDateString()) return omniText.t('bookings.calendar.today');
    if (d.toDateString() === yesterday.toDateString()) return omniText.t('inbox.time.yesterday');
    return d.toLocaleDateString(omniText.locale, { weekday: 'short', month: 'short', day: 'numeric' });
  }

  // Only senders that aren't obvious from the bubble's side and colour get a label: the customer
  // (left) and member replies from the dashboard (right, accent) go unlabelled.
  function bubbleLabel(origin) {
    if (origin === 'whatsapp_customer' || origin === 'telegram_customer' || origin === 'dashboard') return '';
    return '<div class="inbox-msg-source">' + escapeHtml(originLabel(origin)) + '</div>';
  }

  // A voice note plays; a photo, location, contact card, Flows answer… reads by its type (inbox-message-view.js, QA t10-09);
  // anything else is its text.
  function messageBody(m) {
    if (window.InboxVoice && window.InboxVoice.isVoice(m)) return window.InboxVoice.html(m);
    var typed = window.InboxMessageView ? window.InboxMessageView.html(m) : null;
    return typed !== null && typed !== undefined ? typed : escapeHtml(m.content || '');
  }

  // When a message happened: when the customer wrote it or it was sent (the order the server returns them in, QA t3-inbox-16),
  // else when it was saved.
  function messageAt(m) { return m.receivedAt || m.sentAt || m.createdAt; }

  // keepPlace: re-rendering the same conversation (a refresh, earlier messages) keeps the reader where they were unless they were
  // at the newest message; a newly opened conversation starts at the bottom.
  function renderMessages(messages, hasEarlier, keepPlace) {
    var fromBottom = messagesEl.scrollHeight - messagesEl.scrollTop;
    var atBottom = fromBottom - messagesEl.clientHeight < 40;
    messagesEl.innerHTML = '';
    if (hasEarlier) {
      // Only the newest messages are loaded; this asks for one more page (QA t3-inbox-16).
      var earlier = document.createElement('div');
      earlier.className = 'inbox-earlier';
      earlier.innerHTML = '<button type="button" class="au-btn au-btn--outlined au-btn--sm">' + escapeHtml(omniText.t('inbox.thread.loadEarlier')) + '</button>';
      earlier.querySelector('button').addEventListener('click', function (e) {
        e.currentTarget.disabled = true;
        threadTake = messages.length + threadPage;
        refreshCurrentConversation().catch(refreshFailed);
      });
      messagesEl.appendChild(earlier);
    }
    var lastDay = null;
    messages.forEach(function (m) {
      var day = new Date(messageAt(m)).toDateString();
      if (day !== lastDay) {
        lastDay = day;
        var divider = document.createElement('div');
        divider.className = 'inbox-day-divider';
        divider.innerHTML = '<span>' + escapeHtml(formatDay(messageAt(m))) + '</span>';
        messagesEl.appendChild(divider);
      }
      // System markers (mode changes, hand-offs) are notes for the team; an automatic message the contact received
      // (opt-out confirmation, reminder) carries {"automatic":true} and is shown as a bubble.
      if (m.origin === 'system' && m.senderType === 'system' && !/"automatic"\s*:\s*true/.test(m.metadataJson || '')) {
        var marker = document.createElement('div');
        marker.className = 'inbox-mode-marker';
        marker.innerHTML = '<span>' + escapeHtml(markerText(m)) + ' &middot; ' + formatClock(messageAt(m)) + '</span>';
        messagesEl.appendChild(marker);
        return;
      }
      var row = document.createElement('div');
      row.className = 'inbox-msg-row ' + (m.direction === 'outbound' ? 'outbound' : 'inbound');

      var bubble = document.createElement('div');
      bubble.className = 'inbox-msg-bubble ' + originBubbleClass(m.origin, m.direction);
      var tick = m.direction === 'outbound' ? deliveryStatusIcon(m.deliveryStatus) : '';
      var failureNote = m.deliveryStatus === 'failed' && m.failureReason
        ? '<div class="inbox-msg-failure">' + escapeHtml(m.failureReason) + '</div>' : '';
      bubble.innerHTML =
        bubbleLabel(m.origin) +
        '<div class="inbox-msg-content" dir="auto">' + messageBody(m) + '</div>' +
        failureNote +
        '<div class="inbox-msg-time" title="' + escapeHtml(formatTime(messageAt(m))) + '">' + formatClock(messageAt(m)) + ' ' + tick +
        // A message the contact edited afterwards (Telegram; QA t3-inbox-17) shows the new text and says so.
        (/"editedAt"\s*:/.test(m.metadataJson || '') ? ' <span class="inbox-msg-edited">' + escapeHtml(omniText.t('inbox.thread.edited')) + '</span>' : '') + '</div>';

      row.appendChild(bubble);
      messagesEl.appendChild(row);
    });
    messagesEl.scrollTop = keepPlace && !atBottom ? messagesEl.scrollHeight - fromBottom : messagesEl.scrollHeight;
  }

  // ConversationResponse (from the API) doesn't carry contact/service display info — that's a
  // list-only concern (ConversationListRow). We already have it on the clicked list item's own
  // data attributes; a conversation that isn't in the loaded list (a notification's link, an older one)
  // takes it from its contact instead.
  function renderConversationHeader(conversation) {
    var listItem = document.querySelector('.inbox-conv-item[data-conversation-id="' + conversation.id + '"]');
    var showContact = function (contactName, service) {
      nameEl.textContent = contactName || omniText.t('inbox.unknownName');
      threadAvatarEl.textContent = initials(contactName);
      serviceEl.textContent = [service, channelLabel(conversation.channel)].filter(Boolean).join(' · ');
    };
    if (listItem) {
      showContact(listItem.getAttribute('data-contact-name'), listItem.getAttribute('data-service-name') || '');
    } else {
      showContact(null, '');
      var fill = function (contact) {
        if (currentConversationId === conversation.id) showContact(contact.fullName, contact.serviceName || '');
      };
      if (contactInfoCache[conversation.contactId]) {
        fill(contactInfoCache[conversation.contactId]);
      } else {
        apiGet(withClient('/api/contacts/' + conversation.contactId)).then(function (contact) {
          contactInfoCache[conversation.contactId] = contact;
          fill(contact);
        }).catch(function () { /* the header keeps "Unknown" */ });
      }
    }
    modeEl.textContent = conversation.mode === 'ai' ? omniText.t('inbox.thread.aiReplying')
      : conversation.mode === 'human' ? omniText.t('inbox.thread.teamReplying') : omniText.t('inbox.thread.awaitingApproval');
    modeEl.className = 'inbox-mode inbox-mode-' + conversation.mode;
    // Only offer the switch that makes sense for the current mode.
    btnTakeover.hidden = conversation.mode === 'human';
    // Without the AI assistant in the plan nothing would answer, so the conversation stays with members.
    btnReturnAi.hidden = conversation.mode === 'ai' || cfg.aiAssistantAvailable === false;
    var closed = conversation.status === 'closed' || conversation.status === 'archived';
    btnMore.hidden = closed; // "⋯" only holds Close conversation for now
    btnReopen.hidden = !closed; // a closed conversation offers Reopen instead
    setMoreMenu(false);

    // Backend is authoritative for this (see MessageService.SendAsync's ServiceWindowClosedException)
    // — this just mirrors it in the UI so members aren't surprised by a rejected send.
    // The 24h customer-service window (and message templates) are WhatsApp-only — a Telegram
    // conversation can always be replied to, so its composer is never disabled and has no template button.
    var isWhatsApp = conversation.channel === 'whatsapp';
    // A channel that isn't connected can't send at all (QA t8-18): the composer and templates are off, with a notice.
    var channelOff = conversation.channelConnected === false;
    currentWindowOpen = !channelOff && (!isWhatsApp || !!conversation.isServiceWindowOpen);
    composerInputEl.disabled = !currentWindowOpen;
    composerSendEl.disabled = !currentWindowOpen;
    windowClosedNoticeEl.hidden = currentWindowOpen || channelOff;
    var channelOffEl = document.getElementById('inbox-channel-off-notice');
    if (channelOffEl) channelOffEl.hidden = !channelOff;
    windowEndsAt = isWhatsApp && currentWindowOpen && conversation.serviceWindowExpiresAt ? new Date(conversation.serviceWindowExpiresAt) : null;
    updateWindowCountdown();
    updateComposerCount();
    btnSendTemplate.hidden = !isWhatsApp || channelOff;
    hideTemplatePicker();
  }

  // ---------------------------------------------------------------------
  // Data loading
  // ---------------------------------------------------------------------
  // How many messages the open conversation shows: null = the server's first page (Inbox / ThreadPageSize); "Show earlier
  // messages" adds a page (threadPage = the first page's size) and refreshes keep asking for that many.
  var threadTake = null;
  var threadPage = 0;

  function selectConversation(id) {
    var switchingConversation = id !== currentConversationId;
    currentConversationId = id;
    if (switchingConversation) threadTake = null;
    emptyEl.hidden = true;
    threadEl.hidden = false;
    shellEl.classList.add('has-thread'); // phones show one pane at a time
    if (switchingConversation) {
      contactInfoEl.hidden = true; // don't show the previous conversation's contact while the new one loads
      markRead(id); // opening a conversation = reading it
      // Drop its unread marker right away instead of waiting for the round trip.
      var opened = document.querySelector('.inbox-conv-item[data-conversation-id="' + id + '"]');
      if (opened) {
        opened.classList.remove('unread');
        var b = opened.querySelector('.inbox-unread-badge');
        if (b) b.remove();
      }
    }

    document.querySelectorAll('.inbox-conv-item').forEach(function (el) {
      el.classList.toggle('active', el.getAttribute('data-conversation-id') === id);
    });

    var url = '/api/conversations/' + id + (threadTake ? '?take=' + threadTake : '');
    return apiGet(withClient(url)).then(function (result) {
      refreshWorked();
      if (id !== currentConversationId) return; // another conversation was opened meanwhile
      if (!threadTake) threadPage = result.messages.length;
      currentContactId = result.conversation.contactId;
      renderConversationHeader(result.conversation);
      renderMessages(result.messages, result.hasEarlier, !switchingConversation);
      if (!contactInfoEl.hidden) {
        loadContactInfo(currentContactId); // panel was left open across a refresh — keep it current
      }
    });
  }

  // ---------------------------------------------------------------------
  // Contact info corner
  // ---------------------------------------------------------------------
  function loadContactInfo(contactId) {
    var cached = contactInfoCache[contactId];
    var render = function (contact) {
      var name = contact.fullName || nameEl.textContent || omniText.t('inbox.unknownName');
      document.getElementById('inbox-contact-name').textContent = name;
      document.getElementById('inbox-contact-avatar').textContent = initials(name);
      var stage = STAGES[contact.stageKey];
      var stageEl = document.getElementById('inbox-contact-stage');
      stageEl.textContent = stage ? stage.label : (contact.stageKey || '—').replace(/_/g, ' ');
      stageEl.className = 'badge ' + ((stage && CATEGORY_BADGES[stage.category]) || 'badge-gray');
      document.getElementById('inbox-contact-phone').textContent = contact.phone || '—';
      document.getElementById('inbox-contact-email').textContent = contact.email || '—';
      document.getElementById('inbox-contact-service').textContent = contact.serviceName || '—';
      document.getElementById('inbox-contact-source').textContent = contact.source || '—';
      document.getElementById('inbox-contact-timeline').textContent = contact.desiredTimeline || '—';
      document.getElementById('inbox-contact-followup').textContent = formatDate(contact.nextFollowupAt);
      document.getElementById('inbox-contact-created').textContent = formatDate(contact.createdAt);
      document.getElementById('inbox-contact-profile').href = '/dashboard/contacts/' + contact.id;
      // The contact page opens its "add order" popup for ?order=new.
      document.getElementById('inbox-contact-order').href = '/dashboard/contacts/' + contact.id + '?order=new';

      var step = stage ? journeyStep(stage.category) : 0;
      var journeyWrap = document.getElementById('inbox-contact-journey-wrap');
      // Off-path stages (lost, no-show…) show just the badge; so does every contact on a plan without bookings, whose journey
      // (booked, attended) would only point at what the plan doesn't include (QA t3-inbox-20).
      journeyWrap.hidden = step === 0 || journeyWrap.getAttribute('data-bookings') === 'off';
      document.querySelectorAll('#inbox-contact-journey i').forEach(function (el, i) { el.classList.toggle('on', i < step); });
    };

    if (cached) {
      render(cached);
      return Promise.resolve();
    }
    return apiGet(withClient('/api/contacts/' + contactId)).then(function (contact) {
      contactInfoCache[contactId] = contact;
      render(contact);
    });
  }

  btnInfo.addEventListener('click', function () {
    if (!currentContactId) return;
    var willShow = contactInfoEl.hidden;
    contactInfoEl.hidden = !willShow;
    if (willShow) loadContactInfo(currentContactId);
  });

  btnContactClose.addEventListener('click', function () { contactInfoEl.hidden = true; });

  // Phones: back from the thread to the list.
  btnBack.addEventListener('click', function () {
    shellEl.classList.remove('has-thread');
    contactInfoEl.hidden = true;
  });

  function refreshCurrentConversation() {
    if (!currentConversationId) return Promise.resolve();
    return selectConversation(currentConversationId);
  }

  // ---------------------------------------------------------------------
  // Composer + mode controls
  // ---------------------------------------------------------------------
  // The longest text one message may have (Telegram's and WhatsApp's limit is 4096). Near it the composer counts, past it
  // Send waits (QA t3-inbox-14: the 4108-character message was refused by Telegram after sending).
  var MAX_MESSAGE = 4096;
  var countEl = document.getElementById('inbox-composer-count');
  function updateComposerCount() {
    if (!countEl) return;
    var length = composerInputEl.value.length;
    countEl.hidden = length < MAX_MESSAGE - 300;
    countEl.textContent = omniText.t('inbox.composer.count', [length, MAX_MESSAGE]);
    countEl.classList.toggle('is-over', length > MAX_MESSAGE);
    if (currentWindowOpen) composerSendEl.disabled = length > MAX_MESSAGE;
  }
  composerInputEl.addEventListener('input', updateComposerCount);

  // WhatsApp's 24-hour window: how long is left, and the composer closes when it ends while the page is open (QA t8-19:
  // the next send failed with a 409).
  var windowEndsAt = null;
  var windowEl = document.getElementById('inbox-composer-window');
  function updateWindowCountdown() {
    if (!windowEl) return;
    if (!windowEndsAt) { windowEl.hidden = true; return; }
    var left = windowEndsAt.getTime() - Date.now();
    if (left <= 0) {
      windowEndsAt = null;
      windowEl.hidden = true;
      currentWindowOpen = false;
      composerInputEl.disabled = true;
      composerSendEl.disabled = true;
      windowClosedNoticeEl.hidden = false;
      return;
    }
    var hours = Math.floor(left / 3600000), minutes = Math.max(1, Math.ceil((left % 3600000) / 60000));
    windowEl.hidden = false;
    windowEl.textContent = hours > 0 ? omniText.t('inbox.composer.windowHours', [hours, minutes]) : omniText.t('inbox.composer.windowMinutes', [minutes]);
    windowEl.classList.toggle('is-soon', left < 3600000);
  }
  setInterval(updateWindowCountdown, 30000);

  // Enter sends (matches WhatsApp/Slack-style chat composers); Shift+Enter inserts a newline.
  composerInputEl.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (typeof composerEl.requestSubmit === 'function') {
        composerEl.requestSubmit();
      } else {
        composerEl.dispatchEvent(new Event('submit', { cancelable: true }));
      }
    }
  });

  // Optimistic UI: show the message in the thread the instant Send is pressed, instead of
  // waiting for the full send -> WhatsApp API -> DB write -> re-fetch round trip. WhatsApp pushes
  // the message to the customer's phone as soon as Meta accepts it — the very first step in that
  // chain — so without this, our own UI update always lagged behind the phone notification.
  // On success, refreshCurrentConversation() rebuilds #inbox-messages from the server's real data
  // (including the real timestamp/delivery status), which naturally replaces this bubble — no
  // explicit swap needed. On failure, it's removed and the typed text is restored.
  function appendOptimisticMessage(content) {
    var row = document.createElement('div');
    row.className = 'inbox-msg-row outbound';
    row.setAttribute('data-optimistic', 'true');
    var bubble = document.createElement('div');
    bubble.className = 'inbox-msg-bubble inbox-msg-member';
    bubble.innerHTML =
      '<div class="inbox-msg-content" dir="auto">' + escapeHtml(content) + '</div>' +
      '<div class="inbox-msg-time">' + escapeHtml(omniText.t('inbox.composer.sending')) + '</div>';
    row.appendChild(bubble);
    messagesEl.appendChild(row);
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }

  function removeOptimisticMessage() {
    var el = messagesEl.querySelector('[data-optimistic="true"]');
    if (el) el.remove();
  }

  composerEl.addEventListener('submit', function (e) {
    e.preventDefault();
    if (!currentConversationId) return;
    var content = composerInputEl.value.trim();
    if (!content) return;

    appendOptimisticMessage(content);
    composerInputEl.value = '';

    composerSendEl.disabled = true; // basic guard against accidental double-submit
    apiPost(withClient('/api/conversations/' + currentConversationId + '/messages/send'), { content: content })
      .then(function () {
        return Promise.all([refreshCurrentConversation(), loadConversationList()]);
      })
      .catch(function (err) {
        removeOptimisticMessage();
        composerInputEl.value = content; // give the text back so they can retry
        showError(omniText.t('inbox.composer.sendFailed', [err.message]));
      })
      .then(function () { composerSendEl.disabled = false; });
  });

  // ---------------------------------------------------------------------
  // Send Template — usable any time (proactive outreach), and the only option once the 24h
  // service window is closed. See MessageService.SendTemplateAsync / POST .../messages/send-template.
  // ---------------------------------------------------------------------
  function loadApprovedTemplates() {
    if (approvedTemplates) return Promise.resolve(approvedTemplates);
    return apiGet(withClient('/api/whatsapp/templates')).then(function (templates) {
      // Approved templates with a body to show: one known only from a Meta event has none, and was preselected with an
      // empty preview (QA t8-19).
      approvedTemplates = (templates || []).filter(function (t) { return t.status === 'approved' && (t.body || '').trim() !== ''; });
      return approvedTemplates;
    });
  }

  function renderTemplateVariables(template) {
    templateVariablesEl.innerHTML = '';
    var matches = (template.body || '').match(/\{\{\d+\}\}/g) || [];
    var count = matches.reduce(function (max, m) { return Math.max(max, parseInt(m.replace(/\D/g, ''), 10)); }, 0);
    for (var i = 1; i <= count; i++) {
      var input = document.createElement('input');
      input.type = 'text';
      input.className = 'text-input mb-1';
      input.placeholder = omniText.t('inbox.template.valueFor', ['{{' + i + '}}']);
      input.setAttribute('data-var-index', i);
      input.addEventListener('input', updateTemplatePreview);
      templateVariablesEl.appendChild(input);
    }
    updateTemplatePreview();
  }

  function currentTemplateBodyParameters() {
    return Array.prototype.slice.call(templateVariablesEl.querySelectorAll('input')).map(function (el) { return el.value; });
  }

  function updateTemplatePreview() {
    var template = selectedTemplate();
    if (!template) { templatePreviewEl.textContent = ''; return; }
    var rendered = template.body || '';
    currentTemplateBodyParameters().forEach(function (val, idx) {
      rendered = rendered.split('{{' + (idx + 1) + '}}').join(val || ('{{' + (idx + 1) + '}}'));
    });
    templatePreviewEl.textContent = rendered;
  }

  function selectedTemplate() {
    var id = templateSelectEl.value;
    return (approvedTemplates || []).filter(function (t) { return t.id === id; })[0];
  }

  function showTemplatePicker() {
    if (!currentConversationId) return;
    loadApprovedTemplates().then(function (templates) {
      templateSelectEl.innerHTML = '';
      if (templates.length === 0) {
        templateSelectEl.innerHTML = '<option value="">' + escapeHtml(omniText.t('inbox.template.none')) + '</option>';
      } else {
        templates.forEach(function (t) {
          var opt = document.createElement('option');
          opt.value = t.id;
          // Its category says what it costs (marketing is charged at a higher rate than utility; QA t8-19).
          var categoryKey = 'values.' + String(t.category || '').toLowerCase(); // values.marketing / values.utility
          var category = t.category ? omniText.t(categoryKey) : '';
          opt.textContent = t.name + ' (' + t.language + ')' + (category && category.indexOf('values.') !== 0 ? ' · ' + category : '');
          templateSelectEl.appendChild(opt);
        });
      }
      composerEl.hidden = true;
      templatePickerEl.hidden = false;
      if (templates.length > 0) renderTemplateVariables(templates[0]);
    });
  }

  function hideTemplatePicker() {
    templatePickerEl.hidden = true;
    composerEl.hidden = false;
  }

  templateSelectEl.addEventListener('change', function () {
    var t = selectedTemplate();
    if (t) renderTemplateVariables(t);
  });

  btnSendTemplate.addEventListener('click', showTemplatePicker);
  btnTemplateCancel.addEventListener('click', hideTemplatePicker);

  templatePickerEl.addEventListener('submit', function (e) {
    e.preventDefault();
    var template = selectedTemplate();
    if (!template || !currentConversationId) return;

    var sendBtn = document.getElementById('inbox-template-send');
    sendBtn.disabled = true;
    apiPost(withClient('/api/conversations/' + currentConversationId + '/messages/send-template'), {
      whatsAppTemplateId: template.id,
      bodyParameters: currentTemplateBodyParameters()
    })
      .then(function () {
        hideTemplatePicker();
        return Promise.all([refreshCurrentConversation(), loadConversationList()]);
      })
      .catch(function (err) {
        showError(omniText.t('inbox.template.sendFailed', [err.message]));
      })
      .then(function () { sendBtn.disabled = false; });
  });

  btnTakeover.addEventListener('click', function () {
    if (!currentConversationId) return;
    apiPost(withClient('/api/conversations/' + currentConversationId + '/take-over'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); })
      .catch(actionFailed);
  });

  btnReturnAi.addEventListener('click', function () {
    if (!currentConversationId) return;
    apiPost(withClient('/api/conversations/' + currentConversationId + '/return-to-ai'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); })
      .catch(actionFailed);
  });

  btnReopen.addEventListener('click', function () {
    if (!currentConversationId) return;
    apiPost(withClient('/api/conversations/' + currentConversationId + '/reopen'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); })
      .catch(actionFailed);
  });

  // "⋯" menu in the thread header.
  function setMoreMenu(open) {
    moreMenuEl.hidden = !open;
    btnMore.setAttribute('aria-expanded', open ? 'true' : 'false');
  }
  btnMore.addEventListener('click', function (e) {
    e.stopPropagation();
    setMoreMenu(moreMenuEl.hidden);
  });
  document.addEventListener('click', function (e) {
    if (!moreMenuEl.hidden && !moreMenuEl.contains(e.target)) setMoreMenu(false);
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !moreMenuEl.hidden) setMoreMenu(false);
  });

  btnClose.addEventListener('click', function () {
    setMoreMenu(false);
    if (!currentConversationId) return;
    var id = currentConversationId;
    ask(omniText.t('inbox.thread.close'), omniText.t('inbox.thread.closeConfirm'), omniText.t('inbox.thread.close')).then(function (ok) {
      if (!ok) return;
      apiPost(withClient('/api/conversations/' + id + '/close'))
        .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); })
        .catch(actionFailed);
    });
  });

  // Wire up the server-rendered initial list rows (before any SignalR/refresh replaces them).
  document.querySelectorAll('.inbox-conv-item').forEach(function (el) {
    el.addEventListener('click', function () {
      selectConversation(el.getAttribute('data-conversation-id')).catch(actionFailed);
    });
  });
  renderTimestamps();
  // Replace the server-rendered first paint with the client-rendered list so unread badges / the tab
  // title are consistent from the start.
  // Dashboard 'Needs attention' link (/inbox?filter=needs) opens with that tab; "Open in Inbox" from a contact page
  // (/inbox?search=<phone>) searches every conversation and opens the only match straight away.
  var startQuery = new URLSearchParams(window.location.search);
  var startTab = startQuery.get('filter') && document.querySelector('.inbox-filter[data-filter="' + startQuery.get('filter') + '"]');
  if (startTab) selectTab(startTab);
  if (startQuery.get('search') && searchEl) {
    searchEl.value = startQuery.get('search');
    listSearch = searchEl.value.trim();
  }
  loadConversationList().then(function () {
    // Deep link from a notification bell click (/inbox?conversationId=...): it opens even when it isn't among the rows loaded.
    var wanted = startQuery.get('conversationId');
    if (wanted) return selectConversation(wanted);
    if (listSearch && listItems.length === 1) return selectConversation(listItems[0].id);
  }).catch(refreshFailed);

  // ---------------------------------------------------------------------
  // SignalR — real-time notifications only. Every handler re-fetches from the API rather than
  // trusting the event payload as authoritative data (see file header).
  // ---------------------------------------------------------------------
  if (window.signalR) {
    var connection = new signalR.HubConnectionBuilder()
      .withUrl(cfg.hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on('NewMessage', function (payload) {
      var inOpenConversation = payload && payload.conversationId === currentConversationId;
      if (inOpenConversation) {
        refreshCurrentConversation().catch(refreshFailed);
        // A customer message arriving in the conversation members are looking at is seen immediately.
        if (payload.direction === 'inbound') markRead(currentConversationId);
      }
      // Any other conversation: the refreshed list shows it as unread (badge + bold + tab title).
      loadConversationList().catch(refreshFailed);
    });

    connection.on('MessageStatusUpdated', function (payload) {
      if (payload && payload.conversationId === currentConversationId) {
        refreshCurrentConversation().catch(refreshFailed);
      }
    });

    connection.on('ConversationUpdated', function () {
      loadConversationList().catch(refreshFailed);
    });

    connection.on('ConversationModeChanged', function (payload) {
      loadConversationList().catch(refreshFailed);
      if (payload && payload.conversationId === currentConversationId) {
        refreshCurrentConversation().catch(refreshFailed);
      }
    });

    // On reconnect, state may have changed while disconnected — re-read everything from the API
    // rather than assuming we didn't miss anything.
    connection.onreconnected(function () {
      loadConversationList().catch(refreshFailed);
      refreshCurrentConversation().catch(refreshFailed);
    });

    connection.start().catch(function (err) {
      console.error('[inbox] SignalR connection failed', err);
    });
  }
})();
