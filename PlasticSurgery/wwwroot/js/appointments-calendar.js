/* Appointments — month calendar. Reads/writes the existing /api/appointments, /api/leads and /api/procedures APIs
   (the same appointments table used by AI bookings, staff edits and everything else — this page owns no data of
   its own). Every value from the server is written with textContent (never innerHTML), so a lead/procedure name
   can't inject markup. */
(function () {
    'use strict';

    var state = {
        year: 0, month: 0,           // the visible month (1-based month, matching the API)
        monthData: null,             // the last GET /api/appointments/calendar response (covers the grid AND the drawer)
        drawerDate: null,            // "yyyy-MM-dd" the day drawer is currently showing, or null
        leadSearchTimer: null,
        selectedLead: null           // { id, label } chosen in the New Appointment form
    };

    var WEEKDAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
    var MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

    function $(id) { return document.getElementById(id); }

    function el(tag, attrs, kids) {
        var node = document.createElement(tag);
        if (attrs) {
            Object.keys(attrs).forEach(function (k) {
                var v = attrs[k];
                if (v === null || v === undefined || v === false) return;
                if (k === 'class') node.className = v;
                else if (k === 'text') node.textContent = v;
                else if (k.indexOf('on') === 0) node.addEventListener(k.slice(2), v);
                else node.setAttribute(k, v === true ? '' : v);
            });
        }
        (kids || []).forEach(function (kid) {
            if (kid === null || kid === undefined) return;
            node.appendChild(typeof kid === 'string' ? document.createTextNode(kid) : kid);
        });
        return node;
    }

    function api(method, path, body) {
        var opts = { method: method, credentials: 'same-origin', headers: { 'Accept': 'application/json' } };
        if (body !== undefined) {
            opts.headers['Content-Type'] = 'application/json';
            opts.body = JSON.stringify(body);
        }
        return fetch(path, opts).then(function (res) {
            if (res.status === 204) return null;
            return res.text().then(function (text) {
                var data = null;
                try { data = text ? JSON.parse(text) : null; } catch (e) { /* not JSON */ }
                if (!res.ok) {
                    var msg = (data && (data.error || data.title)) || ('Request failed (' + res.status + ').');
                    throw new Error(msg);
                }
                return data;
            });
        });
    }

    function showMessage(text, kind) {
        var box = $('cal-message');
        box.textContent = '';
        if (!text) return;
        box.appendChild(el('div', { class: 'alert ' + (kind === 'error' ? 'alert-warning' : 'alert-info') + ' mb-3' }, [text]));
    }

    /** Past appointments still booked/confirmed have no recorded outcome — staff open each one and set attended / no-show / canceled. */
    function renderOutcomeNotice(count) {
        var box = $('cal-outcome');
        box.textContent = '';
        if (!count) return;
        box.appendChild(el('div', { class: 'alert alert-warning mb-3' }, [
            count + (count === 1 ? ' past appointment needs an outcome' : ' past appointments need an outcome') +
            ' - open it and mark attended, no-show or canceled.'
        ]));
    }

    // ---------------------------------------------------------------- month grid

    function loadMonth(year, month) {
        state.year = year; state.month = month;
        $('cal-title').textContent = MONTH_NAMES[month - 1] + ' ' + year;
        return api('GET', '/api/appointments/calendar?year=' + year + '&month=' + month).then(function (data) {
            state.monthData = data;
            renderOutcomeNotice(data.needsOutcomeCount);
            renderGrid(data);
            // Keep the drawer's contents in sync if it's open and its date is still in the new month's data.
            if (state.drawerDate) renderDrawer(state.drawerDate);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** "yyyy-MM-dd" for today, in the BROWSER's own local date — used only to highlight "today" on the grid and
        to pick which month opens first; all appointment grouping/display uses the server-computed clinic-local
        LocalDate/LocalTime instead, so which day an appointment falls on is never affected by this. */
    function todayIso() {
        var d = new Date();
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }

    function addDaysIso(iso, days) {
        var d = new Date(iso + 'T00:00:00');
        d.setDate(d.getDate() + days);
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }

    function renderGrid(data) {
        var grid = $('cal-grid');
        grid.textContent = '';

        var byDate = {};
        data.items.forEach(function (a) { (byDate[a.localDate] = byDate[a.localDate] || []).push(a); });

        var monthPrefix = data.year + '-' + String(data.month).padStart(2, '0');
        var today = todayIso();
        var cursor = data.gridStart;
        while (true) {
            grid.appendChild(dayCell(cursor, byDate[cursor] || [], cursor.indexOf(monthPrefix) === 0, cursor === today));
            if (cursor === data.gridEnd) break;
            cursor = addDaysIso(cursor, 1);
        }
    }

    function dayCell(dateIso, items, isCurrentMonth, isToday) {
        var dayNum = parseInt(dateIso.slice(8, 10), 10);
        var classes = 'cal-day' + (isCurrentMonth ? '' : ' other-month') + (isToday ? ' today' : '');

        var kids = [el('div', { class: 'cal-day-num', text: String(dayNum) })];
        var shown = items.slice(0, 3);
        shown.forEach(function (a) {
            var label = a.localTime + ' ' + shortName(a.leadFullName) + (a.procedureName ? ' - ' + a.procedureName : '');
            kids.push(el('div', { class: 'cal-chip status-' + a.status, title: label, text: label }));
        });
        if (items.length > shown.length) {
            kids.push(el('div', { class: 'cal-more', text: '+' + (items.length - shown.length) + ' more' }));
        }
        kids.push(el('button', {
            type: 'button', class: 'cal-day-add', title: 'New appointment on ' + dateIso, 'aria-label': 'New appointment',
            onclick: function (e) { e.stopPropagation(); openNewAppointment(dateIso); }
        }, ['+']));

        return el('div', { class: classes, onclick: function () { openDrawer(dateIso); } }, kids);
    }

    function shortName(fullName) {
        if (!fullName) return 'Unknown';
        var parts = fullName.trim().split(/\s+/);
        return parts.length < 2 ? parts[0] : parts[0] + ' ' + parts[1][0] + '.';
    }

    // ---------------------------------------------------------------- day drawer

    function openDrawer(dateIso) {
        state.drawerDate = dateIso;
        renderDrawer(dateIso);
        $('cal-drawer-overlay').hidden = false;
        $('cal-drawer').hidden = false;
    }

    function closeDrawer() {
        state.drawerDate = null;
        $('cal-drawer-overlay').hidden = true;
        $('cal-drawer').hidden = true;
    }

    function renderDrawer(dateIso) {
        var d = new Date(dateIso + 'T00:00:00');
        $('cal-drawer-title').textContent = d.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });

        var body = $('cal-drawer-body');
        body.textContent = '';
        var items = ((state.monthData && state.monthData.items) || []).filter(function (a) { return a.localDate === dateIso; });

        if (!items.length) {
            body.appendChild(el('div', { class: 'text-subtle', style: 'padding:1rem 0.25rem', text: 'No appointments scheduled for this day.' }));
            return;
        }

        items.forEach(function (a) {
            body.appendChild(el('div', { class: 'cal-appt-row', onclick: function () { window.location.href = '/dashboard/appointments/' + a.id; } }, [
                el('div', { class: 'flex items-center gap-2', style: 'justify-content:space-between' }, [
                    el('span', { class: 'cal-appt-time', text: a.localTime }),
                    el('span', { class: 'badge ' + badgeClass(a.status), text: statusLabel(a.status) })
                ]),
                el('div', { class: 'cal-appt-name', text: a.leadFullName || 'Unknown patient' }),
                el('div', { class: 'cal-appt-procedure', text: a.procedureName || 'No procedure on file' })
            ]));
        });
    }

    var BADGE_CLASS = { booked: 'badge-amber', confirmed: 'badge-blue', attended: 'badge-green', canceled: 'badge-gray', no_show: 'badge-red', rescheduled: 'badge-purple' };
    var STATUS_LABEL = { booked: 'Booked', confirmed: 'Confirmed', attended: 'Attended', canceled: 'Canceled', no_show: 'No-show', rescheduled: 'Rescheduled' };
    function badgeClass(status) { return BADGE_CLASS[status] || 'badge-gray'; }
    function statusLabel(status) { return STATUS_LABEL[status] || status; }

    // ---------------------------------------------------------------- new appointment

    /** Converts a wall-clock date+time typed by staff into the correct UTC instant for the CLINIC's own timezone
        (not the browser's) — the same "what day/time is this really" question the server already answers when
        reading the calendar, just worked in reverse. Vanilla JS, no library: for the given calendar date, asks
        Intl how far `timeZone` sits from UTC (this naturally accounts for DST) and applies that offset. */
    function zonedWallClockToUtcIso(dateStr, timeStr, timeZone) {
        var naiveUtc = new Date(dateStr + 'T' + timeStr + ':00Z');
        var asZoned = new Date(naiveUtc.toLocaleString('en-US', { timeZone: timeZone }));
        var asUtc = new Date(naiveUtc.toLocaleString('en-US', { timeZone: 'UTC' }));
        var offsetMs = asZoned.getTime() - asUtc.getTime();
        return new Date(naiveUtc.getTime() - offsetMs).toISOString();
    }

    function openNewAppointment(prefillDateIso) {
        $('cal-new-error').textContent = '';
        $('cal-new-lead-search').value = '';
        $('cal-new-lead-id').value = '';
        $('cal-new-lead-results').textContent = '';
        state.selectedLead = null;
        $('cal-new-date').value = prefillDateIso || state.drawerDate || todayIso();
        $('cal-new-time').value = '09:00';
        $('cal-new-duration').value = '30';
        $('cal-new-location').value = '';
        $('cal-new-notes').value = '';
        loadProcedures();
        $('cal-new-overlay').hidden = false;
    }

    function closeNewAppointment() { $('cal-new-overlay').hidden = true; }

    function loadProcedures() {
        var sel = $('cal-new-procedure');
        api('GET', '/api/procedures').then(function (procedures) {
            sel.textContent = '';
            sel.appendChild(el('option', { value: '', text: 'No procedure' }));
            procedures.forEach(function (p) { sel.appendChild(el('option', { value: p.id, text: p.name })); });
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function searchLeads(query) {
        var box = $('cal-new-lead-results');
        if (!query || query.trim().length < 2) { box.textContent = ''; return; }
        api('GET', '/api/leads?take=8&search=' + encodeURIComponent(query.trim())).then(function (res) {
            box.textContent = '';
            var items = (res && res.items) || [];
            if (!items.length) { box.appendChild(el('div', { text: 'No matching patients.' })); return; }
            items.forEach(function (lead) {
                var label = (lead.fullName || 'Unnamed') + (lead.phone ? ' · ' + lead.phone : '');
                box.appendChild(el('div', { class: 'bm-clickable', style: 'padding:.2rem 0', onclick: function () { pickLead(lead, label); } }, [label]));
            });
        }).catch(function () { /* transient — staff can retype */ });
    }

    function pickLead(lead, label) {
        state.selectedLead = { id: lead.id, label: label };
        $('cal-new-lead-search').value = label;
        $('cal-new-lead-id').value = lead.id;
        $('cal-new-lead-results').textContent = '';
    }

    function saveNewAppointment() {
        var err = $('cal-new-error');
        err.textContent = '';
        var leadId = $('cal-new-lead-id').value;
        var date = $('cal-new-date').value;
        var time = $('cal-new-time').value;
        if (!leadId) { err.textContent = 'Search for the patient and pick one from the list.'; return; }
        if (!date || !time) { err.textContent = 'Enter a date and time.'; return; }

        var timeZone = (state.monthData && state.monthData.timezone) || 'UTC';
        var startIso = zonedWallClockToUtcIso(date, time, timeZone);
        var durationMin = parseInt($('cal-new-duration').value, 10);
        var endIso = durationMin > 0 ? new Date(new Date(startIso).getTime() + durationMin * 60000).toISOString() : null;

        var body = {
            clinicId: '00000000-0000-0000-0000-000000000000', // ignored server-side — the clinic always comes from the logged-in user
            leadId: leadId,
            procedureId: $('cal-new-procedure').value || null,
            appointmentType: 'consultation',
            scheduledStart: startIso,
            scheduledEnd: endIso,
            locationType: $('cal-new-location').value || null,
            locationName: null,
            notes: $('cal-new-notes').value || null
        };

        var btn = $('cal-new-save');
        btn.disabled = true;
        api('POST', '/api/appointments', body).then(function () {
            btn.disabled = false;
            closeNewAppointment();
            showMessage('Appointment created.');
            return loadMonth(state.year, state.month);
        }).catch(function (e) {
            btn.disabled = false;
            err.textContent = e.message;
        });
    }

    // ---------------------------------------------------------------- wiring

    /** Live updates: the server sends "AppointmentChanged" (over the same clinic-scoped SignalR hub the Inbox uses) after an appointment is
        created, rescheduled, canceled or has its status changed — by staff, the AI, or another tab. We just re-fetch the visible month from the
        API (the database stays the source of truth); the open drawer and New Appointment form are left alone. */
    function connectLive() {
        if (!window.signalR) return;
        var timer = null;
        function refresh() {
            clearTimeout(timer);
            timer = setTimeout(function () { loadMonth(state.year, state.month); }, 300); // coalesce bursts
        }
        var connection = new signalR.HubConnectionBuilder().withUrl('/hubs/inbox').withAutomaticReconnect().build();
        connection.on('AppointmentChanged', refresh);
        connection.onreconnected(refresh); // events missed while disconnected
        connection.start().catch(function (err) { console.error('[calendar] live updates unavailable', err); });
    }

    function init() {
        var today = new Date();
        loadMonth(today.getFullYear(), today.getMonth() + 1);
        connectLive();

        $('cal-prev').addEventListener('click', function () {
            var m = state.month - 1, y = state.year;
            if (m < 1) { m = 12; y--; }
            loadMonth(y, m);
        });
        $('cal-next').addEventListener('click', function () {
            var m = state.month + 1, y = state.year;
            if (m > 12) { m = 1; y++; }
            loadMonth(y, m);
        });
        $('cal-today').addEventListener('click', function () {
            var t = new Date();
            loadMonth(t.getFullYear(), t.getMonth() + 1);
        });

        $('cal-drawer-close').addEventListener('click', closeDrawer);
        $('cal-drawer-overlay').addEventListener('click', closeDrawer);
        $('cal-drawer-add').addEventListener('click', function () { openNewAppointment(state.drawerDate); });

        $('cal-new').addEventListener('click', function () { openNewAppointment(null); });
        $('cal-new-close').addEventListener('click', closeNewAppointment);
        $('cal-new-cancel').addEventListener('click', closeNewAppointment);
        $('cal-new-overlay').addEventListener('click', function (e) { if (e.target === $('cal-new-overlay')) closeNewAppointment(); });
        $('cal-new-save').addEventListener('click', saveNewAppointment);
        $('cal-new-lead-search').addEventListener('input', function (e) {
            state.selectedLead = null;
            $('cal-new-lead-id').value = '';
            clearTimeout(state.leadSearchTimer);
            var q = e.target.value;
            state.leadSearchTimer = setTimeout(function () { searchLeads(q); }, 250);
        });

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (!$('cal-new-overlay').hidden) closeNewAppointment();
            else if (!$('cal-drawer').hidden) closeDrawer();
        });
    }

    if ($('cal-grid')) init();
})();
