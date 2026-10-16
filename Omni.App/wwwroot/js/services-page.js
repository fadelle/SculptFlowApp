// Services list (search, "How it works", clickable rows) and the Add/Edit form (code suggestion, counter).
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
      var tr = e.target.closest('tr[data-href]');
      if (tr) window.location.href = tr.dataset.href;
    });
  }

  // Suggest a code from the name, only while the code box is empty or still holds our last suggestion.
  var name = document.getElementById('svc-name');
  var code = document.getElementById('svc-code');
  if (name && code) {
    var suggested = code.value === '' ? '' : null;
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

  var desc = document.getElementById('svc-desc');
  var count = document.getElementById('svc-desc-count');
  if (desc && count) {
    var update = function () { count.textContent = desc.value.length + ' / 500'; };
    desc.addEventListener('input', update);
    update();
  }
})();
