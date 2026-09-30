// Theme toggle (sidebar user menu): switches between Light and Dark.
// The choice is kept per browser in localStorage and applied as <html data-theme="light|dark">.
// Until someone picks, the page follows the OS setting (site.css's prefers-color-scheme rules). A tiny
// inline script in the page <head> applies the saved choice before first paint so there's no flash.
(function () {
  var KEY = 'sf-theme';
  var btn = document.getElementById('theme-toggle');
  if (!btn) return;

  function current() {
    try {
      var t = localStorage.getItem(KEY);
      if (t === 'light' || t === 'dark') return t;
    } catch (e) { /* private mode */ }
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function show(theme) {
    btn.setAttribute('data-mode', theme);
    // The button lives in the sidebar user menu and names the mode it switches TO.
    var label = btn.querySelector('.theme-toggle-label');
    if (label) label.textContent = theme === 'dark' ? 'Light mode' : 'Dark mode';
  }

  function apply(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    try { localStorage.setItem(KEY, theme); } catch (e) { /* private mode: still applies for this page */ }
    show(theme);
  }

  show(current());
  btn.addEventListener('click', function () {
    apply(btn.getAttribute('data-mode') === 'dark' ? 'light' : 'dark');
  });
})();
