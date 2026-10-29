// Services list (search, "How it works", clickable rows) and the Add/Edit popup (filled from the Edit button's data-svc-*,
// code suggestion, counter). modal.js opens and closes the popup.
(function () {
  var how = document.getElementById('svc-how-btn');
  if (how) {
    how.addEventListener('click', function () {
      var box = document.getElementById('svc-how');
      box.hidden = !box.hidden;
      how.setAttribute('aria-expanded', String(!box.hidden));
    });
  }

  var table = document.getElementById('svc-table');
  if (table) {
    var search = document.getElementById('svc-search');
    var none = document.getElementById('svc-no-match');
    search.addEventListener('input', function () {
      var q = search.value.trim().toLowerCase(), shown = 0;
      table.querySelectorAll('tbody tr[data-search]').forEach(function (tr) {
        var hit = !q || tr.dataset.search.indexOf(q) !== -1;
        tr.hidden = !hit;
        if (hit) shown++;
      });
      none.hidden = shown > 0;
    });
    table.addEventListener('click', function (e) {
      if (e.target.closest('a, button, form, input')) return;
      var tr = e.target.closest('tr');
      var edit = tr && tr.querySelector('[data-svc-edit]');
      if (edit) edit.click();
    });
  }

  // The popup: empty for "Add", the row's service for "Edit".
  var overlay = document.getElementById('svc-overlay');
  var name = document.getElementById('svc-name');
  var code = document.getElementById('svc-code');
  var desc = document.getElementById('svc-desc');
  var count = document.getElementById('svc-desc-count');
  var suggested = null;
  var updateCount = function () { if (desc && count) count.textContent = omniText.t('common.counter', [desc.value.length, 500]); };
  if (overlay) {
    overlay.addEventListener('modal:open', function (e) {
      var t = e.detail.trigger, d = t && t.hasAttribute('data-svc-edit') ? t.dataset : null;
      var title = document.getElementById('svc-form-title'), submit = document.getElementById('svc-submit');
      document.getElementById('svc-id').value = d ? d.svcId : '';
      name.value = d ? d.svcName || '' : '';
      code.value = d ? d.svcCode || '' : '';
      document.getElementById('svc-duration').value = d ? d.svcDuration || '' : '';
      desc.value = d ? d.svcDesc || '' : '';
      title.textContent = d ? title.dataset.titleEdit : title.dataset.titleAdd;
      submit.textContent = d ? submit.dataset.labelEdit : submit.dataset.labelAdd;
      suggested = code.value === '' ? '' : null;
      overlay.querySelectorAll('.au-alert').forEach(function (a) { a.remove(); });
      updateCount();
    });
  }

  // Suggest a code from the name, only while the code box is empty or still holds our last suggestion.
  if (name && code) {
    suggested = code.value === '' ? '' : null;
    var suggest = function (n) {
      var words = n.trim().toUpperCase().replace(/[^A-Z0-9 ]/g, '').split(/\s+/).filter(Boolean);
      if (!words.length) return '';
      return words.length === 1 ? words[0].slice(0, 5) : words.map(function (w) { return w[0]; }).join('').slice(0, 5);
    };
    name.addEventListener('input', function () {
      if (suggested === null || code.value !== suggested) return;
      suggested = suggest(name.value);
      code.value = suggested;
    });
    code.addEventListener('input', function () { suggested = null; });
  }

  if (desc && count) {
    desc.addEventListener('input', updateCount);
    updateCount();
  }
})();
