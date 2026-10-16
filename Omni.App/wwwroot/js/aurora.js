/* ==========================================================================
   AURORA — UI behaviours. Vanilla JS, no dependencies, no build step.
   Every feature is OPT-IN by markup (data-au-* attributes); a page that does
   not use a feature is untouched. Never put business logic here.
   Public API: window.Aurora.{theme, toast, confirm, copy, progress, dialog}
   ========================================================================== */
(function () {
  'use strict';

  var root = document.documentElement;
  var APP = root.getAttribute('data-au-app') || 'aurora';
  var KEY = {
    theme: APP + '.themeMode',
    collapsed: APP + '.sidebarCollapsed',
    favs: APP + '.nav.favorites',
    recent: APP + '.nav.recent',
  };
  var reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  var desktop = window.matchMedia ? window.matchMedia('(min-width: 900px)') : { matches: true };

  // --- tiny helpers --------------------------------------------------------
  function $(sel, ctx) {
    return (ctx || document).querySelector(sel);
  }
  function $$(sel, ctx) {
    return Array.prototype.slice.call((ctx || document).querySelectorAll(sel));
  }
  function store(key, value) {
    try {
      if (value === undefined) return localStorage.getItem(key);
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch (e) {
      /* private mode / blocked storage: degrade silently */
    }
    return null;
  }
  function readJson(key, fallback) {
    try {
      var raw = store(key);
      return raw ? JSON.parse(raw) : fallback;
    } catch (e) {
      return fallback;
    }
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function icon(name, cls) {
    return '<span class="au-icon' + (cls ? ' ' + cls : '') + '" aria-hidden="true">' + esc(name) + '</span>';
  }
  function isTyping(el) {
    return !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));
  }
  function debounce(fn, ms) {
    var t;
    return function () {
      var args = arguments,
        self = this;
      clearTimeout(t);
      t = setTimeout(function () {
        fn.apply(self, args);
      }, ms);
    };
  }

  // A click whose target is the <dialog> itself AND lands outside its box is a
  // backdrop click (a click on empty space inside the dialog also targets it).
  function isBackdropClick(e, dlg) {
    if (e.target !== dlg) return false;
    var r = dlg.getBoundingClientRect();
    return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  }

  // --- theme (light / dark) -------------------------------------------------
  var theme = {
    get: function () {
      return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    },
    set: function (mode) {
      root.setAttribute('data-theme', mode);
      root.setAttribute('data-bs-theme', mode); // Bootstrap 5.3+ follows along
      store(KEY.theme, mode);
      $$('[data-au-theme-toggle]').forEach(syncThemeToggle);
      document.dispatchEvent(new CustomEvent('au:themechange', { detail: { mode: mode } }));
    },
    toggle: function () {
      theme.set(theme.get() === 'dark' ? 'light' : 'dark');
    },
  };
  function syncThemeToggle(btn) {
    var dark = theme.get() === 'dark';
    var label = dark ? 'Switch to light mode' : 'Switch to dark mode';
    btn.setAttribute('aria-label', label);
    if (btn.hasAttribute('data-au-tip')) btn.setAttribute('data-au-tip', label);
    var glyph = btn.querySelector('.au-icon');
    if (glyph) {
      glyph.textContent = dark ? 'light_mode' : 'dark_mode';
      glyph.classList.remove('au-animate-in');
      void glyph.offsetWidth; // restart the animation
      glyph.classList.add('au-animate-in');
    }
  }

  // --- top progress bar (navigation / submit in flight) ---------------------
  var progress = {
    el: null,
    start: function () {
      if (progress.el) progress.el.classList.add('is-active');
    },
    stop: function () {
      if (progress.el) progress.el.classList.remove('is-active');
    },
  };

  // --- toasts ----------------------------------------------------------------
  var TOAST_MS = 4500;
  var TOAST_MAX = 4;
  var TOAST_ICON = { success: 'check_circle', error: 'error', warning: 'warning', info: 'info' };
  function toastHost() {
    var host = $('.au-toasts');
    if (!host) {
      host = document.createElement('div');
      host.className = 'au-toasts';
      host.setAttribute('aria-live', 'polite');
      document.body.appendChild(host);
    }
    return host;
  }
  function showToast(severity, message) {
    if (!message) return;
    var sev = TOAST_ICON[severity] ? severity : 'info';
    var host = toastHost();
    while (host.children.length >= TOAST_MAX) host.removeChild(host.firstElementChild);
    var text = String(message);
    if (text.length > 300) text = text.slice(0, 297) + '…'; // the full error lives in the page panel
    var el = document.createElement('div');
    el.className = 'au-toast au-toast--' + sev;
    el.setAttribute('role', sev === 'error' ? 'alert' : 'status');
    el.innerHTML =
      icon(TOAST_ICON[sev]) +
      '<div class="au-toast__msg">' +
      esc(text) +
      '</div>' +
      '<button type="button" class="au-icon-btn au-icon-btn--sm" aria-label="Dismiss">' +
      icon('close') +
      '</button>';
    var dismiss = function () {
      if (!el.parentNode) return;
      el.classList.add('is-leaving');
      setTimeout(function () {
        if (el.parentNode) el.parentNode.removeChild(el);
      }, 200);
    };
    el.querySelector('button').addEventListener('click', dismiss);
    host.appendChild(el);
    setTimeout(dismiss, TOAST_MS);
  }
  var toast = {
    success: function (m) {
      showToast('success', m);
    },
    error: function (m) {
      showToast('error', m);
    },
    warning: function (m) {
      showToast('warning', m);
    },
    info: function (m) {
      showToast('info', m);
    },
  };

  // --- clipboard (falls back to execCommand on plain-http origins) ----------
  function copy(text, container) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {
      var host = container || document.body; // inside a <dialog>, pass the dialog
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      host.appendChild(ta);
      ta.select();
      var ok = false;
      try {
        ok = document.execCommand('copy');
      } catch (e) {
        ok = false;
      }
      host.removeChild(ta);
      if (ok) resolve();
      else reject(new Error('copy failed'));
    });
  }

  // --- dialogs / confirm ----------------------------------------------------
  var dialog = {
    open: function (target) {
      var d = typeof target === 'string' ? $(target) : target;
      if (d && typeof d.showModal === 'function' && !d.open) d.showModal();
      return d;
    },
    close: function (target) {
      var d = typeof target === 'string' ? $(target) : target;
      if (d && d.open) d.close();
    },
  };
  var confirmEl = null;
  function confirmDialog(opts) {
    opts = opts || {};
    if (!confirmEl) {
      confirmEl = document.createElement('dialog');
      confirmEl.className = 'au-dialog';
      confirmEl.setAttribute('aria-labelledby', 'au-confirm-title');
      confirmEl.innerHTML =
        '<h2 class="au-dialog__title" id="au-confirm-title"></h2>' +
        '<div class="au-dialog__content"><div class="au-dialog__text"></div></div>' +
        '<div class="au-dialog__actions">' +
        '<button type="button" class="au-btn" data-au-cancel>Cancel</button>' +
        '<button type="button" class="au-btn" data-au-ok>Confirm</button>' +
        '</div>';
      document.body.appendChild(confirmEl);
      confirmEl.addEventListener('click', function (e) {
        if (isBackdropClick(e, confirmEl)) confirmEl.close('cancel');
      });
    }
    var tone = opts.tone || 'primary';
    confirmEl.querySelector('.au-dialog__title').textContent = opts.title || 'Are you sure?';
    var text = confirmEl.querySelector('.au-dialog__text');
    if (opts.html) text.innerHTML = opts.html;
    else text.textContent = opts.message || '';
    var ok = confirmEl.querySelector('[data-au-ok]');
    ok.className = 'au-btn au-btn--' + tone;
    ok.textContent = opts.confirmLabel || 'Confirm';
    confirmEl.querySelector('[data-au-cancel]').textContent = opts.cancelLabel || 'Cancel';
    return new Promise(function (resolve) {
      function done(result) {
        ok.removeEventListener('click', onOk);
        cancel.removeEventListener('click', onCancel);
        confirmEl.removeEventListener('close', onClose);
        resolve(result);
      }
      var cancel = confirmEl.querySelector('[data-au-cancel]');
      function onOk() {
        confirmEl.close('ok');
      }
      function onCancel() {
        confirmEl.close('cancel');
      }
      function onClose() {
        done(confirmEl.returnValue === 'ok');
      }
      ok.addEventListener('click', onOk);
      cancel.addEventListener('click', onCancel);
      confirmEl.addEventListener('close', onClose);
      confirmEl.returnValue = '';
      confirmEl.showModal();
      (tone === 'danger' ? cancel : ok).focus(); // destructive: default focus is Cancel
    });
  }
  function confirmOptions(el, fallback) {
    var src = el && el.hasAttribute('data-au-confirm') ? el : fallback;
    return {
      message: src.getAttribute('data-au-confirm'),
      title: src.getAttribute('data-au-confirm-title') || undefined,
      confirmLabel: src.getAttribute('data-au-confirm-label') || undefined,
      tone: src.getAttribute('data-au-confirm-tone') || undefined,
    };
  }

  // --- sidebar: collapse rail, mobile drawer, filter, favourites, recent ----
  function initSidebar() {
    var sidebar = $('.au-sidebar');
    $$('[data-au-sidebar-toggle]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        if (desktop.matches) {
          var next = !root.classList.contains('au-side-collapsed');
          root.classList.toggle('au-side-collapsed', next);
          store(KEY.collapsed, next ? '1' : '0');
        } else {
          root.classList.toggle('au-side-open');
        }
      });
    });
    var scrim = $('.au-scrim');
    if (scrim)
      scrim.addEventListener('click', function () {
        root.classList.remove('au-side-open');
      });
    if (!sidebar) return;

    // Collapsed rail: a group click expands the rail instead of toggling.
    sidebar.addEventListener('click', function (e) {
      var summary = e.target.closest('.au-nav-group > summary');
      if (summary && desktop.matches && root.classList.contains('au-side-collapsed')) {
        e.preventDefault();
        root.classList.remove('au-side-collapsed');
        store(KEY.collapsed, '0');
        summary.parentElement.open = true;
      }
    });

    initNavFilter(sidebar);
    initFavorites(sidebar);
    recordVisit(sidebar);
  }

  function initNavFilter(sidebar) {
    var box = $('[data-au-nav-filter]', sidebar);
    if (!box) return;
    var input = box.querySelector('input');
    var clear = box.querySelector('button');
    var empty = $('.au-nav-empty', sidebar);
    var tree = $('[data-au-nav-tree]', sidebar) || sidebar;
    var filtering = false;
    function apply() {
      var raw = input.value.trim();
      var q = raw.toLowerCase();
      var now = q.length > 0;
      if (now !== filtering) {
        // a closed <details> hides its children no matter what CSS says,
        // so open every group while filtering and restore afterwards
        $$('details.au-nav-group', tree).forEach(function (d) {
          if (now) {
            d.setAttribute('data-au-was-open', d.open ? '1' : '0');
            d.open = true;
          } else {
            d.open = d.getAttribute('data-au-was-open') === '1';
            d.removeAttribute('data-au-was-open');
          }
        });
        filtering = now;
      }
      box.classList.toggle('has-value', now);
      sidebar.classList.toggle('is-filtering', now);
      var shown = 0;
      $$('.au-nav-row', tree).forEach(function (row) {
        var link = row.querySelector('.au-nav-item');
        var hay = ((link.getAttribute('data-trail') || '') + ' ' + link.textContent).toLowerCase();
        var match = !now || hay.indexOf(q) !== -1;
        row.classList.toggle('is-filtered-out', !match);
        if (now && match) shown++;
      });
      if (empty) {
        empty.hidden = !(now && shown === 0);
        empty.textContent = 'No pages match “' + raw + '”.';
      }
    }
    input.addEventListener('input', apply);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        input.value = '';
        apply();
      }
    });
    if (clear)
      clear.addEventListener('click', function () {
        input.value = '';
        apply();
        input.focus();
      });
  }

  function initFavorites(sidebar) {
    var host = $('[data-au-favorites]', sidebar);
    var tree = $('[data-au-nav-tree]', sidebar) || sidebar;
    function render() {
      var favs = readJson(KEY.favs, []);
      var byPath = {};
      // skip the filter-only child rows: they share their group row's href
      $$('.au-nav-row:not(.au-nav-row--search-only) > .au-nav-item', tree).forEach(function (a) {
        byPath[a.getAttribute('href')] = a;
      });
      $$('.au-nav-star', sidebar).forEach(function (b) {
        var on = favs.some(function (f) {
          return f.path === b.getAttribute('data-path');
        });
        b.classList.toggle('is-on', on);
        b.setAttribute('aria-pressed', on ? 'true' : 'false');
        var label = b.getAttribute('data-label') || '';
        b.setAttribute('aria-label', (on ? 'Unpin ' : 'Pin ') + label);
        b.title = on ? 'Unpin from favorites' : 'Pin to favorites';
      });
      if (!host) return;
      var rows = favs
        .filter(function (f) {
          return byPath[f.path];
        })
        .map(function (f) {
          var row = byPath[f.path].closest('.au-nav-row').cloneNode(true);
          row.classList.remove('is-filtered-out');
          var star = row.querySelector('.au-nav-star');
          if (star) star.classList.add('is-on');
          return row;
        });
      host.innerHTML = '';
      if (!rows.length) return;
      var label = document.createElement('div');
      label.className = 'au-side-section';
      label.innerHTML = icon('star', 'au-icon--fill') + 'Favorites';
      host.appendChild(label);
      rows.forEach(function (r) {
        host.appendChild(r);
      });
    }
    sidebar.addEventListener('click', function (e) {
      var star = e.target.closest('.au-nav-star');
      if (!star) return;
      e.preventDefault();
      e.stopPropagation();
      var path = star.getAttribute('data-path');
      var favs = readJson(KEY.favs, []);
      var exists = favs.some(function (f) {
        return f.path === path;
      });
      favs = exists
        ? favs.filter(function (f) {
            return f.path !== path;
          })
        : favs.concat([{ path: path, label: star.getAttribute('data-label') || path }]);
      store(KEY.favs, JSON.stringify(favs));
      render();
    });
    render();
  }

  function recordVisit(sidebar) {
    var active = $('[data-au-nav-tree] .au-nav-row:not(.au-nav-row--search-only) > .au-nav-item.is-active', sidebar);
    if (!active) return;
    var path = active.getAttribute('href');
    var label = (active.querySelector('.au-nav-item__label') || active).textContent.trim();
    var recent = readJson(KEY.recent, []).filter(function (r) {
      return r.path !== path;
    });
    recent.unshift({ path: path, label: label, at: Date.now() });
    store(KEY.recent, JSON.stringify(recent.slice(0, 8)));
  }

  // --- command palette (Ctrl/⌘+K quick navigation over the nav tree) -------
  function initPalette() {
    var dlg = $('dialog.au-palette');
    var dataEl = $('#au-nav-data');
    if (!dlg || !dataEl) return;
    var pages = [];
    try {
      pages = JSON.parse(dataEl.textContent || '[]');
    } catch (e) {
      pages = [];
    }
    var input = dlg.querySelector('input');
    var list = dlg.querySelector('.au-palette__list');
    var results = [];
    var index = 0;

    function byPath(path) {
      for (var i = 0; i < pages.length; i++) if (pages[i].path === path) return pages[i];
      return null;
    }
    function compute() {
      var q = input.value.trim().toLowerCase();
      var groups = [];
      if (!q) {
        var favs = readJson(KEY.favs, []).map(function (f) {
          return byPath(f.path);
        }).filter(Boolean);
        var recent = readJson(KEY.recent, []).map(function (r) {
          return byPath(r.path);
        }).filter(Boolean);
        if (favs.length) groups.push({ title: 'Favorites', items: favs });
        if (recent.length) groups.push({ title: 'Recent', items: recent.slice(0, 6) });
        groups.push({ title: 'All pages', items: pages });
      } else {
        var terms = q.split(/\s+/);
        groups.push({
          title: 'Pages',
          items: pages
            .filter(function (p) {
              var hay = ((p.trail || []).join(' ') + ' ' + p.label).toLowerCase();
              return terms.every(function (t) {
                return hay.indexOf(t) !== -1;
              });
            })
            .slice(0, 40),
        });
      }
      return groups;
    }
    function render() {
      var groups = compute();
      results = [];
      var html = '';
      groups.forEach(function (g) {
        if (!g.items.length) return;
        html += '<li class="au-palette__group" role="presentation">' + esc(g.title) + '</li>';
        g.items.forEach(function (p) {
          var i = results.length;
          results.push(p);
          html +=
            '<li class="au-palette__item" role="option" id="au-pal-' +
            i +
            '" data-i="' +
            i +
            '" aria-selected="' +
            (i === index) +
            '">' +
            icon(p.icon || 'article') +
            '<span>' +
            esc(p.label) +
            '</span><span class="au-palette__trail">' +
            esc((p.trail || []).join(' › ')) +
            '</span></li>';
        });
      });
      if (!results.length) html = '<li class="au-palette__group">No pages match.</li>';
      list.innerHTML = html;
      var sel = list.querySelector('[aria-selected="true"]');
      if (sel) sel.scrollIntoView({ block: 'nearest' });
    }
    function go(i) {
      var p = results[i];
      if (!p) return;
      dlg.close();
      progress.start();
      window.location.href = p.path;
    }
    function open() {
      input.value = '';
      index = 0;
      render();
      dlg.showModal();
      input.focus();
    }
    input.addEventListener('input', function () {
      index = 0;
      render();
    });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (!results.length) return;
        index = (index + (e.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length;
        render();
      } else if (e.key === 'Enter') {
        e.preventDefault();
        go(index);
      }
    });
    list.addEventListener('click', function (e) {
      var item = e.target.closest('.au-palette__item');
      if (item) go(Number(item.getAttribute('data-i')));
    });
    dlg.addEventListener('click', function (e) {
      if (isBackdropClick(e, dlg)) dlg.close();
    });
    document.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        if (!dlg.open) open();
      }
    });
    $$('[data-au-palette-open]').forEach(function (b) {
      b.addEventListener('click', open);
    });
  }

  // --- global scope selector (one entity, e.g. Clinic) ----------------------
  // Server contract: GET {endpoint}&q=&skip=&take= → { items:[{id,name,isActive,sub}], hasMore }
  // Picking sets ?{param}=<id> (or ?{param}= for "All") and reloads; the server
  // persists the choice (cookie) and every scope-aware page reads it.
  function initScope() {
    $$('[data-au-scope]').forEach(function (host) {
      var input = host.querySelector('.au-combobox__input');
      var list = host.querySelector('.au-combobox__list');
      if (!input || !list) return;
      var endpoint = host.getAttribute('data-endpoint');
      var param = host.getAttribute('data-param') || 'scope';
      var allLabel = host.getAttribute('data-all-label') || 'All';
      var currentId = host.getAttribute('data-current-id') || '';
      var currentName = host.getAttribute('data-current-name') || '';
      var resetParams = (host.getAttribute('data-reset-params') || 'page').split(',');
      var PAGE = 25;
      var items = [];
      var hasMore = false;
      var loading = false;
      var q = '';
      var active = -1;
      var seq = 0;

      // keep the active scope in the URL so a copied link reproduces the view
      if (currentId) {
        var here = new URL(window.location.href);
        if (!here.searchParams.has(param)) {
          here.searchParams.set(param, currentId);
          history.replaceState(history.state, '', here.toString());
        }
      }

      function options() {
        return [{ id: '', name: allLabel, isActive: true, all: true }].concat(items);
      }
      function render(status) {
        var opts = options();
        var html = opts
          .map(function (o, i) {
            var cls = 'au-combobox__opt' + (o.all ? ' au-combobox__opt--all' : '') + (o.id === currentId ? ' is-current' : '');
            return (
              '<li class="' + cls + '" role="option" id="' + list.id + '-' + i + '" data-i="' + i + '" aria-selected="' + (i === active) + '">' +
              '<span class="au-combobox__opt-name"><span>' + esc(o.name || 'Unnamed') + '</span>' +
              (!o.all && o.isActive === false ? '<span class="au-chip au-chip--outlined" style="height:18px;font-size:10px">inactive</span>' : '') +
              '</span>' +
              (o.sub ? '<span class="au-combobox__opt-sub">' + esc(o.sub) + '</span>' : '') +
              '</li>'
            );
          })
          .join('');
        if (status) html += '<li class="au-combobox__status" role="presentation">' + esc(status) + '</li>';
        list.innerHTML = html;
        input.setAttribute('aria-activedescendant', active >= 0 ? list.id + '-' + active : '');
      }
      function load(reset) {
        if (!endpoint || (loading && !reset)) return;
        if (reset) {
          items = [];
          hasMore = false;
        }
        loading = true;
        var my = ++seq;
        render('Loading…');
        var url = endpoint + (endpoint.indexOf('?') === -1 ? '?' : '&') + 'q=' + encodeURIComponent(q) + '&skip=' + items.length + '&take=' + PAGE;
        fetch(url, { headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
          .then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
          })
          .then(function (data) {
            if (my !== seq) return;
            items = items.concat(data.items || []);
            hasMore = !!data.hasMore;
            loading = false;
            render(items.length ? null : q ? 'No matches' : null);
          })
          .catch(function (err) {
            if (my !== seq) return;
            loading = false;
            render('Could not load the list (' + err.message + ')');
          });
      }
      function openList() {
        if (!list.hidden) return;
        list.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        active = -1;
        q = '';
        load(true);
      }
      function closeList(restore) {
        list.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        if (restore) input.value = currentName;
      }
      function pick(o) {
        closeList(false);
        if ((o.id || '') === currentId) {
          input.value = currentName;
          return;
        }
        var url = new URL(window.location.href);
        url.searchParams.set(param, o.id || ''); // empty = explicit "All" (server clears)
        resetParams.forEach(function (p) {
          if (p) url.searchParams.delete(p.trim());
        });
        progress.start();
        window.location.assign(url.toString());
      }
      var search = debounce(function () {
        q = input.value.trim();
        active = -1;
        load(true);
      }, 300);

      input.addEventListener('focus', function () {
        input.select();
        openList();
      });
      input.addEventListener('click', openList);
      input.addEventListener('input', function () {
        if (list.hidden) openList();
        search();
      });
      input.addEventListener('keydown', function (e) {
        var count = options().length;
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
          e.preventDefault();
          openList();
          active = (active + (e.key === 'ArrowDown' ? 1 : -1) + count) % count;
          render();
          var el = document.getElementById(list.id + '-' + active);
          if (el) el.scrollIntoView({ block: 'nearest' });
        } else if (e.key === 'Enter') {
          if (!list.hidden && active >= 0) {
            e.preventDefault();
            pick(options()[active]);
          }
        } else if (e.key === 'Escape') {
          closeList(true);
        }
      });
      input.addEventListener('blur', function () {
        setTimeout(function () {
          if (!host.contains(document.activeElement)) closeList(true);
        }, 150);
      });
      list.addEventListener('mousedown', function (e) {
        e.preventDefault(); // keep focus in the input
        var li = e.target.closest('.au-combobox__opt');
        if (li) pick(options()[Number(li.getAttribute('data-i'))]);
      });
      list.addEventListener('scroll', function () {
        if (hasMore && !loading && list.scrollTop + list.clientHeight >= list.scrollHeight - 48) load(false);
      });
    });
  }

  // --- forms: confirm gate, busy state, auto-submit filters, date range -----
  function initForms() {
    // Bubble phase on document: jQuery-unobtrusive validation runs first on the
    // form and stops an invalid submit, so we never confirm an invalid form.
    document.addEventListener('submit', function (e) {
      if (e.defaultPrevented) return;
      var form = e.target;
      if (form.method === 'dialog') return; // closes a <dialog>; not a navigation, no busy state
      var submitter = e.submitter || null;
      var gate = (submitter && submitter.hasAttribute('data-au-confirm')) || form.hasAttribute('data-au-confirm');
      if (gate && !form.__auConfirmed) {
        e.preventDefault();
        confirmDialog(confirmOptions(submitter, form)).then(function (ok) {
          if (!ok) return;
          form.__auConfirmed = true;
          // requestSubmit(submitter) keeps the submitter's name/value + formaction
          // (Razor's asp-page-handler on a button depends on it)
          if (form.requestSubmit) form.requestSubmit(submitter || undefined);
          else form.submit();
        });
        return;
      }
      form.__auConfirmed = false;
      if (form.target === '_blank' || form.hasAttribute('data-au-no-busy')) return;
      // NEVER disable the submitter here: a disabled button is dropped from the
      // form data and the page handler would not be selected.
      if (submitter && submitter.classList.contains('au-btn')) submitter.setAttribute('aria-busy', 'true');
      progress.start();
    });

    document.addEventListener('change', function (e) {
      var el = e.target;
      var form = el.form;
      if (!form || !form.hasAttribute('data-au-autosubmit')) return;
      if (el.hasAttribute('data-au-range-select') && el.value === 'custom') return; // wait for Apply
      if (el.closest('[data-au-range-custom]')) return;
      var isText = el.tagName === 'TEXTAREA' || (el.tagName === 'INPUT' && /^(text|search|email|tel|url|number)$/i.test(el.type));
      if (isText) return; // text filters submit on Enter, never on every keystroke
      resetPage(form);
      if (form.requestSubmit) form.requestSubmit();
      else form.submit();
    });

    $$('[data-au-range]').forEach(function (range) {
      var select = range.querySelector('[data-au-range-select]');
      var custom = range.querySelector('[data-au-range-custom]');
      if (!select || !custom) return;
      var sync = function () {
        custom.hidden = select.value !== 'custom';
      };
      select.addEventListener('change', sync);
      sync();
    });
  }
  function resetPage(form) {
    var name = form.getAttribute('data-au-page-param') || 'page';
    var first = form.getAttribute('data-au-page-first') || '1';
    var input = form.querySelector('[name="' + name + '"]');
    if (input) input.value = first;
  }

  // --- clicks: dialogs, drawers, copy, row links, confirm links, refresh ----
  function initClicks() {
    document.addEventListener('click', function (e) {
      var t = e.target;

      var opener = t.closest('[data-au-open]');
      if (opener) {
        e.preventDefault();
        dialog.open(opener.getAttribute('data-au-open'));
        return;
      }
      var closer = t.closest('[data-au-close]');
      if (closer) {
        var d = closer.closest('dialog');
        if (d) d.close();
        return;
      }
      // click on the backdrop of a dismissable dialog / drawer
      if (t.tagName === 'DIALOG' && (t.classList.contains('au-drawer') || t.hasAttribute('data-au-dismissable')) && isBackdropClick(e, t)) {
        t.close();
        return;
      }

      var copyBtn = t.closest('[data-au-copy]');
      if (copyBtn) {
        e.preventDefault();
        e.stopPropagation();
        var value = copyBtn.getAttribute('data-au-copy');
        var src = copyBtn.getAttribute('data-au-copy-target');
        if (src) {
          var srcEl = $(src);
          value = srcEl ? srcEl.textContent : '';
        }
        copy(value || '', copyBtn.closest('dialog') || undefined).then(
          function () {
            toast.success(copyBtn.getAttribute('data-au-copy-message') || 'Copied');
          },
          function () {
            toast.error('Could not copy to clipboard');
          }
        );
        return;
      }

      var refresh = t.closest('[data-au-refresh]');
      if (refresh) {
        e.preventDefault();
        progress.start();
        window.location.reload();
        return;
      }

      var link = t.closest('a[data-au-confirm]');
      if (link) {
        e.preventDefault();
        confirmDialog(confirmOptions(link, link)).then(function (ok) {
          if (ok) {
            progress.start();
            window.location.href = link.href;
          }
        });
        return;
      }

      // row → drawer (partial HTML) or row → page
      var row = t.closest('[data-au-drawer-url], [data-au-href]');
      if (row && !t.closest('a, button, input, select, textarea, label, summary, [data-au-stop]')) {
        if (row.hasAttribute('data-au-drawer-url')) {
          e.preventDefault();
          openRemoteDrawer(row.getAttribute('data-au-drawer-url'), row.getAttribute('data-au-drawer') || '#au-drawer');
          return;
        }
        var href = row.getAttribute('data-au-href');
        if (e.ctrlKey || e.metaKey || e.button === 1) window.open(href, '_blank');
        else {
          progress.start();
          window.location.href = href;
        }
        return;
      }

      // ordinary same-tab navigation → show the progress strip
      var a = t.closest('a[href]');
      if (
        a &&
        !e.defaultPrevented &&
        !e.ctrlKey &&
        !e.metaKey &&
        !e.shiftKey &&
        (!a.target || a.target === '_self') &&
        !a.hasAttribute('download') &&
        a.origin === window.location.origin &&
        !(a.pathname === window.location.pathname && a.search === window.location.search && a.hash)
      ) {
        progress.start();
      }
    });

    // keyboard: Enter on a focused clickable row
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      var row = e.target.closest && e.target.closest('tr[data-au-href], tr[data-au-drawer-url]');
      if (row && e.target === row) row.click();
    });
  }

  function openRemoteDrawer(url, selector) {
    var drawer = $(selector);
    if (!drawer) return;
    var body = drawer.querySelector('[data-au-drawer-body]') || drawer;
    body.innerHTML =
      '<span class="au-skeleton au-skeleton--text" style="width:60%"></span>' +
      '<span class="au-skeleton" style="height:120px;margin-top:16px"></span>';
    dialog.open(drawer);
    fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
      .then(function (r) {
        return r.text().then(function (html) {
          if (!r.ok) throw { status: r.status, html: html };
          return html;
        });
      })
      .then(function (html) {
        body.innerHTML = html; // a server-rendered partial (already HTML-encoded by Razor)
      })
      .catch(function (err) {
        body.innerHTML =
          '<div class="au-alert au-alert--error">' + icon('error', 'au-alert__icon') +
          '<div class="au-alert__body"><div class="au-alert__title">Could not load the details</div>' +
          esc(err && err.status ? 'The server answered HTTP ' + err.status + ' for ' + url : 'The request did not reach the server: ' + url) +
          '</div></div>';
      });
  }

  // --- dropdowns (<details class="au-dropdown">): one open at a time --------
  function initDropdowns() {
    document.addEventListener(
      'toggle',
      function (e) {
        var d = e.target;
        if (!d.classList || !d.classList.contains('au-dropdown') || !d.open) return;
        $$('details.au-dropdown[open]').forEach(function (o) {
          if (o !== d) o.open = false;
        });
      },
      true
    );
    document.addEventListener('click', function (e) {
      $$('details.au-dropdown[open]').forEach(function (d) {
        if (!d.contains(e.target) || e.target.closest('.au-menu__item')) d.open = false;
      });
    });
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Escape') return;
      $$('details.au-dropdown[open]').forEach(function (d) {
        d.open = false;
        var s = d.querySelector('summary');
        if (s) s.focus();
      });
      root.classList.remove('au-side-open');
    });
  }

  // --- boot -------------------------------------------------------------------
  function boot() {
    progress.el = $('.au-topbar-progress');
    $$('[data-au-theme-toggle]').forEach(function (btn) {
      syncThemeToggle(btn);
      btn.addEventListener('click', theme.toggle);
    });
    initSidebar();
    initPalette();
    initScope();
    initForms();
    initClicks();
    initDropdowns();

    // server-rendered toasts (e.g. from TempData after Post-Redirect-Get)
    $$('[data-au-toast]').forEach(function (el) {
      showToast(el.getAttribute('data-au-toast'), el.textContent.trim());
      el.parentNode.removeChild(el);
    });

    // keep the active section tab visible on narrow screens
    var tab = $('.au-section-tabs .au-tab.is-active');
    if (tab && tab.scrollIntoView) tab.scrollIntoView({ block: 'nearest', inline: 'center' });

    // soft page-enter motion (visual only)
    var main = $('.au-main');
    if (main && main.animate && !reduceMotion) {
      main.animate(
        [
          { opacity: 0.3, transform: 'translateY(6px)' },
          { opacity: 1, transform: 'none' },
        ],
        { duration: 260, easing: 'cubic-bezier(0.2, 0.8, 0.3, 1)' }
      );
    }
  }

  // back/forward-cache restore: clear the busy state the page was frozen with
  window.addEventListener('pageshow', function (e) {
    if (!e.persisted) return;
    progress.stop();
    $$('[aria-busy="true"]').forEach(function (b) {
      b.removeAttribute('aria-busy');
    });
  });

  window.Aurora = { theme: theme, toast: toast, confirm: confirmDialog, copy: copy, progress: progress, dialog: dialog };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
