// Sign in / Register: show-password buttons, Caps Lock warning, password rule + match checks,
// and a one-time submit ("Signing in…").
(function () {
  document.querySelectorAll('[data-pw-toggle]').forEach(function (btn) {
    btn.addEventListener('click', function () {
      var input = btn.parentElement.querySelector('input');
      var show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      btn.textContent = show ? '🙈' : '👁';
      btn.setAttribute('aria-label', show ? omniText.t('auth.password.hide') : omniText.t('auth.password.show'));
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
      rule.textContent = !pw.value ? omniText.t('auth.password.rule')
        : left > 0 ? omniText.t('auth.password.ruleLeft', [left]) : omniText.t('auth.password.ruleMet');

      var bad = confirm.value && confirm.value !== pw.value;
      match.hidden = !confirm.value;
      match.className = bad ? 'field-err' : 'field-ok';
      match.textContent = bad ? omniText.t('auth.password.mismatch') : omniText.t('auth.password.match');
      confirm.classList.toggle('input-error', !!bad);
      confirm.setCustomValidity(bad ? omniText.t('auth.password.mismatch') : '');
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
        btn.innerHTML = '<span class="spin spin-light"></span>';
        btn.appendChild(document.createTextNode(btn.getAttribute('data-busy-label')));
      }
    });
  });
})();
