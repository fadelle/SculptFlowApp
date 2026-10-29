// Settings → Auto-respond: the channel tabs switch panels of the one form (no reload; #auto-reply-tab remembers the tab so
// the page comes back on it after Save), and each panel's reopened-conversation message shows only for "Different message".
(function () {
  'use strict';
  var tabs = document.querySelectorAll('[data-channel-tab]');
  var tabField = document.getElementById('auto-reply-tab');
  tabs.forEach(function (tab) {
    tab.addEventListener('click', function (e) {
      e.preventDefault();
      var channel = tab.getAttribute('data-channel-tab');
      tabs.forEach(function (t) {
        var on = t === tab;
        t.classList.toggle('is-active', on);
        t.setAttribute('aria-selected', String(on));
      });
      document.querySelectorAll('[data-channel-panel]').forEach(function (p) {
        p.hidden = p.getAttribute('data-channel-panel') !== channel;
      });
      if (tabField) tabField.value = channel;
    });
  });

  document.querySelectorAll('[data-channel-panel]').forEach(function (panel) {
    var box = panel.querySelector('[data-reopened-message]');
    if (!box) return;
    panel.querySelectorAll('[data-reopened-mode]').forEach(function (radio) {
      radio.addEventListener('change', function () {
        if (radio.checked) box.hidden = radio.value !== 'custom';
      });
    });
  });
})();
