/*
 * Inbox message bodies by type (QA t10-09): a photo, video, document or sticker shows a file chip (with its file name and
 * caption), a location links to a map, a shared contact shows its name and number, and system, order, Flows, welcome and
 * unsupported messages get a label instead of an empty bubble. Labels come from the strings file, so the English
 * placeholders the channel parsers store ("[video]", "[location]") are never shown. Loaded before inbox.js, which asks
 * InboxMessageView.html(message) for the body and renders plain text itself when it answers null.
 */
(function () {
  'use strict';

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function meta(message) {
    try {
      var parsed = message.metadataJson ? JSON.parse(message.metadataJson) : null;
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (e) {
      return {};
    }
  }

  // "[video]", "[contact]"…: what a parser stores when the message has no text of its own.
  function isPlaceholder(content) {
    return !content || /^\[[a-z _]+\]$/i.test(String(content).trim());
  }

  // One literal call per label, so the strings guard sees every key used.
  function label(type) {
    switch (type) {
      case 'image': return omniText.t('inbox.msgType.image');
      case 'video': return omniText.t('inbox.msgType.video');
      case 'document': return omniText.t('inbox.msgType.document');
      case 'sticker': return omniText.t('inbox.msgType.sticker');
      case 'location': return omniText.t('inbox.msgType.location');
      case 'contacts': return omniText.t('inbox.msgType.contact');
      case 'system': return omniText.t('inbox.msgType.system');
      case 'order': return omniText.t('inbox.msgType.order');
      case 'request_welcome': return omniText.t('inbox.msgType.welcome');
      case 'flow': return omniText.t('inbox.msgType.flow');
      case 'reaction_removed': return omniText.t('inbox.msgType.reactionRemoved');
      default: return omniText.t('inbox.msgType.unsupported');
    }
  }

  var ICONS = {
    image: { icon: 'image' },
    video: { icon: 'movie' },
    document: { icon: 'description' },
    sticker: { icon: 'mood' },
    location: { icon: 'location_on' },
    contacts: { icon: 'contact_phone' },
    other: { icon: 'info' }
  };

  function chip(type, text, href) {
    var icon = (ICONS[type] || ICONS.other).icon;
    var inner = '<span class="au-icon" aria-hidden="true">' + icon + '</span><span>' + escapeHtml(text) + '</span>';
    return href
      ? '<a class="inbox-attachment" href="' + escapeHtml(href) + '" target="_blank" rel="noopener noreferrer">' + inner + '</a>'
      : '<span class="inbox-attachment">' + inner + '</span>';
  }

  function caption(message) {
    return isPlaceholder(message.content) ? '' : '<div class="inbox-attachment-caption">' + escapeHtml(message.content) + '</div>';
  }

  function contactsText(list) {
    if (!Array.isArray(list)) return '';
    return list.map(function (c) {
      var name = c && c.name ? (c.name.formatted_name || [c.name.first_name, c.name.last_name].filter(Boolean).join(' ')) : '';
      var phone = c && Array.isArray(c.phones) && c.phones[0] ? c.phones[0].phone || c.phones[0].wa_id || '' : '';
      return [name, phone].filter(Boolean).join(' · ');
    }).filter(Boolean).join(', ');
  }

  // The body HTML for a message whose type needs more than its text, else null.
  function html(message) {
    if (!message) return null;
    var type = message.messageType;
    var m = meta(message);
    switch (type) {
      case 'image': case 'video': case 'document': case 'sticker': {
        var name = m.filename || m.file_name || '';
        return chip(type, label(type) + (name ? ': ' + name : '')) + caption(message);
      }
      case 'location': {
        var lat = m.latitude, lng = m.longitude;
        var place = [m.name, m.address].filter(Boolean).join(' · ');
        var href = typeof lat === 'number' && typeof lng === 'number'
          ? 'https://www.openstreetmap.org/?mlat=' + lat + '&mlon=' + lng + '#map=17/' + lat + '/' + lng : null;
        return chip('location', label('location') + (place ? ': ' + place : ''), href);
      }
      case 'contacts': {
        var who = contactsText(Array.isArray(m) ? m : m.contacts) || (m.first_name ? [m.first_name, m.last_name, m.phone_number].filter(Boolean).join(' ') : '');
        return chip('contacts', label('contacts') + (who ? ': ' + who : ''));
      }
      case 'reaction':
        return isPlaceholder(message.content) && !(m.emoji) ? chip('other', label('reaction_removed')) : null;
      case 'interactive':
        // A WhatsApp Flows answer (nfm_reply) carries no title.
        return isPlaceholder(message.content) ? chip('other', label(m.type === 'nfm_reply' ? 'flow' : 'unsupported')) : null;
      case 'system': case 'order': case 'request_welcome':
        return isPlaceholder(message.content) ? chip('other', label(type)) : chip('other', label(type)) + caption(message);
      case 'text': case 'button': case 'audio': case 'template': case 'command':
        return null;
      default:
        return isPlaceholder(message.content) ? chip('other', label('unsupported')) : null;
    }
  }

  // The conversation list's preview is the last message's stored text: a placeholder reads in the member's language.
  function previewText(text) {
    switch (String(text || '').trim().toLowerCase()) {
      case '[photo]': case '[image]': return label('image');
      case '[video]': case '[video message]': return label('video');
      case '[document]': return label('document');
      case '[sticker]': return label('sticker');
      case '[location]': return label('location');
      case '[contact]': return label('contacts');
      case '[voice message]': case '[audio]': return omniText.t('inbox.voice.label');
      case '[unsupported message]': case '[unsupported]': return label('unsupported');
      default: return text;
    }
  }

  window.InboxMessageView = { html: html, isPlaceholder: isPlaceholder, previewText: previewText };
})();
