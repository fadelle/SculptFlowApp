// Sign in / Register: show-password buttons, Caps Lock warning, password rule + match checks,
// and a one-time submit ("Signing in…").
(function () {
  document.querySelectorAll('[data-pw-toggle]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var input = btn.parentElement.querySelector('input');
      var show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      btn.textContent = show ? '🙈' : '👁';
      btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
      btn.title = btn.getAttribute('aria-label');
      input.focus();
    });
  });

  var caps = document.querySelector('[data-caps]');
  if (caps) {
    document.querySelectorAll('[data-pw]').forEach(function (input) {
      ['keydown', 'keyup'].forEach(function (t) {
        input.addEventListener(t, function (e) {
          if (e.getModifierState) caps.hidden = !e.getModifierState('CapsLock');
        });
      });
      input.addEventListener('blur', function () { caps.hidden = true; });
    });
  }

  var pw = document.querySelector('[data-pw-new]');
  var confirm = document.querySelector('[data-pw-confirm]');
  if (pw && confirm) {
    var rule = document.querySelector('[data-pw-rule]');
    var match = document.querySelector('[data-pw-match]');
    var check = function () {
      var left = 6 - pw.value.length;
      rule.className = pw.value && left <= 0 ? 'field-ok' : 'text-subtle';
      rule.textContent = !pw.value ? 'At least 6 characters'
        : left > 0 ? 'At least 6 characters (' + left + ' more)' : '✓ At least 6 characters';

      var bad = confirm.value && confirm.value !== pw.value;
      match.hidden = !confirm.value;
      match.className = bad ? 'field-err' : 'field-ok';
      match.textContent = bad ? "Passwords don't match" : '✓ Passwords match';
      confirm.classList.toggle('input-error', !!bad);
      confirm.setCustomValidity(bad ? "Passwords don't match" : '');
    };
    pw.addEventListener('input', check);
    confirm.addEventListener('input', check);
  }

  document.querySelectorAll('[data-auth-form]').forEach(function (form) {
    form.addEventListener('submit', function (e) {
      if (form.dataset.sent) { e.preventDefault(); return; }
      form.dataset.sent = '1';
      var btn = form.querySelector('[data-busy-label]');
      if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<span class="spin spin-light"></span>' + btn.getAttribute('data-busy-label');
      }
    });
  });
})();
