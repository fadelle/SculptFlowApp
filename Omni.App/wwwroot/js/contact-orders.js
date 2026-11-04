// Contact page orders popup (Pages/Dashboard/ContactDetail.cshtml, #order-overlay): modal.js opens it; this switches it
// between adding an order (the Add button) and editing one (an Edit button's data-order-*: id, service name, status,
// amounts, currency). Adding picks a service; editing shows the service and adds status, deposit and final amount. The
// status list offers only the current status and the ones it may move to (data-order-next, OrderService's table).
(function () {
  var overlay = document.getElementById('order-overlay');
  if (!overlay) return;
  var form = document.getElementById('order-form');
  var title = document.getElementById('order-form-title');
  var submit = document.getElementById('order-submit');

  function set(id, value) {
    var el = document.getElementById(id);
    if (el) el.value = value == null ? '' : value;
  }

  overlay.addEventListener('modal:open', function (e) {
    var trigger = e.detail && e.detail.trigger;
    var editing = !!(trigger && trigger.hasAttribute('data-order-id'));
    if (!trigger) return; // opened on load (a failed save or ?order=new): the page already filled it
    form.reset();
    set('order-id', editing ? trigger.getAttribute('data-order-id') : '');
    document.getElementById('order-service-field').hidden = editing;
    form.querySelectorAll('[data-order-edit-only]').forEach(function (el) { el.hidden = !editing; });
    title.textContent = title.getAttribute(editing ? 'data-title-edit' : 'data-title-add');
    submit.textContent = submit.getAttribute(editing ? 'data-label-edit' : 'data-label-add');
    if (!editing) {
      set('order-quoted', ''); set('order-currency', ''); set('order-notes', '');
      return;
    }
    document.getElementById('order-service-name').textContent = trigger.getAttribute('data-order-service') || '—';
    var current = trigger.getAttribute('data-order-status');
    var next = (trigger.getAttribute('data-order-next') || '').split(',');
    document.querySelectorAll('#order-status option').forEach(function (o) {
      var allowed = o.value === current || next.indexOf(o.value) >= 0;
      o.disabled = !allowed;
      o.hidden = !allowed;
    });
    set('order-status', current);
    set('order-quoted', trigger.getAttribute('data-order-quoted'));
    set('order-deposit', trigger.getAttribute('data-order-deposit'));
    set('order-final', trigger.getAttribute('data-order-final'));
    set('order-currency', trigger.getAttribute('data-order-currency'));
    set('order-notes', '');
  });
})();
