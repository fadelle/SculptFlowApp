// Bots list (Pages/Bots/Index): the "New bot" popup. Each option is its own form posting the template (Create handler).
(function () {
  'use strict';
  var overlay = document.getElementById('new-bot-overlay');
  var openBtn = document.getElementById('new-bot-btn');
  if (!overlay || !openBtn) return;

  function open() {
    overlay.hidden = false;
    var first = overlay.querySelector('.bot-choice');
    if (first) first.focus();
  }
  function close() { overlay.hidden = true; openBtn.focus(); }

  openBtn.addEventListener('click', open);
  document.getElementById('new-bot-close').addEventListener('click', close);
  overlay.addEventListener('click', function (e) { if (e.target === overlay) close(); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !overlay.hidden) close(); });
})();
