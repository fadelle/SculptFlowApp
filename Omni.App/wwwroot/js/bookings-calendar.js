/* Bookings — month calendar. Reads/writes the existing /api/bookings, /api/contacts and /api/services APIs
   (the same bookings table used by AI bookings, member edits and everything else — this page owns no data of
   its own). Every value from the server is written with textContent (never innerHTML), so a contact/service name
   can't inject markup. */
(function () {
    'use strict';

    var state = {
        year: 0, month: 0,           // the visible month (1-based month, matching the API)
        monthData: null,             // the last GET /api/bookings/calendar response (covers the grid AND the drawer)
        drawerDate: null,            // "yyyy-MM-dd" the day drawer is currently showing, or null
        contactSearchTimer: null,
        selectedContact: null,          // { id, label } chosen in the New Booking form
        editingId: null                 // the booking the dialog is editing (QA t5-ops-04), null = a new booking
    };

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
                    var msg = (data && (data.error || data.title)) || omniText.t('common.errors.requestFailed', [res.status]);
                    var error = new Error(msg);
                    error.code = data && data.code; // e.g. "booking_rules": a rule the member may book past (QA t5-ops-01)
                    throw error;
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

    /** Past bookings still booked/confirmed have no recorded outcome — members open each one and set attended / no-show / canceled. */
    function renderOutcomeNotice(count) {
        var box = $('cal-outcome');
        box.textContent = '';
        if (!count) return;
        box.appendChild(el('div', { class: 'alert alert-warning mb-3' }, [
            omniText.plural('dashboard.attention.outcomes', count) +
            ' ' + omniText.t('bookings.outcome.text')
        ]));
    }

    // ---------------------------------------------------------------- month grid

    function loadMonth(year, month) {
        state.year = year; state.month = month;
        $('cal-title').textContent = new Date(year, month - 1, 1).toLocaleDateString(window.omniText.locale, { month: 'long', year: 'numeric' });
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
            var title = a.localTime + ' ' + (a.contactFullName || omniText.t('bookings.unknownContact')) + (a.serviceName ? ' - ' + a.serviceName : '') + ' (' + statusLabel(a.status) + ')';
            kids.push(el('div', { class: 'cal-chip status-' + a.status, title: title, onclick: function (e) { e.stopPropagation(); openBooking(a); } }, [
                el('b', { text: a.localTime }), ' ', el('bdi', { text: shortName(a.contactFullName) })
            ]));
        });
        if (items.length > shown.length) {
            kids.push(el('div', { class: 'cal-more', text: omniText.t('bookings.calendar.more', [items.length - shown.length]) }));
        }
        kids.push(el('button', {
            type: 'button', class: 'cal-day-add', title: omniText.t('bookings.calendar.newOn', [dateIso]), 'aria-label': omniText.t('bookings.new.title'),
            onclick: function (e) { e.stopPropagation(); openNewBooking(dateIso); }
        }, ['+']));

        return el('div', { class: classes, onclick: function () { openDrawer(dateIso); } }, kids);
    }

    function shortName(fullName) {
        if (!fullName) return omniText.t('bookings.unknownContact');
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
        box.appendChild(el('strong', { text: omniText.t('bookings.today.label') }));
        box.appendChild(document.createTextNode(' ' + (todays.length === 0 ? omniText.t('bookings.today.none')
            : omniText.plural('bookings.today', todays.length))));
        if (next) {
            box.appendChild(document.createTextNode(' · ' + omniText.t('bookings.today.next', [next.localTime, shortName(next.contactFullName)])));
        } else if (todays.length) {
            box.appendChild(document.createTextNode(' · ' + omniText.t('bookings.today.noneLeft')));
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
            box.appendChild(el('div', { class: 'agenda-empty', text: omniText.t('bookings.agenda.empty') }));
            return;
        }
        dates.forEach(function (dateIso) {
            var d = new Date(dateIso + 'T00:00:00');
            var label = d.toLocaleDateString(window.omniText.locale, { weekday: 'short', month: 'short', day: 'numeric' });
            box.appendChild(el('div', { class: 'agenda-day' + (dateIso < today ? ' past' : ''), text: dateIso === today ? omniText.t('bookings.agenda.today', [label]) : label }));
            byDate[dateIso]
                .sort(function (a, b) { return a.localTime < b.localTime ? -1 : 1; })
                .forEach(function (a) {
                    box.appendChild(el('a', { class: 'agenda-row' + (dateIso < today ? ' past' : ''), href: '/dashboard/bookings/' + a.id, onclick: function (e) { e.preventDefault(); openBooking(a); } }, [
                        el('span', { class: 'cal-appt-time', text: a.localTime }),
                        el('div', { class: 'agenda-row-main' }, [
                            el('div', { class: 'cal-appt-name', dir: 'auto', text: a.contactFullName || omniText.t('bookings.unknownContact') }),
                            el('div', { class: 'cal-appt-service', text: a.serviceName || omniText.t('contactDetail.noService') })
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
        $('cal-drawer-title').textContent = d.toLocaleDateString(window.omniText.locale, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });

        var body = $('cal-drawer-body');
        body.textContent = '';
        var items = ((state.monthData && state.monthData.items) || []).filter(function (a) { return a.localDate === dateIso; });

        if (!items.length) {
            body.appendChild(el('div', { class: 'text-subtle', style: 'padding:1rem 0.25rem', text: omniText.t('bookings.drawer.empty') }));
            return;
        }

        items.forEach(function (a) {
            body.appendChild(el('div', { class: 'cal-appt-row', onclick: function () { openBooking(a); } }, [
                el('div', { class: 'flex items-center gap-2', style: 'justify-content:space-between' }, [
                    el('span', { class: 'cal-appt-time', text: a.localTime }),
                    el('span', { class: 'badge ' + badgeClass(a.status), text: statusLabel(a.status) })
                ]),
                el('div', { class: 'cal-appt-name', dir: 'auto', text: a.contactFullName || omniText.t('bookings.unknownContact') }),
                el('div', { class: 'cal-appt-service', text: a.serviceName || omniText.t('contactDetail.noService') })
            ]));
        });
    }

    var BADGE_CLASS = { booked: 'badge-amber', confirmed: 'badge-blue', attended: 'badge-green', canceled: 'badge-gray', no_show: 'badge-red', rescheduled: 'badge-purple' };
    var STATUS_LABEL = {
        booked: omniText.t('values.booked'), confirmed: omniText.t('values.confirmed'), attended: omniText.t('values.attended'),
        canceled: omniText.t('values.canceled'), no_show: omniText.t('values.noShow'), rescheduled: omniText.t('values.rescheduled')
    };
    function badgeClass(status) { return BADGE_CLASS[status] || 'badge-gray'; }
    function statusLabel(status) { return STATUS_LABEL[status] || status; }

    // ---------------------------------------------------------------- booking details popup

    var LOCATION_LABEL = { in_person: omniText.t('bookings.location.inPerson'), video: omniText.t('bookings.location.video'), phone: omniText.t('bookings.location.phone') };
    function locationLabel(value) { return value ? (LOCATION_LABEL[value] || value) : '—'; }

    function initialOf(name) { return (name || '?').trim().charAt(0).toUpperCase() || '?'; }

    /** Opens the popup from a calendar item right away (name, time, status), then fills in location/notes from
        GET /api/bookings/{id}, which the calendar feed doesn't carry. */
    function openBooking(a) {
        state.openBooking = a;
        var d = new Date(a.localDate + 'T00:00:00');
        $('cal-appt-avatar').textContent = initialOf(a.contactFullName);
        $('cal-appt-name').textContent = a.contactFullName || omniText.t('bookings.unknownContact');
        $('cal-appt-when').textContent = d.toLocaleDateString(window.omniText.locale, { weekday: 'short', month: 'short', day: 'numeric' }) + ' · ' + a.localTime;
        $('cal-appt-service').textContent = a.serviceName || omniText.t('contactDetail.noService');
        $('cal-appt-location').textContent = '…';
        $('cal-appt-notes').textContent = '…';
        $('cal-appt-notes-row').hidden = false;
        offerStatuses($('cal-appt-status'), a.status);
        $('cal-appt-edit').hidden = !canEdit(a.status);
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

    /** The statuses a member can pick from each status, as BookingStatus.NextFrom on the server (QA t5-ops-03): canceled and
        rescheduled are final, an outcome can only be corrected, and "rescheduled" is never picked by hand. */
    var NEXT_STATUS = {
        booked: ['confirmed', 'attended', 'no_show', 'canceled'],
        confirmed: ['booked', 'attended', 'no_show', 'canceled'],
        attended: ['no_show'],
        no_show: ['attended']
    };

    function offerStatuses(select, current) {
        var next = NEXT_STATUS[current] || [];
        Array.prototype.forEach.call(select.options, function (o) {
            var offered = o.value === current || next.indexOf(o.value) >= 0;
            o.hidden = !offered;
            o.disabled = !offered;
        });
        select.value = current;
        select.disabled = next.length === 0;
    }

    /** Only a booked or confirmed booking can be edited (the server refuses the rest). */
    function canEdit(status) { return status === 'booked' || status === 'confirmed'; }

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
            showMessage(omniText.t('bookingDetail.saved'));
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
        setDialogMode(null);
        $('cal-new-error').textContent = '';
        clearContact();
        $('cal-new-date').value = prefillDateIso || state.drawerDate || todayIso();
        $('cal-new-time').value = '09:00';
        var tz = gridTimeZone();
        $('cal-new-tz-hint').textContent = tz ? (currentView() === 'mine' ? omniText.t('bookings.new.tzMine', [tz]) : omniText.t('bookings.new.tzClient', [tz])) : '';
        $('cal-new-duration').value = '30';
        $('cal-new-location').value = '';
        $('cal-new-notes').value = '';
        updateRange();
        loadServices();
        $('cal-new-overlay').hidden = false;
        $('cal-new-contact-search').focus();
    }

    /** Switches the dialog between a new booking and editing one: title, button label, and whether the contact can change. */
    function setDialogMode(editingId) {
        state.editingId = editingId;
        var editing = !!editingId;
        $('cal-new-title').textContent = $('cal-new-title').getAttribute(editing ? 'data-title-edit' : 'data-title-new');
        $('cal-new-save').textContent = $('cal-new-save').getAttribute(editing ? 'data-label-edit' : 'data-label-new');
        $('cal-new-contact-clear').hidden = editing;
        $('cal-new-contact-add-open').hidden = editing;
        closeAddContact();
    }

    /** "2026-11-04T07:00:00Z" seen in an IANA zone -> { date: "2026-11-04", time: "09:00" }. */
    function zonedParts(iso, timeZone) {
        var parts = new Intl.DateTimeFormat('en-CA', {
            timeZone: timeZone || 'UTC', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
        }).formatToParts(new Date(iso));
        var get = function (t) { return parts.find(function (p) { return p.type === t; }).value; };
        return { date: get('year') + '-' + get('month') + '-' + get('day'), time: get('hour') + ':' + get('minute') };
    }

    /** Opens the dialog on an existing booking (the details popup's "Edit", or ?edit={id} from the booking's own page):
        its time in the calendar's timezone, duration, service, place and notes. The contact stays. */
    function openEditBooking(id) {
        return api('GET', '/api/bookings/' + id).then(function (full) {
            if (!canEdit(full.status)) { showMessage(omniText.t('bookings.edit.notEditable'), 'error'); return; }
            setDialogMode(full.id);
            $('cal-new-error').textContent = '';
            clearContact();
            pickContact({ id: full.contactId, fullName: full.contactFullName, phone: null, serviceId: null });
            var timeZone = gridTimeZone() || OmniTime.viewerTimeZone || 'UTC';
            var when = zonedParts(full.scheduledStart, timeZone);
            $('cal-new-date').value = when.date;
            $('cal-new-time').value = when.time;
            $('cal-new-tz-hint').textContent = gridTimeZone() ? (currentView() === 'mine' ? omniText.t('bookings.new.tzMine', [timeZone]) : omniText.t('bookings.new.tzClient', [timeZone])) : '';
            var minutes = full.scheduledEnd ? Math.round((new Date(full.scheduledEnd) - new Date(full.scheduledStart)) / 60000) : 0;
            $('cal-new-duration').value = minutes > 0 ? String(minutes) : '';
            var location = $('cal-new-location');
            if (full.locationType && !Array.prototype.some.call(location.options, function (o) { return o.value === full.locationType; })) {
                location.appendChild(el('option', { value: full.locationType, text: full.locationType })); // a place another tool set
            }
            location.value = full.locationType || '';
            $('cal-new-notes').value = full.notes || '';
            updateRange();
            loadServices(full.serviceId, full.serviceName);
            $('cal-new-overlay').hidden = false;
            $('cal-new-time').focus();
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** "09:00 – 09:30" under the date/time/duration row. */
    function updateRange() {
        var time = $('cal-new-time').value;
        var minutes = parseInt($('cal-new-duration').value, 10);
        $('cal-new-range').textContent = time && minutes ? time + ' – ' + addMinutes(time, minutes) : '';
    }

    function closeNewBooking() { $('cal-new-overlay').hidden = true; }

    function loadServices(selectedId, selectedName) {
        var sel = $('cal-new-service');
        api('GET', '/api/services').then(function (services) {
            sel.textContent = '';
            sel.appendChild(el('option', { value: '', text: omniText.t('bookings.new.noService') }));
            services.forEach(function (p) {
                var option = el('option', { value: p.id, text: p.name });
                if (p.bookingDuration > 0) option.setAttribute('data-duration', String(p.bookingDuration));
                sel.appendChild(option);
            });
            if (selectedId && !services.some(function (p) { return p.id === selectedId; })) {
                sel.appendChild(el('option', { value: selectedId, text: selectedName || omniText.t('contactDetail.noService') }));
            }
            sel.value = selectedId || '';
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function searchContacts(query) {
        var box = $('cal-new-contact-results');
        if (!query || query.trim().length < 2) { box.textContent = ''; box.hidden = true; return; }
        api('GET', '/api/contacts?take=8&search=' + encodeURIComponent(query.trim())).then(function (res) {
            box.textContent = '';
            box.hidden = false;
            var items = (res && res.items) || [];
            // The list covers the "+ New contact" link under the search, so it carries the same choice last (QA t5-ops-04).
            var addOption = el('button', { type: 'button', class: 'picker-option picker-option--add', id: 'cal-new-contact-add-option', onclick: openAddContact }, [
                el('span', { class: 'au-icon', 'aria-hidden': 'true', text: 'person_add' }),
                el('span', { class: 'picker-option-name', text: omniText.t('bookings.new.addContact') })
            ]);
            if (!items.length) {
                box.appendChild(el('div', { class: 'picker-empty', text: omniText.t('bookings.new.noMatches') }));
                box.appendChild(addOption);
                return;
            }
            items.forEach(function (contact) {
                box.appendChild(el('button', { type: 'button', class: 'picker-option', role: 'option', onclick: function () { pickContact(contact); } }, [
                    el('span', { class: 'avatar-sm', text: initialOf(contact.fullName) }),
                    el('span', { class: 'picker-option-main' }, [
                        el('span', { class: 'picker-option-name', text: contact.fullName || omniText.t('bookings.unnamed') }),
                        el('span', { class: 'picker-option-sub', text: [contact.phone, contact.serviceName].filter(Boolean).join(' · ') })
                    ])
                ]));
            });
            box.appendChild(addOption);
        }).catch(function () { /* transient — members can retype */ });
    }

    /** Picked contact shows as a chip (with × to change); their contact's service pre-fills the Service field. */
    function pickContact(contact) {
        var label = (contact.fullName || omniText.t('bookings.unnamed')) + (contact.phone ? ' · ' + contact.phone : '');
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

    /** Opens the inline "+ New contact" fields, filled from what was typed in the search (a number goes to the phone). */
    function openAddContact() {
        var typed = $('cal-new-contact-search').value.trim();
        var looksLikePhone = /^[+\d][\d\s().-]*$/.test(typed) && typed.replace(/\D/g, '').length >= 3;
        $('cal-new-contact-name').value = looksLikePhone ? '' : typed;
        $('cal-new-contact-phone').value = looksLikePhone ? typed : '';
        $('cal-new-contact-add-error').textContent = '';
        $('cal-new-contact-results').hidden = true;
        $('cal-new-contact-add').hidden = false;
        $('cal-new-contact-add-open').hidden = true;
        $(looksLikePhone || !typed ? 'cal-new-contact-name' : 'cal-new-contact-phone').focus();
    }

    function closeAddContact() {
        $('cal-new-contact-add').hidden = true;
        $('cal-new-contact-add-open').hidden = !!state.editingId;
    }

    function saveAddContact() {
        var err = $('cal-new-contact-add-error');
        err.textContent = '';
        var name = $('cal-new-contact-name').value.trim();
        var phone = $('cal-new-contact-phone').value.trim();
        if (!name && !phone) { err.textContent = omniText.t('contacts.errors.nameOrPhoneRequired'); return; }
        var btn = $('cal-new-contact-add-save');
        btn.disabled = true;
        api('POST', '/api/contacts/manual', { fullName: name || null, phone: phone || null }).then(function (contact) {
            btn.disabled = false;
            closeAddContact();
            pickContact(contact);
        }).catch(function (e) {
            btn.disabled = false;
            err.textContent = e.message;
        });
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
        if (!contactId && !state.editingId) { err.textContent = omniText.t('bookings.new.pickContact'); return; }
        if (!date || !time) { err.textContent = omniText.t('bookings.new.dateTimeRequired'); return; }

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

        var editingId = state.editingId;
        function send(overrideRules) {
            return editingId
                ? api('PATCH', '/api/bookings/' + editingId, {
                    scheduledStart: startIso, scheduledEnd: endIso, serviceId: body.serviceId, locationType: body.locationType, notes: body.notes,
                    overrideRules: overrideRules
                })
                : api('POST', '/api/bookings', Object.assign({}, body, { overrideRules: overrideRules }));
        }

        var btn = $('cal-new-save');
        btn.disabled = true;
        send(false).catch(function (e) {
            // Outside opening hours, in the past, inside the notice or beyond the horizon: the member may book it anyway.
            // An overlap with another booking is never allowed (it comes back as a plain error).
            if (e.code !== 'booking_rules') throw e;
            return askBookAnyway(e.message).then(function (yes) {
                if (!yes) { var stop = new Error(''); stop.cancelled = true; throw stop; }
                return send(true);
            });
        }).then(function () {
            btn.disabled = false;
            closeNewBooking();
            showMessage(editingId ? omniText.t('bookings.edit.saved') : omniText.t('bookings.new.created'));
            return loadMonth(state.year, state.month);
        }).catch(function (e) {
            btn.disabled = false;
            if (!e.cancelled) err.textContent = e.message;
        });
    }

    /** "You are closed at that time. … Book anyway?" (the Aurora kit's dialog, or the browser's). Resolves true to book. */
    function askBookAnyway(reason) {
        var message = omniText.t('bookings.rules.confirm', [reason]);
        if (window.Aurora && window.Aurora.confirm) {
            return window.Aurora.confirm({ title: omniText.t('bookings.rules.title'), message: message, confirmLabel: omniText.t('bookings.rules.bookAnyway'),
                cancelLabel: omniText.t('common.cancel') });
        }
        return Promise.resolve(window.confirm(message));
    }

    // ---------------------------------------------------------------- wiring

    /** Live updates: the server sends "BookingChanged" (over the same client-scoped SignalR hub the Inbox uses) after a booking is
        created, rescheduled, canceled or has its status changed — by members, the AI, or another tab. We just re-fetch the visible month from the
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
        $('cal-new-contact-add-open').addEventListener('click', openAddContact);
        $('cal-new-contact-add-cancel').addEventListener('click', closeAddContact);
        $('cal-new-contact-add-save').addEventListener('click', saveAddContact);
        ['cal-new-contact-name', 'cal-new-contact-phone'].forEach(function (id) {
            $(id).addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); saveAddContact(); } });
        });
        $('cal-new-time').addEventListener('input', updateRange);
        $('cal-new-duration').addEventListener('input', updateRange);
        // Choosing a service takes its length (QA t5-ops-19: the dialog kept 30 minutes and stored 14:00-14:30 for a 60-minute service).
        $('cal-new-service').addEventListener('change', function () {
            var option = this.options[this.selectedIndex];
            var minutes = option && option.getAttribute('data-duration');
            if (minutes) { $('cal-new-duration').value = minutes; updateRange(); }
        });

        $('cal-appt-close').addEventListener('click', closeBooking);
        $('cal-appt-cancel').addEventListener('click', closeBooking);
        $('cal-appt-overlay').addEventListener('click', function (e) { if (e.target === $('cal-appt-overlay')) closeBooking(); });
        $('cal-appt-save').addEventListener('click', saveBookingStatus);
        $('cal-appt-edit').addEventListener('click', function () {
            var a = state.openBooking;
            if (!a) return;
            closeBooking();
            openEditBooking(a.id);
        });

        // The booking page's "Edit" links here with ?edit={id}; drop it from the address so a reload doesn't reopen the dialog.
        var editId = new URLSearchParams(window.location.search).get('edit');
        if (editId) {
            history.replaceState(null, '', window.location.pathname);
            openEditBooking(editId);
        }

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (!$('cal-appt-overlay').hidden) closeBooking();
            else if (!$('cal-new-overlay').hidden) closeNewBooking();
            else if (!$('cal-drawer').hidden) closeDrawer();
        });
    }

    if ($('cal-grid')) init();
})();
