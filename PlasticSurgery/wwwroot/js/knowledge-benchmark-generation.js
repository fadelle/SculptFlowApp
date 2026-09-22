/* One benchmark generation's own page — /KnowledgeBase/Benchmark/Generations/{id}. Drives the same standalone
   /api/knowledge/benchmark API as the main Benchmark page, always scoped to this one generationId.
   Every value from the server is written with textContent (never innerHTML), so question/chunk text can't inject markup. */
(function () {
    'use strict';

    var API = '/api/knowledge/benchmark';
    var PAGE_SIZE = 25;
    var GEN_ID = document.getElementById('bmg-root') ? document.getElementById('bmg-root').dataset.id : null;

    var state = { caseView: 'all', caseSkip: 0, caseTotal: 0 };

    // ---------------------------------------------------------------- helpers (same behaviour as the main page's)

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
        return fetch(API + path, opts).then(function (res) {
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

    function pct(v) {
        if (v === null || v === undefined) return '—';
        var s = (v * 100).toFixed(1);
        return s.replace(/\.0$/, '') + '%';
    }

    function num(v, digits) {
        return v === null || v === undefined ? '—' : Number(v).toFixed(digits === undefined ? 2 : digits);
    }

    function fmtDate(iso) {
        if (!iso) return '—';
        var d = new Date(iso);
        return d.toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    }

    function showMessage(text, kind, detailsList) {
        var box = $('bmg-message');
        box.textContent = '';
        if (!text) return;
        var alert = el('div', { class: 'alert ' + (kind === 'error' ? 'alert-warning' : 'alert-info') + ' mb-3' }, [text]);
        if (detailsList && detailsList.length) {
            var ul = el('ul', { style: 'margin:.4rem 0 0 1.1rem;padding:0;font-size:.82rem' });
            detailsList.forEach(function (d) { ul.appendChild(el('li', { text: d })); });
            alert.appendChild(el('details', { style: 'margin-top:.3rem' }, [el('summary', { text: 'Details' }), ul]));
        }
        box.appendChild(alert);
    }

    function badgeFor(classification, rank) {
        switch (classification) {
            case 'EXACT_CHUNK_HIT': return el('span', { class: 'badge badge-green', text: 'Exact chunk' + (rank ? ' · #' + rank : '') });
            case 'DOCUMENT_ONLY_HIT': return el('span', { class: 'badge badge-amber', text: 'Document only' + (rank ? ' · #' + rank : '') });
            case 'MISS': return el('span', { class: 'badge badge-red', text: 'Miss' });
            case 'STALE_CASE': return el('span', { class: 'badge badge-gray', text: 'Stale' });
            case 'ERROR': return el('span', { class: 'badge badge-purple', text: 'Error' });
            default: return el('span', { class: 'text-subtle', text: '—' });
        }
    }

    function settingsLine(s) {
        if (!s) return '';
        var parts = [];
        if (s.chunkSizeTokens != null) parts.push('chunk ' + s.chunkSizeTokens + ' / overlap ' + s.chunkOverlapTokens + ' tokens');
        if (s.topK != null) parts.push('Top K ' + s.topK);
        if (s.minimumSimilarity != null) parts.push('min similarity ' + s.minimumSimilarity);
        if (s.embeddingModel) parts.push(s.embeddingModel + (s.vectorDimension ? ' (' + s.vectorDimension + 'd)' : ''));
        if (s.similarityMethod) parts.push(s.similarityMethod);
        return parts.join(' · ');
    }

    function statCard(label, value, hint) {
        return el('div', { class: 'stat-card' }, [
            el('div', { class: 'label', text: label }),
            el('div', { class: 'value', text: value }),
            hint ? el('div', { class: 'hint', text: hint }) : null
        ]);
    }

    // ---------------------------------------------------------------- summary (the generation itself)

    function loadSummary() {
        return api('GET', '/generations/' + GEN_ID).then(function (d) {
            renderSummary(d);
            return d;
        }).catch(function (e) {
            $('bmg-summary').textContent = '';
            showMessage(e.message, 'error');
        });
    }

    function renderSummary(d) {
        var g = d.summary;
        document.title = 'Generation ' + g.id.slice(0, 8) + '… — Knowledge Retrieval Benchmark';

        var box = $('bmg-summary');
        box.textContent = '';
        box.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.78rem;margin-bottom:.5rem', text: 'Generation ID: ' + g.id }));
        var badge = el('span', {
            class: 'badge ' + ({ completed: 'badge-green', pending: 'badge-teal', failed: 'badge-red' }[g.status] || 'badge-gray'),
            text: g.status === 'pending' ? 'Waiting for n8n…' : g.status === 'cancelled' ? 'Stopped' : g.status.charAt(0).toUpperCase() + g.status.slice(1)
        });
        box.appendChild(el('div', { class: 'flex items-center gap-2', style: 'flex-wrap:wrap' }, [
            badge,
            el('span', { text: 'Requested ' + fmtDate(g.requestedAt) + (g.completedAt ? ' · finished ' + fmtDate(g.completedAt) : '') }),
        ]));
        box.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.85rem;margin-top:.4rem', text:
            g.chunksSent + ' chunks sent · ' + g.questionsReturned + ' questions returned · ' + g.casesCreated + ' cases created · ' +
            g.rejectedCount + ' rejected' + (g.casesRemaining !== g.casesCreated ? ' · ' + g.casesRemaining + ' cases left (some deleted)' : '') }));

        if (g.errorMessage) box.appendChild(el('div', { class: 'bm-verdict bad', style: 'margin-top:.7rem', text: g.errorMessage }));
        if (g.status === 'pending') box.appendChild(el('div', { class: 'bm-verdict neutral', style: 'margin-top:.7rem', text: 'Still waiting for the question generator to call back. This page updates by itself.' }));

        if (g.casesRemaining > 0) {
            box.appendChild(el('div', { class: 'mt-2' }, [
                el('button', { type: 'button', class: 'btn btn-primary btn-sm', id: 'bmg-run', text: 'Run Benchmark for this generation', onclick: runThisGeneration })
            ]));
        }

        renderScores(g.latestScores);

        var rejBox = $('bmg-rejected');
        if (d.rejected.length) {
            rejBox.hidden = false;
            $('bmg-rejected-title').textContent = 'Rejected questions (' + g.rejectedCount + (g.rejectedCount > d.rejected.length ? ', first ' + d.rejected.length + ' shown' : '') + ')';
            var tb = $('bmg-rejected-body');
            tb.textContent = '';
            d.rejected.forEach(function (r) { tb.appendChild(el('tr', null, [el('td', { style: 'white-space:normal', text: r.question || '—' }), el('td', { style: 'white-space:normal', text: r.reason })])); });
        } else {
            rejBox.hidden = true;
        }

        $('bmg-chunks-count').textContent = String(d.sentChunks.length);
        var list = $('bmg-chunks-list');
        list.textContent = '';
        d.sentChunks.forEach(function (c) { list.appendChild(el('li', null, [c.documentTitle + ' ', el('span', { class: 'text-subtle', text: '· chunk ' + c.chunkId.slice(0, 8) + '…' })])); });

        if (d.rawResponse) {
            $('bmg-raw-wrap').hidden = false;
            $('bmg-raw').textContent = d.rawResponse;
        } else {
            $('bmg-raw-wrap').hidden = true;
        }
    }

    function renderScores(s) {
        var host = $('bmg-scores');
        host.textContent = '';
        if (!s) {
            host.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.85rem', text: 'Not run yet — click "Run Benchmark for this generation" above.' }));
            return;
        }
        host.appendChild(el('div', { class: 'bm-subtitle', text: 'Scores (latest completed run, ' + s.scoredCases + ' scored ' + fmtDate(s.runAt) + ')' }));
        host.appendChild(el('div', { class: 'stat-grid' }, [
            statCard('Chunk Top-1', pct(s.chunkTop1)), statCard('Chunk Top-3', pct(s.chunkTop3)),
            statCard('Chunk Top-5', pct(s.chunkTop5)), statCard('Chunk MRR', num(s.chunkMrr)),
            statCard('Doc Top-3', pct(s.documentTop3))
        ]));
    }

    function runThisGeneration() {
        var btn = $('bmg-run');
        if (btn) btn.disabled = true;
        showMessage('');
        api('POST', '/runs', { scope: 'generation', generationId: GEN_ID }).then(function () {
            showMessage('Running the benchmark for this generation…');
            setTimeout(refreshRunStatus, 1500);
        }).catch(function (e) {
            showMessage(e.message, 'error');
            if (btn) btn.disabled = false;
        });
    }

    /** Polls the clinic dashboard (shared with the main Benchmark page's own run) just to know when the run this
        generation kicked off has finished, then re-reads the summary/cases to show the fresh scores. */
    function refreshRunStatus() {
        api('GET', '').then(function (dash) {
            if (dash.runInProgress) {
                setTimeout(refreshRunStatus, 2000);
            } else {
                loadSummary();
                loadCases();
            }
        }).catch(function () { /* transient — user can just reload */ });
    }

    // ---------------------------------------------------------------- this generation's cases

    function loadCases() {
        var qs = '?view=' + encodeURIComponent(state.caseView) + '&generationId=' + encodeURIComponent(GEN_ID) + '&skip=' + state.caseSkip + '&take=' + PAGE_SIZE;
        return api('GET', '/cases' + qs).then(function (res) {
            state.caseTotal = res.totalCount;
            renderCases(res.items);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function renderCases(items) {
        var body = $('bmg-cases-body');
        body.textContent = '';
        if (!items.length) {
            body.appendChild(el('tr', { class: 'empty-row' }, [el('td', { colspan: '5', text: 'No cases match this filter.' })]));
        }

        items.forEach(function (c) {
            var last = c.lastResult;
            var qCell = el('td', { style: 'max-width:360px;white-space:normal' }, [
                el('a', { href: '#', class: 'bm-cell-clip', style: 'white-space:normal', title: c.question, text: c.question, onclick: function (e) { e.preventDefault(); openCase(c.id); } })
            ]);
            var expCell = el('td', { style: 'max-width:320px' }, [
                el('div', { class: 'bm-cell-clip', text: c.expectedDocumentTitle || '—', style: 'font-weight:600' }),
                el('div', { class: 'bm-cell-clip text-subtle', style: 'font-size:.75rem', title: c.expectedChunkPreview || '', text: c.expectedChunkPreview || '' })
            ]);
            var reviewCell = el('td', null, [el('span', { class: 'badge ' + (c.isReviewed ? 'badge-green' : 'badge-gray'), text: c.isReviewed ? 'Reviewed' : 'Not reviewed' })]);
            var lastCell = el('td', null, [
                c.isStale
                    ? el('span', { class: 'badge badge-gray', title: c.staleExplanation || '', text: 'Stale' })
                    : (last ? badgeFor(last.classification, last.expectedChunkRank) : el('span', { class: 'text-subtle', text: 'Not run' }))
            ]);
            var actions = el('td', null, [el('div', { class: 'flex items-center gap-2' }, [
                el('button', { type: 'button', class: 'btn btn-sm', text: 'Open', onclick: function () { openCase(c.id); } }),
                el('button', { type: 'button', class: 'btn btn-sm', text: c.isReviewed ? 'Unreview' : 'Mark reviewed', onclick: function () { toggleReviewed(c); } }),
                el('button', { type: 'button', class: 'btn btn-sm', text: 'Delete', onclick: function () { deleteCase(c); } })
            ])]);
            body.appendChild(el('tr', null, [qCell, expCell, reviewCell, lastCell, actions]));
        });

        var pager = $('bmg-cases-pager');
        pager.hidden = state.caseTotal <= PAGE_SIZE;
        var from = state.caseTotal === 0 ? 0 : state.caseSkip + 1;
        var to = Math.min(state.caseSkip + PAGE_SIZE, state.caseTotal);
        $('bmg-cases-range').textContent = from + '–' + to + ' of ' + state.caseTotal;
        $('bmg-cases-prev').disabled = state.caseSkip === 0;
        $('bmg-cases-next').disabled = state.caseSkip + PAGE_SIZE >= state.caseTotal;
    }

    function toggleReviewed(c) {
        api('POST', '/cases/' + c.id + '/review', { reviewed: !c.isReviewed })
            .then(function () { return loadCases(); })
            .catch(function (e) { showMessage(e.message, 'error'); });
    }

    function deleteCase(c) {
        if (!window.confirm('Delete this test case? Past run results keep their record of it.')) return;
        api('DELETE', '/cases/' + c.id)
            .then(function () { return Promise.all([loadCases(), loadSummary()]); })
            .catch(function (e) { showMessage(e.message, 'error'); });
    }

    // ---------------------------------------------------------------- overlay: case detail/edit + failed-case analysis

    function openOverlay(title, bodyNode) {
        $('bmg-modal-title').textContent = title;
        var body = $('bmg-modal-body');
        body.textContent = '';
        body.appendChild(bodyNode);
        $('bmg-overlay').hidden = false;
    }

    function closeOverlay() { $('bmg-overlay').hidden = true; }

    function openCase(caseId) {
        api('GET', '/cases/' + caseId).then(function (d) {
            var c = d.case;
            var wrap = el('div');

            if (c.isStale) {
                wrap.appendChild(el('div', { class: 'bm-verdict neutral' }, [
                    el('strong', { text: 'Stale — excluded from scoring. ' }),
                    c.staleExplanation || '',
                    ' Delete this case and generate a new one for the current text.'
                ]));
            }

            var questionBox = el('textarea', { class: 'text-input', rows: '2', maxlength: '500', id: 'bmg-edit-question' });
            questionBox.value = c.question;
            var status = el('div', { class: 'text-subtle', style: 'font-size:.78rem;min-height:1.1em' });

            function save(markReviewed) {
                status.textContent = 'Saving…';
                api('PUT', '/cases/' + c.id, { question: questionBox.value })
                    .then(function () { return markReviewed ? api('POST', '/cases/' + c.id + '/review', { reviewed: true }) : null; })
                    .then(function () { status.textContent = 'Saved.'; return loadCases(); })
                    .catch(function (e) { status.textContent = e.message; });
            }

            wrap.appendChild(el('div', { class: 'bm-subtitle', text: 'Patient question' }));
            wrap.appendChild(questionBox);
            wrap.appendChild(el('div', { class: 'flex items-center gap-2', style: 'margin:.5rem 0 .2rem' }, [
                el('button', { type: 'button', class: 'btn btn-primary btn-sm', text: 'Save', onclick: function () { save(false); } }),
                el('button', { type: 'button', class: 'btn btn-sm', text: 'Save & mark reviewed', onclick: function () { save(true); } }),
                el('span', { class: 'badge ' + (c.isReviewed ? 'badge-green' : 'badge-gray'), text: c.isReviewed ? 'Reviewed' : 'Not reviewed' })
            ]));
            wrap.appendChild(status);

            wrap.appendChild(el('div', { class: 'bm-subtitle', style: 'margin-top:1rem', text: 'Expected document' }));
            wrap.appendChild(el('div', { text: c.expectedDocumentTitle || '—', style: 'font-weight:600' }));
            wrap.appendChild(el('div', { class: 'bm-subtitle', style: 'margin-top:.8rem', text: d.expectedChunkContent ? 'Expected chunk (source text)' : 'Expected chunk (saved excerpt — the chunk no longer exists)' }));
            wrap.appendChild(el('div', { class: 'bm-block', text: d.expectedChunkContent || c.expectedChunkPreview || '' }));

            if (d.latestResult) {
                wrap.appendChild(el('div', { class: 'bm-subtitle', style: 'margin-top:1.1rem', text: 'Latest result — ' + fmtDate(d.latestResult.result.createdAt) }));
                wrap.appendChild(analysisNode(d.latestResult));
            } else {
                wrap.appendChild(el('div', { class: 'text-subtle', style: 'margin-top:1rem;font-size:.85rem', text: 'This case has not been part of a run yet.' }));
            }
            openOverlay('Test case', wrap);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** The failed-case analysis: verdict, the settings in force, and the ranked chunks retrieval actually returned.
        Identical to the main Benchmark page's — kept in sync by hand since each page is a self-contained script. */
    function analysisNode(detail) {
        var r = detail.result;
        var s = detail.runSettings || {};
        var wrap = el('div');

        var verdict, cls;
        if (r.classification === 'EXACT_CHUNK_HIT') {
            cls = 'ok';
            verdict = 'Exact chunk found at rank ' + r.expectedChunkRank + (r.expectedChunkRank === 1 ? ' — retrieved first.' : '.');
        } else if (r.classification === 'DOCUMENT_ONLY_HIT') {
            cls = 'partial';
            verdict = 'Exact chunk not found — but the correct document was found at rank ' + r.expectedDocumentBestRank +
                '. Retrieval understood the topic but did not return the specific passage. This can point to chunk size, overlap, ranking or how the content is structured.';
        } else if (r.classification === 'MISS') {
            cls = 'bad';
            verdict = 'Miss — neither the expected chunk nor its document was returned.';
            if (r.expectedBelowThreshold) {
                verdict += ' The expected chunk did rank in the top ' + (s.topK || '') + ' with similarity ' + num(r.expectedChunkScore, 3) +
                    ', but that is below the minimum similarity (' + (s.minimumSimilarity != null ? s.minimumSimilarity : '?') + '), so search dropped it.';
            }
        } else if (r.classification === 'STALE_CASE') {
            cls = 'neutral';
            verdict = 'Stale — the expected chunk changed or no longer exists, so this case was not scored.';
        } else {
            cls = 'neutral';
            verdict = 'Retrieval errored, so this case was not scored: ' + (r.errorMessage || 'unknown error');
        }
        wrap.appendChild(el('div', { class: 'bm-verdict ' + cls, text: verdict }));

        if (r.classification !== 'STALE_CASE') {
            wrap.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.78rem;margin-bottom:.6rem',
                text: 'Run settings: ' + settingsLine(s) + (r.latencyMs != null ? ' · ' + r.latencyMs + ' ms' : '') }));
        }

        wrap.appendChild(el('div', { class: 'bm-subtitle', text: 'Expected' }));
        wrap.appendChild(el('div', { style: 'font-weight:600', text: r.expectedDocumentTitle || '—' }));
        wrap.appendChild(el('div', { class: 'bm-block', style: 'margin-top:.3rem', text: detail.expectedChunkContent || r.expectedChunkPreview || '' }));

        var rows = detail.retrieved || [];
        wrap.appendChild(el('div', { class: 'bm-subtitle', style: 'margin-top:1rem',
            text: 'Retrieved results (' + rows.filter(function (x) { return x.passedThreshold; }).length + ' returned' + (rows.some(function (x) { return !x.passedThreshold; }) ? ', ' + rows.filter(function (x) { return !x.passedThreshold; }).length + ' below the minimum similarity' : '') + ')' }));
        if (!rows.length) {
            wrap.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.85rem', text: r.classification === 'STALE_CASE' || r.classification === 'ERROR' ? 'Nothing was retrieved.' : 'Search returned no results.' }));
            return wrap;
        }

        var tbody = el('tbody');
        rows.forEach(function (x) {
            var tags = [];
            if (x.isExpectedChunk) tags.push(el('span', { class: 'badge badge-green', text: 'Expected chunk' }));
            else if (x.isExpectedDocument) tags.push(el('span', { class: 'badge badge-amber', text: 'Expected document' }));
            if (!x.passedThreshold) tags.push(el('span', { class: 'badge badge-gray', text: 'Below min similarity — dropped' }));
            var rowClass = x.isExpectedChunk ? 'expected-chunk' : (x.isExpectedDocument ? 'expected-doc' : '');
            if (!x.passedThreshold) rowClass += ' dropped';
            tbody.appendChild(el('tr', { class: rowClass }, [
                el('td', { text: '#' + x.rank }),
                el('td', null, [el('div', { style: 'font-weight:600', text: x.title }), el('div', { class: 'flex gap-2', style: 'gap:.3rem;flex-wrap:wrap;margin-top:.2rem' }, tags)]),
                el('td', { text: num(x.score, 3) }),
                el('td', { text: x.preview })
            ]));
        });
        wrap.appendChild(el('div', { class: 'table-card table-scroll' }, [el('table', { class: 'data-table bm-retrieved' }, [
            el('thead', null, [el('tr', null, [el('th', { text: 'Rank' }), el('th', { text: 'Document' }), el('th', { text: 'Score' }), el('th', { text: 'Chunk preview' })])]),
            tbody
        ])]));
        return wrap;
    }

    // ---------------------------------------------------------------- wiring

    function init() {
        $('bmg-case-view').addEventListener('change', function (e) { state.caseView = e.target.value; state.caseSkip = 0; loadCases(); });
        $('bmg-cases-prev').addEventListener('click', function () { state.caseSkip = Math.max(0, state.caseSkip - PAGE_SIZE); loadCases(); });
        $('bmg-cases-next').addEventListener('click', function () { state.caseSkip += PAGE_SIZE; loadCases(); });
        $('bmg-modal-close').addEventListener('click', closeOverlay);
        $('bmg-overlay').addEventListener('click', function (e) { if (e.target === $('bmg-overlay')) closeOverlay(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeOverlay(); });

        loadSummary();
        loadCases();
    }

    if (GEN_ID) init();
})();
