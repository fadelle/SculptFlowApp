/* Knowledge Retrieval Benchmark page — drives the standalone /api/knowledge/benchmark API.
   Every value from the server is written with textContent (never innerHTML), so question/chunk text can't inject markup. */
(function () {
    'use strict';

    var API = '/api/knowledge/benchmark';
    var PAGE_SIZE = 25;

    var state = {
        dashboard: null,
        caseView: 'manual',    // this page only ever shows MANUAL cases — generated cases live on their own generation's page
        caseSkip: 0,
        caseTotal: 0,
        runId: null,           // the run whose results are shown
        resultFilter: '',
        wasRunning: false,
        genWasRunning: false,
        pendingGenId: null,     // the generation "Stop generating" cancels
        pollTimer: null
    };

    // ---------------------------------------------------------------- helpers

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
        var box = $('bm-message');
        box.textContent = '';
        if (!text) return;
        var kids = [text];
        var alert = el('div', { class: 'alert ' + (kind === 'error' ? 'alert-warning' : 'alert-info') + ' mb-3' }, kids);
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

    // ---------------------------------------------------------------- dashboard

    function loadDashboard() {
        return api('GET', '').then(function (d) {
            state.dashboard = d;
            renderDashboard(d);
            return d;
        });
    }

    function renderDashboard(d) {
        $('bm-counts').textContent = '';
        $('bm-counts').appendChild(el('span', null, [
            el('strong', { text: String(d.totalCases) }), ' cases · ',
            el('strong', { text: String(d.reviewedCases) }), ' reviewed · ',
            el('strong', { text: String(d.staleCases) }), ' stale',
            el('span', { class: 'text-subtle', text: '  (' + d.generatedCases + ' generated, ' + d.manualCases + ' manual)' })
        ]));

        var running = d.runInProgress;
        var generating = d.generationInProgress;
        $('bm-run').disabled = running || d.totalCases - d.staleCases <= 0;
        $('bm-generate').disabled = running || generating || !d.generatorConfigured;
        var note = $('bm-generate-note');
        if (!d.generatorConfigured) {
            note.hidden = false;
            note.textContent = 'Generate Test Cases needs the question-generator webhook (N8n__KnowledgeBenchmarkWebhookUrl) to be configured. You can still add cases by hand.';
        } else if (generating) {
            note.hidden = false;
            note.textContent = 'Waiting for the question generator (n8n) to send the questions… this page updates by itself, so you can leave it open or come back later. "Stop generating" stops waiting; n8n may still finish its run, but its reply will be ignored.';
        } else if (note.dataset.busy !== '1') {
            note.hidden = true;
        }
        state.pendingGenId = d.pendingGenerationId || null;
        $('bm-stop').hidden = !generating;
        $('bm-stop').disabled = false;

        renderProgress(d);
        renderMetrics(d);

        // Poll while a run is pending/running; when it finishes, refresh everything and open its results.
        if (running) {
            state.wasRunning = true;
            schedulePoll();
        } else if (state.wasRunning) {
            state.wasRunning = false;
            if (d.latestRun) state.runId = d.latestRun.id;
            refreshAll();
        }

        if (generating) {
            state.genWasRunning = true;
            schedulePoll();
        } else if (state.genWasRunning) {
            state.genWasRunning = false;
            onGenerationFinished();
        }
    }

    function schedulePoll() {
        if (state.pollTimer) return;
        state.pollTimer = setTimeout(function () {
            state.pollTimer = null;
            loadDashboard().catch(function () { /* transient — next tick retries */ });
            loadRuns();
            loadGenerations();
        }, 2000);
    }

    function renderProgress(d) {
        var run = d.latestRun;
        var box = $('bm-progress');
        if (run && (run.status === 'pending' || run.status === 'running')) {
            box.hidden = false;
            var total = Math.max(run.totalCases, 1);
            $('bm-progress-bar').style.width = Math.min(100, Math.round(run.processedCases * 100 / total)) + '%';
            $('bm-progress-text').textContent = run.status === 'pending'
                ? 'Waiting to start…'
                : 'Running the benchmark… ' + run.processedCases + ' of ' + run.totalCases + ' cases';
        } else {
            box.hidden = true;
            if (run && run.status === 'failed' && run.errorSummary && state.lastFailedShown !== run.id) {
                state.lastFailedShown = run.id;
                showMessage('The last benchmark run failed: ' + run.errorSummary, 'error');
            }
        }
    }

    function renderMetrics(d) {
        var host = $('bm-metrics');
        host.textContent = '';
        var run = d.latestCompletedRun;
        if (!run) {
            host.appendChild(el('div', { class: 'card', style: 'padding:1.1rem 1.25rem' }, [
                el('div', { class: 'text-muted', text: 'No benchmark has been run yet. Generate or add some test cases, then click Run Benchmark.' })
            ]));
            return;
        }

        var scopeLabel = { all: 'All cases', generated: 'Generated cases', reviewed: 'Reviewed cases',
            generation: 'One generation (' + (run.generationId || '').slice(0, 8) + '…)' }[run.caseScope] || run.caseScope;
        host.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.8rem;margin-bottom:.6rem' }, [
            'Latest completed run — ' + scopeLabel + ' · ' + fmtDate(run.completedAt || run.createdAt) + ' · ' +
            run.scoredCases + ' scored' + (run.staleCases ? ', ' + run.staleCases + ' stale (not scored)' : '') +
            (run.errorCases ? ', ' + run.errorCases + ' errored (not scored)' : '')
        ]));

        host.appendChild(el('div', { class: 'bm-subtitle', text: 'Strict chunk retrieval — the exact expected chunk was returned' }));
        host.appendChild(el('div', { class: 'stat-grid' }, [
            statCard('Top-1', pct(run.chunkTop1Accuracy), 'exact chunk ranked first'),
            statCard('Top-3', pct(run.chunkTop3Accuracy), 'exact chunk in the first 3'),
            statCard('Top-5', pct(run.chunkTop5Accuracy), 'exact chunk in the first 5'),
            statCard('MRR', num(run.chunkMrr), 'mean reciprocal rank (1 = always first)')
        ]));

        host.appendChild(el('div', { class: 'bm-subtitle', style: 'margin-top:1rem', text: 'Document retrieval — secondary: right topic, maybe the wrong chunk' }));
        host.appendChild(el('div', { class: 'stat-grid' }, [
            statCard('Top-1', pct(run.documentTop1Accuracy)),
            statCard('Top-3', pct(run.documentTop3Accuracy)),
            statCard('Top-5', pct(run.documentTop5Accuracy)),
            statCard('MRR', num(run.documentMrr)),
            statCard('Average latency', run.averageLatencyMs == null ? '—' : Math.round(run.averageLatencyMs) + ' ms', 'question → ranked chunks')
        ]));

        var s = run.settings || {};
        var notes = ['Settings used: ' + settingsLine(s)];
        if (s.indexedChunkCount != null) {
            notes.push('The index held ' + s.indexedChunkCount + ' active chunks averaging ' + (s.avgChunkChars == null ? '?' : Math.round(s.avgChunkChars)) + ' characters.');
        }
        if (s.topK != null && s.topK < 5) {
            notes.push('Top K is ' + s.topK + ', so Top-5 can never be higher than Top-' + s.topK + '.');
        }
        host.appendChild(el('div', { class: 'bm-metric-note', text: notes.join(' ') }));
        host.appendChild(el('div', { class: 'bm-metric-note', text:
            'Chunk metrics are strict (only the exact chunk counts). Document metrics are a diagnostic: a document hit without a chunk hit means the topic was found but not the specific passage.' }));
    }

    // ---------------------------------------------------------------- cases

    function loadCases() {
        var qs = '?view=' + encodeURIComponent(state.caseView) + '&skip=' + state.caseSkip + '&take=' + PAGE_SIZE;
        return api('GET', '/cases' + qs).then(function (res) {
            state.caseTotal = res.totalCount;
            renderCases(res.items);
        });
    }

    function renderCases(items) {
        var body = $('bm-cases-body');
        body.textContent = '';
        if (!items.length) {
            body.appendChild(el('tr', { class: 'empty-row' }, [el('td', { colspan: '5', text: 'No manual test cases yet — click "Add manual case".' })]));
        }

        items.forEach(function (c) {
            var last = c.lastResult;
            var qCell = el('td', { style: 'max-width:340px;white-space:normal' }, [
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

        var pager = $('bm-cases-pager');
        pager.hidden = state.caseTotal <= PAGE_SIZE;
        var from = state.caseTotal === 0 ? 0 : state.caseSkip + 1;
        var to = Math.min(state.caseSkip + PAGE_SIZE, state.caseTotal);
        $('bm-cases-range').textContent = from + '–' + to + ' of ' + state.caseTotal;
        $('bm-cases-prev').disabled = state.caseSkip === 0;
        $('bm-cases-next').disabled = state.caseSkip + PAGE_SIZE >= state.caseTotal;
    }

    function toggleReviewed(c) {
        api('POST', '/cases/' + c.id + '/review', { reviewed: !c.isReviewed })
            .then(function () { return Promise.all([loadCases(), loadDashboard()]); })
            .catch(function (e) { showMessage(e.message, 'error'); });
    }

    function deleteCase(c) {
        if (!window.confirm('Delete this test case? Past run results keep their record of it.')) return;
        api('DELETE', '/cases/' + c.id)
            .then(function () { return Promise.all([loadCases(), loadDashboard()]); })
            .catch(function (e) { showMessage(e.message, 'error'); });
    }

    // ---------------------------------------------------------------- generate / run

    function generate() {
        var btn = $('bm-generate');
        var note = $('bm-generate-note');
        btn.disabled = true;
        note.dataset.busy = '1';
        note.hidden = false;
        note.textContent = 'Sampling 20 chunks and handing them to the question generator…';
        showMessage('');

        api('POST', '/cases/generate').then(function (res) {
            note.dataset.busy = '0';
            note.hidden = true;
            if (res.status === 'pending') {
                // Normal case: n8n acknowledged and will send the questions back on its own. The page polls until they arrive.
                state.genWasRunning = true;
                showMessage('Generation started — the question generator is writing questions for ' + res.chunksSent +
                    ' chunks. This page updates by itself when they arrive. Generation ID: ' + res.generationId);
            } else if (res.message) {
                showMessage(res.message, 'info');
            } else {
                // A workflow that answered synchronously: the questions are already stored.
                var details = (res.rejected || []).map(function (r) { return (r.question ? '"' + r.question + '" — ' : '') + r.reason; });
                showMessage('Generation ' + res.generationId + ' completed: ' + res.casesCreated + ' test case' + (res.casesCreated === 1 ? '' : 's') +
                    ' created from ' + res.chunksSent + ' chunks' + (details.length ? ' (' + details.length + ' rejected)' : '') + '.', 'info', details);
            }
            return refreshAll();
        }).catch(function (e) {
            note.dataset.busy = '0';
            note.hidden = true;
            showMessage(e.message, 'error');
            refreshAll();
        });
    }

    /** "Stop generating": cancels the pending generation so Generate is free again (n8n's late reply is ignored by the app). */
    function stopGenerating() {
        var id = state.pendingGenId;
        if (!id) return;
        if (!window.confirm('Stop waiting for the question generator?\n\nn8n may still finish writing questions, but this generation is cancelled and its reply will be ignored. You can start a new generation right away.')) return;
        $('bm-stop').disabled = true;
        api('POST', '/generations/' + id + '/cancel').then(function () {
            state.genWasRunning = false;
            showMessage('Generation ' + id + ' was stopped. You can generate again.');
            return refreshAll();
        }).catch(function (e) {
            showMessage(e.message, 'error');
            return refreshAll();   // e.g. it had just completed — show the real state
        });
    }

    /** Called once when a pending generation stops being pending: say how it ended and refresh the lists. */
    function onGenerationFinished() {
        loadGenerations().then(function (gens) {
            var g = gens && gens[0];
            if (g && g.status === 'completed') {
                showMessage('Generation ' + g.id + ' completed: ' + g.casesCreated + ' test case' + (g.casesCreated === 1 ? '' : 's') +
                    ' created from ' + g.chunksSent + ' chunks' + (g.rejectedCount ? ' (' + g.rejectedCount + ' rejected — open it below to see why)' : '') + '.');
            } else if (g && g.status === 'failed') {
                showMessage('Generation ' + g.id + ' failed: ' + (g.errorMessage || 'unknown error'), 'error');
            } else if (g && g.status === 'cancelled') {
                showMessage('Generation ' + g.id + ' was stopped.');
            }
            loadCases();
            loadDashboard();
        });
    }

    // ---------------------------------------------------------------- generations

    function loadGenerations() {
        return api('GET', '/generations?take=20').then(function (gens) {
            renderGenerations(gens);
            return gens;
        }).catch(function (e) { showMessage(e.message, 'error'); return []; });
    }

    function renderGenerations(gens) {
        var body = $('bm-gens-body');
        body.textContent = '';
        if (!gens.length) {
            body.appendChild(el('tr', { class: 'empty-row' }, [el('td', { colspan: '12', text: 'No generations yet — click "Generate Test Cases".' })]));
            return;
        }
        gens.forEach(function (g) {
            var s = g.latestScores;
            var badge = el('span', {
                class: 'badge ' + ({ completed: 'badge-green', pending: 'badge-teal', failed: 'badge-red' }[g.status] || 'badge-gray'),
                title: g.errorMessage || '',
                text: g.status === 'pending' ? 'Waiting for n8n…' : g.status === 'cancelled' ? 'Stopped' : g.status.charAt(0).toUpperCase() + g.status.slice(1)
            });
            body.appendChild(el('tr', null, [
                el('td', null, [el('div', { text: fmtDate(g.requestedAt) }), el('div', { class: 'text-subtle', style: 'font-size:.7rem', title: g.id, text: g.id.slice(0, 8) + '…' })]),
                el('td', null, [badge]),
                el('td', { text: String(g.chunksSent) }),
                el('td', { text: g.status === 'pending' ? '—' : String(g.questionsReturned) }),
                el('td', { text: g.status === 'pending' ? '—' : g.casesCreated + (g.casesRemaining !== g.casesCreated ? ' (' + g.casesRemaining + ' left)' : '') }),
                el('td', { text: g.status === 'pending' ? '—' : String(g.rejectedCount) }),
                el('td', { text: s ? pct(s.chunkTop1) : '—' }),
                el('td', { text: s ? pct(s.chunkTop3) : '—' }),
                el('td', { text: s ? pct(s.chunkTop5) : '—' }),
                el('td', { text: s ? num(s.chunkMrr) : '—' }),
                el('td', { class: 'text-subtle', style: 'font-size:.75rem', text: s ? s.scoredCases + ' cases · ' + fmtDate(s.runAt) : 'not run yet' }),
                el('td', null, [el('div', { class: 'flex items-center gap-2' }, [
                    el('a', { class: 'btn btn-sm', href: '/KnowledgeBase/Benchmark/Generations/' + g.id, text: 'Open' }),
                    el('button', {
                        type: 'button', class: 'btn btn-primary btn-sm', text: 'Run',
                        title: 'Run the benchmark for just this generation’s ' + g.casesRemaining + ' case(s)',
                        disabled: (g.casesRemaining === 0 || $('bm-run').disabled) ? true : null,
                        onclick: function () { runGeneration(g.id); }
                    })
                ])])
            ]));
        });
    }

    function runBenchmark() {
        var scope = $('bm-scope').value;
        $('bm-run').disabled = true;
        showMessage('');
        api('POST', '/runs', { scope: scope }).then(function () {
            state.wasRunning = true;
            return loadDashboard();
        }).catch(function (e) {
            showMessage(e.message, 'error');
            loadDashboard();
        });
    }

    /** "Run" on one Generations row: benchmarks ONLY that generation's own cases, whatever their type/reviewed state. */
    function runGeneration(generationId) {
        showMessage('');
        api('POST', '/runs', { scope: 'generation', generationId: generationId }).then(function () {
            state.wasRunning = true;
            showMessage('Running the benchmark for generation ' + generationId + '…');
            return loadDashboard();
        }).catch(function (e) {
            showMessage(e.message, 'error');
            loadDashboard();
        });
    }

    // ---------------------------------------------------------------- runs + results

    function loadRuns() {
        return api('GET', '/runs?take=20').then(renderRuns);
    }

    function renderRuns(runs) {
        var body = $('bm-runs-body');
        body.textContent = '';
        if (!runs.length) {
            body.appendChild(el('tr', { class: 'empty-row' }, [el('td', { colspan: '11', text: 'No runs yet.' })]));
            return;
        }
        var scopeLabel = { all: 'All', generated: 'Generated', reviewed: 'Reviewed' };
        function ScopeLabel(r) {
            if (r.caseScope === 'generation') return 'Generation ' + (r.generationId || '').slice(0, 8) + '…';
            return scopeLabel[r.caseScope] || r.caseScope;
        }
        runs.forEach(function (r) {
            var statusBadge = el('span', {
                class: 'badge ' + ({ completed: 'badge-green', running: 'badge-teal', pending: 'badge-gray', failed: 'badge-red' }[r.status] || 'badge-gray'),
                title: r.errorSummary || '',
                text: r.status === 'running' ? 'Running ' + r.processedCases + '/' + r.totalCases : r.status.charAt(0).toUpperCase() + r.status.slice(1)
            });
            var tr = el('tr', null, [
                el('td', { text: fmtDate(r.startedAt || r.createdAt) }),
                el('td', { text: ScopeLabel(r) + ' · ' + r.scoredCases + ' scored' + (r.staleCases ? ' · ' + r.staleCases + ' stale' : '') }),
                el('td', null, [statusBadge]),
                el('td', { text: pct(r.chunkTop1Accuracy) }),
                el('td', { text: pct(r.chunkTop3Accuracy) }),
                el('td', { text: pct(r.chunkTop5Accuracy) }),
                el('td', { text: num(r.chunkMrr) }),
                el('td', { text: pct(r.documentTop3Accuracy) }),
                el('td', { text: r.averageLatencyMs == null ? '—' : Math.round(r.averageLatencyMs) + ' ms' }),
                el('td', { style: 'max-width:260px' }, [el('span', { class: 'bm-cell-clip text-subtle', style: 'font-size:.75rem', title: settingsLine(r.settings), text: settingsLine(r.settings) })]),
                el('td', null, [el('button', { type: 'button', class: 'btn btn-sm', text: 'View results', onclick: function () { selectRun(r.id); } })])
            ]);
            body.appendChild(tr);
        });
    }

    function selectRun(runId) {
        state.runId = runId;
        loadResults();
        $('bm-results-section').scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function loadResults() {
        var section = $('bm-results-section');
        if (!state.runId) { section.hidden = true; return Promise.resolve(); }
        var qs = '?skip=0&take=500' + (state.resultFilter ? '&classification=' + encodeURIComponent(state.resultFilter) : '');
        api('GET', '/runs/' + state.runId + '/generations').then(renderRunGenerations).catch(function () { /* optional panel */ });
        return api('GET', '/runs/' + state.runId + '/results' + qs).then(function (res) {
            section.hidden = false;
            $('bm-results-title').textContent = 'Run results (' + res.totalCount + ')';
            renderResults(res.items);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    /** "Scores by generation" for the selected run (cases with no generation — manual ones — are grouped last). */
    function renderRunGenerations(rows) {
        var host = $('bm-run-gens');
        var body = $('bm-run-gens-body');
        body.textContent = '';
        host.hidden = rows.length === 0;
        rows.forEach(function (b) {
            var s = b.scores;
            body.appendChild(el('tr', null, [
                el('td', null, b.generationId
                    ? [el('div', { text: fmtDate(b.generationRequestedAt) }), el('div', { class: 'text-subtle', style: 'font-size:.7rem', title: b.generationId, text: b.generationId.slice(0, 8) + '…' })]
                    : [el('span', { class: 'text-subtle', text: 'No generation (manual cases)' })]),
                el('td', { text: String(s.scoredCases) }),
                el('td', { text: pct(s.chunkTop1) }), el('td', { text: pct(s.chunkTop3) }), el('td', { text: pct(s.chunkTop5) }),
                el('td', { text: num(s.chunkMrr) }), el('td', { text: pct(s.documentTop3) })
            ]));
        });
    }

    function renderResults(items) {
        var body = $('bm-results-body');
        body.textContent = '';
        if (!items.length) {
            body.appendChild(el('tr', { class: 'empty-row' }, [el('td', { colspan: '7', text: 'No results for this filter.' })]));
            return;
        }
        items.forEach(function (r) {
            var open = function (e) { if (e) e.preventDefault(); openResult(r.runId, r.id); };
            body.appendChild(el('tr', { class: 'bm-clickable', onclick: open }, [
                el('td', { style: 'max-width:380px' }, [el('span', { class: 'bm-cell-clip', title: r.question, text: r.question })]),
                el('td', null, [badgeFor(r.classification, r.expectedChunkRank)]),
                el('td', { text: r.expectedChunkRank == null ? '—' : '#' + r.expectedChunkRank }),
                el('td', { text: r.expectedDocumentBestRank == null ? '—' : '#' + r.expectedDocumentBestRank }),
                el('td', { text: r.expectedChunkScore == null ? '—' : num(r.expectedChunkScore, 3) + (r.expectedBelowThreshold ? ' (below min)' : '') }),
                el('td', { text: r.latencyMs == null ? '—' : r.latencyMs + ' ms' }),
                el('td', null, [el('button', { type: 'button', class: 'btn btn-sm', text: 'Analyze', onclick: function (e) { e.stopPropagation(); open(); } })])
            ]));
        });
    }

    // ---------------------------------------------------------------- overlay: case + failed-case analysis

    function openOverlay(title, bodyNode) {
        $('bm-modal-title').textContent = title;
        var body = $('bm-modal-body');
        body.textContent = '';
        body.appendChild(bodyNode);
        $('bm-overlay').hidden = false;
    }

    function closeOverlay() { $('bm-overlay').hidden = true; }

    function openResult(runId, resultId) {
        api('GET', '/runs/' + runId + '/results/' + resultId).then(function (detail) {
            var wrap = el('div');
            wrap.appendChild(el('div', { class: 'bm-subtitle', text: 'Question' }));
            wrap.appendChild(el('div', { class: 'bm-block', text: detail.result.question }));
            wrap.appendChild(el('div', { style: 'height:.9rem' }));
            wrap.appendChild(analysisNode(detail));
            openOverlay('Case analysis', wrap);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

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

            var questionBox = el('textarea', { class: 'text-input', rows: '2', maxlength: '500', id: 'bm-edit-question' });
            questionBox.value = c.question;
            var status = el('div', { class: 'text-subtle', style: 'font-size:.78rem;min-height:1.1em' });

            function save(markReviewed) {
                status.textContent = 'Saving…';
                api('PUT', '/cases/' + c.id, { question: questionBox.value })
                    .then(function () { return markReviewed ? api('POST', '/cases/' + c.id + '/review', { reviewed: true }) : null; })
                    .then(function () { status.textContent = 'Saved.'; return Promise.all([loadCases(), loadDashboard()]); })
                    .catch(function (e) { status.textContent = e.message; });
            }

            wrap.appendChild(el('div', { class: 'bm-subtitle', text: 'Patient question' }));
            wrap.appendChild(questionBox);
            wrap.appendChild(el('div', { class: 'flex items-center gap-2', style: 'margin:.5rem 0 .2rem' }, [
                el('button', { type: 'button', class: 'btn btn-primary btn-sm', text: 'Save', onclick: function () { save(false); } }),
                el('button', { type: 'button', class: 'btn btn-sm', text: 'Save & mark reviewed', onclick: function () { save(true); } }),
                el('span', { class: 'badge ' + (c.isReviewed ? 'badge-green' : 'badge-gray'), text: c.isReviewed ? 'Reviewed' : 'Not reviewed' }),
                el('span', { class: 'badge ' + (c.caseType === 'manual' ? 'badge-blue' : 'badge-gray'), text: c.caseType === 'manual' ? 'Manual' : 'Generated' })
            ]));
            wrap.appendChild(status);
            if (c.generationId) {
                wrap.appendChild(el('div', { class: 'text-subtle', style: 'font-size:.75rem;margin-top:.2rem', text: 'Generation ID: ' + c.generationId }));
            }

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

    /** The failed-case analysis: verdict, the settings in force, and the ranked chunks retrieval actually returned. */
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

    // ---------------------------------------------------------------- add manual case

    function toggleAddForm(show) {
        var form = $('bm-add-form');
        form.hidden = show === undefined ? !form.hidden : !show;
        if (!form.hidden) loadSourceDocuments();
    }

    function loadSourceDocuments() {
        var sel = $('bm-add-doc');
        api('GET', '/source-documents').then(function (docs) {
            sel.textContent = '';
            sel.appendChild(el('option', { value: '', text: docs.length ? 'Choose a document…' : 'No active documents with chunks' }));
            docs.forEach(function (d) { sel.appendChild(el('option', { value: d.id, text: d.title + ' (' + d.chunkCount + ' chunk' + (d.chunkCount === 1 ? '' : 's') + ')' })); });
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function loadSourceChunks(documentId) {
        var sel = $('bm-add-chunk');
        sel.textContent = '';
        if (!documentId) {
            sel.disabled = true;
            sel.appendChild(el('option', { value: '', text: 'Choose a document first' }));
            return;
        }
        api('GET', '/source-documents/' + documentId + '/chunks').then(function (chunks) {
            sel.disabled = false;
            sel.appendChild(el('option', { value: '', text: 'Choose the chunk…' }));
            chunks.forEach(function (c) { sel.appendChild(el('option', { value: c.id, text: '#' + (c.chunkIndex + 1) + ' — ' + c.preview })); });
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    function saveManualCase() {
        var question = $('bm-add-question').value;
        var documentId = $('bm-add-doc').value;
        var chunkId = $('bm-add-chunk').value;
        if (!question.trim() || !documentId || !chunkId) {
            showMessage('Enter the question and choose the document and chunk that answer it.', 'error');
            return;
        }
        api('POST', '/cases', { question: question, documentId: documentId, chunkId: chunkId }).then(function () {
            $('bm-add-question').value = '';
            $('bm-add-doc').value = '';
            loadSourceChunks('');
            toggleAddForm(false);
            showMessage('Case added.', 'info');
            return refreshAll();
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    // ---------------------------------------------------------------- wiring

    function refreshAll() {
        return Promise.all([loadDashboard(), loadCases(), loadRuns(), loadGenerations(), state.runId ? loadResults() : Promise.resolve()]);
    }

    function init() {
        $('bm-generate').addEventListener('click', generate);
        $('bm-stop').addEventListener('click', stopGenerating);
        $('bm-run').addEventListener('click', runBenchmark);
        $('bm-cases-prev').addEventListener('click', function () { state.caseSkip = Math.max(0, state.caseSkip - PAGE_SIZE); loadCases(); });
        $('bm-cases-next').addEventListener('click', function () { state.caseSkip += PAGE_SIZE; loadCases(); });
        $('bm-result-filter').addEventListener('change', function (e) { state.resultFilter = e.target.value; loadResults(); });
        $('bm-add-toggle').addEventListener('click', function () { toggleAddForm(); });
        $('bm-add-cancel').addEventListener('click', function () { toggleAddForm(false); });
        $('bm-add-doc').addEventListener('change', function (e) { loadSourceChunks(e.target.value); });
        $('bm-add-save').addEventListener('click', saveManualCase);
        $('bm-modal-close').addEventListener('click', closeOverlay);
        $('bm-overlay').addEventListener('click', function (e) { if (e.target === $('bm-overlay')) closeOverlay(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeOverlay(); });

        loadDashboard().then(function (d) {
            // Show the newest completed run's results by default.
            if (d.latestCompletedRun) state.runId = d.latestCompletedRun.id;
            return Promise.all([loadCases(), loadRuns(), loadGenerations(), state.runId ? loadResults() : null]);
        }).catch(function (e) { showMessage(e.message, 'error'); });
    }

    if ($('bm-generate')) init();
})();
