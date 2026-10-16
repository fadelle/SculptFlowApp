/*
 * Inbox voice notes: turns an audio message into a player. Loaded before inbox.js, which asks this module for the bubble
 * body of any message of type "audio" and keeps rendering everything else itself. The audio is streamed from
 * GET /api/messages/{id}/media (Omni fetches it from the channel with its own token), so nothing here knows a provider.
 */
(function () {
  'use strict';

  // What the channel parsers put in Content when the voice note has no caption.
  var PLACEHOLDERS = { '[voice message]': true, '[audio]': true };

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function isVoice(message) {
    return !!message && message.messageType === 'audio';
  }

  function html(message) {
    var caption = message.content && !PLACEHOLDERS[message.content]
      ? '<div class="inbox-voice-caption">' + escapeHtml(message.content) + '</div>'
      : '';
    // preload="none": nothing is downloaded from the provider until someone presses play.
    return '<div class="inbox-voice">' +
      '<audio controls preload="none" aria-label="Voice message" src="/api/messages/' + encodeURIComponent(message.id) + '/media"></audio>' +
      caption +
      '</div>';
  }

  // A failed load (expired file, bot disconnected, a browser that can't play Ogg/Opus) swaps the player for a plain note.
  // 'error' doesn't bubble, so listen in the capture phase once for every player.
  document.addEventListener('error', function (event) {
    var el = event.target;
    if (!el || el.tagName !== 'AUDIO' || !el.closest || !el.closest('.inbox-voice')) return;
    var note = document.createElement('div');
    note.className = 'inbox-voice-error';
    note.textContent = 'This voice note can’t be played here.';
    el.replaceWith(note);
  }, true);

  window.InboxVoice = { isVoice: isVoice, html: html };
})();
