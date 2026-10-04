# SculptFlow — instructions for AI assistants

## Keep the system design document current (standing rule)

`docs/system-design.html` is the high-level and low-level design of SculptFlow: frontend, backend, workers, SignalR,
integrations, the n8n contracts and the database. It must always match the code.

**Whenever a change adds, removes or alters a feature, update `docs/system-design.html` in the same change.** That
includes new or changed Razor Pages or JS files, API routes or their auth, service rules, SignalR events, background
workers, integrations or OAuth flows, anything n8n sends or receives, `Database/schema.sql`, and config keys. Section 12
of the document maps each kind of change to the sections to edit. Then:

1. Update "Last verified against code" (and the code baseline commit, if known) in the header.
2. Add a row to the change log in section 12.
3. If you find the document already disagreed with the code, fix it and note it in section 11 (doc drift).

Write it from the code, not from `PROJECT_HANDOFF.md` alone; the handoff's status lines can be stale. Keep the file
plain HTML + CSS with no build step and no external scripts, so it stays easy to edit by hand.

## Other project docs

- `PROJECT_HANDOFF.md` — session handoff and project state (decisions, TODOs, history). Update it in place too.
- `README.md` — short orientation and local setup.
