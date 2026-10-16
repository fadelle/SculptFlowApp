/* Bookings — month calendar. Reads/writes the existing /api/bookings, /api/contacts and /api/services APIs
   (the same bookings table used by AI bookings, staff edits and everything else — this page owns no data of
   its own). Every value from the server is written with textContent (never innerHTML), so a contact/service name
   can't inject markup. */
(function () {
    'use strict';

    var state = {
        year: 0, month: 0,           // the visible month (1-based month, matching the API)
        monthData: null,             // the last GET /api/bookings/calendar response (covers the grid AND the drawer)
        drawerDate: null,            // "yyyy-MM-dd" the day drawer is currently showing, or null
        contactSearchTimer: null,
        selectedContact: null           // { id, label } chosen in the New Booking form
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

    /** Past bookings still booked/confirmed have no recorded outcome — staff open each one and set attended / no-show / canceled. */
    function renderOutcomeNotice(count) {
        var box = $('cal-outcome');
        box.textContent = '';
        if (!count) return;
        box.appendChild(el('div', { class: 'alert alert-warning mb-3' }, [
            count + (count === 1 ? ' past booking needs an outcome' : ' past bookings need an outcome') +
            ' - open it and mark attended, no-show or canceled.'
        ]));
    }

    // ---------------------------------------------------------------- month grid

    function loadMonth(year, month) {
        state.year = year; state.month = month;
        $('cal-title').textContent = MONTH_NAMES[month - 1] + ' ' + year;
        return api('GET', '/api/bookings/calendar?year=' + year + '&month=' + month + '&view=' + currentView()).then(function (data) {
            state.monthData = data;
            renderOutcomeNotice(data.needsOutcomeCount);
            renderGrid(data);
            renderAgenda(data);
            renderTodayLine(data);
            // Keep the drawer's contents in sync if it's open and its date is still in the new month's data.
            if (state.drawerDate) renderDrawer(state.drawerDate);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** 'client' (default) or 'mine' — the page's "Client time | My time" switch (_TimeViewSwitch, local-time.js). */
    function currentView() {
        var sw = document.querySelector('.time-view-switch');
        return OmniTime.timeView(sw ? sw.getAttribute('data-client-tz') : null);
    }

    /** The timezone the server grouped the grid in (LocalDate/LocalTime) — the client's, or the viewer's on "My time".
        "Today", "now" and typed new-booking times use the same zone. Falls back to the browser's clock until the
        first month has loaded. */
    function gridTimeZone() {
        return (state.monthData && state.monthData.timezone) || null;
    }

    /** "yyyy-MM-dd" for today in the grid's timezone. */
    function todayIso() {
        var tz = gridTimeZone();
        if (tz) return OmniTime.todayIn(tz);
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
            // Chip shows just the time (bold) and a short name; the service is in the tooltip and the day drawer.
            var title = a.localTime + ' ' + (a.contactFullName || 'Unknown') + (a.serviceName ? ' - ' + a.serviceName : '') + ' (' + statusLabel(a.status) + ')';
            kids.push(el('div', { class: 'cal-chip status-' + a.status, title: title, onclick: function (e) { e.stopPropagation(); openBooking(a); } }, [
                el('b', { text: a.localTime }), ' ' + shortName(a.contactFullName)
            ]));
        });
        if (items.length > shown.length) {
            kids.push(el('div', { class: 'cal-more', text: '+' + (items.length - shown.length) + ' more' }));
        }
        kids.push(el('button', {
            type: 'button', class: 'cal-day-add', title: 'New booking on ' + dateIso, 'aria-label': 'New booking',
            onclick: function (e) { e.stopPropagation(); openNewBooking(dateIso); }
        }, ['+']));

        return el('div', { class: classes, onclick: function () { openDrawer(dateIso); } }, kids);
    }

    function shortName(fullName) {
        if (!fullName) return 'Unknown';
        var parts = fullName.trim().split(/\s+/);
        return parts.length < 2 ? parts[0] : parts[0] + ' ' + parts[1][0] + '.';
    }

    /** "Today: 3 bookings · next at 11:00 with Layla H." above the calendar. Only refreshed when the loaded
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
        box.appendChild(document.createTextNode(' ' + (todays.length === 0 ? 'no bookings'
            : todays.length + (todays.length === 1 ? ' booking' : ' bookings'))));
        if (next) {
            box.appendChild(document.createTextNode(' · next at '));
            box.appendChild(el('strong', { text: next.localTime }));
            box.appendChild(document.createTextNode(' with ' + shortName(next.contactFullName)));
        } else if (todays.length) {
            box.appendChild(document.createTextNode(' · none left for today'));
        }
        box.hidden = false;
    }

    /** Phone layout: the 7-column grid is too cramped, so site.css hides it and shows this day-by-day list of the
        visible month's bookings instead (same data, same links). */
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
            box.appendChild(el('div', { class: 'agenda-empty', text: 'No bookings this month.' }));
            return;
        }
        dates.forEach(function (dateIso) {
            var d = new Date(dateIso + 'T00:00:00');
            var label = d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
            box.appendChild(el('div', { class: 'agenda-day' + (dateIso < today ? ' past' : ''), text: (dateIso === today ? 'Today · ' : '') + label }));
            byDate[dateIso]
                .sort(function (a, b) { return a.localTime < b.localTime ? -1 : 1; })
                .forEach(function (a) {
                    box.appendChild(el('a', { class: 'agenda-row' + (dateIso < today ? ' past' : ''), href: '/dashboard/bookings/' + a.id, onclick: function (e) { e.preventDefault(); openBooking(a); } }, [
                        el('span', { class: 'cal-appt-time', text: a.localTime }),
                        el('div', { class: 'agenda-row-main' }, [
                            el('div', { class: 'cal-appt-name', text: a.contactFullName || 'Unknown contact' }),
                            el('div', { class: 'cal-appt-service', text: a.serviceName || 'No service on file' })
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
            body.appendChild(el('div', { class: 'text-subtle', style: 'padding:1rem 0.25rem', text: 'No bookings scheduled for this day.' }));
            return;
        }

        items.forEach(function (a) {
            body.appendChild(el('div', { class: 'cal-appt-row', onclick: function () { openBooking(a); } }, [
                el('div', { class: 'flex items-center gap-2', style: 'justify-content:space-between' }, [
                    el('span', { class: 'cal-appt-time', text: a.localTime }),
                    el('span', { class: 'badge ' + badgeClass(a.status), text: statusLabel(a.status) })
                ]),
                el('div', { class: 'cal-appt-name', text: a.contactFullName || 'Unknown contact' }),
                el('div', { class: 'cal-appt-service', text: a.serviceName || 'No service on file' })
            ]));
        });
    }

    var BADGE_CLASS = { booked: 'badge-amber', confirmed: 'badge-blue', attended: 'badge-green', canceled: 'badge-gray', no_show: 'badge-red', rescheduled: 'badge-purple' };
    var STATUS_LABEL = { booked: 'Booked', confirmed: 'Confirmed', attended: 'Attended', canceled: 'Canceled', no_show: 'No-show', rescheduled: 'Rescheduled' };
    function badgeClass(status) { return BADGE_CLASS[status] || 'badge-gray'; }
    function statusLabel(status) { return STATUS_LABEL[status] || status; }

    // ---------------------------------------------------------------- booking details popup

    var LOCATION_LABEL = { in_person: 'In person', video: 'Video call', phone: 'Phone' };
    function locationLabel(value) { return value ? (LOCATION_LABEL[value] || value) : '—'; }

    function initialOf(name) { return (name || '?').trim().charAt(0).toUpperCase() || '?'; }

    /** Opens the popup from a calendar item right away (name, time, status), then fills in location/notes from
        GET /api/bookings/{id}, which the calendar feed doesn't carry. */
    function openBooking(a) {
        state.openBooking = a;
        var d = new Date(a.localDate + 'T00:00:00');
        $('cal-appt-avatar').textContent = initialOf(a.contactFullName);
        $('cal-appt-name').textContent = a.contactFullName || 'Unknown contact';
        $('cal-appt-when').textContent = d.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' }) + ' · ' + a.localTime;
        $('cal-appt-service').textContent = a.serviceName || 'No service on file';
        $('cal-appt-location').textContent = '…';
        $('cal-appt-notes').textContent = '…';
        $('cal-appt-notes-row').hidden = false;
        $('cal-appt-status').value = a.status;
        $('cal-appt-error').textContent = '';
        $('cal-appt-full').href = '/dashboard/bookings/' + a.id;
        $('cal-appt-contact').href = '/dashboard/contacts/' + a.contactId;
        $('cal-appt-overlay').hidden = false;

        api('GET', '/api/bookings/' + a.id).then(function (full) {
            if (!state.openBooking || state.openBooking.id !== a.id) return; // another one was opened meanwhile
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

    function closeBooking() {
        state.openBooking = null;
        $('cal-appt-overlay').hidden = true;
    }

    function saveBookingStatus() {
        var a = state.openBooking;
        if (!a) return;
        var status = $('cal-appt-status').value;
        if (status === a.status) { closeBooking(); return; }
        var btn = $('cal-appt-save');
        btn.disabled = true;
        api('PATCH', '/api/bookings/' + a.id + '/status', { status: status }).then(function () {
            btn.disabled = false;
            closeBooking();
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

    // ---------------------------------------------------------------- new booking

    function openNewBooking(prefillDateIso) {
        $('cal-new-error').textContent = '';
        clearContact();
        $('cal-new-date').value = prefillDateIso || state.drawerDate || todayIso();
        $('cal-new-time').value = '09:00';
        var tz = gridTimeZone();
        $('cal-new-tz-hint').textContent = tz ? (currentView() === 'mine' ? 'your time' : 'client time') + ' (' + tz + ')' : '';
        $('cal-new-duration').value = '30';
        $('cal-new-location').value = '';
        $('cal-new-notes').value = '';
        updateRange();
        loadServices();
        $('cal-new-overlay').hidden = false;
        $('cal-new-contact-search').focus();
    }

    /** "09:00 – 09:30" under the date/time/duration row. */
    function updateRange() {
        var time = $('cal-new-time').value;
        var minutes = parseInt($('cal-new-duration').value, 10);
        $('cal-new-range').textContent = time && minutes ? time + ' – ' + addMinutes(time, minutes) : '';
    }

    function closeNewBooking() { $('cal-new-overlay').hidden = true; }

    function loadServices() {
        var sel = $('cal-new-service');
        api('GET', '/api/services').then(function (services) {
            sel.textContent = '';
            sel.appendChild(el('option', { value: '', text: 'No service' }));
            services.forEach(function (p) { sel.appendChild(el('option', { value: p.id, text: p.name })); });
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function searchContacts(query) {
        var box = $('cal-new-contact-results');
        if (!query || query.trim().length < 2) { box.textContent = ''; box.hidden = true; return; }
        api('GET', '/api/contacts?take=8&search=' + encodeURIComponent(query.trim())).then(function (res) {
            box.textContent = '';
            box.hidden = false;
            var items = (res && res.items) || [];
            if (!items.length) { box.appendChild(el('div', { class: 'picker-empty', text: 'No matching contacts.' })); return; }
            items.forEach(function (contact) {
                box.appendChild(el('button', { type: 'button', class: 'picker-option', role: 'option', onclick: function () { pickContact(contact); } }, [
                    el('span', { class: 'avatar-sm', text: initialOf(contact.fullName) }),
                    el('span', { class: 'picker-option-main' }, [
                        el('span', { class: 'picker-option-name', text: contact.fullName || 'Unnamed' }),
                        el('span', { class: 'picker-option-sub', text: [contact.phone, contact.serviceName].filter(Boolean).join(' · ') })
                    ])
                ]));
            });
        }).catch(function () { /* transient — staff can retype */ });
    }

    /** Picked contact shows as a chip (with × to change); their contact's service pre-fills the Service field. */
    function pickContact(contact) {
        var label = (contact.fullName || 'Unnamed') + (contact.phone ? ' · ' + contact.phone : '');
        state.selectedContact = { id: contact.id, label: label };
        $('cal-new-contact-id').value = contact.id;
        $('cal-new-contact-results').hidden = true;
        $('cal-new-contact-search').hidden = true;
        $('cal-new-contact-chip-avatar').textContent = initialOf(contact.fullName);
        $('cal-new-contact-chip-label').textContent = label;
        $('cal-new-contact-chip').hidden = false;
        if (contact.serviceId) {
            var sel = $('cal-new-service');
            var hasOption = Array.prototype.some.call(sel.options, function (o) { return o.value === contact.serviceId; });
            if (hasOption) sel.value = contact.serviceId;
        }
    }

    function clearContact() {
        state.selectedContact = null;
        $('cal-new-contact-id').value = '';
        $('cal-new-contact-search').value = '';
        $('cal-new-contact-search').hidden = false;
        $('cal-new-contact-chip').hidden = true;
        $('cal-new-contact-results').textContent = '';
        $('cal-new-contact-results').hidden = true;
    }

    function saveNewBooking() {
        var err = $('cal-new-error');
        err.textContent = '';
        var contactId = $('cal-new-contact-id').value;
        var date = $('cal-new-date').value;
        var time = $('cal-new-time').value;
        if (!contactId) { err.textContent = 'Search for the contact and pick one from the list.'; return; }
        if (!date || !time) { err.textContent = 'Enter a date and time.'; return; }

        var timeZone = gridTimeZone() || OmniTime.viewerTimeZone || 'UTC';
        var startIso = OmniTime.zonedToUtcIso(date, time, timeZone);
        var durationMin = parseInt($('cal-new-duration').value, 10);
        var endIso = durationMin > 0 ? new Date(new Date(startIso).getTime() + durationMin * 60000).toISOString() : null;

        var body = {
            clientId: '00000000-0000-0000-0000-000000000000', // ignored server-side — the client always comes from the logged-in user
            contactId: contactId,
            serviceId: $('cal-new-service').value || null,
            bookingType: 'standard',
            scheduledStart: startIso,
            scheduledEnd: endIso,
            locationType: $('cal-new-location').value || null,
            locationName: null,
            notes: $('cal-new-notes').value || null
        };

        var btn = $('cal-new-save');
        btn.disabled = true;
        api('POST', '/api/bookings', body).then(function () {
            btn.disabled = false;
            closeNewBooking();
            showMessage('Booking created.');
            return loadMonth(state.year, state.month);
        }).catch(function (e) {
            btn.disabled = false;
            err.textContent = e.message;
        });
    }

    // ---------------------------------------------------------------- wiring

    /** Live updates: the server sends "BookingChanged" (over the same client-scoped SignalR hub the Inbox uses) after a booking is
        created, rescheduled, canceled or has its status changed — by staff, the AI, or another tab. We just re-fetch the visible month from the
        API (the database stays the source of truth); the open drawer and New Booking form are left alone. */
    function connectLive() {
        if (!window.signalR) return;
        var timer = null;
        function refresh() {
            clearTimeout(timer);
            timer = setTimeout(function () { loadMonth(state.year, state.month); }, 300); // coalesce bursts
        }
        var connection = new signalR.HubConnectionBuilder().withUrl('/hubs/inbox').withAutomaticReconnect().build();
        connection.on('BookingChanged', refresh);
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
        $('cal-drawer-add').addEventListener('click', function () { openNewBooking(state.drawerDate); });

        $('cal-new').addEventListener('click', function () { openNewBooking(null); });
        $('cal-new-close').addEventListener('click', closeNewBooking);
        $('cal-new-cancel').addEventListener('click', closeNewBooking);
        $('cal-new-overlay').addEventListener('click', function (e) { if (e.target === $('cal-new-overlay')) closeNewBooking(); });
        $('cal-new-save').addEventListener('click', saveNewBooking);
        $('cal-new-contact-search').addEventListener('input', function (e) {
            state.selectedContact = null;
            $('cal-new-contact-id').value = '';
            clearTimeout(state.contactSearchTimer);
            var q = e.target.value;
            state.contactSearchTimer = setTimeout(function () { searchContacts(q); }, 250);
        });
        $('cal-new-contact-clear').addEventListener('click', function () { clearContact(); $('cal-new-contact-search').focus(); });
        $('cal-new-time').addEventListener('input', updateRange);
        $('cal-new-duration').addEventListener('input', updateRange);

        $('cal-appt-close').addEventListener('click', closeBooking);
        $('cal-appt-cancel').addEventListener('click', closeBooking);
        $('cal-appt-overlay').addEventListener('click', function (e) { if (e.target === $('cal-appt-overlay')) closeBooking(); });
        $('cal-appt-save').addEventListener('click', saveBookingStatus);

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (!$('cal-appt-overlay').hidden) closeBooking();
            else if (!$('cal-new-overlay').hidden) closeNewBooking();
            else if (!$('cal-drawer').hidden) closeDrawer();
        });
    }

    if ($('cal-grid')) init();
})();
