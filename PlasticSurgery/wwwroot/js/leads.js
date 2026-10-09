// Interested People list (Pages/Dashboard/Leads.cshtml):
//  - search updates as you type, and the Status/Source filters apply on change. It asks the server
//    for the same page with the new query string and swaps in just the results, so the search box
//    keeps focus. The filtering itself is still the page's normal server-side query.
//  - clicking anywhere on a row opens that lead.
(function () {
  var form = document.getElementById('leads-filters');
  var results = document.getElementById('leads-results');
  if (!form || !results) return;

  var requestId = 0;
  var timer = null;

  function refresh() {
    var url = window.location.pathname;
    var params = new URLSearchParams(new FormData(form));
    Array.from(params.keys()).forEach(function (key) { if (!params.get(key)) params.delete(key); });
    if (params.toString()) url += '?' + params.toString();

    var id = ++requestId;
    fetch(url, { headers: { 'X-Requested-With': 'fetch' } })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
      .then(function (html) {
        if (id !== requestId) return; // a newer search already went out
        var fresh = new DOMParser().parseFromString(html, 'text/html').getElementById('leads-results');
        if (!fresh) throw new Error('no results in response');
        results.innerHTML = fresh.innerHTML;
        // The swapped-in <time data-local> cells still hold the server's UTC text: show them in the viewer's time again.
        if (window.SculptTime) window.SculptTime.render(results);
        history.replaceState(null, '', url);
      })
      .catch(function () { form.submit(); }); // fall back to a normal page load
  }

  form.addEventListener('submit', function (e) { e.preventDefault(); clearTimeout(timer); refresh(); });
  form.querySelector('input[name="Search"]').addEventListener('input', function () {
    clearTimeout(timer);
    timer = setTimeout(refresh, 350);
  });
  form.querySelectorAll('select').forEach(function (select) {
    select.addEventListener('change', refresh);
  });

  results.addEventListener('click', function (e) {
    if (e.target.closest('a, button')) return; // real links keep working as links
    var row = e.target.closest('tr[data-href]');
    if (row) window.location.href = row.getAttribute('data-href');
  });
})();
