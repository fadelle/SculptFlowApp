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
        return api('GET', '/api/appointments/calendar?year=' + year + '&month=' + month + '&view=' + currentView()).then(function (data) {
            state.monthData = data;
            renderOutcomeNotice(data.needsOutcomeCount);
            renderGrid(data);
            renderAgenda(data);
            renderTodayLine(data);
            // Keep the drawer's contents in sync if it's open and its date is still in the new month's data.
            if (state.drawerDate) renderDrawer(state.drawerDate);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** 'clinic' (default) or 'mine' — the page's "Clinic time | My time" switch (_TimeViewSwitch, local-time.js). */
    function currentView() {
        var sw = document.querySelector('.time-view-switch');
        return SculptTime.timeView(sw ? sw.getAttribute('data-clinic-tz') : null);
    }

    /** The timezone the server grouped the grid in (LocalDate/LocalTime) — the clinic's, or the viewer's on "My time".
        "Today", "now" and typed new-appointment times use the same zone. Falls back to the browser's clock until the
        first month has loaded. */
    function gridTimeZone() {
        return (state.monthData && state.monthData.timezone) || null;
    }

    /** "yyyy-MM-dd" for today in the grid's timezone. */
    function todayIso() {
        var tz = gridTimeZone();
        if (tz) return SculptTime.todayIn(tz);
        var d = new Date();
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }

    /** "HH:mm" for right now in the grid's timezone. */
    function nowTimeInGrid() {
        return new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: gridTimeZone() || undefined });
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
            grid.appendChild(dayCell(cursor, byDate[cursor] || [], cursor.indexOf(monthPrefix) === 0, cursor === today, cursor < today));
            if (cursor === data.gridEnd) break;
            cursor = addDaysIso(cursor, 1);
        }
    }

    function dayCell(dateIso, items, isCurrentMonth, isToday, isPast) {
        var dayNum = parseInt(dateIso.slice(8, 10), 10);
        var classes = 'cal-day' + (isCurrentMonth ? '' : ' other-month') + (isToday ? ' today' : '') + (isPast ? ' past' : '');

        var kids = [el('div', { class: 'cal-day-num', text: String(dayNum) })];
        var shown = items.slice(0, 3);
        shown.forEach(function (a) {
            // Chip shows just the time (bold) and a short name; the procedure is in the tooltip and the day drawer.
            var title = a.localTime + ' ' + (a.leadFullName || 'Unknown') + (a.procedureName ? ' - ' + a.procedureName : '') + ' (' + statusLabel(a.status) + ')';
            kids.push(el('div', { class: 'cal-chip status-' + a.status, title: title, onclick: function (e) { e.stopPropagation(); openAppointment(a); } }, [
                el('b', { text: a.localTime }), ' ' + shortName(a.leadFullName)
            ]));
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

    /** "Today: 3 appointments · next at 11:00 with Layla H." above the calendar. Only refreshed when the loaded
        month's data covers today (so paging to another month keeps the last known line). Canceled ones don't count. */
    function renderTodayLine(data) {
        var box = $('cal-today-line');
        if (!box) return;
        var today = todayIso();
        if (today < data.gridStart || today > data.gridEnd) return;

        var todays = data.items
            .filter(function (a) { return a.localDate === today && a.status !== 'canceled'; })
            .sort(function (a, b) { return a.localTime < b.localTime ? -1 : 1; });
        var nowTime = nowTimeInGrid();
        var next = todays.filter(function (a) { return a.localTime >= nowTime; })[0];

        box.textContent = '';
        box.appendChild(el('strong', { text: 'Today:' }));
        box.appendChild(document.createTextNode(' ' + (todays.length === 0 ? 'no appointments'
            : todays.length + (todays.length === 1 ? ' appointment' : ' appointments'))));
        if (next) {
            box.appendChild(document.createTextNode(' · next at '));
            box.appendChild(el('strong', { text: next.localTime }));
            box.appendChild(document.createTextNode(' with ' + shortName(next.leadFullName)));
        } else if (todays.length) {
            box.appendChild(document.createTextNode(' · none left for today'));
        }
        box.hidden = false;
    }

    /** Phone layout: the 7-column grid is too cramped, so site.css hides it and shows this day-by-day list of the
        visible month's appointments instead (same data, same links). */
    function renderAgenda(data) {
        var box = $('cal-agenda');
        if (!box) return;
        box.textContent = '';
        var monthPrefix = data.year + '-' + String(data.month).padStart(2, '0');
        var today = todayIso();
        var byDate = {};
        data.items
            .filter(function (a) { return a.localDate.indexOf(monthPrefix) === 0; })
            .forEach(function (a) { (byDate[a.localDate] = byDate[a.localDate] || []).push(a); });
        var dates = Object.keys(byDate).sort();
        if (!dates.length) {
            box.appendChild(el('div', { class: 'agenda-empty', text: 'No appointments this month.' }));
            return;
        }
        dates.forEach(function (dateIso) {
            var d = new Date(dateIso + 'T00:00:00');
            var label = d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
            box.appendChild(el('div', { class: 'agenda-day' + (dateIso < today ? ' past' : ''), text: (dateIso === today ? 'Today · ' : '') + label }));
            byDate[dateIso]
                .sort(function (a, b) { return a.localTime < b.localTime ? -1 : 1; })
                .forEach(function (a) {
                    box.appendChild(el('a', { class: 'agenda-row' + (dateIso < today ? ' past' : ''), href: '/dashboard/appointments/' + a.id, onclick: function (e) { e.preventDefault(); openAppointment(a); } }, [
                        el('span', { class: 'cal-appt-time', text: a.localTime }),
                        el('div', { class: 'agenda-row-main' }, [
                            el('div', { class: 'cal-appt-name', text: a.leadFullName || 'Unknown patient' }),
                            el('div', { class: 'cal-appt-procedure', text: a.procedureName || 'No procedure on file' })
                        ]),
                        el('span', { class: 'badge ' + badgeClass(a.status), text: statusLabel(a.status) })
                    ]));
                });
        });
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
            body.appendChild(el('div', { class: 'cal-appt-row', onclick: function () { openAppointment(a); } }, [
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

    // ---------------------------------------------------------------- appointment details popup

    var LOCATION_LABEL = { in_person: 'In person', video: 'Video call', phone: 'Phone' };
    function locationLabel(value) { return value ? (LOCATION_LABEL[value] || value) : '—'; }

    function initialOf(name) { return (name || '?').trim().charAt(0).toUpperCase() || '?'; }

    /** Opens the popup from a calendar item right away (name, time, status), then fills in location/notes from
        GET /api/appointments/{id}, which the calendar feed doesn't carry. */
    function openAppointment(a) {
        state.openAppointment = a;
        var d = new Date(a.localDate + 'T00:00:00');
        $('cal-appt-avatar').textContent = initialOf(a.leadFullName);
        $('cal-appt-name').textContent = a.leadFullName || 'Unknown patient';
        $('cal-appt-when').textContent = d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }) + ' · ' + a.localTime;
        $('cal-appt-procedure').textContent = a.procedureName || 'No procedure on file';
        $('cal-appt-location').textContent = '…';
        $('cal-appt-notes').textContent = '…';
        $('cal-appt-notes-row').hidden = false;
        $('cal-appt-status').value = a.status;
        $('cal-appt-error').textContent = '';
        $('cal-appt-full').href = '/dashboard/appointments/' + a.id;
        $('cal-appt-lead').href = '/dashboard/leads/' + a.leadId;
        $('cal-appt-overlay').hidden = false;

        api('GET', '/api/appointments/' + a.id).then(function (full) {
            if (!state.openAppointment || state.openAppointment.id !== a.id) return; // another one was opened meanwhile
            $('cal-appt-location').textContent = locationLabel(full.locationType);
            $('cal-appt-notes').textContent = full.notes || '—';
            $('cal-appt-notes-row').hidden = !full.notes;
            if (full.scheduledEnd) {
                var mins = Math.round((new Date(full.scheduledEnd) - new Date(full.scheduledStart)) / 60000);
                if (mins > 0) $('cal-appt-when').textContent += ' – ' + addMinutes(a.localTime, mins);
            }
        }).catch(function () {
            $('cal-appt-location').textContent = '—';
            $('cal-appt-notes-row').hidden = true;
        });
    }

    function closeAppointment() {
        state.openAppointment = null;
        $('cal-appt-overlay').hidden = true;
    }

    function saveAppointmentStatus() {
        var a = state.openAppointment;
        if (!a) return;
        var status = $('cal-appt-status').value;
        if (status === a.status) { closeAppointment(); return; }
        var btn = $('cal-appt-save');
        btn.disabled = true;
        api('PATCH', '/api/appointments/' + a.id + '/status', { status: status }).then(function () {
            btn.disabled = false;
            closeAppointment();
            showMessage('Saved.');
            return loadMonth(state.year, state.month);
        }).catch(function (e) {
            btn.disabled = false;
            $('cal-appt-error').textContent = e.message;
        });
    }

    /** "09:00" + 30 -> "09:30" (wall-clock arithmetic for the end-time hint; wraps past midnight). */
    function addMinutes(hhmm, minutes) {
        var parts = hhmm.split(':');
        var total = (parseInt(parts[0], 10) * 60 + parseInt(parts[1], 10) + minutes) % (24 * 60);
        return String(Math.floor(total / 60)).padStart(2, '0') + ':' + String(total % 60).padStart(2, '0');
    }

    // ---------------------------------------------------------------- new appointment

    function openNewAppointment(prefillDateIso) {
        $('cal-new-error').textContent = '';
        clearLead();
        $('cal-new-date').value = prefillDateIso || state.drawerDate || todayIso();
        $('cal-new-time').value = '09:00';
        var tz = gridTimeZone();
        $('cal-new-tz-hint').textContent = tz ? (currentView() === 'mine' ? 'your time' : 'clinic time') + ' (' + tz + ')' : '';
        $('cal-new-duration').value = '30';
        $('cal-new-location').value = '';
        $('cal-new-notes').value = '';
        updateRange();
        loadProcedures();
        $('cal-new-overlay').hidden = false;
        $('cal-new-lead-search').focus();
    }

    /** "09:00 – 09:30" under the date/time/duration row. */
    function updateRange() {
        var time = $('cal-new-time').value;
        var minutes = parseInt($('cal-new-duration').value, 10);
        $('cal-new-range').textContent = time && minutes ? time + ' – ' + addMinutes(time, minutes) : '';
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
        if (!query || query.trim().length < 2) { box.textContent = ''; box.hidden = true; return; }
        api('GET', '/api/leads?take=8&search=' + encodeURIComponent(query.trim())).then(function (res) {
            box.textContent = '';
            box.hidden = false;
            var items = (res && res.items) || [];
            if (!items.length) { box.appendChild(el('div', { class: 'picker-empty', text: 'No matching patients.' })); return; }
            items.forEach(function (lead) {
                box.appendChild(el('button', { type: 'button', class: 'picker-option', role: 'option', onclick: function () { pickLead(lead); } }, [
                    el('span', { class: 'avatar-sm', text: initialOf(lead.fullName) }),
                    el('span', { class: 'picker-option-main' }, [
                        el('span', { class: 'picker-option-name', text: lead.fullName || 'Unnamed' }),
                        el('span', { class: 'picker-option-sub', text: [lead.phone, lead.procedureName].filter(Boolean).join(' · ') })
                    ])
                ]));
            });
        }).catch(function () { /* transient — staff can retype */ });
    }

    /** Picked patient shows as a chip (with × to change); their lead's procedure pre-fills the Procedure field. */
    function pickLead(lead) {
        var label = (lead.fullName || 'Unnamed') + (lead.phone ? ' · ' + lead.phone : '');
        state.selectedLead = { id: lead.id, label: label };
        $('cal-new-lead-id').value = lead.id;
        $('cal-new-lead-results').hidden = true;
        $('cal-new-lead-search').hidden = true;
        $('cal-new-lead-chip-avatar').textContent = initialOf(lead.fullName);
        $('cal-new-lead-chip-label').textContent = label;
        $('cal-new-lead-chip').hidden = false;
        if (lead.procedureId) {
            var sel = $('cal-new-procedure');
            var hasOption = Array.prototype.some.call(sel.options, function (o) { return o.value === lead.procedureId; });
            if (hasOption) sel.value = lead.procedureId;
        }
    }

    function clearLead() {
        state.selectedLead = null;
        $('cal-new-lead-id').value = '';
        $('cal-new-lead-search').value = '';
        $('cal-new-lead-search').hidden = false;
        $('cal-new-lead-chip').hidden = true;
        $('cal-new-lead-results').textContent = '';
        $('cal-new-lead-results').hidden = true;
    }

    function saveNewAppointment() {
        var err = $('cal-new-error');
        err.textContent = '';
        var leadId = $('cal-new-lead-id').value;
        var date = $('cal-new-date').value;
        var time = $('cal-new-time').value;
        if (!leadId) { err.textContent = 'Search for the patient and pick one from the list.'; return; }
        if (!date || !time) { err.textContent = 'Enter a date and time.'; return; }

        var timeZone = gridTimeZone() || SculptTime.viewerTimeZone || 'UTC';
        var startIso = SculptTime.zonedToUtcIso(date, time, timeZone);
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
        window.addEventListener('sf-time-view', function () { loadMonth(state.year, state.month); });

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
            var t = todayIso().split('-');
            loadMonth(parseInt(t[0], 10), parseInt(t[1], 10));
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
        $('cal-new-lead-clear').addEventListener('click', function () { clearLead(); $('cal-new-lead-search').focus(); });
        $('cal-new-time').addEventListener('input', updateRange);
        $('cal-new-duration').addEventListener('input', updateRange);

        $('cal-appt-close').addEventListener('click', closeAppointment);
        $('cal-appt-cancel').addEventListener('click', closeAppointment);
        $('cal-appt-overlay').addEventListener('click', function (e) { if (e.target === $('cal-appt-overlay')) closeAppointment(); });
        $('cal-appt-save').addEventListener('click', saveAppointmentStatus);

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (!$('cal-appt-overlay').hidden) closeAppointment();
            else if (!$('cal-new-overlay').hidden) closeNewAppointment();
            else if (!$('cal-drawer').hidden) closeDrawer();
        });
    }

    if ($('cal-grid')) init();
})();
