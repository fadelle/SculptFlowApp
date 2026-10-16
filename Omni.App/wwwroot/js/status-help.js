// Opens/closes the "what do these mean?" status reference (Pages/Shared/_StatusHelp.cshtml).
// Any button with [data-status-help-trigger] on the page opens the same shared overlay.
(function () {
  var overlay = document.getElementById('status-help-overlay');
  if (!overlay) return;

  var closeBtn = document.getElementById('status-help-close');
  var triggers = document.querySelectorAll('[data-status-help-trigger]');

  function open() { overlay.hidden = false; }
  function close() { overlay.hidden = true; }

  triggers.forEach(function (btn) { btn.addEventListener('click', open); });
  if (closeBtn) closeBtn.addEventListener('click', close);

  overlay.addEventListener('click', function (e) {
    if (e.target === overlay) close();
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !overlay.hidden) close();
  });
})();
