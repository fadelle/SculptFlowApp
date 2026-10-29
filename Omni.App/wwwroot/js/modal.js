// Popups (docs/UI_GUIDE.md §0.4): <div class="bm-overlay" data-modal id="…" hidden> holding one .card.
// [data-modal-open="id"] opens it (its href is the no-script fallback), [data-modal-close], a click on the backdrop or
// Escape closes it. An overlay rendered without `hidden` (a form that failed to save, an edit opened by its address) is
// open on load. data-modal-close-href on the overlay: closing goes to that address instead (the popup's own page state).
// The overlay gets a "modal:open" event whose detail.trigger is the element that opened it, so a page script can fill
// the form from the trigger's data-* attributes.
(function () {
  'use strict';
  var lastTrigger = null;

  function focusFirst(overlay) {
    var el = overlay.querySelector('[autofocus], input:not([type=hidden]):not([disabled]), textarea, select, button:not([data-modal-close])');
    if (el) el.focus();
  }

  function open(overlay, trigger) {
    if (!overlay) return;
    lastTrigger = trigger || document.activeElement;
    overlay.hidden = false;
    overlay.dispatchEvent(new CustomEvent('modal:open', { detail: { trigger: trigger || null } }));
    focusFirst(overlay);
  }

  function close(overlay) {
    if (!overlay || overlay.hidden) return;
    var href = overlay.getAttribute('data-modal-close-href');
    if (href) { window.location.href = href; return; }
    overlay.hidden = true;
    if (lastTrigger && lastTrigger.focus) lastTrigger.focus();
  }

  document.addEventListener('click', function (e) {
    var opener = e.target.closest('[data-modal-open]');
    if (opener) {
      e.preventDefault();
      open(document.getElementById(opener.getAttribute('data-modal-open')), opener);
      return;
    }
    var closer = e.target.closest('[data-modal-close]');
    if (closer) {
      e.preventDefault();
      close(closer.closest('[data-modal]'));
      return;
    }
    if (e.target.hasAttribute && e.target.hasAttribute('data-modal')) close(e.target);
  });

  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    var openOnes = document.querySelectorAll('[data-modal]:not([hidden])');
    if (openOnes.length) close(openOnes[openOnes.length - 1]);
  });

  var startOpen = document.querySelector('[data-modal]:not([hidden])');
  if (startOpen) focusFirst(startOpen);

  window.omniModal = {
    open: function (id, trigger) { open(document.getElementById(id), trigger); },
    close: function (id) { close(document.getElementById(id)); }
  };
})();
