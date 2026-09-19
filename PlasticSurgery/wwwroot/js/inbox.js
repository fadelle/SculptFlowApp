// Drives the /inbox page: conversation list, message thread, composer, and the mode controls
// (Take Over / Return to AI / Close). PostgreSQL (via the REST API) is the only source of truth —
// SignalR only tells us *that* something changed; every handler below reacts by re-fetching from
// the API rather than trusting the SignalR payload's own fields as authoritative. If the socket
// disconnects or a page load happens after missing events entirely, nothing is lost: reloading
// re-reads current state from the database the normal way.
(function () {
  var cfg = window.PS_INBOX_CONFIG || {};
  var currentConversationId = null;
  var currentLeadId = null;
  var currentWindowOpen = true;
  var leadInfoCache = {}; // leadId -> LeadResponse, avoids refetching every toggle
  var approvedTemplates = null; // WhatsAppTemplateResponse[], lazy-loaded on first "Send Template" click

  var listEl = document.getElementById('inbox-list');
  var emptyEl = document.getElementById('inbox-empty');
  var threadEl = document.getElementById('inbox-thread-inner');
  var nameEl = document.getElementById('inbox-thread-name');
  var procedureEl = document.getElementById('inbox-thread-procedure');
  var modeEl = document.getElementById('inbox-thread-mode');
  var messagesEl = document.getElementById('inbox-messages');
  var composerEl = document.getElementById('inbox-composer');
  var composerInputEl = document.getElementById('inbox-composer-input');
  var composerSendEl = document.getElementById('inbox-composer-send');
  var btnTakeover = document.getElementById('inbox-btn-takeover');
  var btnReturnAi = document.getElementById('inbox-btn-return-ai');
  var btnClose = document.getElementById('inbox-btn-close');
  var btnInfo = document.getElementById('inbox-btn-info');
  var leadInfoEl = document.getElementById('inbox-lead-info');
  var windowClosedNoticeEl = document.getElementById('inbox-window-closed-notice');
  var btnSendTemplate = document.getElementById('inbox-btn-send-template');
  var templatePickerEl = document.getElementById('inbox-template-picker');
  var templateSelectEl = document.getElementById('inbox-template-select');
  var templateVariablesEl = document.getElementById('inbox-template-variables');
  var templatePreviewEl = document.getElementById('inbox-template-preview');
  var btnTemplateCancel = document.getElementById('inbox-template-cancel');

  // ---------------------------------------------------------------------
  // API helpers
  // ---------------------------------------------------------------------
  function apiGet(path) {
    return fetch(path).then(function (res) {
      if (!res.ok) throw new Error('Request failed (' + res.status + ')');
      return res.json();
    });
  }

  function apiPost(path, body) {
    return fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body || {})
    }).then(function (res) {
      if (!res.ok) {
        return res.json().catch(function () { return {}; }).then(function (problem) {
          throw new Error(problem.error || problem.detail || ('Request failed (' + res.status + ')'));
        });
      }
      return res.json().catch(function () { return {}; });
    });
  }

  // The API resolves clinicId server-side from the logged-in session (CurrentClinicContext) now —
  // this used to append ?clinicId=... to every URL; kept as a no-op pass-through so every call site
  // below doesn't need touching.
  function withClinic(path) {
    return path;
  }

  // ---------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------
  var ORIGIN_LABELS = {
    whatsapp_customer: 'Customer',
    telegram_customer: 'Customer',
    whatsapp_business_app: 'WhatsApp Business App',
    dashboard: 'Staff',
    ai: 'AI',
    system: 'System'
  };

  function originLabel(origin) {
    return ORIGIN_LABELS[origin] || origin;
  }

  function originBubbleClass(origin, direction) {
    if (origin === 'whatsapp_customer' || origin === 'telegram_customer') return 'inbox-msg-customer';
    if (origin === 'ai') return 'inbox-msg-ai';
    if (origin === 'dashboard') return 'inbox-msg-staff';
    if (origin === 'whatsapp_business_app') return 'inbox-msg-staff-app';
    return 'inbox-msg-system';
  }

  function formatTime(iso) {
    try {
      var d = new Date(iso);
      return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    } catch (e) {
      return iso;
    }
  }

  function renderTimestamps() {
    document.querySelectorAll('.inbox-conv-time[data-timestamp]').forEach(function (el) {
      el.textContent = formatTime(el.getAttribute('data-timestamp'));
    });
  }

  function renderConversationList(items) {
    listEl.innerHTML = '';
    if (!items || items.length === 0) {
      listEl.innerHTML = '<div class="text-subtle" style="padding:1.5rem 1rem">No conversations yet.</div>';
      return;
    }
    items.forEach(function (c) {
      var div = document.createElement('div');
      div.className = 'inbox-conv-item' + (c.id === currentConversationId ? ' active' : '');
      div.setAttribute('data-conversation-id', c.id);
      div.setAttribute('data-lead-name', c.leadFullName || 'Unknown');
      div.setAttribute('data-procedure-name', c.procedureName || '');

      // "Needs Human" means AI is paused AND nobody's replied to the customer's last message yet
      // — not just "mode is human". Staff sending from the dashboard also sets mode to human, but
      // that's a conversation actively being handled, not one waiting for attention.
      var needsHuman = (c.mode === 'human' && c.lastMessageDirection === 'inbound')
        ? ' <span class="badge badge-red" style="margin-left:.3rem">Needs Human</span>' : '';

      div.innerHTML =
        '<div class="flex items-center justify-between">' +
          '<span class="flex items-center gap-1">' +
            '<span class="inbox-channel-icon" style="background:' + channelColor(c.channel) + '" title="' + escapeHtml(c.channel) + '">' + (channelIcon(c.channel) || channelInitials(c.channel)) + '</span>' +
            '<span class="inbox-conv-name">' + escapeHtml(c.leadFullName || 'Unknown') + '</span>' +
          '</span>' +
          '<span class="badge ' + badgeClassForMode(c.mode) + '">' + escapeHtml(c.mode) + '</span>' +
        '</div>' +
        (c.procedureName ? '<div class="inbox-conv-procedure">' + escapeHtml(c.procedureName) + '</div>' : '') +
        '<div class="inbox-conv-preview">' + escapeHtml(c.lastMessagePreview || 'No messages yet') + needsHuman + '</div>' +
        '<div class="inbox-conv-time" data-timestamp="' + (c.lastMessageAt || c.createdAt) + '"></div>';

      div.addEventListener('click', function () { selectConversation(c.id); });
      listEl.appendChild(div);
    });
    renderTimestamps();
  }

  // Mirrors Pages/Shared/ChannelIconHelper.cs — keep both in sync if a channel is added.
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
  function channelColor(channel) { return (CHANNEL_ICONS[channel] || {}).hex || '#9ca3af'; }

  // Mirrors ChannelIconHelper.Svg() in C# — recognizable brand glyph instead of plain initials.
  var WHATSAPP_SVG = '<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 0 1-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 0 1-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 0 1 2.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0 0 12.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 0 0 5.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 0 0-3.48-8.413"/></svg>';
  var TELEGRAM_SVG = '<svg viewBox="0 0 24 24" fill="#fff" xmlns="http://www.w3.org/2000/svg"><path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.3 3.64 12c-.88-.25-.89-.86.2-1.3l15.97-6.16c.73-.33 1.43.18 1.15 1.3l-2.72 12.81c-.19.91-.74 1.13-1.5.71L12.6 16.3l-1.99 1.93c-.23.23-.42.42-.83.42z"/></svg>';
  function channelIcon(channel) { return channel === 'whatsapp' ? WHATSAPP_SVG : (channel === 'telegram' ? TELEGRAM_SVG : null); }

  function badgeClassForMode(mode) {
    if (mode === 'ai') return 'badge-teal';
    if (mode === 'human') return 'badge-red';
    if (mode === 'approval') return 'badge-purple';
    return 'badge-gray';
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
      case 'sent': return '<span class="inbox-msg-tick" title="Sent">&#10003;</span>';
      case 'delivered': return '<span class="inbox-msg-tick" title="Delivered">&#10003;&#10003;</span>';
      case 'read': return '<span class="inbox-msg-tick inbox-msg-tick-read" title="Read">&#10003;&#10003;</span>';
      case 'failed': return '<span class="inbox-msg-tick inbox-msg-tick-failed" title="Failed">&#9888;</span>';
      case 'deleted': return '<span class="inbox-msg-tick" title="Deleted">&#128465;</span>';
      default: return '';
    }
  }

  function renderMessages(messages) {
    messagesEl.innerHTML = '';
    messages.forEach(function (m) {
      var row = document.createElement('div');
      row.className = 'inbox-msg-row ' + (m.direction === 'outbound' ? 'outbound' : 'inbound');

      var bubble = document.createElement('div');
      bubble.className = 'inbox-msg-bubble ' + originBubbleClass(m.origin, m.direction);
      var tick = m.direction === 'outbound' ? deliveryStatusIcon(m.deliveryStatus) : '';
      var failureNote = m.deliveryStatus === 'failed' && m.failureReason
        ? '<div class="inbox-msg-failure">' + escapeHtml(m.failureReason) + '</div>' : '';
      bubble.innerHTML =
        '<div class="inbox-msg-source">' + escapeHtml(originLabel(m.origin)) + '</div>' +
        '<div class="inbox-msg-content">' + escapeHtml(m.content || '') + '</div>' +
        failureNote +
        '<div class="inbox-msg-time">' + formatTime(m.createdAt) + ' ' + tick + '</div>';

      row.appendChild(bubble);
      messagesEl.appendChild(row);
    });
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }

  // ConversationResponse (from the API) doesn't carry lead/procedure display info — that's a
  // list-only concern (ConversationListRow). We already have it on the clicked list item's own
  // data attributes, so read it from there instead of adding it to the conversation DTO.
  function renderConversationHeader(conversation) {
    var listItem = document.querySelector('.inbox-conv-item[data-conversation-id="' + conversation.id + '"]');
    nameEl.textContent = (listItem && listItem.getAttribute('data-lead-name')) || 'Unknown';
    procedureEl.textContent = (listItem && listItem.getAttribute('data-procedure-name')) || '';
    modeEl.textContent = conversation.mode === 'ai' ? 'AI Active' : (conversation.mode === 'human' ? 'AI Paused (human)' : 'Awaiting approval');
    modeEl.className = 'badge ' + badgeClassForMode(conversation.mode);

    // Backend is authoritative for this (see MessageService.SendAsync's ServiceWindowClosedException)
    // — this just mirrors it in the UI so staff aren't surprised by a rejected send.
    // The 24h customer-service window (and message templates) are WhatsApp-only — a Telegram
    // conversation can always be replied to, so its composer is never disabled and has no template button.
    var isWhatsApp = conversation.channel === 'whatsapp';
    currentWindowOpen = !isWhatsApp || !!conversation.isServiceWindowOpen;
    composerInputEl.disabled = !currentWindowOpen;
    composerSendEl.disabled = !currentWindowOpen;
    windowClosedNoticeEl.hidden = currentWindowOpen;
    btnSendTemplate.hidden = !isWhatsApp;
    hideTemplatePicker();
  }

  // ---------------------------------------------------------------------
  // Data loading
  // ---------------------------------------------------------------------
  function loadConversationList() {
    return apiGet(withClinic('/api/conversations')).then(function (result) {
      renderConversationList(result.items);
    });
  }

  function selectConversation(id) {
    var switchingConversation = id !== currentConversationId;
    currentConversationId = id;
    emptyEl.hidden = true;
    threadEl.hidden = false;
    if (switchingConversation) {
      leadInfoEl.hidden = true; // don't show the previous conversation's lead while the new one loads
    }

    document.querySelectorAll('.inbox-conv-item').forEach(function (el) {
      el.classList.toggle('active', el.getAttribute('data-conversation-id') === id);
    });

    return apiGet(withClinic('/api/conversations/' + id)).then(function (result) {
      currentLeadId = result.conversation.leadId;
      renderConversationHeader(result.conversation);
      renderMessages(result.messages);
      if (!leadInfoEl.hidden) {
        loadLeadInfo(currentLeadId); // panel was left open across a refresh — keep it current
      }
    });
  }

  // ---------------------------------------------------------------------
  // Lead info corner
  // ---------------------------------------------------------------------
  function loadLeadInfo(leadId) {
    var cached = leadInfoCache[leadId];
    var render = function (lead) {
      document.getElementById('inbox-lead-phone').textContent = lead.phone || '—';
      document.getElementById('inbox-lead-email').textContent = lead.email || '—';
      document.getElementById('inbox-lead-procedure').textContent = lead.procedureName || '—';
      document.getElementById('inbox-lead-source').textContent = lead.source || '—';
      document.getElementById('inbox-lead-status').textContent = lead.status || '—';
      document.getElementById('inbox-lead-timeline').textContent = lead.desiredTimeline || '—';
    };

    if (cached) {
      render(cached);
      return Promise.resolve();
    }
    return apiGet(withClinic('/api/leads/' + leadId)).then(function (lead) {
      leadInfoCache[leadId] = lead;
      render(lead);
    });
  }

  btnInfo.addEventListener('click', function () {
    if (!currentLeadId) return;
    var willShow = leadInfoEl.hidden;
    leadInfoEl.hidden = !willShow;
    if (willShow) loadLeadInfo(currentLeadId);
  });

  function refreshCurrentConversation() {
    if (!currentConversationId) return Promise.resolve();
    return selectConversation(currentConversationId);
  }

  // ---------------------------------------------------------------------
  // Composer + mode controls
  // ---------------------------------------------------------------------
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
    bubble.className = 'inbox-msg-bubble inbox-msg-staff';
    bubble.innerHTML =
      '<div class="inbox-msg-source">Staff</div>' +
      '<div class="inbox-msg-content">' + escapeHtml(content) + '</div>' +
      '<div class="inbox-msg-time">Sending…</div>';
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
    apiPost(withClinic('/api/conversations/' + currentConversationId + '/messages/send'), { content: content })
      .then(function () {
        return Promise.all([refreshCurrentConversation(), loadConversationList()]);
      })
      .catch(function (err) {
        removeOptimisticMessage();
        composerInputEl.value = content; // give the text back so they can retry
        alert('Could not send: ' + err.message);
      })
      .then(function () { composerSendEl.disabled = false; });
  });

  // ---------------------------------------------------------------------
  // Send Template — usable any time (proactive outreach), and the only option once the 24h
  // service window is closed. See MessageService.SendTemplateAsync / POST .../messages/send-template.
  // ---------------------------------------------------------------------
  function loadApprovedTemplates() {
    if (approvedTemplates) return Promise.resolve(approvedTemplates);
    return apiGet(withClinic('/api/whatsapp/templates')).then(function (templates) {
      approvedTemplates = (templates || []).filter(function (t) { return t.status === 'approved'; });
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
      input.placeholder = '{{' + i + '}} value';
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
        templateSelectEl.innerHTML = '<option value="">No approved templates yet</option>';
      } else {
        templates.forEach(function (t) {
          var opt = document.createElement('option');
          opt.value = t.id;
          opt.textContent = t.name + ' (' + t.language + ')';
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
    apiPost(withClinic('/api/conversations/' + currentConversationId + '/messages/send-template'), {
      whatsAppTemplateId: template.id,
      bodyParameters: currentTemplateBodyParameters()
    })
      .then(function () {
        hideTemplatePicker();
        return Promise.all([refreshCurrentConversation(), loadConversationList()]);
      })
      .catch(function (err) {
        alert('Could not send template: ' + err.message);
      })
      .then(function () { sendBtn.disabled = false; });
  });

  btnTakeover.addEventListener('click', function () {
    if (!currentConversationId) return;
    apiPost(withClinic('/api/conversations/' + currentConversationId + '/take-over'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); });
  });

  btnReturnAi.addEventListener('click', function () {
    if (!currentConversationId) return;
    apiPost(withClinic('/api/conversations/' + currentConversationId + '/return-to-ai'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); });
  });

  btnClose.addEventListener('click', function () {
    if (!currentConversationId) return;
    if (!confirm('Close this conversation?')) return;
    apiPost(withClinic('/api/conversations/' + currentConversationId + '/close'))
      .then(function () { return Promise.all([refreshCurrentConversation(), loadConversationList()]); });
  });

  // Wire up the server-rendered initial list rows (before any SignalR/refresh replaces them).
  document.querySelectorAll('.inbox-conv-item').forEach(function (el) {
    el.addEventListener('click', function () {
      selectConversation(el.getAttribute('data-conversation-id'));
    });
  });
  renderTimestamps();

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
      loadConversationList();
      if (payload && payload.conversationId === currentConversationId) {
        refreshCurrentConversation();
      }
    });

    connection.on('MessageStatusUpdated', function (payload) {
      if (payload && payload.conversationId === currentConversationId) {
        refreshCurrentConversation();
      }
    });

    connection.on('ConversationUpdated', function () {
      loadConversationList();
    });

    connection.on('ConversationModeChanged', function (payload) {
      loadConversationList();
      if (payload && payload.conversationId === currentConversationId) {
        refreshCurrentConversation();
      }
    });

    // On reconnect, state may have changed while disconnected — re-read everything from the API
    // rather than assuming we didn't miss anything.
    connection.onreconnected(function () {
      loadConversationList();
      refreshCurrentConversation();
    });

    connection.start().catch(function (err) {
      console.error('[inbox] SignalR connection failed', err);
    });
  }
})();
