// Generic checkbox + chips dropdown, used wherever the app needs a friendlier alternative to a
// native <select multiple> (currently: the Campaign audience builder's advanced filters). The real
// form data is the checkboxes inside each ".ms-options" block — this only adds a nicer UI on top;
// nothing here decides what's selected, it just reflects/toggles those checkboxes. Reusable: any
// page can drop in the ".ms" markup (see Create.cshtml for the shape) and call initMultiSelects().
(function () {
  function closeAllExcept(except) {
    document.querySelectorAll('.ms-panel:not([hidden])').forEach(function (panel) {
      if (panel !== except) panel.hidden = true;
    });
  }

  function renderChips(container) {
    var checked = Array.from(container.querySelectorAll('.ms-options input[type="checkbox"]:checked'));
    var chipsEl = container.querySelector('.ms-chips');
    var triggerText = container.querySelector('.ms-trigger-text');
    var label = container.getAttribute('data-ms-label') || '';

    chipsEl.innerHTML = '';
    checked.forEach(function (cb) {
      var chip = document.createElement('span');
      chip.className = 'ms-chip';
      var text = document.createElement('span');
      text.textContent = cb.nextElementSibling ? cb.nextElementSibling.textContent : cb.value;
      var remove = document.createElement('button');
      remove.type = 'button';
      remove.className = 'ms-chip-x';
      remove.setAttribute('aria-label', 'Remove');
      remove.textContent = '×';
      remove.addEventListener('click', function (e) {
        e.stopPropagation();
        cb.checked = false;
        cb.dispatchEvent(new Event('change', { bubbles: true }));
        renderChips(container);
      });
      chip.appendChild(text);
      chip.appendChild(remove);
      chipsEl.appendChild(chip);
    });

    triggerText.textContent = checked.length > 0 ? label + ' (' + checked.length + ')' : label;
    container.classList.toggle('ms-has-selection', checked.length > 0);
  }

  function wire(container) {
    var trigger = container.querySelector('.ms-trigger');
    var panel = container.querySelector('.ms-panel');
    var search = container.querySelector('.ms-search');
    var options = Array.from(container.querySelectorAll('.ms-option'));
    var clearBtn = container.querySelector('.ms-clear-btn');
    if (!trigger || !panel) return;

    trigger.addEventListener('click', function (e) {
      e.stopPropagation();
      var willOpen = panel.hidden;
      closeAllExcept(willOpen ? panel : null);
      panel.hidden = !willOpen;
      if (willOpen && search) { search.value = ''; options.forEach(function (o) { o.style.display = ''; }); search.focus(); }
    });

    panel.addEventListener('click', function (e) { e.stopPropagation(); });

    if (search) {
      search.addEventListener('input', function () {
        var q = search.value.trim().toLowerCase();
        options.forEach(function (opt) {
          opt.style.display = opt.textContent.toLowerCase().indexOf(q) === -1 ? 'none' : '';
        });
      });
    }

    container.querySelectorAll('.ms-options input[type="checkbox"]').forEach(function (cb) {
      cb.addEventListener('change', function () { renderChips(container); });
    });

    if (clearBtn) {
      clearBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        container.querySelectorAll('.ms-options input[type="checkbox"]:checked').forEach(function (cb) {
          cb.checked = false;
          cb.dispatchEvent(new Event('change', { bubbles: true }));
        });
      });
    }

    renderChips(container);
  }

  document.addEventListener('click', function () { closeAllExcept(null); });

  window.initMultiSelects = function (root) {
    (root || document).querySelectorAll('[data-ms]').forEach(wire);
  };

  document.addEventListener('DOMContentLoaded', function () { window.initMultiSelects(); });
  if (document.readyState !== 'loading') window.initMultiSelects();
})();
