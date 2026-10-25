// Bot editor (Pages/Bots/Edit): the knowledge-scope switch and the test conversation panel. A message goes to
// POST /api/bots/{id}/test/messages (the bot runs in n8n in the background); the transcript is polled from
// GET /api/bots/{id}/test/{sessionId} until the bot has answered or handed off. Reset deletes the session.
(function () {
  'use strict';

  // ---- Knowledge Base: the source list shows only for "selected sources"
  var sources = document.getElementById('bot-sources');
  document.querySelectorAll('[data-bot-scope]').forEach(function (radio) {
    radio.addEventListener('change', function () {
      if (sources && radio.checked) sources.hidden = radio.value !== 'selected';
    });
  });

  // ---- Test conversation
  var panel = document.getElementById('bot-test');
  if (!panel) return;
  var botId = panel.getAttribute('data-bot-id');
  var log = document.getElementById('bot-test-log');
  var form = document.getElementById('bot-test-form');
  var input = document.getElementById('bot-test-input');
  var statusEl = document.getElementById('bot-test-status');
  var resetBtn = document.getElementById('bot-test-reset');
  var storageKey = 'omni.botTest.' + botId;
  var sessionId = null;
  var pollTimer = null;
  var pollStarted = 0;
  var POLL_MS = 1500;
  var POLL_GIVE_UP_MS = 90000;
  // The plan has no AI assistant or the bot isn't ready yet: the page rendered the input disabled, and it stays so.
  var unavailable = input.disabled;

  try { sessionId = window.sessionStorage.getItem(storageKey); } catch (e) { sessionId = null; }

  function setStatus(text) {
    statusEl.textContent = text || '';
    statusEl.hidden = !text;
  }

  function showError(message) {
    if (window.Aurora && window.Aurora.toast) window.Aurora.toast.error(message);
    else setStatus(message);
  }

  function request(method, path, body) {
    return fetch(path, {
      method: method,
      headers: body ? { 'Content-Type': 'application/json' } : {},
      body: body ? JSON.stringify(body) : undefined
    }).then(function (res) {
      if (res.status === 204) return null;
      return res.json().catch(function () { return {}; }).then(function (data) {
        if (!res.ok) {
          var error = new Error(data.error || omniText.t('common.errors.requestFailed', [res.status]));
          error.status = res.status;
          throw error;
        }
        return data;
      });
    });
  }

  function render(transcript) {
    log.textContent = '';
    transcript.lines.forEach(function (line) {
      var bubble = document.createElement('div');
      bubble.className = 'bot-test-line bot-test-line--' + line.role;
      bubble.setAttribute('dir', 'auto');
      bubble.textContent = line.text;
      log.appendChild(bubble);
    });
    log.scrollTop = log.scrollHeight;

    var locked = unavailable || transcript.handedOff;
    input.disabled = locked;
    form.querySelector('button').disabled = locked;
    if (transcript.handedOff) setStatus(omniText.t('bots.test.handedOff'));
    else if (transcript.waiting) setStatus(omniText.t('bots.test.thinking'));
    else setStatus('');
  }

  function stopPolling() {
    if (pollTimer) window.clearTimeout(pollTimer);
    pollTimer = null;
  }

  function poll() {
    if (!sessionId) return;
    request('GET', '/api/bots/' + botId + '/test/' + sessionId).then(function (transcript) {
      render(transcript);
      if (transcript.waiting && Date.now() - pollStarted < POLL_GIVE_UP_MS) {
        pollTimer = window.setTimeout(poll, POLL_MS);
      } else {
        stopPolling();
        if (transcript.waiting) setStatus(omniText.t('bots.test.noReply'));
      }
    }).catch(function (error) {
      stopPolling();
      if (error.status === 404) forget();
      else showError(error.message);
    });
  }

  function startPolling() {
    stopPolling();
    pollStarted = Date.now();
    poll();
  }

  function remember(id) {
    sessionId = id;
    try { window.sessionStorage.setItem(storageKey, id); } catch (e) { /* private mode: the session lasts for this page only */ }
  }

  function forget() {
    sessionId = null;
    try { window.sessionStorage.removeItem(storageKey); } catch (e) { /* nothing stored */ }
    log.textContent = '';
    input.disabled = unavailable;
    form.querySelector('button').disabled = unavailable;
    setStatus('');
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    var text = input.value.trim();
    if (!text || input.disabled) return;
    input.value = '';
    request('POST', '/api/bots/' + botId + '/test/messages', { sessionId: sessionId, text: text }).then(function (result) {
      remember(result.sessionId);
      startPolling();
    }).catch(function (error) {
      input.value = text;
      showError(error.message);
    });
  });

  resetBtn.addEventListener('click', function () {
    stopPolling();
    var old = sessionId;
    forget();
    if (old) request('DELETE', '/api/bots/' + botId + '/test/' + old).catch(function () { /* already gone */ });
    input.focus();
  });

  if (sessionId && !unavailable) startPolling();
})();
