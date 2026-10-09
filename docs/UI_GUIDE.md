# Aurora UI — Client Portal appearance guide

> **What this is.** The *visual* language of the Omni Support Portal ("Aurora": indigo→violet
> brand, dark or light sidebar, glass header, glass-lit panels, light + dark mode), adapted for a
> **customer-facing** portal. It covers **appearance only** — tokens, components, layouts, screen
> recipes and content tone. It deliberately says nothing about data access, scoping or business
> logic: the Client Portal keeps all of that exactly as it is.
>
> **Where it lives.** Put this file at `docs/UI_GUIDE.md` and reference it from `CLAUDE.md`. The
> CSS/JS kit is embedded verbatim in the appendices — **extract it, never retype it** (§0.2). The
> kit is identical to the Admin Portal's, so both products stay one family.

---

## 0. Read this first (Claude)

### 0.1 Appearance only — the prime directive
Change how screens **look**, never what they **do**. Do not change, without the user's explicit OK:
controllers/PageModels/components' logic, routes, form `method`/`action`, field `name`s and model
binding, antiforgery tokens, validation attributes and scripts, authentication/authorization,
API calls, any `id`/`class`/`data-*` existing JS relies on, and query-string names.
Allowed: new layout chrome, wrapping elements, adding classes, replacing presentational markup,
presentation-only partials/components. When in doubt, keep the element and add the Aurora class.

### 0.2 Install the kit (verbatim extraction)
Run from the **web project folder** (the one with `wwwroot/`; for a non-.NET stack, edit the
target paths in the `au-file` markers below to the project's static folder first).

```powershell
$guide = '..\docs\UI_GUIDE.md'   # adjust
$md = [IO.File]::ReadAllText((Resolve-Path $guide))
$rx = [regex]'(?s)<!-- au-file: (?<path>\S+) -->\s*````[a-z]*\r?\n(?<body>.*?)\r?\n````'
foreach ($m in $rx.Matches($md)) {
  $path = Join-Path (Get-Location) $m.Groups['path'].Value
  New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
  [IO.File]::WriteAllText($path, ($m.Groups['body'].Value -replace "`r`n", "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "wrote $path"
}
```

Node alternative:

```bash
node -e 'const fs=require("fs"),p=require("path");const md=fs.readFileSync(process.argv[1],"utf8");for(const m of md.matchAll(/<!-- au-file: (\S+) -->\s*````[a-z]*\r?\n([\s\S]*?)\r?\n````/g)){fs.mkdirSync(p.dirname(m[1]),{recursive:true});fs.writeFileSync(m[1],m[2].replace(/\r\n/g,"\n")+"\n");console.log("wrote",m[1])}' ../docs/UI_GUIDE.md
```

Result: `wwwroot/css/aurora.css`, `wwwroot/css/aurora-bootstrap.css` (only if the project uses
Bootstrap 5) and `wwwroot/js/aurora.js`. They are vendored and shared with the Admin Portal —
fix bugs in both copies; keep screen-specific styles out of them.

### 0.3 Order of work
1. **Inventory** (no edits): stack and version, CSS framework (Bootstrap?), layout files, sign-in
   flow (Identity UI? external IdP?), JS that touches the DOM, pages list, existing error/status
   message patterns. Propose a screen-by-screen plan and wait for the user's OK.
2. **Kit + layout** (§3): head, fonts, shell, theme toggle. Build, run, check every screen in light
   and dark, at desktop and 390px.
3. **Screens one at a time** with the recipes in §6, re-testing each form after it changes.
4. **Auth screens** last (they often come from Identity UI; restyle without changing the flow).

### 0.4 MySculptFlow clinic app — how this project applies the guide
This app had its own hand-rolled CSS and 21 page scripts before Aurora. Where the rest of this guide is generic, **this
section wins**. Copy says *patient* where the guide says *customer*: the people using the app are **clinic staff**.

- **Keep it simple** (decided 2026-10-09). Staff get the sidebar, the page title, the notification bell, the theme
  toggle and the user menu, nothing more: no palette, scope, favourites, nav filter, section tabs or breadcrumbs (A13).
- **No Bootstrap, jQuery or unobtrusive validation.** Appendix B is not installed. Forms post plain `name=` fields with
  HTML5 attributes; server-side rule errors come back as the page's `ErrorMessage`.
- **Brand and look:** dark sidebar; teal brand (`data-brand="teal"` on `<html>` in both layouts); name *MySculptFlow*
  with the clinic's name as the sidebar subtitle; `wwwroot/logo.svg` is the brand mark (no `_BrandMark`). The look is
  **calm**: `site.css` section 2 switches off the ambient backdrop, the glow under buttons and the panel sheen, and sets
  `--au-r-panel` to 18px. It also makes the teal ramp AA: primary `#0f766e` (5.5:1 on white), gradient
  `#0f766e → #0e7490` in light and `#2dd4bf → #22d3ee` (dark text) in dark. These are the only kit tokens overridden.
- **CSS order differs from §3.1:** `fonts.css → aurora.css → site.css`. `site.css` comes last because it (1) maps the
  older token names (`--accent`, `--surface`, `--badge-*`…) onto `--au-*`, so the older classes the page scripts still
  build (`badge-*`, `alert-*`, `btn`, `card`, `data-table`, `text-input`, `stat-card`, `inbox-*`, `cal-*`…) follow the kit
  in both themes, and (2) holds the brand calibration. Rule: **never restyle an `au-*` class in `site.css`**; new markup
  uses kit classes; the older classes stay for script-built markup and for page parts the scripts depend on.
- **Files** (repo `CLAUDE.md` conventions):

  | Guide | Here |
  |---|---|
  | `_Layout` / `_AuthLayout` + pre-paint script | `Pages/Shared/_Layout`, `_AuthLayout`, `_ThemeScript` (keys `client.themeMode`, `client.sidebarCollapsed`; reads the old `sf-theme` / `sf-sidebar` once) |
  | Sidebar, header, user menu | `_Sidebar` (four captioned groups; `#sidebar-inbox-badge` is filled by `sidebar.js` / `inbox.js`), `_Header` (menu button = drawer on phones, icon rail on desktop; bell ids read by `notifications.js`; theme toggle; `<details>` user menu with the existing sign-out form; Swagger link only in Development) |
  | `fonts.css` (self-hosted) | `wwwroot/css/fonts.css` + `wwwroot/fonts`: Inter (variable, latin) and a **Material Symbols subset** holding only the icons in use (`icons.txt`). After adding an icon run `node docs/tools/icon-font.js` from the repo root: it scans the views and scripts for `class="… au-icon …">name<`, `Icon: "name"` and `@* icon:name *@`. JetBrains Mono isn't shipped; `.au-mono` falls back to the system monospace. |
  | Feedback, empty states | `_Messages` (`Entities/Dtos/Ui/PageMessagesModel(Status, Error)`), `_EmptyState` (`EmptyStateModel`; pass `Icon:` by name), `_NoClinic` |
  | `Ui` helpers | `Common/Helpers/UiTextHelper` (`Initials`, `Amount`, `ShortReference`, `SplitError`) |
  | Error / 404 pages | `Pages/Status` (404/400 via `UseStatusCodePagesWithReExecute`, not for `/api`, `/hubs` or static files; shell when signed in, `_AuthLayout` otherwise) and `Pages/Error` (always `_AuthLayout`: the shell reads the clinic from the database, which may be what failed) |

- **Kit behaviours:** the theme toggle and the sidebar drawer/rail are the kit's (the old `theme.js` and inline drawer
  script are gone). Confirms are `data-au-confirm` + `-title` + `-label` (+ `-tone="danger"` for deletes and disconnects;
  sending a campaign uses the primary tone) on the form; inside a script, call `Aurora.confirm({ title, message,
  confirmLabel, tone })` (a Promise) through the script's `ask()` helper, which falls back to the browser's `confirm()`
  when the kit isn't loaded (`inbox.js`, both benchmark scripts). Server toasts come from `_Messages`; client
  toasts via `Aurora.toast` (Inbox send failures). Submit busy/progress is automatic; the sign-in forms carry
  `data-au-no-busy` because `auth.js` shows its own "Signing in…". **Not used:** `data-au-href` (pages have their own
  row-click scripts on `tr[data-href]`) and `data-au-open` dialogs (popups stay `.bm-overlay > .card`, driven by their
  scripts).
- **Feedback (A5, A8):** `StatusMessage` → a toast when it is 90 characters or fewer, an inline `.au-alert--success`
  otherwise (it usually says what happens next). `ErrorMessage` → `.au-alert--error` titled "That didn't go through";
  text after "Something failed: " goes under *Technical details*, and .NET's "(Parameter 'x')" suffix is dropped.
  Reference codes appear only on the Error page (`UiTextHelper.ShortReference` of the trace id).
- **Status tones (A11):** `StatusBadgeHelper` (colour) + `FilterLabelHelper` (plain label) is the one map; add a status
  there. Colours: `badge-green` success, `badge-amber` waiting, `badge-red` problem, `badge-blue` information,
  `badge-gray` inactive/past, `badge-purple` AI, `badge-teal` brand. Light mode uses the darker shade of each tone for the
  text (AA). The page scripts (Inbox, calendar, templates, benchmark) emit the same classes.
- **Script hooks a restyle must keep:** every `id`, `data-*` and `name` the scripts read; classes that are hooks
  (`seg-opt`, `aud-card`, `cat-card`, `kb-choice`, `seg-cards`, `var-*`, `ms-*`, `week-row`, `inbox-filter`,
  `inbox-conv-item`, `time-view-switch`, `row-actions`); a popup's card stays the direct child of its overlay (backdrop
  clicks close on `e.target === overlay`); `templates-page.js` reads a row's second cell and copies `.row-actions`;
  `leads.js` swaps `#leads-results`; elements whose class a script rewrites (`[data-pw-rule]`, `[data-pw-match]`,
  `[data-status-for]`, `#inbox-lead-status`, `#inbox-thread-mode`, `[data-role=status]`, the dashboard health dot) keep
  plain classes; `[hidden] { display: none !important }` stays in `site.css`.
- **Recipes as built** (copy a page of the same kind): *list* `Procedures/Index` (page head → toolbar → `.au-table-card`
  with `.au-table--stack` and `data-label`s, or `_EmptyState`); *detail* `Dashboard/LeadDetail` (`.au-back-header` →
  `.detail-chips` → panels with `.au-detail-rows`); *form* `Procedures/Edit` (stacked `.au-form-field`s in a panel,
  `.au-form-actions`); *settings with sections* `Settings/ClinicInfo`; *popup* `WhatsApp/Templates` (`.bm-overlay >
  .card`); *sign-in* `Account/Login` (`.au-auth__card`). Multi-part table cells wrap their content in one `<div>` so the
  stacked phone layout keeps label and value side by side.
- **Money:** Billing shows its currency; Dashboard revenue uses `UiTextHelper.Amount` (no symbol: the production
  container runs with the invariant culture, and bookings can each carry their own currency).
- **Kit fixes (same as the Admin copy):** `aurora.js` ignores `<form method="dialog">` submits; `.au-fullpage-msg` gets
  `grid-template-columns: minmax(0, 1fr)` and its `__inner` `width: 100%`.
- **Future work:** Arabic / RTL (not needed yet; §3.2 has the steps: `lang`/`dir`, an Arabic font first in
  `--au-font-sans`, mirror the physical-direction rules, check every screen in RTL).

---

## 1. The look — customer-facing variant

Same family as the Admin Portal: **Inter** text, **Material Symbols Outlined** icons, the
**indigo→violet gradient** on primary actions (with a soft coloured glow), cool off-white
`#f2f5fa` / near-black `#070b15` ground with a faint **aurora backdrop**, white/navy **panels with
a hairline, a long soft shadow and a glass top-light**, generous radii (10px controls, **30px**
cards and panels), a **sticky glass header**, and a full **dark mode**.

What changes for customers:

| Admin Portal (operators) | Client Portal (customers) |
|--------------------------|---------------------------|
| Dense tables, mono ids everywhere | Calmer spacing, ids only where a customer needs to quote them |
| Global scope selector, Ctrl+K, favourites, section tabs, breadcrumb dropdowns | **None of these.** Title + theme toggle + user menu |
| Technical error panels (exception, trace, SQLSTATE) | **Friendly errors** + a short reference code — never internals |
| Notched labels in toolbars | **Stacked labels** on every form |
| Desktop-first | **Mobile-first**: stacked tables, wrapped actions, off-canvas menu |
| Dark sidebar always | Dark sidebar (default) **or** the light sidebar variant |

---

## 2. Appearance rules

| # | Rule |
|---|------|
| A1 | **Tokens only.** Screens use kit classes or `var(--au-*)`; no raw hex/rgb in views. A new colour need = a new token (light **and** dark) in `aurora.css`. |
| A2 | **Light and dark are both first-class.** Theme on `<html data-theme>`, stamped before first paint; the toggle lives in the header. Default = the OS preference. |
| A3 | **One primary action per region**, the gradient button. Secondary = outlined; tertiary = text. |
| A4 | **Plain language.** Labels, statuses and errors are written for a patient/customer, not a developer ("Awaiting payment", not `PENDING_PAYMENT`). |
| A5 | **Errors are friendly and actionable**: what happened, what to do next, and a short reference code. Never show exception names, stack traces, SQL or HTTP internals. |
| A6 | **Every screen has loading, empty and error states.** Empty states say what's empty and offer the next step (a button). |
| A7 | **Destructive or irreversible actions are confirmed** (`data-au-confirm`, danger tone, default focus on Cancel) and the confirm says the consequence. |
| A8 | **Every form submit gets feedback** — a toast after redirect ("Your appointment is booked."), inline field errors on validation. |
| A9 | **Mobile-first.** Check every screen at 390px: tables use `.au-table--stack`, actions wrap, touch targets ≥ 40px, no horizontal scroll except inside a table card. |
| A10 | **Accessible (WCAG AA).** Labels on every field, `aria-label` on icon-only buttons, visible focus ring, 4.5:1 text contrast in both modes, reduced motion honoured. |
| A11 | **Consistent status colours.** success = done/confirmed/paid · warning = pending/awaiting · error = failed/overdue/cancelled-by-error · info = informational · neutral outlined = inactive/past. |
| A12 | **Numbers, dates and money are formatted for the user's locale**, consistently (one date style app-wide), with `—` for "none". |
| A13 | **No operator chrome.** Don't port the Admin Portal's scope selector, command palette, favourites, environment banner or debug details. |
| A14 | **Docs are part of done** — a change that makes this guide wrong updates it in the same pass. |

---

## 3. Wiring

### 3.1 Head and layout (Razor example — translate 1:1 for MVC/Blazor/other)

```cshtml
<!DOCTYPE html>
<html lang="en" data-au-app="client">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@(ViewData["Title"] is string t ? $"{t} · Patient Portal" : "Patient Portal")</title>
    <script>
        (function () {
            try {
                var saved = localStorage.getItem('client.themeMode');
                var mode = saved === 'light' || saved === 'dark' ? saved
                    : (window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
                var r = document.documentElement;
                r.setAttribute('data-theme', mode);
                r.setAttribute('data-bs-theme', mode);
                r.style.backgroundColor = mode === 'dark' ? '#070b15' : '#f2f5fa';
            } catch (e) { }
        })();
    </script>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />  @* only if already used *@
    <link rel="stylesheet" href="~/css/fonts.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />    @* cleaned, §3.3 *@
    <link rel="stylesheet" href="~/css/aurora.css" asp-append-version="true" />
    <link rel="stylesheet" href="~/css/aurora-bootstrap.css" asp-append-version="true" /> @* only with Bootstrap *@
</head>
<body>
    <div class="au-app">
        <nav class="au-sidebar" aria-label="Main navigation">           @* add au-sidebar--light for the light variant *@
            <a class="au-brand" href="~/">
                <span class="au-brand__mark"><partial name="_BrandMark" model="38" /></span>
                <span class="au-brand__text"><span class="au-brand__name">Nile Dental</span><span class="au-brand__sub">Patient Portal</span></span>
            </a>
            <div class="au-side-rule"></div>
            <div class="au-side-scroll">
                @* one .au-nav-row per page — keep the project's existing links and their order *@
                <div class="au-nav-row"><a class="au-nav-item is-active" href="~/" aria-current="page"><span class="au-icon" aria-hidden="true">home</span><span class="au-nav-item__label">Home</span></a></div>
                <div class="au-nav-row"><a class="au-nav-item" href="~/Appointments"><span class="au-icon" aria-hidden="true">event</span><span class="au-nav-item__label">Appointments</span></a></div>
                <div class="au-side-section">Account</div>
                <div class="au-nav-row"><a class="au-nav-item" href="~/Profile"><span class="au-icon" aria-hidden="true">person</span><span class="au-nav-item__label">Profile</span></a></div>
            </div>
        </nav>
        <div class="au-scrim"></div>
        <div class="au-main-col">
            <header class="au-header">
                <div class="au-header__bar">
                    <button type="button" class="au-icon-btn au-show-mobile" data-au-sidebar-toggle aria-label="Open menu"><span class="au-icon" aria-hidden="true">menu</span></button>
                    <div class="au-header__title-wrap"><h1 class="au-header__title">@ViewData["Title"]</h1></div>
                    <div class="au-header__tools">
                        <button type="button" class="au-icon-btn au-icon-btn--bordered" data-au-theme-toggle aria-label="Toggle color mode"><span class="au-icon" aria-hidden="true">dark_mode</span></button>
                        <partial name="_UserMenu" />
                    </div>
                </div>
                <div class="au-topbar-progress" aria-hidden="true"><div class="au-topbar-progress__bar"></div></div>
            </header>
            <main class="au-main" id="main">@RenderBody()</main>
        </div>
    </div>
    <div class="au-toasts" aria-live="polite"></div>
    @* server-rendered toasts after redirects, e.g. from TempData: <div hidden data-au-toast="success">Saved.</div> *@

    <script src="~/lib/jquery/dist/jquery.min.js"></script>                 @* keep what the project had *@
    <script src="~/js/aurora.js" asp-append-version="true"></script>
    <script src="~/js/site.js" asp-append-version="true"></script>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

- Active state: mark the current page's row `is-active` + `aria-current="page"` (match on the
  request path, segment-aware: `/Appointments` also covers `/Appointments/Details/7`).
- `data-au-app="client"` namespaces the stored preferences (`client.themeMode`, …); the pre-paint
  script must use the same prefix.
- CSS order: **bootstrap → site → aurora → aurora-bootstrap**.
- Desktop shows the sidebar; under 900px it becomes an off-canvas drawer opened by the menu button.
- The rail/collapse toggle is optional for customers — omit `data-au-sidebar-toggle` on desktop if not wanted
  (the menu button above is `.au-show-mobile`).

User menu (`_UserMenu`) — keep the existing sign-out form exactly (it is usually a POST with an antiforgery token):

```cshtml
<details class="au-dropdown au-user-menu">
    <summary aria-label="Account menu"><span class="au-avatar" aria-hidden="true">SA</span><span class="au-hide-mobile">Sara Ali</span><span class="au-icon" aria-hidden="true">expand_more</span></summary>
    <div class="au-menu au-menu--end">
        <span class="au-user-menu__who"><strong>Sara Ali</strong><span>sara.ali@example.com</span></span>
        <a class="au-menu__item" href="~/Profile"><span class="au-icon" aria-hidden="true">person</span>Profile</a>
        <div class="au-menu__sep"></div>
        <form method="post" asp-area="Identity" asp-page="/Account/Logout" asp-route-returnUrl="/"> @* ← the project's existing form *@
            <button type="submit" class="au-menu__item au-menu__item--danger"><span class="au-icon" aria-hidden="true">logout</span>Sign out</button>
        </form>
    </div>
</details>
```

### 3.2 Fonts and icons
Self-host in `wwwroot/fonts/` (customers' networks vary; a missing icon font would show ligature
names): Inter (variable latin `woff2`), JetBrains Mono (only for reference codes) and Material
Symbols Outlined (`woff2`). `fonts.css`:

```css
@font-face { font-family: 'Inter Variable'; font-style: normal; font-weight: 100 900; font-display: swap;
  src: url('../fonts/inter-latin-wght-normal.woff2') format('woff2'); }
@font-face { font-family: 'JetBrains Mono Variable'; font-style: normal; font-weight: 100 800; font-display: swap;
  src: url('../fonts/jetbrains-mono-latin-wght-normal.woff2') format('woff2'); }
@font-face { font-family: 'Material Symbols Outlined'; font-style: normal; font-weight: 100 700; font-display: block;
  src: url('../fonts/material-symbols-outlined.woff2') format('woff2'); }
```

Icons: `<span class="au-icon" aria-hidden="true">event</span>` (names from fonts.google.com/icons, Outlined).
`.au-icon` is clipped to 1em, so a missing font degrades to a blank square.

**Arabic / RTL (if needed):** set `<html lang="ar" dir="rtl">`, put an Arabic-capable font first in
`--au-font-sans` (Inter has no Arabic glyphs — e.g. IBM Plex Sans Arabic or Noto Sans Arabic,
self-hosted), and mirror the few physical-direction rules (sidebar border and accent bar, drawer
side, toast corner, `margin-left: auto` in toolbars) under `[dir='rtl']`. Check every screen in RTL.

### 3.3 Template gotchas (ASP.NET Core templates)
Remove/neutralise in `site.css`: `html { font-size: 14px }` (+ its media query — the kit assumes
16px), `body { margin-bottom: 60px }` and any absolute `.footer`, the custom `.btn:focus … box-shadow`
rules. Replace the old `navbar`/`footer` with the shell; keep `<script type="importmap">` and
`MapStaticAssets()` if present; empty `_Layout.cshtml.css` once the old navbar is gone.

---

## 4. Tokens (summary — values live in `aurora.css`)

| Token | Light | Dark |
|-------|-------|------|
| `--au-primary` | `#4f46e5` | `#818cf8` |
| `--au-gradient` | `#6366f1 → #8b5cf6 → #a855f7` (135°) | same |
| `--au-bg` / `--au-paper` | `#f2f5fa` / `#ffffff` | `#070b15` / `#0d1526` |
| `--au-text` / `-2` / `-3` | `#0b1220` / `#5b6b82` / `#9aa8bc` | `#e7edf8` / `#93a5c0` / `#556685` |
| `--au-success/warning/error/info` | `#059669 #d97706 #e11d48 #0284c7` | `#34d399 #fbbf24 #fb7185 #38bdf8` |
| `--au-divider` | `rgba(15,23,42,.08)` | `rgba(148,163,184,.14)` |

Shape: 8 chips · 10 buttons/inputs · 12 menus/alerts · 14 cards · 18 dialogs/auth card · **30 panels,
action cards, toolbars** · 40 hero. Type: body 14px, inputs 16px (no iOS zoom), page title
1.3rem/800, panel title 14px/700, eyebrow 12px uppercase. Spacing: 8px grid; page padding 24px (16px mobile).

**Brand options.** Default is the shared indigo→violet. `<html data-brand="teal">` switches only
the brand ramp to teal→cyan. A per-clinic brand colour, if ever needed, is the same mechanism:
override only `--au-primary*`, `--au-secondary*`, `--au-gradient*`, `--au-brand-glow*`,
`--au-focus-ring` and the sidebar active tokens — and re-check AA contrast for white text on the
gradient in both modes.

---

## 5. Shell

```
┌──────────────┬───────────────────────────────────────────────────────────┐
│ [+] Nile     │ ☰  Home                                        [☾] (SA Sara Ali ▾) │ ← glass header
│     Dental   │───────────────────────────────────────────────────────────│
│──────────────│  Good morning, Sara                [Download] [+ Book]    │ ← .au-page-head
│ ┃▣ Home      │  Your next visit is Thursday at 10:30.                    │
│  ▢ Appts     │  ① Choose service ──── ② Pick a time ──── ③ Confirm       │ ← .au-steps (flows)
│  ▢ Invoices  │  ╭ Book a visit  → ╮ ╭ Pay an invoice → ╮ ╭ Message us → ╮│ ← .au-action-card
│ ACCOUNT ─────│  ╭ Upcoming appointments ──────────────────── See all → ╮ │
│  ▢ Profile   │  │ table (stacks into cards on mobile)                   │ │
│  ▢ Help      │  ╰───────────────────────────────────────────────────────╯ │
└──────────────┴───────────────────────────────────────────────────────────┘
```

- **Sidebar**: brand (38px mark, name, "Patient Portal"), a flat list of pages with optional
  section captions (`Account`), active row = indigo wash + gradient bar. Dark by default;
  `.au-sidebar--light` is a token swap if the brand calls for a lighter feel.
- **Header**: page title, theme toggle, user menu. No breadcrumb unless the portal has real depth.
- **Main**: content starts with `.au-page-head` (title + one-line purpose + primary action) when the
  screen has a primary action; otherwise straight into panels.

---

## 6. Screen recipes

### 6.1 Home
`.au-page-head` (greeting + the single most important fact, e.g. next appointment; actions on the
right) → optional `.au-steps` if a flow is in progress → a `.au-grid au-grid--3` of
`.au-action-card` (icon tile tinted with `style="--tile: var(--au-success)"` etc., title, one line,
arrow) → panels (`.au-panel au-panel--round au-panel--pad`) for upcoming items, with "See all →".

```html
<a class="au-action-card" href="/Appointments/Book">
  <span class="au-icon-tile"><span class="au-icon" aria-hidden="true">event_available</span></span>
  <span><span class="au-action-card__title">Book a visit</span>
        <span class="au-action-card__text">Choose a doctor and a time that suits you.</span></span>
  <span class="au-icon au-action-card__arrow" aria-hidden="true">arrow_forward</span>
</a>
```

### 6.2 List (appointments, invoices, documents)

```html
<div class="au-table-card">
  <table class="au-table au-table--stack">
    <thead><tr><th>Date</th><th>Doctor</th><th>Status</th><th class="au-shrink"></th></tr></thead>
    <tbody>
      <tr>
        <td data-label="Date">Thu, Oct 16 · 10:30</td>
        <td data-label="Doctor">Dr. Layla Mansour</td>
        <td data-label="Status"><span class="au-chip au-chip--success">Confirmed</span></td>
        <td><a class="au-btn au-btn--sm au-btn--outlined" href="…">Reschedule</a></td>
      </tr>
    </tbody>
  </table>
  <!-- pager: "1–10 of 42" + previous/next, as in the existing paging (keep its parameters) -->
</div>
```

- Every `<td>` that carries data gets `data-label` — under 640px each row becomes a labelled card.
- Filters (if the screen has them) sit in an `.au-toolbar` above the card; keep the existing form
  and parameter names, just restyle (`.au-field` + `.au-label` or stacked labels).
- Empty → `.au-empty` with icon, a sentence and a next-step button ("Book your first visit").
- Clickable rows: `data-au-href="…"` (+ `tabindex="0"`); keep explicit buttons for actions.

### 6.3 Detail
`.au-back-header` (← back, title, subtitle) → chips row (`.au-drawer__chips` style) → panels with
`.au-kv au-kv--2` (eyebrow label + value) → actions at the bottom right (`.au-form-actions`).
A reference number the customer may quote is shown with `.au-mono` + copy button
(`data-au-copy="…"`).

### 6.4 Forms
- One panel per logical group; `.au-form-grid` (2 columns, 1 on mobile; `.au-span-2` for wide fields).
- **Stacked labels**: `.au-form-field` > `.au-form-label` (+ `<span class="au-required">*</span>`) >
  input (`.au-input` / `.au-select` / `.au-textarea`) > `.au-help` hint > validation message.
- Keep `asp-for` / `name` / `asp-validation-for` untouched; the kit styles `.field-validation-error`
  and `.input-validation-error`.
- Actions (`.au-form-actions`): Cancel (text) then the primary gradient button; destructive actions
  outlined-danger on the far left, behind a confirm.
- Multi-step flows: `.au-steps` with `is-done` / `is-current` (labels collapse to numbers on mobile).
- Submit buttons get a spinner automatically while the page posts (`aria-busy`); never disable the
  clicked submit button yourself (a disabled button's value is not posted).

### 6.5 Sign-in, sign-up, password reset (`.au-auth`)

```html
<main class="au-auth">
  <div class="au-auth__card">
    <div class="au-auth__brand">
      <span class="au-brand__mark"><!-- 56px brand mark --></span>
      <div><h1 class="au-auth__title">Welcome back</h1><p class="au-auth__sub">Sign in to manage your appointments.</p></div>
    </div>
    <form class="au-auth__form" method="post"><!-- the EXISTING form fields, restyled as .au-form-field -->
      <div class="au-form-field"><label class="au-form-label" for="Email">Email</label><input class="au-input" id="Email" … /></div>
      <div class="au-form-field"><label class="au-form-label" for="Password">Password</label><input class="au-input" type="password" … /></div>
      <div class="au-auth__row"><label class="au-check"><input type="checkbox" …> Keep me signed in</label><a class="au-link" href="…">Forgot password?</a></div>
      <button class="au-btn au-btn--primary au-btn--lg au-btn--block" type="submit">Sign in</button>
    </form>
    <p class="au-auth__foot">New here? <a class="au-link" href="…">Create an account</a></p>
  </div>
</main>
```

Auth screens use a separate layout without the sidebar/header (same `<head>`). If they come from
ASP.NET Identity UI (scaffolded pages using `.form-floating`), restyle the scaffolded markup; never
change the Identity handlers.

### 6.6 Feedback
- **Toasts** (bottom-right, filled, 4.5s): after every successful submit + redirect. Server side,
  render `<div hidden data-au-toast="success">Your appointment is booked.</div>` (e.g. from TempData);
  client side `Aurora.toast.success('…')`.
- **Inline alerts** (`.au-alert` + tone): persistent page-level messages ("You have 1 unpaid invoice.").
- **Errors**: `.au-alert.au-alert--error` with a title ("We couldn't save your changes"), one plain
  sentence of what to do, and a short reference (`<span class="au-mono">8F2C-1A90</span>`, e.g. the
  first characters of the trace id) — log the full details server-side, show none of them.
- **Error / 404 pages**: `.au-fullpage-msg` (gradient code, heading, one friendly sentence, a
  "Back to home" primary button, the reference code).
- **Confirm**: `data-au-confirm="Cancel your appointment on Oct 16? This can't be undone."`
  + `data-au-confirm-tone="danger"` + `data-au-confirm-label="Cancel appointment"`.
- **Loading**: navigation/submits show the 2px top bar automatically; async blocks use `.au-skeleton`.

---

## 7. Components (customer-facing subset)

| Need | Class(es) |
|------|-----------|
| Surfaces | `.au-panel` (+`--round`, `--pad`) · `.au-card` · `.au-action-card` |
| Buttons | `.au-btn` + `--primary` / `--outlined` / `--text-primary` / `--danger(-outlined)` · sizes `--sm`/`--lg`/`--block` · `.au-icon-btn` |
| Forms | `.au-form-grid` · `.au-form-field` · `.au-form-label` · `.au-required` · `.au-input` · `.au-select` · `.au-textarea` · `.au-help` · `.au-check` · `.au-switch` · `.au-segmented` |
| Status | `.au-chip` + `--success/warning/error/info` (filled) · `--outlined` · `--soft` · `.au-dot` |
| Tables | `.au-table-card` · `.au-table` · `.au-table--stack` (+ `data-label`) · `.au-empty` |
| Structure | `.au-page-head` · `.au-back-header` · `.au-steps` · `.au-kv` · `.au-tabs` · `.au-divider-label` |
| Feedback | `.au-alert` (+tone) · toasts (`data-au-toast`, `Aurora.toast`) · `data-au-confirm` · `.au-skeleton` · `.au-spinner` · `.au-progress` |
| Overlays | `<dialog class="au-dialog">` + `data-au-open`/`data-au-close` · `<details class="au-dropdown">` + `.au-menu` |
| Identity | `.au-avatar` · `.au-user-menu` · `.au-icon-tile` · brand mark |
| Auth | `.au-auth` · `__card` · `__brand` · `__title` · `__sub` · `__form` · `__row` · `__foot` |
| Layout | `.au-stack` (`--au-gap`) · `.au-row` · `.au-grid--2/3/4` · `.au-spacer` · `.au-hide-mobile` / `.au-show-mobile` |

Brand mark: a 32×32 SVG — dark rounded tile (`rx=8`, fill `#0d1117`) with a 2px gradient stroke and
a ~3px gradient glyph. It (and `aurora.css`) are the only places raw colours appear. Give each
inline SVG a unique gradient id.

`_BrandMark.cshtml`:

```cshtml
@model int
@{ var gid = "au-mark-" + Guid.NewGuid().ToString("N")[..8]; }
<svg width="@Model" height="@Model" viewBox="0 0 32 32" role="img" aria-label="Patient Portal">
    <defs>
        <linearGradient id="@(gid)" x1="0" y1="0" x2="32" y2="32" gradientUnits="userSpaceOnUse">
            <stop offset="0" stop-color="#6366f1" /><stop offset=".55" stop-color="#8b5cf6" /><stop offset="1" stop-color="#a855f7" />
        </linearGradient>
    </defs>
    <rect x="1" y="1" width="30" height="30" rx="8" fill="#0d1117" stroke="url(#@(gid))" stroke-width="2" />
    @* the glyph — replace with the product's own mark *@
    <path d="M16 9.5v13M9.5 16h13" fill="none" stroke="url(#@(gid))" stroke-width="3" stroke-linecap="round" />
</svg>
```

---

## 8. Content & UX rules for customers

- **Voice:** warm, short, second person ("Your appointment is confirmed"). Buttons are verbs
  ("Book appointment", "Pay now"). No jargon, no internal codes, no ALL-CAPS statuses.
- **Status labels:** map every internal status to a plain label + tone in ONE place per status type,
  and reuse it on every screen.
- **Dates:** relative where it helps ("Tomorrow · 10:30"), otherwise one style app-wide
  (e.g. `Thu, Oct 16 · 10:30`); always the customer's time zone; ISO value in `title`.
- **Money:** currency code/symbol per locale, two decimals, right-aligned in tables.
- **Empty states:** say what's empty *and* offer the next step.
- **Errors:** what happened · what to do · reference code. Never blame the user; never expose internals.
- **Confirmations:** state the consequence and the object ("Cancel your appointment on Oct 16?").
- **Touch:** controls ≥ 40px tall, primary actions reachable without horizontal scrolling at 390px.
- **Performance:** self-hosted, subset fonts; no extra UI frameworks; images sized and lazy-loaded.

---

## 9. Definition of done (per screen)

- [ ] Builds and runs; no console errors.
- [ ] Checked in **light and dark**, at desktop and **390px** (and RTL if the portal supports Arabic).
- [ ] Every form posts and validates exactly as before; success shows a toast, failure an inline message.
- [ ] No raw colours in the view; only kit classes/tokens.
- [ ] Loading, empty (with next step) and friendly error states present.
- [ ] Destructive actions confirmed; icon-only buttons labelled; keyboard path works.
- [ ] Copy reviewed for plain language; statuses use the shared label/tone map.
- [ ] This guide updated if a rule or the kit changed.

---

## Appendices — the kit (verbatim; extract with §0.2, do not retype)

Identical to the Admin Portal's appendices. Appendix A also contains the admin-only shell pieces
(scope selector, command palette, favourites) — they are inert unless their markup is used; leave
them so both portals share one kit.

### Appendix A — `wwwroot/css/aurora.css`

<!-- au-file: wwwroot/css/aurora.css -->
````css
/* Aurora UI kit — aurora.css (tokens · base · components · shell · client extras).
   Ported from the Omni Support Portal (React/MUI) design system. Vendored: fix bugs here,
   keep page-specific styles elsewhere. Pair with aurora.js. */

/* ==========================================================================
   AURORA — design tokens. The ONLY place raw colours live.
   Components read var(--au-*) so light and dark stay in lockstep.
   Mode is chosen by <html data-theme="light|dark"> (stamped before first paint).
   ========================================================================== */
:root {
  color-scheme: light;

  /* --- type ------------------------------------------------------------- */
  --au-font-sans: 'Inter Variable', Inter, ui-sans-serif, system-ui, -apple-system,
    BlinkMacSystemFont, 'Segoe UI', sans-serif;
  --au-font-mono: 'JetBrains Mono Variable', 'JetBrains Mono', ui-monospace, SFMono-Regular,
    Menlo, Consolas, monospace;
  --au-font-icons: 'Material Symbols Outlined';

  /* --- brand (indigo → violet) ------------------------------------------- */
  --au-primary: #4f46e5;
  --au-primary-light: #818cf8;
  --au-primary-dark: #4338ca;
  --au-on-primary: #ffffff;
  --au-secondary: #7c3aed;
  --au-secondary-light: #a78bfa;
  --au-secondary-dark: #6d28d9;
  --au-gradient: linear-gradient(135deg, #6366f1 0%, #8b5cf6 55%, #a855f7 100%);
  --au-gradient-soft: linear-gradient(135deg, rgba(99, 102, 241, 0.08), rgba(168, 85, 247, 0.06));
  --au-brand-glow: rgba(109, 90, 232, 0.55);
  --au-brand-glow-strong: rgba(109, 90, 232, 0.7);

  /* --- semantic ---------------------------------------------------------- */
  --au-success: #059669;
  --au-success-dark: #047857;
  --au-on-success: #ffffff;
  --au-warning: #d97706;
  --au-warning-dark: #b45309;
  --au-on-warning: #ffffff;
  --au-error: #e11d48;
  --au-error-dark: #be123c;
  --au-on-error: #ffffff;
  --au-info: #0284c7;
  --au-info-dark: #0369a1;
  --au-on-info: #ffffff;
  /* toast fills (MUI "filled" alert) */
  --au-toast-success: var(--au-success);
  --au-toast-warning: var(--au-warning);
  --au-toast-error: var(--au-error);
  --au-toast-info: var(--au-info);
  --au-on-toast-success: #ffffff;
  --au-on-toast-warning: #ffffff;
  --au-on-toast-error: #ffffff;
  --au-on-toast-info: #ffffff;

  /* --- surfaces & text --------------------------------------------------- */
  --au-bg: #f2f5fa;
  --au-paper: #ffffff;
  --au-text: #0b1220;
  --au-text-2: #5b6b82; /* secondary */
  --au-text-3: #9aa8bc; /* disabled / hints */
  --au-divider: rgba(15, 23, 42, 0.08);
  --au-hover: rgba(0, 0, 0, 0.04);
  --au-selected: rgba(0, 0, 0, 0.08);
  --au-disabled: rgba(0, 0, 0, 0.26);
  --au-disabled-bg: rgba(0, 0, 0, 0.12);
  --au-input-bg: #ffffff;
  --au-input-border: #dbe3ee;
  --au-input-border-hover: #b9c5d6;
  --au-label-notch: #ffffff; /* solid colour of an input's fill, used to cut the label notch */
  --au-code-bg: #f6f8fc;
  --au-table-head-bg: #f7f9fc; /* must be SOLID (sticky header) */
  --au-glass: rgba(255, 255, 255, 0.72);
  --au-sheen: linear-gradient(180deg, rgba(255, 255, 255, 0.75) 0%, rgba(255, 255, 255, 0) 60%);
  --au-mix-bg: #ffffff; /* alert tint base   */
  --au-mix-fg: #000000; /* alert text base   */
  --au-hero-a: 12%;
  --au-hero-b: 8%;

  /* --- elevation --------------------------------------------------------- */
  --au-shadow-paper: 0 1px 2px rgba(15, 23, 42, 0.04), 0 12px 32px -18px rgba(15, 23, 42, 0.14);
  --au-shadow-pop: 0 4px 10px rgba(15, 23, 42, 0.06), 0 20px 44px -16px rgba(15, 23, 42, 0.24);
  --au-shadow-toast: 0 3px 5px -1px rgba(0, 0, 0, 0.2), 0 6px 10px 0 rgba(0, 0, 0, 0.14),
    0 1px 18px 0 rgba(0, 0, 0, 0.12);
  --au-backdrop: rgba(15, 23, 42, 0.4);
  --au-tooltip-bg: #0f172a;
  --au-focus-ring: 0 0 0 3px rgba(79, 70, 229, 0.16);
  --au-focus-ring-error: 0 0 0 3px rgba(225, 29, 72, 0.16);

  /* --- controls ---------------------------------------------------------- */
  --au-segment-bg: rgba(11, 18, 32, 0.045);
  --au-segment-selected: #ffffff;
  --au-segment-shadow: 0 1px 3px rgba(15, 23, 42, 0.12);
  --au-meter-track: rgba(11, 18, 32, 0.08);
  --au-select-chevron: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%235b6b82'%3E%3Cpath d='M7 10l5 5 5-5z'/%3E%3C/svg%3E");
  --au-scroll-thumb: #c3cddc;
  --au-scroll-thumb-hover: #94a3b8;

  /* --- ambient aurora backdrop ------------------------------------------- */
  --au-aurora: radial-gradient(1000px 520px at 88% -12%, rgba(99, 102, 241, 0.09), transparent 62%),
    radial-gradient(820px 460px at -8% 28%, rgba(168, 85, 247, 0.06), transparent 60%),
    radial-gradient(900px 520px at 55% 112%, rgba(14, 165, 233, 0.05), transparent 62%);

  /* --- sidebar: deliberately DARK in BOTH modes -------------------------- */
  --au-side-bg: linear-gradient(180deg, #0b1020 0%, #0a0e1c 55%, #0b0f1f 100%);
  --au-side-bg-solid: #0b1020;
  --au-side-hover: rgba(148, 163, 184, 0.08);
  --au-side-text: #8b9bb5;
  --au-side-text-strong: #eef2f9;
  --au-side-section: #5d6d89;
  --au-side-active-bg: linear-gradient(90deg, rgba(99, 102, 241, 0.22) 0%, rgba(139, 92, 246, 0.1) 100%);
  --au-side-active-text: #e0e7ff;
  --au-side-active-icon: #a5b4fc;
  --au-side-border: rgba(148, 163, 184, 0.1);

  /* --- shape ------------------------------------------------------------- */
  --au-r-xs: 8px; /* chips, menu items, toggle buttons, tooltips */
  --au-r-sm: 10px; /* buttons, inputs, icon buttons, tables, default panel */
  --au-r-md: 12px; /* menus, popovers, alerts, accordions, toasts */
  --au-r-card: 14px; /* .au-card */
  --au-r-dialog: 18px; /* dialogs, auth card */
  --au-r-pill: 20px; /* sidebar rows, sidebar filter */
  --au-r-control: 25px; /* header pills: search, scope, environment */
  --au-r-panel: 30px; /* toolbars, stat tiles, status cards, dashboard panels */
  --au-r-hero: 40px; /* landing hero */

  /* --- motion ------------------------------------------------------------ */
  --au-ease-out: cubic-bezier(0.2, 0.8, 0.3, 1);
  --au-t-fast: 0.12s;
  --au-t: 0.15s;
  --au-t-slow: 0.26s;

  /* --- layout ------------------------------------------------------------ */
  --au-sidebar-w: 272px;
  --au-sidebar-w-collapsed: 76px;
  --au-header-h: 64px;
  --au-z-header: 10;
  --au-z-sidebar: 20;
  --au-z-toast: 1400;
}

:root[data-theme='dark'] {
  color-scheme: dark;

  --au-primary: #818cf8;
  --au-primary-light: #a5b4fc;
  --au-primary-dark: #6366f1;
  --au-on-primary: #ffffff;
  --au-secondary: #a78bfa;
  --au-secondary-light: #c4b5fd;
  --au-secondary-dark: #8b5cf6;
  --au-gradient-soft: linear-gradient(135deg, rgba(129, 140, 248, 0.14), rgba(168, 85, 247, 0.1));

  --au-success: #34d399;
  --au-success-dark: #10b981;
  --au-on-success: rgba(0, 0, 0, 0.87);
  --au-warning: #fbbf24;
  --au-warning-dark: #f59e0b;
  --au-on-warning: rgba(0, 0, 0, 0.87);
  --au-error: #fb7185;
  --au-error-dark: #f43f5e;
  --au-on-error: rgba(0, 0, 0, 0.87);
  --au-info: #38bdf8;
  --au-info-dark: #0ea5e9;
  --au-on-info: rgba(0, 0, 0, 0.87);
  --au-toast-success: var(--au-success-dark);
  --au-toast-warning: var(--au-warning-dark);
  --au-toast-error: var(--au-error-dark);
  --au-toast-info: var(--au-info-dark);
  --au-on-toast-success: rgba(0, 0, 0, 0.87);
  --au-on-toast-warning: rgba(0, 0, 0, 0.87);
  --au-on-toast-error: #ffffff;
  --au-on-toast-info: rgba(0, 0, 0, 0.87);

  --au-bg: #070b15;
  --au-paper: #0d1526;
  --au-text: #e7edf8;
  --au-text-2: #93a5c0;
  --au-text-3: #556685;
  --au-divider: rgba(148, 163, 184, 0.14);
  --au-hover: rgba(255, 255, 255, 0.08);
  --au-selected: rgba(255, 255, 255, 0.16);
  --au-disabled: rgba(255, 255, 255, 0.3);
  --au-disabled-bg: rgba(255, 255, 255, 0.12);
  --au-input-bg: rgba(148, 163, 184, 0.05);
  --au-input-border: rgba(148, 163, 184, 0.2);
  --au-input-border-hover: rgba(148, 163, 184, 0.36);
  --au-label-notch: #111a2b;
  --au-code-bg: #0a101d;
  --au-table-head-bg: #111a2b;
  --au-glass: rgba(9, 13, 24, 0.72);
  --au-sheen: linear-gradient(180deg, rgba(255, 255, 255, 0.05) 0%, rgba(255, 255, 255, 0.012) 42%,
      rgba(255, 255, 255, 0) 100%);
  --au-mix-bg: #000000;
  --au-mix-fg: #ffffff;
  --au-hero-a: 28%;
  --au-hero-b: 16%;

  --au-shadow-paper: 0 1px 2px rgba(0, 0, 0, 0.35), 0 16px 40px -22px rgba(0, 0, 0, 0.55);
  --au-shadow-pop: 0 4px 10px rgba(0, 0, 0, 0.35), 0 24px 48px -16px rgba(0, 0, 0, 0.6);
  --au-backdrop: rgba(2, 6, 16, 0.6);
  --au-tooltip-bg: #1c2740;
  --au-focus-ring: 0 0 0 3px rgba(129, 140, 248, 0.24);
  --au-focus-ring-error: 0 0 0 3px rgba(251, 113, 133, 0.24);

  --au-segment-bg: rgba(231, 237, 248, 0.08);
  --au-segment-selected: rgba(148, 163, 184, 0.16);
  --au-segment-shadow: 0 1px 3px rgba(0, 0, 0, 0.4);
  --au-meter-track: rgba(231, 237, 248, 0.1);
  --au-select-chevron: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='%2393a5c0'%3E%3Cpath d='M7 10l5 5 5-5z'/%3E%3C/svg%3E");
  --au-scroll-thumb: #33415a;
  --au-scroll-thumb-hover: #48597a;

  --au-aurora: radial-gradient(1000px 520px at 88% -12%, rgba(99, 102, 241, 0.16), transparent 62%),
    radial-gradient(820px 460px at -8% 28%, rgba(168, 85, 247, 0.1), transparent 60%),
    radial-gradient(900px 520px at 55% 112%, rgba(14, 165, 233, 0.08), transparent 62%);
}

/* Optional alternate brand ramp (teal → cyan). Put data-brand="teal" on <html>.
   Only brand tokens change; every other token is shared. */
:root[data-brand='teal'] {
  --au-primary: #0d9488;
  --au-primary-light: #2dd4bf;
  --au-primary-dark: #0f766e;
  --au-secondary: #0891b2;
  --au-secondary-light: #22d3ee;
  --au-secondary-dark: #0e7490;
  --au-gradient: linear-gradient(135deg, #0d9488 0%, #0891b2 55%, #06b6d4 100%);
  --au-gradient-soft: linear-gradient(135deg, rgba(13, 148, 136, 0.08), rgba(6, 182, 212, 0.06));
  --au-brand-glow: rgba(8, 145, 178, 0.5);
  --au-brand-glow-strong: rgba(8, 145, 178, 0.65);
  --au-focus-ring: 0 0 0 3px rgba(13, 148, 136, 0.16);
  --au-side-active-bg: linear-gradient(90deg, rgba(13, 148, 136, 0.24) 0%, rgba(6, 182, 212, 0.1) 100%);
  --au-side-active-text: #ccfbf1;
  --au-side-active-icon: #5eead4;
}
:root[data-brand='teal'][data-theme='dark'] {
  --au-primary: #2dd4bf;
  --au-primary-light: #5eead4;
  --au-primary-dark: #14b8a6;
  --au-on-primary: #052823;
  --au-secondary: #22d3ee;
  --au-secondary-light: #67e8f9;
  --au-secondary-dark: #06b6d4;
  --au-gradient-soft: linear-gradient(135deg, rgba(45, 212, 191, 0.14), rgba(34, 211, 238, 0.1));
  --au-focus-ring: 0 0 0 3px rgba(45, 212, 191, 0.24);
}

/* ==========================================================================
   AURORA — base: document, typography, ambient backdrop, motion, scrollbars
   ========================================================================== */
*,
*::before,
*::after {
  box-sizing: border-box;
}

html {
  font-size: 16px; /* rem base — do NOT shrink it (the Razor template sets 14px; remove that) */
  -webkit-text-size-adjust: 100%;
}

body {
  margin: 0;
  min-width: 320px;
  min-height: 100vh;
  font-family: var(--au-font-sans);
  font-size: 0.875rem;
  line-height: 1.5;
  color: var(--au-text);
  background-color: var(--au-bg);
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

/* Ambient aurora backdrop — fixed, non-interactive, behind everything. */
body::before {
  content: '';
  position: fixed;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  background: var(--au-aurora);
}

::selection {
  background: color-mix(in srgb, var(--au-primary) 24%, transparent);
}

button,
input,
textarea,
select {
  font: inherit;
  color: inherit;
}

a {
  color: inherit;
  text-decoration: none;
}

/* Links inside running text / tables that should look like links. */
.au-link {
  color: var(--au-primary);
  font-weight: 600;
  text-decoration: none;
}
.au-link:hover {
  text-decoration: underline;
}
/* Plain links inside page content (scaffolded "Edit | Details | Delete" etc.) */
.au-main a:not([class]) {
  color: var(--au-primary);
  font-weight: 500;
}
.au-main a:not([class]):hover {
  text-decoration: underline;
}

pre,
code,
kbd,
samp {
  font-family: var(--au-font-mono);
}
pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
}

img,
svg {
  vertical-align: middle;
}

/* --- headings (sizes/weights from the Aurora type scale) ----------------- */
h1,
.au-h1 {
  font-size: 1.6rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  line-height: 1.2;
  margin: 0 0 0.5rem;
}
h2,
.au-h2 {
  font-size: 1.3rem;
  font-weight: 800;
  letter-spacing: -0.015em;
  line-height: 1.25;
  margin: 0 0 0.5rem;
}
h3,
.au-h3 {
  font-size: 1.05rem;
  font-weight: 750;
  letter-spacing: -0.01em;
  line-height: 1.3;
  margin: 0 0 0.5rem;
}
h4,
.au-h4 {
  font-size: 0.95rem;
  font-weight: 750;
  letter-spacing: -0.01em;
  margin: 0 0 0.5rem;
}
h5,
h6,
.au-h5 {
  font-size: 0.875rem;
  font-weight: 700;
  margin: 0 0 0.5rem;
}
p {
  margin: 0 0 0.75rem;
}

/* --- text utilities ------------------------------------------------------ */
.au-muted {
  color: var(--au-text-2);
}
.au-faint {
  color: var(--au-text-3);
}
.au-caption {
  font-size: 0.75rem;
  color: var(--au-text-2);
}
.au-eyebrow {
  display: block;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--au-text-2);
}
.au-mono {
  font-family: var(--au-font-mono);
  font-size: 12.5px;
}
.au-num {
  font-variant-numeric: tabular-nums;
}
.au-strong {
  font-weight: 650;
}
.au-ellipsis {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
.au-text-success {
  color: var(--au-success);
}
.au-text-warning {
  color: var(--au-warning);
}
.au-text-error {
  color: var(--au-error);
}
.au-text-info {
  color: var(--au-info);
}
.au-text-primary {
  color: var(--au-primary);
}
.au-gradient-text {
  background-image: var(--au-gradient);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
}

/* --- layout helpers ------------------------------------------------------ */
.au-stack {
  display: flex;
  flex-direction: column;
  gap: var(--au-gap, 16px);
}
.au-row {
  display: flex;
  align-items: center;
  gap: var(--au-gap, 8px);
  min-width: 0;
}
.au-row--wrap {
  flex-wrap: wrap;
}
.au-row--between {
  justify-content: space-between;
}
.au-spacer {
  flex: 1;
}
.au-grid {
  display: grid;
  gap: var(--au-gap, 16px);
}
.au-grid--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.au-grid--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
.au-grid--4 {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}
@media (max-width: 1100px) {
  .au-grid--4 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .au-grid--3 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (max-width: 640px) {
  .au-grid--2,
  .au-grid--3 {
    grid-template-columns: minmax(0, 1fr);
  }
}
.au-sr-only {
  position: absolute !important;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

/* --- icons: Material Symbols (same glyph family as @mui/icons-material) --
   <span class="au-icon" aria-hidden="true">search</span>                  */
.au-icon {
  font-family: var(--au-font-icons);
  font-weight: normal;
  font-style: normal;
  font-size: 20px;
  line-height: 1;
  letter-spacing: normal;
  text-transform: none;
  display: inline-block;
  width: 1em; /* if the font is missing, the ligature name is clipped, not spilled */
  height: 1em;
  overflow: hidden;
  white-space: nowrap;
  word-wrap: normal;
  direction: ltr;
  flex-shrink: 0;
  user-select: none;
  -webkit-font-smoothing: antialiased;
  font-feature-settings: 'liga';
  font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 20;
}
.au-icon--fill {
  font-variation-settings: 'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 20;
}
.au-icon--xs {
  font-size: 14px;
}
.au-icon--sm {
  font-size: 16px;
}
.au-icon--md {
  font-size: 18px;
}
.au-icon--lg {
  font-size: 24px;
}

/* --- motion primitives --------------------------------------------------- */
@keyframes auPageIn {
  from {
    opacity: 0.3;
    transform: translateY(6px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
@keyframes auFadeSlideIn {
  from {
    opacity: 0;
    transform: translateY(4px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
@keyframes auSpin {
  to {
    transform: rotate(360deg);
  }
}
@keyframes auIndeterminate {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(320%);
  }
}
@keyframes auPulse {
  0% {
    opacity: 1;
  }
  50% {
    opacity: 0.4;
  }
  100% {
    opacity: 1;
  }
}
@keyframes auSlideInRight {
  from {
    opacity: 0;
    transform: translateX(24px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
@keyframes auDialogIn {
  from {
    opacity: 0;
    transform: translateY(8px) scale(0.98);
  }
  to {
    opacity: 1;
    transform: none;
  }
}
@keyframes auDrawerIn {
  from {
    transform: translateX(100%);
  }
  to {
    transform: none;
  }
}
@keyframes auFadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

.au-animate-in {
  animation: auFadeSlideIn 0.24s ease;
}
.au-spin {
  animation: auSpin 0.9s linear infinite;
}

@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.001s !important;
    transition-duration: 0.001s !important;
  }
  /* looping indicators: stop outright instead of strobing */
  .au-spin,
  .au-spinner,
  .au-progress--indeterminate .au-progress__bar,
  .au-topbar-progress__bar,
  .au-skeleton {
    animation: none !important;
  }
}

/* --- refined thin scrollbars, theme-aware -------------------------------- */
* {
  scrollbar-width: thin;
  scrollbar-color: var(--au-scroll-thumb) transparent;
}
*::-webkit-scrollbar {
  width: 10px;
  height: 10px;
}
*::-webkit-scrollbar-track {
  background: transparent;
}
*::-webkit-scrollbar-thumb {
  background-color: var(--au-scroll-thumb);
  border-radius: 8px;
  border: 2px solid transparent;
  background-clip: content-box;
}
*::-webkit-scrollbar-thumb:hover {
  background-color: var(--au-scroll-thumb-hover);
}

/* ==========================================================================
   AURORA — components. Every colour comes from a token; never hard-code one.
   ========================================================================== */

/* --- surfaces ------------------------------------------------------------ */
/* Panel = MUI <Paper variant="outlined">: hairline border, soft shadow and a
   faint "glass top-light" (sheen) so surfaces catch the aurora backdrop. */
.au-panel {
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-sm);
  box-shadow: var(--au-shadow-paper);
  min-width: 0;
}
.au-panel--round {
  border-radius: var(--au-r-panel);
}
.au-panel--pad {
  padding: 20px;
}
.au-panel--pad-sm {
  padding: 12px;
}
.au-panel__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 16px;
  min-width: 0;
}
.au-panel__title {
  margin: 0;
  font-size: 0.875rem;
  font-weight: 700;
  letter-spacing: 0;
}
.au-panel__link {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--au-primary);
  white-space: nowrap;
}
.au-panel__link:hover {
  text-decoration: underline;
}
.au-card {
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-card);
  box-shadow: var(--au-shadow-paper);
  padding: 16px;
}
.au-divider {
  height: 1px;
  border: 0;
  margin: 16px 0;
  background: var(--au-divider);
}
.au-divider-label {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 16px 0 8px;
  font-size: 0.75rem;
  color: var(--au-text-2);
}
.au-divider-label::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--au-divider);
}

/* --- buttons ------------------------------------------------------------- */
.au-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 36px;
  padding: 6px 14px;
  border: 1px solid transparent;
  border-radius: var(--au-r-sm);
  background: transparent;
  color: var(--au-text);
  font-size: 0.875rem;
  font-weight: 650;
  line-height: 1.5;
  letter-spacing: 0;
  text-transform: none;
  white-space: nowrap;
  text-decoration: none;
  cursor: pointer;
  user-select: none;
  transition: transform var(--au-t-fast) ease, box-shadow 0.18s ease, background-color 0.18s ease,
    border-color 0.18s ease, filter 0.18s ease, color 0.18s ease;
}
.au-btn:hover {
  background-color: var(--au-hover);
  text-decoration: none;
}
.au-btn:active {
  transform: scale(0.98);
}
.au-btn:focus-visible {
  outline: none;
  box-shadow: var(--au-focus-ring);
}
.au-btn .au-icon {
  font-size: 18px;
  margin-inline: -2px;
}
/* contained primary — the brand gradient CTA */
.au-btn--primary {
  color: var(--au-on-primary);
  background-color: var(--au-primary);
  background-image: var(--au-gradient);
  box-shadow: 0 2px 10px -2px var(--au-brand-glow);
}
.au-btn--primary:hover {
  background-color: var(--au-primary);
  filter: brightness(1.07);
  box-shadow: 0 4px 18px -4px var(--au-brand-glow-strong);
}
/* outlined neutral — secondary actions, Export, Refresh, date range */
.au-btn--outlined {
  border-color: var(--au-input-border);
  background-color: var(--au-paper);
}
.au-btn--outlined:hover {
  border-color: var(--au-input-border-hover);
  background-color: color-mix(in srgb, var(--au-text) 4%, var(--au-paper));
}
.au-btn--outlined-primary {
  border-color: color-mix(in srgb, var(--au-primary) 50%, transparent);
  color: var(--au-primary);
}
.au-btn--outlined-primary:hover {
  border-color: var(--au-primary);
  background-color: color-mix(in srgb, var(--au-primary) 4%, transparent);
}
/* text button */
.au-btn--text {
  padding-inline: 8px;
}
.au-btn--text-primary {
  padding-inline: 8px;
  color: var(--au-primary);
}
.au-btn--text-primary:hover {
  background-color: color-mix(in srgb, var(--au-primary) 6%, transparent);
}
/* semantic contained (destructive confirms etc.) */
.au-btn--danger {
  color: var(--au-on-error);
  background-color: var(--au-error);
}
.au-btn--danger:hover {
  background-color: var(--au-error-dark);
}
.au-btn--success {
  color: var(--au-on-success);
  background-color: var(--au-success);
}
.au-btn--success:hover {
  background-color: var(--au-success-dark);
}
.au-btn--warning {
  color: var(--au-on-warning);
  background-color: var(--au-warning);
}
.au-btn--warning:hover {
  background-color: var(--au-warning-dark);
}
.au-btn--danger-outlined {
  border-color: color-mix(in srgb, var(--au-error) 50%, transparent);
  color: var(--au-error);
}
.au-btn--danger-outlined:hover {
  border-color: var(--au-error);
  background-color: color-mix(in srgb, var(--au-error) 5%, transparent);
}
/* sizes */
.au-btn--sm {
  min-height: 30px;
  padding: 4px 10px;
  font-size: 0.8125rem;
}
.au-btn--lg {
  min-height: 42px;
  padding: 8px 22px;
  font-size: 0.9375rem;
}
.au-btn--block {
  display: flex;
  width: 100%;
}
/* disabled / busy */
.au-btn:disabled,
.au-btn.is-disabled,
.au-btn[aria-disabled='true'] {
  color: var(--au-disabled);
  background-image: none;
  box-shadow: none;
  filter: none;
  cursor: default;
  pointer-events: none;
}
.au-btn--primary:disabled,
.au-btn--primary.is-disabled,
.au-btn--danger:disabled,
.au-btn--success:disabled,
.au-btn--warning:disabled {
  background-color: var(--au-disabled-bg);
}
.au-btn--outlined:disabled,
.au-btn--outlined.is-disabled {
  border-color: var(--au-disabled-bg);
}
.au-btn[aria-busy='true'] {
  pointer-events: none;
  opacity: 0.85;
}
.au-btn[aria-busy='true']::before {
  content: '';
  width: 14px;
  height: 14px;
  border: 2px solid currentColor;
  border-right-color: transparent;
  border-radius: 50%;
  animation: auSpin 0.75s linear infinite;
}

/* --- icon buttons -------------------------------------------------------- */
.au-icon-btn {
  display: inline-grid;
  place-items: center;
  width: 36px;
  height: 36px;
  padding: 0;
  border: 0;
  border-radius: var(--au-r-sm);
  background: transparent;
  color: var(--au-text-2);
  cursor: pointer;
  flex-shrink: 0;
  transition: background-color var(--au-t) ease, color var(--au-t) ease,
    transform var(--au-t-fast) ease, border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-icon-btn:hover {
  background-color: color-mix(in srgb, var(--au-text) 6%, transparent);
  color: var(--au-text);
}
.au-icon-btn:active {
  transform: scale(0.94);
}
.au-icon-btn:focus-visible {
  outline: none;
  box-shadow: var(--au-focus-ring);
}
.au-icon-btn:disabled,
.au-icon-btn.is-disabled {
  color: var(--au-disabled);
  pointer-events: none;
}
.au-icon-btn--bordered {
  border: 1px solid var(--au-divider);
  background-color: var(--au-paper);
}
.au-icon-btn--bordered:hover {
  background-color: var(--au-paper);
  color: var(--au-primary);
}
.au-icon-btn--primary:hover {
  color: var(--au-primary);
}
.au-icon-btn--sm {
  width: 28px;
  height: 28px;
  border-radius: var(--au-r-xs);
}
.au-icon-btn--sm .au-icon {
  font-size: 17px;
}
.au-icon-btn--xs {
  width: 20px;
  height: 20px;
  border-radius: 6px;
}
.au-icon-btn--xs .au-icon {
  font-size: 13px;
}

/* --- form controls ------------------------------------------------------- */
.au-input,
.au-select,
.au-textarea {
  display: block;
  width: 100%;
  min-height: 40px;
  padding: 7.5px 14px;
  border: 1px solid var(--au-input-border);
  border-radius: var(--au-r-sm);
  background-color: var(--au-input-bg);
  color: var(--au-text);
  font-size: 1rem;
  line-height: 1.5;
  outline: 0;
  appearance: none;
  transition: border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-textarea {
  min-height: 96px;
  resize: vertical;
}
.au-input::placeholder,
.au-textarea::placeholder {
  color: color-mix(in srgb, var(--au-text) 42%, transparent);
  opacity: 1;
}
.au-input:hover,
.au-select:hover,
.au-textarea:hover {
  border-color: var(--au-input-border-hover);
}
.au-input:focus,
.au-select:focus,
.au-textarea:focus {
  border-color: var(--au-primary);
  box-shadow: var(--au-focus-ring);
}
.au-input:disabled,
.au-select:disabled,
.au-textarea:disabled,
.au-input[readonly] {
  background-color: var(--au-hover);
  color: var(--au-text-2);
  cursor: not-allowed;
}
.au-select {
  padding-right: 36px;
  background-image: var(--au-select-chevron);
  background-repeat: no-repeat;
  background-position: right 8px center;
  background-size: 22px;
  cursor: pointer;
}
.au-select[multiple],
.au-select[size] {
  background-image: none;
  padding-right: 14px;
}
.au-input--sm,
.au-select--sm {
  min-height: 34px;
  padding-top: 4px;
  padding-bottom: 4px;
  font-size: 0.875rem;
}
.au-input[type='datetime-local'],
.au-input[type='date'] {
  font-size: 0.9375rem;
}
:root[data-theme='dark'] .au-input::-webkit-calendar-picker-indicator {
  filter: invert(0.75);
}
.au-select option {
  background-color: var(--au-paper);
  color: var(--au-text);
}

/* Input with a leading/trailing icon */
.au-input-icon {
  position: relative;
  display: block;
  min-width: 0;
}
.au-input-icon > .au-icon {
  position: absolute;
  left: 12px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--au-text-3);
  font-size: 20px;
  pointer-events: none;
}
.au-input-icon > .au-input {
  padding-left: 40px;
}

/* Field with a NOTCHED label (the MUI outlined look). The label sits on the
   top border; the bottom half of its background is the input fill, which
   hides the border line behind it on any surface. */
.au-field {
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.au-field > .au-label {
  position: absolute;
  top: 0;
  left: 10px;
  z-index: 1;
  max-width: calc(100% - 20px);
  transform: translateY(-50%);
  padding: 0 5px;
  border-radius: 4px;
  background: linear-gradient(to bottom, transparent 45%, var(--au-label-notch) 45%);
  color: var(--au-text-2);
  font-size: 0.75rem;
  font-weight: 500;
  line-height: 1.2;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  pointer-events: none;
  transition: color var(--au-t) ease;
}
.au-field:focus-within > .au-label {
  color: var(--au-primary);
}
.au-field.is-invalid > .au-label,
.au-field:has(.input-validation-error) > .au-label {
  color: var(--au-error);
}
/* Stacked label — long forms and customer-facing screens */
.au-form-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.au-form-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--au-text);
}
.au-form-label .au-required {
  color: var(--au-error);
  margin-left: 2px;
}
.au-help {
  font-size: 0.75rem;
  color: var(--au-text-2);
}
.au-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px 16px;
}
.au-form-grid > .au-span-2 {
  grid-column: 1 / -1;
}
@media (max-width: 640px) {
  .au-form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
.au-form-actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 8px;
  margin-top: 20px;
}

/* Validation — matches ASP.NET unobtrusive validation class names */
.field-validation-error,
.au-field-error {
  display: block;
  margin-top: 4px;
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--au-error);
}
.field-validation-valid {
  display: none;
}
.input-validation-error,
.au-input.is-invalid,
.au-select.is-invalid,
.au-textarea.is-invalid {
  border-color: var(--au-error) !important;
}
.input-validation-error:focus,
.au-input.is-invalid:focus {
  box-shadow: var(--au-focus-ring-error) !important;
}
.validation-summary-errors ul {
  margin: 0;
  padding-left: 18px;
}
.validation-summary-valid {
  display: none;
}

/* checkbox / radio — native controls, brand accent */
.au-check {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  font-size: 0.875rem;
}
/* :where() = zero specificity, so Bootstrap .form-check-input layout still wins */
:where(input[type='checkbox'], input[type='radio']) {
  accent-color: var(--au-primary);
  width: 16px;
  height: 16px;
  margin: 0;
  cursor: pointer;
}
/* switch: <input type="checkbox" class="au-switch" role="switch"> */
.au-switch {
  appearance: none;
  position: relative;
  width: 36px !important;
  height: 20px !important;
  margin: 0;
  border-radius: 999px;
  background: var(--au-disabled-bg);
  cursor: pointer;
  flex-shrink: 0;
  transition: background-color var(--au-t) ease;
}
.au-switch::after {
  content: '';
  position: absolute;
  top: 2px;
  left: 2px;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  background: #ffffff;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
  transition: transform 0.18s ease;
}
.au-switch:checked {
  background-color: var(--au-primary);
  background-image: var(--au-gradient);
}
.au-switch:checked::after {
  transform: translateX(16px);
}
.au-switch:focus-visible {
  outline: none;
  box-shadow: var(--au-focus-ring);
}

/* --- chips (status / badges) --------------------------------------------- */
.au-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  max-width: 100%;
  height: 22px;
  padding: 0 8px;
  border: 1px solid transparent;
  border-radius: var(--au-r-xs);
  background-color: var(--au-selected);
  color: var(--au-text);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
  vertical-align: middle;
}
.au-chip > span {
  overflow: hidden;
  text-overflow: ellipsis;
}
.au-chip .au-icon {
  font-size: 15px;
}
.au-chip--md {
  height: 28px;
  padding: 0 11px;
}
.au-chip--success {
  background-color: var(--au-success);
  color: var(--au-on-success);
}
.au-chip--warning {
  background-color: var(--au-warning);
  color: var(--au-on-warning);
}
.au-chip--error {
  background-color: var(--au-error);
  color: var(--au-on-error);
}
.au-chip--info {
  background-color: var(--au-info);
  color: var(--au-on-info);
}
.au-chip--primary {
  background-color: var(--au-primary);
  color: var(--au-on-primary);
}
/* outlined = quieter; neutral border unless a tone is set */
.au-chip--outlined {
  background-color: transparent;
  border-color: var(--au-input-border);
  color: var(--au-text);
}
.au-chip--outlined.au-chip--success {
  border-color: color-mix(in srgb, var(--au-success) 70%, transparent);
  color: var(--au-success);
}
.au-chip--outlined.au-chip--warning {
  border-color: color-mix(in srgb, var(--au-warning) 70%, transparent);
  color: var(--au-warning);
}
.au-chip--outlined.au-chip--error {
  border-color: color-mix(in srgb, var(--au-error) 70%, transparent);
  color: var(--au-error);
}
.au-chip--outlined.au-chip--info {
  border-color: color-mix(in srgb, var(--au-info) 70%, transparent);
  color: var(--au-info);
}
.au-chip--outlined.au-chip--primary {
  border-color: color-mix(in srgb, var(--au-primary) 70%, transparent);
  color: var(--au-primary);
}
/* soft = tinted pill (verdicts, counters) */
.au-chip--soft {
  --c: var(--au-text);
  border-color: color-mix(in srgb, var(--au-text) 12%, transparent);
  background-color: color-mix(in srgb, var(--c) 8%, transparent);
  color: var(--c);
  font-weight: 700;
}
.au-chip--soft.au-chip--success {
  --c: var(--au-success);
}
.au-chip--soft.au-chip--warning {
  --c: var(--au-warning);
}
.au-chip--soft.au-chip--error {
  --c: var(--au-error);
}
.au-chip--soft.au-chip--info {
  --c: var(--au-info);
}
.au-chip--soft.au-chip--primary {
  --c: var(--au-primary);
}
a.au-chip,
button.au-chip {
  cursor: pointer;
  transition: filter var(--au-t) ease, background-color var(--au-t) ease;
}
a.au-chip:hover,
button.au-chip:hover {
  filter: brightness(0.97);
  text-decoration: none;
}
.au-chip__delete {
  display: inline-grid;
  place-items: center;
  margin-right: -4px;
  padding: 0;
  border: 0;
  background: none;
  color: inherit;
  opacity: 0.7;
  cursor: pointer;
}
.au-chip__delete:hover {
  opacity: 1;
}
.au-dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
  background: var(--au-text-3);
}
.au-dot--success {
  background: var(--au-success);
}
.au-dot--warning {
  background: var(--au-warning);
}
.au-dot--error {
  background: var(--au-error);
}
.au-dot--info {
  background: var(--au-info);
}

/* --- filter toolbar ------------------------------------------------------ */
.au-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 16px;
  padding: 12px;
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-panel);
  box-shadow: var(--au-shadow-paper);
}
.au-toolbar__filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.au-toolbar__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  margin-left: auto;
}
.au-toolbar__meta {
  font-size: 0.75rem;
  color: var(--au-text-2);
  white-space: nowrap;
}
@media (max-width: 900px) {
  .au-toolbar__filters {
    flex-direction: column;
    align-items: stretch;
    width: 100%;
  }
  .au-toolbar__filters > * {
    width: 100% !important;
  }
  .au-toolbar__actions {
    flex-shrink: 1;
    width: 100%;
    min-width: 0;
    margin-left: 0;
  }
}

/* --- data table ---------------------------------------------------------- */
.au-table-card {
  position: relative;
  overflow: hidden;
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-sm);
  box-shadow: var(--au-shadow-paper);
}
.au-table-card__loading {
  height: 4px;
  overflow: hidden;
  background: transparent;
}
.au-table-card.is-loading .au-table-card__loading {
  background: color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.au-table-card.is-loading .au-table-card__loading::before {
  content: '';
  display: block;
  width: 40%;
  height: 100%;
  border-radius: 999px;
  background-image: var(--au-gradient);
  animation: auIndeterminate 1.1s ease-in-out infinite;
}
.au-table-scroll {
  overflow: auto;
  max-height: var(--au-table-max-h, none);
}
.au-table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: 0.875rem;
  font-variant-numeric: tabular-nums;
}
.au-table thead th {
  position: sticky;
  top: 0;
  z-index: 2;
  height: 44px;
  padding: 6px 10px;
  border-bottom: 1px solid var(--au-divider);
  background: var(--au-table-head-bg);
  color: var(--au-text-2);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-align: left;
  text-transform: uppercase;
  white-space: nowrap;
  vertical-align: middle;
}
.au-table tbody td {
  height: 44px;
  max-width: var(--au-col-max, 420px);
  padding: 6px 10px;
  border-bottom: 1px solid var(--au-divider);
  vertical-align: middle;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  transition: background-color var(--au-t-fast) ease;
}
.au-table tbody tr:last-child td {
  border-bottom: 0;
}
.au-table .au-wrap,
.au-table td.au-wrap {
  white-space: normal;
  word-break: break-word;
}
.au-table .au-right {
  text-align: right;
}
.au-table .au-center {
  text-align: center;
}
.au-table .au-shrink {
  width: 1%;
}
.au-table tbody tr[data-au-href],
.au-table--clickable tbody tr {
  cursor: pointer;
}
.au-table tbody tr[data-au-href]:hover td,
.au-table--clickable tbody tr:hover td {
  background-color: color-mix(in srgb, var(--au-primary) 5%, transparent);
}
.au-table tbody tr.is-selected td {
  background-color: color-mix(in srgb, var(--au-primary) 7%, transparent);
}
.au-table tbody tr[data-au-href]:focus-visible {
  outline: 2px solid var(--au-primary);
  outline-offset: -2px;
}
/* sortable header link */
.au-th-sort {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  color: inherit;
}
.au-th-sort .au-icon {
  display: inline-grid;
  place-items: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  font-size: 16px;
  color: var(--au-text-2);
  transition: background-color var(--au-t) ease;
}
.au-th-sort:hover .au-icon {
  background-color: var(--au-hover);
  color: var(--au-text);
}
.au-th-sort.is-sorted .au-icon {
  color: var(--au-text);
}
/* two-line cell: code + subtitle */
.au-cell-2l {
  display: flex;
  flex-direction: column;
  line-height: 1.25;
  min-width: 0;
}
.au-cell-2l > :first-child {
  font-family: var(--au-font-mono);
  font-size: 12.5px;
  overflow: hidden;
  text-overflow: ellipsis;
}
.au-cell-2l > :last-child {
  font-size: 12px;
  color: var(--au-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
}
.au-row-actions {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

/* pager (table footer) */
.au-pager {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 10px;
  border-top: 1px solid var(--au-divider);
}
.au-pager__count {
  color: var(--au-text-2);
  font-variant-numeric: tabular-nums;
}
.au-pager__count strong {
  color: var(--au-text);
  font-weight: 650;
}
.au-pager__nav {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
}
.au-pager__size {
  width: 96px;
}
.au-pager__btn {
  width: 34px;
  height: 34px;
  border: 1px solid var(--au-divider);
}
.au-pager__page {
  min-width: 70px;
  text-align: center;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

/* empty state */
.au-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  min-height: 220px;
  padding: 40px 16px;
  text-align: center;
  color: var(--au-text-2);
  font-weight: 500;
}
.au-empty__icon {
  display: grid;
  place-items: center;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  background: color-mix(in srgb, var(--au-text) 4.5%, transparent);
  color: var(--au-text-3);
}
.au-empty__icon .au-icon {
  font-size: 24px;
}

/* --- KPI stat tile -------------------------------------------------------- */
.au-stat {
  height: 100%;
  padding: 16px;
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-panel);
  box-shadow: var(--au-shadow-paper);
}
.au-stat__value {
  font-size: 26px;
  font-weight: 800;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
  color: var(--au-text);
}
.au-stat__hint {
  font-size: 0.75rem;
  color: var(--au-text-2);
}

/* --- status board (one health card per area, coloured left rail) -------- */
.au-status-board__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}
.au-status-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
@media (min-width: 900px) {
  .au-status-grid {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}
.au-status {
  --au-level: var(--au-text-3);
  position: relative;
  display: block;
  height: 100%;
  overflow: hidden;
  padding: 14px 14px 14px 18px;
  color: inherit;
  text-decoration: none;
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-panel);
  box-shadow: var(--au-shadow-paper);
  transition: border-color var(--au-t) ease, transform var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-status::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 4px;
  background: var(--au-level);
}
a.au-status:hover {
  border-color: var(--au-primary-light);
  transform: translateY(-2px);
  box-shadow: 0 12px 30px -22px color-mix(in srgb, var(--au-primary) 50%, transparent);
  text-decoration: none;
}
.au-status[data-level='crit'] {
  --au-level: var(--au-error);
}
.au-status[data-level='warn'] {
  --au-level: var(--au-warning);
}
.au-status[data-level='ok'] {
  --au-level: var(--au-success);
}
.au-status[data-level='idle']::before {
  opacity: 0.5;
}
.au-status__head {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
  min-width: 0;
  color: var(--au-text-2);
}
.au-status__head .au-icon {
  font-size: 16px;
}
.au-status__head .au-eyebrow {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-status__head .au-dot {
  background: var(--au-level);
}
.au-status__metric {
  font-size: 22px;
  font-weight: 800;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}
.au-status[data-level='crit'] .au-status__metric {
  color: var(--au-error);
}
.au-status__sub {
  display: block;
  font-size: 0.75rem;
  color: var(--au-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* --- key/value fields ---------------------------------------------------- */
.au-kv {
  display: grid;
  gap: 14px;
}
.au-kv--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.au-kv--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}
@media (max-width: 640px) {
  .au-kv--2,
  .au-kv--3 {
    grid-template-columns: minmax(0, 1fr);
  }
}
.au-kv__item {
  min-width: 0;
}
.au-kv__value {
  font-size: 0.875rem;
  word-break: break-word;
}
.au-detail-rows {
  display: flex;
  flex-direction: column;
}
.au-detail-row {
  display: flex;
  gap: 8px;
  padding: 7px 0;
  border-bottom: 1px solid var(--au-divider);
}
.au-detail-row > dt,
.au-detail-row > .au-detail-row__label {
  min-width: 130px;
  margin: 0;
  color: var(--au-text-2);
}
.au-detail-row > dd,
.au-detail-row > .au-detail-row__value {
  flex: 1;
  margin: 0;
  word-break: break-word;
}
@media (max-width: 640px) {
  .au-detail-row {
    flex-direction: column;
    gap: 2px;
  }
}

/* --- copyable id, code block, kbd ---------------------------------------- */
.au-copy {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  min-width: 0;
  max-width: 100%;
}
.au-copy > .au-mono {
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-code {
  margin: 0;
  padding: 10px 12px;
  max-height: 320px;
  overflow: auto;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-md);
  background: var(--au-code-bg);
  color: var(--au-text);
  font-family: var(--au-font-mono);
  font-size: 12.5px;
  line-height: 1.55;
  white-space: pre-wrap;
  word-break: break-word;
}
.au-kbd {
  display: inline-block;
  padding: 0 6px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-xs);
  background: var(--au-bg);
  color: var(--au-text-2);
  font-family: var(--au-font-mono);
  font-size: 11px;
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
}

/* --- tabs (gradient indicator) ------------------------------------------- */
.au-tabs {
  display: flex;
  overflow-x: auto;
  border-bottom: 1px solid var(--au-divider);
  scrollbar-width: none;
}
.au-tabs::-webkit-scrollbar {
  display: none;
}
.au-tab {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  padding: 8px 16px;
  border: 0;
  background: none;
  color: var(--au-text-2);
  font-size: 0.875rem;
  font-weight: 650;
  white-space: nowrap;
  cursor: pointer;
  transition: color var(--au-t) ease;
}
.au-tab:hover {
  color: var(--au-text);
  text-decoration: none;
}
.au-tab .au-icon {
  font-size: 18px;
}
.au-tab.is-active,
.au-tab[aria-selected='true'] {
  color: var(--au-primary);
}
.au-tab.is-active::after,
.au-tab[aria-selected='true']::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 3px;
  border-radius: 3px;
  background-image: var(--au-gradient);
}
.au-tab__count {
  padding: 0 6px;
  border-radius: 999px;
  background: var(--au-selected);
  color: var(--au-text-2);
  font-size: 11px;
  font-weight: 700;
}

/* --- segmented toggle (MUI ToggleButtonGroup) ---------------------------- */
.au-segmented {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  border-radius: var(--au-r-sm);
  background: var(--au-segment-bg);
}
.au-segmented > a,
.au-segmented > button,
.au-segmented > label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 11px;
  border: 0;
  border-radius: var(--au-r-xs);
  background: transparent;
  color: var(--au-text-2);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1.6;
  white-space: nowrap;
  cursor: pointer;
  transition: background-color var(--au-t) ease, color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-segmented > a:hover,
.au-segmented > button:hover,
.au-segmented > label:hover {
  color: var(--au-text);
  text-decoration: none;
}
.au-segmented > .is-active,
.au-segmented > [aria-pressed='true'],
.au-segmented > input:checked + label {
  background: var(--au-segment-selected);
  color: var(--au-primary);
  box-shadow: var(--au-segment-shadow);
}
.au-segmented > input {
  position: absolute;
  opacity: 0;
  pointer-events: none;
}

/* --- alerts --------------------------------------------------------------- */
.au-alert {
  --c: var(--au-info);
  display: flex;
  align-items: flex-start;
  gap: 12px;
  margin: 8px 0;
  padding: 6px 16px;
  border-radius: var(--au-r-md);
  background-color: color-mix(in srgb, var(--c) 10%, var(--au-mix-bg));
  color: color-mix(in srgb, var(--c) 40%, var(--au-mix-fg));
  font-weight: 500;
  line-height: 1.43;
}
.au-alert--success {
  --c: var(--au-success);
}
.au-alert--warning {
  --c: var(--au-warning);
}
.au-alert--error {
  --c: var(--au-error);
}
.au-alert__icon {
  padding-top: 7px;
  color: var(--c);
  font-size: 22px;
}
.au-alert__body {
  flex: 1;
  min-width: 0;
  padding: 8px 0;
  word-break: break-word;
}
.au-alert__title {
  margin-bottom: 2px;
  font-weight: 700;
}
.au-alert__actions {
  display: flex;
  align-items: center;
  gap: 2px;
  padding-top: 4px;
  margin-left: auto;
  margin-right: -8px;
}
.au-alert__actions .au-btn,
.au-alert__actions .au-icon-btn {
  color: inherit;
}

/* Error panel — the ONE way a failed call/operation is shown.
   Headline · cause · What to check · the failing call · <details> · Copy */
.au-error-panel .au-alert__hint {
  margin-top: 6px;
  font-size: 0.875rem;
  opacity: 0.9;
}
.au-error-panel .au-alert__call {
  margin-top: 6px;
  font-family: var(--au-font-mono);
  font-size: 0.75rem;
  opacity: 0.85;
  word-break: break-all;
}
.au-error-panel details {
  margin-top: 8px;
}
.au-error-panel summary {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
  list-style: none;
}
.au-error-panel summary::-webkit-details-marker {
  display: none;
}
.au-error-panel summary::after {
  content: 'expand_more';
  font-family: var(--au-font-icons);
  font-size: 18px;
  transition: transform var(--au-t) ease;
}
.au-error-panel details[open] summary::after {
  transform: rotate(180deg);
}
.au-error-panel dl {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 4px 16px;
  margin: 10px 0 0;
  font-size: 12.5px;
}
.au-error-panel dt {
  font-weight: 700;
  opacity: 0.85;
}
.au-error-panel dd {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
}
.au-error-panel pre {
  margin-top: 10px;
  padding: 8px;
  max-height: 240px;
  overflow: auto;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-sm);
  background: var(--au-code-bg);
  color: var(--au-text);
  font-size: 11.5px;
  white-space: pre;
}

/* --- toasts (bottom-right, filled, auto-dismiss) ------------------------- */
.au-toasts {
  position: fixed;
  right: 24px;
  bottom: 24px;
  z-index: var(--au-z-toast);
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-width: min(440px, calc(100vw - 48px));
  pointer-events: none;
}
.au-toast {
  --c: var(--au-toast-info);
  --on: var(--au-on-toast-info);
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 6px 8px 6px 16px;
  border-radius: var(--au-r-md);
  background: var(--c);
  color: var(--on);
  box-shadow: var(--au-shadow-toast);
  font-weight: 500;
  line-height: 1.43;
  word-break: break-word;
  pointer-events: auto;
  animation: auSlideInRight 0.22s var(--au-ease-out);
}
.au-toast--success {
  --c: var(--au-toast-success);
  --on: var(--au-on-toast-success);
}
.au-toast--warning {
  --c: var(--au-toast-warning);
  --on: var(--au-on-toast-warning);
}
.au-toast--error {
  --c: var(--au-toast-error);
  --on: var(--au-on-toast-error);
}
.au-toast__msg {
  flex: 1;
  padding: 8px 0;
}
.au-toast .au-icon-btn {
  color: inherit;
}
.au-toast .au-icon-btn:hover {
  background-color: rgba(255, 255, 255, 0.16);
}
.au-toast.is-leaving {
  opacity: 0;
  transform: translateX(24px);
  transition: opacity 0.2s ease, transform 0.2s ease;
}

/* --- dialog (native <dialog>) -------------------------------------------- */
dialog.au-dialog {
  width: calc(100% - 32px);
  max-width: 444px;
  max-height: calc(100% - 64px);
  padding: 0;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-dialog);
  background: var(--au-paper);
  color: var(--au-text);
  box-shadow: var(--au-shadow-pop);
  overflow: auto;
}
dialog.au-dialog--sm {
  max-width: 600px;
}
dialog.au-dialog--md {
  max-width: 900px;
}
dialog.au-dialog--lg {
  max-width: 1200px;
}
dialog.au-dialog[open] {
  animation: auDialogIn 0.2s var(--au-ease-out);
}
dialog.au-dialog::backdrop,
dialog.au-drawer::backdrop,
dialog.au-palette::backdrop {
  background: var(--au-backdrop);
  backdrop-filter: blur(5px);
  animation: auFadeIn 0.2s ease;
}
.au-dialog__title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin: 0;
  padding: 16px 24px;
  font-size: 1.02rem;
  font-weight: 750;
  letter-spacing: -0.01em;
}
.au-dialog__content {
  padding: 0 24px 20px;
}
.au-dialog__text {
  margin: 0;
  color: var(--au-text-2);
  font-size: 1rem;
}
.au-dialog__actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: 8px;
  padding: 8px 24px 16px;
}

/* --- drawer (right-side detail panel; native <dialog>) ------------------- */
dialog.au-drawer {
  position: fixed;
  inset: 0 0 0 auto;
  width: 460px;
  max-width: 100%;
  height: 100%;
  max-height: 100%;
  margin: 0;
  padding: 0;
  border: 0;
  border-left: 1px solid var(--au-divider);
  background: var(--au-paper);
  color: var(--au-text);
  box-shadow: var(--au-shadow-pop);
  overflow: auto;
}
dialog.au-drawer--wide {
  width: 720px;
}
dialog.au-drawer[open] {
  animation: auDrawerIn 0.26s var(--au-ease-out);
}
.au-drawer__head {
  position: sticky;
  top: 0;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 16px 20px 8px;
  background: var(--au-paper);
}
.au-drawer__title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
}
.au-drawer__body {
  padding: 8px 20px 24px;
}
.au-drawer__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
}

/* --- menus / dropdowns (<details class="au-dropdown">) ------------------- */
.au-dropdown {
  position: relative;
  display: inline-block;
}
.au-dropdown > summary {
  list-style: none;
  cursor: pointer;
}
.au-dropdown > summary::-webkit-details-marker {
  display: none;
}
.au-menu {
  position: absolute;
  z-index: 30;
  top: calc(100% + 6px);
  min-width: 200px;
  padding: 6px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-md);
  background-color: color-mix(in srgb, var(--au-paper) 92%, transparent);
  backdrop-filter: blur(14px);
  box-shadow: var(--au-shadow-pop);
  animation: auFadeSlideIn 0.16s ease;
}
.au-menu--end {
  right: 0;
}
.au-menu__caption {
  display: block;
  padding: 6px 10px 4px;
  color: var(--au-text-3);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}
.au-menu__item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 6px 10px;
  border: 0;
  border-radius: var(--au-r-xs);
  background: none;
  color: var(--au-text);
  font-size: 0.875rem;
  text-align: left;
  cursor: pointer;
}
.au-menu__item:hover {
  background-color: var(--au-hover);
  text-decoration: none;
}
.au-menu__item.is-selected {
  background-color: color-mix(in srgb, var(--au-primary) 10%, transparent);
}
.au-menu__item .au-icon {
  color: var(--au-text-2);
  font-size: 18px;
}
.au-menu__item--danger {
  color: var(--au-error);
}
.au-menu__item--danger .au-icon {
  color: inherit;
}
.au-menu__sep {
  height: 1px;
  margin: 6px 0;
  background: var(--au-divider);
}

/* --- tooltip: <button data-au-tip="Copy"> (CSS only) --------------------- */
[data-au-tip] {
  position: relative;
}
[data-au-tip]::after {
  content: attr(data-au-tip);
  position: absolute;
  z-index: 50;
  left: 50%;
  top: calc(100% + 6px);
  transform: translate(-50%, -2px);
  padding: 6px 10px;
  border-radius: var(--au-r-xs);
  background: var(--au-tooltip-bg);
  color: #ffffff;
  box-shadow: 0 6px 20px -6px rgba(0, 0, 0, 0.45);
  font-family: var(--au-font-sans);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.4;
  letter-spacing: 0;
  text-transform: none;
  white-space: nowrap;
  opacity: 0;
  pointer-events: none;
  transition: opacity var(--au-t) ease 0.25s, transform var(--au-t) ease 0.25s;
}
[data-au-tip]:hover::after,
[data-au-tip]:focus-visible::after {
  opacity: 1;
  transform: translate(-50%, 0);
}
[data-au-tip-pos='right']::after {
  left: calc(100% + 8px);
  top: 50%;
  transform: translate(-2px, -50%);
}
[data-au-tip-pos='right']:hover::after,
[data-au-tip-pos='right']:focus-visible::after {
  transform: translate(0, -50%);
}
[data-au-tip-pos='left']::after {
  left: auto;
  right: 0;
  transform: translate(0, -2px);
}
[data-au-tip-pos='left']:hover::after {
  transform: none;
}

/* --- progress, meters, loading ------------------------------------------- */
.au-progress {
  height: 4px;
  overflow: hidden;
  border-radius: 999px;
  background: color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.au-progress__bar {
  width: var(--value, 0%);
  height: 100%;
  border-radius: 999px;
  background-image: var(--au-gradient);
  transition: width 0.4s ease;
}
.au-progress--indeterminate .au-progress__bar {
  width: 40%;
  animation: auIndeterminate 1.1s ease-in-out infinite;
}
/* channel/usage meter */
.au-meter {
  height: 6px;
  overflow: hidden;
  border-radius: 999px;
  background: var(--au-meter-track);
}
.au-meter__fill {
  width: var(--value, 0%);
  height: 100%;
  border-radius: 999px;
  background-image: var(--au-gradient);
  transition: width 0.4s ease;
}
.au-meter--success .au-meter__fill {
  background: var(--au-success);
}
.au-meter--warning .au-meter__fill {
  background: var(--au-warning);
}
.au-meter--error .au-meter__fill {
  background: var(--au-error);
}
.au-skeleton {
  display: block;
  border-radius: var(--au-r-sm);
  background-color: color-mix(in srgb, var(--au-text) 6%, transparent);
  animation: auPulse 1.5s ease-in-out 0.5s infinite;
}
.au-skeleton--text {
  height: 0.9em;
  margin: 0.2em 0;
  border-radius: 4px;
}
.au-spinner {
  display: inline-block;
  width: 18px;
  height: 18px;
  border: 2px solid var(--au-primary);
  border-right-color: transparent;
  border-radius: 50%;
  animation: auSpin 0.75s linear infinite;
  vertical-align: middle;
}

/* --- page-level patterns ------------------------------------------------- */
/* Back header for detail pages: ← Title / subtitle */
.au-back-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  min-width: 0;
}
.au-back-header__title {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 700;
  letter-spacing: -0.015em;
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-back-header__sub {
  display: block;
  font-size: 0.75rem;
  color: var(--au-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* Landing hero — a lit glass header */
.au-hero {
  position: relative;
  display: flex;
  align-items: center;
  gap: 16px;
  overflow: hidden;
  padding: 24px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-hero);
  box-shadow: var(--au-shadow-paper);
  background-color: var(--au-paper);
  background-image: radial-gradient(680px 220px at 88% -40%,
      color-mix(in srgb, var(--au-primary) var(--au-hero-a), transparent), transparent 70%),
    radial-gradient(520px 220px at 8% 140%,
      color-mix(in srgb, var(--au-secondary) var(--au-hero-b), transparent), transparent 68%);
}
.au-hero__mark {
  flex-shrink: 0;
  line-height: 0;
  filter: drop-shadow(0 10px 24px color-mix(in srgb, var(--au-primary) 50%, transparent));
}
.au-hero__title {
  margin: 0;
  font-size: 1.3rem;
  font-weight: 800;
  letter-spacing: -0.02em;
}
.au-hero__sub {
  margin: 0;
  color: var(--au-text-2);
  font-size: 1rem;
}
@media (max-width: 640px) {
  .au-hero {
    padding: 20px;
    border-radius: var(--au-r-panel);
  }
}
/* 404 / error page */
.au-fullpage-msg {
  display: grid;
  grid-template-columns: minmax(0, 1fr); /* the column may not grow past the screen to fit a long id */
  place-items: center;
  padding: 80px 16px;
}
.au-fullpage-msg__inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  width: 100%; /* a grid item sizes to its content: without this a long request id overflows a phone */
  max-width: 560px;
  text-align: center;
}
.au-fullpage-msg__code {
  font-size: 72px;
  font-weight: 800;
  line-height: 1;
  background-image: var(--au-gradient);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
}
/* soft brand-tinted icon tile (feature cards, list avatars) */
.au-icon-tile {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: var(--au-r-xs);
  background: color-mix(in srgb, var(--tile, var(--au-primary)) 12%, transparent);
  color: var(--tile, var(--au-primary));
  flex-shrink: 0;
}
:root[data-theme='dark'] .au-icon-tile {
  background: color-mix(in srgb, var(--tile, var(--au-primary)) 18%, transparent);
}
.au-icon-tile .au-icon {
  font-size: 18px;
}
.au-avatar {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background-image: var(--au-gradient);
  color: #ffffff;
  font-size: 12px;
  font-weight: 700;
  flex-shrink: 0;
}

/* ==========================================================================
   AURORA — app shell: dark sidebar · glass sticky header · section tabs ·
   main column. Collapsed rail = <html class="au-side-collapsed">.
   Mobile (<900px) = off-canvas sidebar, <html class="au-side-open">.
   ========================================================================== */
.au-app {
  display: flex;
  min-height: 100vh;
}

/* --- sidebar ------------------------------------------------------------- */
.au-sidebar {
  position: sticky;
  top: 0;
  z-index: var(--au-z-sidebar);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  align-self: flex-start;
  width: var(--au-sidebar-w);
  height: 100vh;
  overflow: hidden;
  background: var(--au-side-bg);
  border-right: 1px solid var(--au-side-border);
  color: var(--au-side-text);
  transition: width 0.2s ease, transform 0.22s var(--au-ease-out);
}
.au-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 20px 20px 12px;
  color: inherit;
  text-decoration: none;
}
.au-brand:hover {
  text-decoration: none;
}
.au-brand__mark {
  flex-shrink: 0;
  line-height: 0;
  filter: drop-shadow(0 6px 14px rgba(0, 0, 0, 0.45));
}
.au-brand__text {
  min-width: 0;
}
.au-brand__name {
  display: block;
  color: var(--au-side-text-strong);
  font-size: 15px;
  font-weight: 800;
  line-height: 1.15;
  letter-spacing: -0.01em;
  white-space: nowrap;
}
.au-brand__sub {
  display: block;
  color: var(--au-side-text);
  font-size: 11px;
  white-space: nowrap;
}
.au-side-rule {
  margin: 0 20px 4px;
  border-bottom: 1px solid var(--au-side-border);
}
.au-side-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 4px 16px 4px;
  padding: 3.2px 8px;
  border: 1px solid var(--au-side-border);
  border-radius: var(--au-r-pill);
  background: var(--au-side-hover);
}
.au-side-filter .au-icon {
  font-size: 16px;
  color: var(--au-side-section);
}
.au-side-filter input {
  flex: 1;
  min-width: 0;
  padding: 4px 0 5px;
  border: 0;
  outline: 0;
  background: transparent;
  color: var(--au-side-text-strong);
  font-size: 13px;
  line-height: 1.4375;
}
.au-side-filter input::placeholder {
  color: var(--au-side-section);
  opacity: 1;
}
.au-side-filter button {
  display: none;
  padding: 2px;
  border: 0;
  border-radius: 6px;
  background: none;
  color: var(--au-side-section);
  cursor: pointer;
}
.au-side-filter.has-value button {
  display: inline-grid;
}
.au-side-filter button:hover {
  color: var(--au-side-text-strong);
}
.au-side-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 4px 0 8px;
  scrollbar-color: #33415a transparent;
}
.au-side-scroll::-webkit-scrollbar-thumb {
  background-color: #33415a;
}
/* section divider: tiny uppercase caption + hairline */
.au-side-section {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 18px 20px 6px;
  color: var(--au-side-section);
  font-size: 10px;
  font-weight: 750;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  white-space: nowrap;
  user-select: none;
}
.au-side-section::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--au-side-border);
}
.au-side-section .au-icon {
  font-size: 12px;
}
/* a leaf row (link) */
.au-nav-item {
  position: relative;
  display: flex;
  align-items: center;
  margin: 0 8px;
  padding: 8.8px 32px 8.8px 10px;
  border-radius: var(--au-r-pill);
  color: var(--au-side-text);
  font-size: 13px;
  font-weight: 500;
  line-height: 1.5;
  text-decoration: none;
  transition: background-color var(--au-t) ease, color var(--au-t) ease, transform var(--au-t) ease;
}
.au-nav-item .au-icon {
  margin-right: 10px;
  font-size: 20px;
  color: inherit;
}
.au-nav-item__label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-nav-item::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  width: 3px;
  height: 0;
  border-radius: 999px;
  background-image: var(--au-gradient);
  transform: translateY(-50%);
  transition: height 0.18s ease;
}
.au-nav-item:hover {
  background-color: var(--au-side-hover);
  color: var(--au-side-text-strong);
  transform: translateX(2px);
  text-decoration: none;
}
.au-nav-item.is-active {
  background: var(--au-side-active-bg);
  color: var(--au-side-active-text);
  font-weight: 700;
}
.au-nav-item.is-active .au-icon {
  color: var(--au-side-active-icon);
}
.au-nav-item.is-active::before {
  height: 16px;
}
.au-nav-item:focus-visible {
  outline: 2px solid var(--au-side-active-icon);
  outline-offset: -2px;
}
/* favourite star, revealed on hover */
.au-nav-row {
  position: relative;
}
.au-nav-star {
  position: absolute;
  right: 12px;
  top: 50%;
  display: grid;
  place-items: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border: 0;
  border-radius: 6px;
  background: none;
  color: var(--au-side-section);
  opacity: 0;
  transform: translateY(-50%);
  cursor: pointer;
  transition: opacity var(--au-t) ease, color var(--au-t) ease;
}
.au-nav-star .au-icon {
  font-size: 15px;
}
.au-nav-row:hover .au-nav-star,
.au-nav-star:focus-visible,
.au-nav-star.is-on {
  opacity: 1;
}
.au-nav-star:hover,
.au-nav-star.is-on {
  color: var(--au-primary-light);
}
.au-nav-star.is-on .au-icon {
  font-variation-settings: 'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 20;
}
/* an expandable group (<details>) */
.au-nav-group {
  margin-bottom: 6px;
}
.au-nav-group--nested {
  margin-bottom: 2px;
}
.au-nav-group > summary {
  display: flex;
  align-items: center;
  margin: 0 8px;
  padding: 8.8px 8px 8.8px 10px;
  border-radius: var(--au-r-pill);
  color: var(--au-side-section);
  font-size: 11px;
  font-weight: 750;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  line-height: 1.5;
  list-style: none;
  cursor: pointer;
  transition: background-color var(--au-t) ease, color var(--au-t) ease;
}
.au-nav-group--nested > summary {
  padding-top: 8px;
  padding-bottom: 8px;
  color: var(--au-side-text);
  font-size: 12.5px;
  font-weight: 650;
  letter-spacing: 0.01em;
  text-transform: none;
}
.au-nav-group > summary::-webkit-details-marker {
  display: none;
}
.au-nav-group > summary:hover {
  background-color: var(--au-side-hover);
  color: var(--au-side-text-strong);
}
.au-nav-group > summary .au-icon:first-child {
  margin-right: 10px;
  font-size: 20px;
}
.au-nav-group > summary .au-nav-item__label {
  flex: 1;
}
.au-nav-group > summary .au-nav-chevron {
  font-size: 18px;
  transition: transform 0.2s ease;
}
.au-nav-group[open] > summary .au-nav-chevron {
  transform: rotate(180deg);
}
.au-nav-children {
  margin: 2px 0 0 18px;
  padding-left: 2px;
  border-left: 1px solid var(--au-side-border);
}
.au-nav-empty {
  padding: 16px 20px;
  color: var(--au-side-section);
  font-size: 12.5px;
}
/* identity footer */
.au-side-footer {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 18px 20px;
  border-top: 1px solid var(--au-side-border);
}
.au-side-footer__text {
  min-width: 0;
}
.au-side-footer__name {
  display: block;
  color: var(--au-side-text-strong);
  font-size: 12.5px;
  font-weight: 650;
  line-height: 1.2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-side-footer__email {
  display: block;
  color: var(--au-side-text);
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* filtering mode: flat list of matching leaves */
.au-sidebar.is-filtering .au-side-section,
.au-sidebar.is-filtering .au-nav-group > summary,
.au-sidebar.is-filtering [data-au-favorites] {
  display: none;
}
.au-sidebar.is-filtering .au-nav-children {
  display: block !important;
  margin: 0;
  padding: 0;
  border: 0;
}
.au-sidebar.is-filtering .au-nav-row.is-filtered-out {
  display: none;
}
/* child views of a collapsed group: only listed while filtering */
.au-nav-row--search-only {
  display: none;
}
.au-sidebar.is-filtering .au-nav-row--search-only:not(.is-filtered-out) {
  display: block;
}

/* --- collapsed icon rail ------------------------------------------------- */
@media (min-width: 900px) {
  html.au-side-collapsed .au-sidebar {
    width: var(--au-sidebar-w-collapsed);
  }
  html.au-side-collapsed .au-brand {
    justify-content: center;
    padding-inline: 0;
  }
  html.au-side-collapsed .au-brand__text,
  html.au-side-collapsed .au-side-filter,
  html.au-side-collapsed .au-side-rule,
  html.au-side-collapsed .au-side-section,
  html.au-side-collapsed [data-au-favorites],
  html.au-side-collapsed .au-nav-item__label,
  html.au-side-collapsed .au-nav-chevron,
  html.au-side-collapsed .au-nav-star,
  html.au-side-collapsed .au-nav-children,
  html.au-side-collapsed .au-side-footer__text {
    display: none !important;
  }
  html.au-side-collapsed .au-side-scroll {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    padding-top: 8px;
  }
  html.au-side-collapsed .au-nav-item,
  html.au-side-collapsed .au-nav-group > summary {
    justify-content: center;
    width: 42px;
    height: 42px;
    margin: 0;
    padding: 0;
  }
  html.au-side-collapsed .au-nav-item::before {
    display: none;
  }
  html.au-side-collapsed .au-nav-item:hover {
    transform: none;
  }
  html.au-side-collapsed .au-nav-item .au-icon,
  html.au-side-collapsed .au-nav-group > summary .au-icon:first-child {
    margin-right: 0;
  }
  html.au-side-collapsed .au-nav-group {
    margin: 0;
  }
  html.au-side-collapsed .au-side-footer {
    justify-content: center;
    padding: 12px 0;
  }
}

/* --- main column + glass header ------------------------------------------ */
.au-main-col {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}
.au-header {
  position: sticky;
  top: 0;
  z-index: var(--au-z-header);
}
.au-header__bar {
  position: relative;
  z-index: 2; /* its dropdowns must paint over the glass tab strip below */
  display: flex;
  align-items: center;
  gap: 12px;
  min-height: var(--au-header-h);
  padding: 10px 24px;
  border-bottom: 1px solid var(--au-divider);
  background: var(--au-glass);
  backdrop-filter: blur(16px) saturate(1.5);
  -webkit-backdrop-filter: blur(16px) saturate(1.5);
}
.au-header__title-wrap {
  min-width: 0;
  animation: auFadeSlideIn 0.24s ease;
}
.au-header__title {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 750;
  letter-spacing: -0.01em;
  line-height: 1.3;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-header__tools {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
}
/* breadcrumb: ancestors are sibling-switcher dropdowns */
.au-crumbs {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  row-gap: 2px;
  margin-bottom: 1px;
}
.au-crumbs__sep {
  margin: 0 2px;
  color: var(--au-text-3);
  font-size: 12px;
}
.au-crumb {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 0 4px;
  border: 0;
  border-radius: 6px;
  background: none;
  color: var(--au-text-2);
  font-size: 12px;
  line-height: 1.3;
  cursor: pointer;
}
.au-crumb .au-icon {
  font-size: 14px;
}
.au-crumb:hover {
  background: var(--au-hover);
  color: var(--au-text);
  text-decoration: none;
}
span.au-crumb {
  cursor: default;
}
span.au-crumb:hover {
  background: none;
  color: var(--au-text-2);
}
/* header pill controls */
.au-search-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-control);
  background: var(--au-paper);
  color: var(--au-text-2);
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
  transition: border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-search-btn .au-icon {
  font-size: 18px;
}
.au-search-btn:hover {
  border-color: var(--au-primary-light);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.au-pill-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-control);
  background: var(--au-paper);
  color: var(--au-text-2);
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.01em;
  cursor: pointer;
  transition: border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-pill-btn .au-icon {
  font-size: 17px;
}
.au-pill-btn:hover {
  border-color: var(--au-primary-light);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.au-pill-btn--danger {
  border-color: var(--au-error);
  background: color-mix(in srgb, var(--au-error) 10%, var(--au-paper));
  color: var(--au-error);
}
/* the global scope selector (one entity: e.g. Clinic) */
.au-scope {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-control);
  background: var(--au-bg);
}
.au-combobox {
  position: relative;
  width: 230px;
}
.au-combobox__input {
  width: 100%;
  min-height: 36px;
  padding: 6px 30px 6px 36px;
  border: 0;
  border-radius: var(--au-r-pill);
  background: var(--au-paper);
  color: var(--au-text);
  font-size: 0.9375rem;
  outline: 0;
  text-overflow: ellipsis;
}
.au-combobox__input:focus {
  box-shadow: var(--au-focus-ring);
}
.au-combobox__input::placeholder {
  color: var(--au-text);
  opacity: 1;
}
.au-combobox > .au-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--au-text-2);
  font-size: 20px;
  pointer-events: none;
}
.au-combobox__caret {
  position: absolute;
  right: 6px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--au-text-2);
  pointer-events: none;
}
.au-combobox__list {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  left: 0;
  z-index: 40;
  max-height: 340px;
  min-width: 260px;
  overflow-y: auto;
  margin: 0;
  padding: 6px;
  list-style: none;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-md);
  background-color: color-mix(in srgb, var(--au-paper) 92%, transparent);
  backdrop-filter: blur(14px);
  box-shadow: var(--au-shadow-pop);
}
.au-combobox__list[hidden] {
  display: none;
}
.au-combobox__opt {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 10px;
  border-radius: var(--au-r-xs);
  cursor: pointer;
}
.au-combobox__opt[aria-selected='true'],
.au-combobox__opt:hover {
  background: var(--au-hover);
}
.au-combobox__opt.is-current {
  background: color-mix(in srgb, var(--au-primary) 10%, transparent);
}
.au-combobox__opt-name {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  font-size: 0.875rem;
}
.au-combobox__opt-name > span:first-child {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-combobox__opt--all .au-combobox__opt-name {
  color: var(--au-text-2);
  font-style: italic;
}
.au-combobox__opt-sub {
  font-size: 11px;
  color: var(--au-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.au-combobox__status {
  padding: 8px 10px;
  color: var(--au-text-2);
  font-size: 0.8125rem;
}
/* active-scope chip in the header */
.au-scope-chip {
  max-width: 260px;
  height: 24px;
  border-color: color-mix(in srgb, var(--au-primary) 70%, transparent);
  color: var(--au-primary);
}
/* section tabs strip (sibling views of the current page) */
.au-section-tabs {
  position: relative;
  z-index: 1;
  padding: 0 16px;
  border-bottom: 1px solid var(--au-divider);
  background: var(--au-glass);
  backdrop-filter: blur(16px) saturate(1.5);
  -webkit-backdrop-filter: blur(16px) saturate(1.5);
}
.au-section-tabs .au-tabs {
  border-bottom: 0;
}
/* one global in-flight signal under the header */
.au-topbar-progress {
  height: 2px;
  overflow: hidden;
  opacity: 0;
  transition: opacity 0.2s ease;
}
.au-topbar-progress.is-active {
  opacity: 1;
}
.au-topbar-progress__bar {
  width: 40%;
  height: 100%;
  background-image: var(--au-gradient);
}
.au-topbar-progress.is-active .au-topbar-progress__bar {
  animation: auIndeterminate 1.1s ease-in-out infinite;
}
/* standing production strip (only if the app can point at production) */
.au-prod-banner {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 3px 16px;
  border-bottom: 1px solid var(--au-error);
  background: color-mix(in srgb, var(--au-error) 14%, var(--au-bg));
  color: var(--au-error);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.04em;
}
.au-prod-banner .au-icon {
  font-size: 15px;
}
/* routed content */
.au-main {
  flex: 1;
  min-width: 0;
  padding: 24px;
}
.au-main--narrow {
  max-width: 1160px;
}

/* --- command palette (Ctrl/⌘+K) ------------------------------------------ */
dialog.au-palette {
  width: calc(100% - 32px);
  max-width: 620px;
  margin-top: 12vh;
  padding: 0;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-dialog);
  background: var(--au-paper);
  color: var(--au-text);
  box-shadow: var(--au-shadow-pop);
  overflow: hidden;
}
dialog.au-palette[open] {
  animation: auDialogIn 0.18s var(--au-ease-out);
}
.au-palette__search {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  border-bottom: 1px solid var(--au-divider);
}
.au-palette__search .au-icon {
  color: var(--au-text-2);
}
.au-palette__search input {
  flex: 1;
  min-width: 0;
  border: 0;
  outline: 0;
  background: transparent;
  font-size: 1rem;
}
.au-palette__list {
  max-height: 52vh;
  overflow-y: auto;
  margin: 0;
  padding: 6px;
  list-style: none;
}
.au-palette__group {
  padding: 8px 10px 4px;
  color: var(--au-text-3);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}
.au-palette__item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border-radius: var(--au-r-sm);
  color: var(--au-text);
  cursor: pointer;
}
.au-palette__item .au-icon {
  color: var(--au-text-2);
}
.au-palette__item[aria-selected='true'] {
  background: color-mix(in srgb, var(--au-primary) 10%, transparent);
}
.au-palette__item[aria-selected='true'] .au-icon {
  color: var(--au-primary);
}
.au-palette__trail {
  margin-left: auto;
  color: var(--au-text-3);
  font-size: 12px;
  white-space: nowrap;
}
.au-palette__foot {
  display: flex;
  gap: 14px;
  padding: 8px 16px;
  border-top: 1px solid var(--au-divider);
  color: var(--au-text-3);
  font-size: 12px;
}

/* --- responsive: off-canvas sidebar under 900px -------------------------- */
.au-scrim {
  display: none;
}
@media (max-width: 899.98px) {
  .au-sidebar {
    position: fixed;
    left: 0;
    top: 0;
    width: var(--au-sidebar-w);
    transform: translateX(-100%);
  }
  html.au-side-open .au-sidebar {
    transform: none;
    box-shadow: var(--au-shadow-pop);
  }
  html.au-side-open .au-scrim {
    display: block;
    position: fixed;
    inset: 0;
    z-index: calc(var(--au-z-sidebar) - 1);
    background: var(--au-backdrop);
    backdrop-filter: blur(3px);
  }
  .au-header__bar {
    flex-wrap: wrap;
    row-gap: 8px;
    padding: 10px 16px;
  }
  /* with a scope selector, tools drop to their own row and it takes the width */
  .au-header__tools:has(.au-scope) {
    order: 3;
    width: 100%;
    margin-left: 0;
    gap: 8px;
    justify-content: flex-end;
  }
  .au-header__tools .au-scope {
    flex: 1;
    min-width: 0;
  }
  .au-header__tools .au-combobox {
    width: 100%;
  }
  .au-main {
    padding: 16px;
  }
  .au-hide-mobile {
    display: none !important;
  }
}
@media (min-width: 900px) {
  .au-show-mobile {
    display: none !important;
  }
}
@media (max-width: 1199.98px) {
  .au-hide-md {
    display: none !important;
  }
}

/* ==========================================================================
   AURORA — client-portal additions (customer-facing screens).
   ========================================================================== */

/* Optional LIGHT sidebar: <nav class="au-sidebar au-sidebar--light">.
   The sidebar reads only --au-side-* tokens, so a variant is a token swap. */
.au-sidebar--light {
  --au-side-bg: var(--au-paper);
  --au-side-hover: var(--au-hover);
  --au-side-text: var(--au-text-2);
  --au-side-text-strong: var(--au-text);
  --au-side-section: var(--au-text-3);
  --au-side-active-bg: var(--au-gradient-soft);
  --au-side-active-text: var(--au-primary);
  --au-side-active-icon: var(--au-primary);
  --au-side-border: var(--au-divider);
  background-image: var(--au-sheen);
}
.au-sidebar--light .au-brand__mark {
  filter: drop-shadow(0 6px 14px color-mix(in srgb, var(--au-primary) 35%, transparent));
}

/* Page head inside content: title + one-line purpose + primary action */
.au-page-head {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px 16px;
  margin-bottom: 20px;
}
.au-page-head__title {
  margin: 0;
  font-size: 1.3rem;
  font-weight: 800;
  letter-spacing: -0.015em;
}
.au-page-head__sub {
  margin: 2px 0 0;
  color: var(--au-text-2);
  font-size: 0.9375rem;
}
.au-page-head__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

/* User menu in the header: <details class="au-dropdown au-user-menu"> */
.au-user-menu > summary {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 3px 8px 3px 3px;
  border: 1px solid var(--au-divider);
  border-radius: 999px;
  background: var(--au-paper);
  color: var(--au-text);
  font-size: 0.875rem;
  font-weight: 600;
  transition: border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-user-menu > summary:hover {
  border-color: var(--au-primary-light);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.au-user-menu > summary .au-icon {
  color: var(--au-text-2);
  font-size: 18px;
}
.au-user-menu .au-menu {
  min-width: 230px;
}
.au-user-menu__who {
  display: block;
  padding: 8px 10px 10px;
  border-bottom: 1px solid var(--au-divider);
  margin-bottom: 6px;
}
.au-user-menu__who strong {
  display: block;
  font-size: 0.875rem;
}
.au-user-menu__who span {
  color: var(--au-text-2);
  font-size: 0.75rem;
}

/* Sign-in / sign-up / reset: a lit glass card on the aurora backdrop */
.au-auth {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 32px 16px;
  overflow: hidden;
}
.au-auth::before {
  content: '';
  position: absolute;
  width: 560px;
  height: 560px;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -62%);
  border-radius: 50%;
  background: radial-gradient(closest-side, color-mix(in srgb, var(--au-primary) 18%, transparent), transparent);
  pointer-events: none;
}
.au-auth__card {
  position: relative;
  width: 100%;
  max-width: 420px;
  padding: 32px;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-dialog);
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  box-shadow: var(--au-shadow-pop);
  animation: auDialogIn 0.3s var(--au-ease-out);
}
.au-auth__brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  margin-bottom: 24px;
  text-align: center;
}
.au-auth__brand .au-brand__mark {
  filter: drop-shadow(0 10px 24px color-mix(in srgb, var(--au-primary) 45%, transparent));
}
.au-auth__title {
  margin: 0;
  font-size: 1.3rem;
  font-weight: 800;
  letter-spacing: -0.02em;
}
.au-auth__sub {
  margin: 0;
  color: var(--au-text-2);
}
.au-auth__form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.au-auth__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  font-size: 0.8125rem;
}
.au-auth__foot {
  margin-top: 20px;
  color: var(--au-text-2);
  font-size: 0.8125rem;
  text-align: center;
}
.au-auth__legal {
  position: relative;
  margin-top: 16px;
  color: var(--au-text-3);
  font-size: 0.75rem;
  text-align: center;
}
@media (max-width: 480px) {
  .au-auth__card {
    padding: 24px 20px;
  }
}

/* Quick-action card (client home): icon tile + title + one line + arrow */
.au-action-card {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  height: 100%;
  padding: 18px;
  color: inherit;
  text-decoration: none;
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-panel);
  box-shadow: var(--au-shadow-paper);
  transition: border-color var(--au-t) ease, transform var(--au-t) ease, box-shadow var(--au-t) ease;
}
.au-action-card:hover {
  border-color: var(--au-primary-light);
  transform: translateY(-2px);
  box-shadow: 0 12px 30px -22px color-mix(in srgb, var(--au-primary) 50%, transparent);
  text-decoration: none;
}
.au-action-card .au-icon-tile {
  width: 40px;
  height: 40px;
  border-radius: var(--au-r-md);
}
.au-action-card .au-icon-tile .au-icon {
  font-size: 22px;
}
.au-action-card__title {
  display: block;
  margin-bottom: 2px;
  font-weight: 700;
}
.au-action-card__text {
  display: block;
  color: var(--au-text-2);
  font-size: 0.8125rem;
}
.au-action-card__arrow {
  margin-left: auto;
  color: var(--au-text-3);
  transition: transform var(--au-t) ease, color var(--au-t) ease;
}
.au-action-card:hover .au-action-card__arrow {
  color: var(--au-primary);
  transform: translateX(3px);
}

/* Stepper for multi-step flows: <ol class="au-steps"><li class="is-done|is-current"> */
.au-steps {
  display: flex;
  gap: 8px;
  margin: 0 0 20px;
  padding: 0;
  list-style: none;
  counter-reset: au-step;
}
.au-steps > li {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 8px;
  min-width: 0;
  color: var(--au-text-2);
  font-size: 0.8125rem;
  font-weight: 600;
  counter-increment: au-step;
}
.au-steps > li::before {
  content: counter(au-step);
  display: grid;
  place-items: center;
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  border: 1px solid var(--au-input-border);
  border-radius: 50%;
  background: var(--au-paper);
  font-size: 12px;
  font-weight: 700;
}
.au-steps > li:not(:last-child)::after {
  content: '';
  flex: 1;
  height: 2px;
  border-radius: 2px;
  background: var(--au-divider);
}
.au-steps > li.is-current {
  color: var(--au-text);
}
.au-steps > li.is-current::before {
  border-color: transparent;
  background-image: var(--au-gradient);
  color: #ffffff;
  box-shadow: 0 2px 10px -2px var(--au-brand-glow);
}
.au-steps > li.is-done::before {
  content: '\2713';
  border-color: transparent;
  background: var(--au-success);
  color: var(--au-on-success);
}
.au-steps > li.is-done::after {
  background: var(--au-success);
}
@media (max-width: 640px) {
  .au-steps > li:not(.is-current) span {
    display: none;
  }
}

/* Responsive table: under 640px every row becomes a card.
   <table class="au-table au-table--stack"> + <td data-label="Status"> */
@media (max-width: 640px) {
  .au-table--stack thead {
    display: none;
  }
  .au-table--stack,
  .au-table--stack tbody,
  .au-table--stack tr,
  .au-table--stack td {
    display: block;
    width: 100%;
  }
  .au-table--stack tbody tr {
    padding: 10px 14px;
    border-bottom: 1px solid var(--au-divider);
  }
  .au-table--stack tbody tr:last-child {
    border-bottom: 0;
  }
  .au-table--stack tbody td {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    height: auto;
    max-width: none;
    padding: 4px 0;
    border: 0;
    white-space: normal;
    text-align: right;
  }
  .au-table--stack tbody td::before {
    content: attr(data-label);
    color: var(--au-text-2);
    font-size: 0.75rem;
    font-weight: 700;
    letter-spacing: 0.05em;
    text-align: left;
    text-transform: uppercase;
  }
  .au-table--stack tbody td:not([data-label])::before {
    content: none;
  }
}
````

### Appendix B — `wwwroot/css/aurora-bootstrap.css` (only if the project uses Bootstrap 5)

<!-- au-file: wwwroot/css/aurora-bootstrap.css -->
````css
/* ==========================================================================
   AURORA — Bootstrap 5 bridge (aurora-bootstrap.css).
   ONLY for a project that already uses Bootstrap. Load it AFTER bootstrap.css
   and aurora.css. It restyles the Bootstrap classes existing pages already use,
   so they take the Aurora look with NO markup changes. Delete sections you don't
   use. Works with 5.1–5.3 (sets both the --bs-* vars and the real properties).
   ========================================================================== */
:root,
[data-bs-theme] {
  --bs-body-font-family: var(--au-font-sans);
  --bs-body-font-size: 0.875rem;
  --bs-body-line-height: 1.5;
  --bs-body-color: var(--au-text);
  --bs-body-bg: var(--au-bg);
  --bs-emphasis-color: var(--au-text);
  --bs-secondary-color: var(--au-text-2);
  --bs-tertiary-color: var(--au-text-3);
  --bs-secondary-bg: var(--au-paper);
  --bs-tertiary-bg: var(--au-table-head-bg);
  --bs-heading-color: inherit;
  --bs-border-color: var(--au-divider);
  --bs-border-color-translucent: var(--au-divider);
  --bs-border-radius: var(--au-r-sm);
  --bs-border-radius-sm: var(--au-r-xs);
  --bs-border-radius-lg: var(--au-r-md);
  --bs-border-radius-xl: var(--au-r-dialog);
  --bs-box-shadow: var(--au-shadow-pop);
  --bs-box-shadow-sm: var(--au-shadow-paper);
  --bs-focus-ring-color: color-mix(in srgb, var(--au-primary) 24%, transparent);
  --bs-code-color: var(--au-text);
  --bs-font-monospace: var(--au-font-mono);
}

/* --- buttons ------------------------------------------------------------- */
.btn {
  --bs-btn-padding-x: 14px;
  --bs-btn-padding-y: 6px;
  --bs-btn-font-size: 0.875rem;
  --bs-btn-font-weight: 650;
  --bs-btn-line-height: 1.5;
  --bs-btn-border-radius: var(--au-r-sm);
  --bs-btn-focus-box-shadow: var(--au-focus-ring);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 36px;
  padding: 6px 14px;
  border-radius: var(--au-r-sm);
  font-size: 0.875rem;
  font-weight: 650;
  line-height: 1.5;
  transition: transform var(--au-t-fast) ease, box-shadow 0.18s ease, background-color 0.18s ease,
    border-color 0.18s ease, filter 0.18s ease, color 0.18s ease;
}
.btn:active {
  transform: scale(0.98);
}
.btn:focus-visible {
  outline: 0;
  box-shadow: var(--au-focus-ring);
}
.btn-sm {
  min-height: 30px;
  padding: 4px 10px;
  font-size: 0.8125rem;
  border-radius: var(--au-r-sm);
}
.btn-lg {
  min-height: 42px;
  padding: 8px 22px;
  font-size: 0.9375rem;
  border-radius: var(--au-r-sm);
}
/* primary = brand gradient CTA */
.btn-primary {
  --bs-btn-color: var(--au-on-primary);
  --bs-btn-bg: var(--au-primary);
  --bs-btn-border-color: transparent;
  --bs-btn-hover-color: var(--au-on-primary);
  --bs-btn-hover-bg: var(--au-primary);
  --bs-btn-hover-border-color: transparent;
  --bs-btn-active-color: var(--au-on-primary);
  --bs-btn-active-bg: var(--au-primary-dark);
  --bs-btn-active-border-color: transparent;
  --bs-btn-disabled-color: var(--au-disabled);
  --bs-btn-disabled-bg: var(--au-disabled-bg);
  --bs-btn-disabled-border-color: transparent;
  color: var(--au-on-primary);
  background-color: var(--au-primary);
  background-image: var(--au-gradient);
  border-color: transparent;
  box-shadow: 0 2px 10px -2px var(--au-brand-glow);
}
.btn.btn-primary:hover,
.btn.btn-primary:focus-visible {
  color: var(--au-on-primary);
  background-color: var(--au-primary);
  border-color: transparent;
  filter: brightness(1.07);
  box-shadow: 0 4px 18px -4px var(--au-brand-glow-strong);
}
.btn.btn-primary:disabled,
.btn.btn-primary.disabled {
  color: var(--au-disabled);
  background-color: var(--au-disabled-bg);
  background-image: none;
  box-shadow: none;
}
/* neutral outlined = secondary actions */
.btn-secondary,
.btn-outline-secondary,
.btn-light,
.btn-outline-light,
.btn-outline-dark,
.btn-default {
  --bs-btn-color: var(--au-text);
  --bs-btn-bg: var(--au-paper);
  --bs-btn-border-color: var(--au-input-border);
  --bs-btn-hover-color: var(--au-text);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-text) 4%, var(--au-paper));
  --bs-btn-hover-border-color: var(--au-input-border-hover);
  --bs-btn-active-color: var(--au-text);
  --bs-btn-active-bg: var(--au-selected);
  --bs-btn-active-border-color: var(--au-input-border-hover);
  --bs-btn-disabled-color: var(--au-disabled);
  --bs-btn-disabled-bg: transparent;
  --bs-btn-disabled-border-color: var(--au-disabled-bg);
  color: var(--au-text);
  background-color: var(--au-paper);
  border-color: var(--au-input-border);
}
.btn.btn-secondary:hover,
.btn.btn-outline-secondary:hover,
.btn.btn-light:hover,
.btn.btn-outline-light:hover,
.btn.btn-outline-dark:hover,
.btn.btn-default:hover {
  color: var(--au-text);
  background-color: color-mix(in srgb, var(--au-text) 4%, var(--au-paper));
  border-color: var(--au-input-border-hover);
}
.btn-outline-primary {
  --bs-btn-color: var(--au-primary);
  --bs-btn-border-color: color-mix(in srgb, var(--au-primary) 50%, transparent);
  --bs-btn-hover-color: var(--au-primary);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-primary) 5%, transparent);
  --bs-btn-hover-border-color: var(--au-primary);
  --bs-btn-active-color: var(--au-primary);
  --bs-btn-active-bg: color-mix(in srgb, var(--au-primary) 10%, transparent);
  --bs-btn-active-border-color: var(--au-primary);
  color: var(--au-primary);
  background-color: transparent;
  border-color: color-mix(in srgb, var(--au-primary) 50%, transparent);
}
.btn.btn-outline-primary:hover {
  color: var(--au-primary);
  background-color: color-mix(in srgb, var(--au-primary) 5%, transparent);
  border-color: var(--au-primary);
}
/* semantic contained */
.btn-danger,
.btn-success,
.btn-warning,
.btn-info {
  --bs-btn-border-color: transparent;
  --bs-btn-hover-border-color: transparent;
  --bs-btn-active-border-color: transparent;
  border-color: transparent;
}
.btn-danger {
  --bs-btn-color: var(--au-on-error);
  --bs-btn-bg: var(--au-error);
  --bs-btn-hover-color: var(--au-on-error);
  --bs-btn-hover-bg: var(--au-error-dark);
  --bs-btn-active-color: var(--au-on-error);
  --bs-btn-active-bg: var(--au-error-dark);
  color: var(--au-on-error);
  background-color: var(--au-error);
}
.btn.btn-danger:hover {
  color: var(--au-on-error);
  background-color: var(--au-error-dark);
}
.btn-success {
  --bs-btn-color: var(--au-on-success);
  --bs-btn-bg: var(--au-success);
  --bs-btn-hover-color: var(--au-on-success);
  --bs-btn-hover-bg: var(--au-success-dark);
  --bs-btn-active-bg: var(--au-success-dark);
  color: var(--au-on-success);
  background-color: var(--au-success);
}
.btn.btn-success:hover {
  color: var(--au-on-success);
  background-color: var(--au-success-dark);
}
.btn-warning {
  --bs-btn-color: var(--au-on-warning);
  --bs-btn-bg: var(--au-warning);
  --bs-btn-hover-color: var(--au-on-warning);
  --bs-btn-hover-bg: var(--au-warning-dark);
  --bs-btn-active-bg: var(--au-warning-dark);
  color: var(--au-on-warning);
  background-color: var(--au-warning);
}
.btn.btn-warning:hover {
  color: var(--au-on-warning);
  background-color: var(--au-warning-dark);
}
.btn-info {
  --bs-btn-color: var(--au-on-info);
  --bs-btn-bg: var(--au-info);
  --bs-btn-hover-color: var(--au-on-info);
  --bs-btn-hover-bg: var(--au-info-dark);
  --bs-btn-active-bg: var(--au-info-dark);
  color: var(--au-on-info);
  background-color: var(--au-info);
}
.btn.btn-info:hover {
  color: var(--au-on-info);
  background-color: var(--au-info-dark);
}
.btn-outline-danger {
  --bs-btn-color: var(--au-error);
  --bs-btn-border-color: color-mix(in srgb, var(--au-error) 50%, transparent);
  --bs-btn-hover-color: var(--au-error);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-error) 5%, transparent);
  --bs-btn-hover-border-color: var(--au-error);
  --bs-btn-active-color: var(--au-error);
  --bs-btn-active-bg: color-mix(in srgb, var(--au-error) 10%, transparent);
  color: var(--au-error);
  background-color: transparent;
  border-color: color-mix(in srgb, var(--au-error) 50%, transparent);
}
.btn.btn-outline-danger:hover {
  color: var(--au-error);
  background-color: color-mix(in srgb, var(--au-error) 5%, transparent);
  border-color: var(--au-error);
}
.btn-outline-success,
.btn-outline-warning,
.btn-outline-info {
  background-color: transparent;
}
.btn-outline-success {
  --bs-btn-color: var(--au-success);
  --bs-btn-border-color: color-mix(in srgb, var(--au-success) 50%, transparent);
  --bs-btn-hover-color: var(--au-success);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-success) 6%, transparent);
  --bs-btn-hover-border-color: var(--au-success);
  color: var(--au-success);
  border-color: color-mix(in srgb, var(--au-success) 50%, transparent);
}
.btn-outline-warning {
  --bs-btn-color: var(--au-warning);
  --bs-btn-border-color: color-mix(in srgb, var(--au-warning) 50%, transparent);
  --bs-btn-hover-color: var(--au-warning);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-warning) 6%, transparent);
  --bs-btn-hover-border-color: var(--au-warning);
  color: var(--au-warning);
  border-color: color-mix(in srgb, var(--au-warning) 50%, transparent);
}
.btn-outline-info {
  --bs-btn-color: var(--au-info);
  --bs-btn-border-color: color-mix(in srgb, var(--au-info) 50%, transparent);
  --bs-btn-hover-color: var(--au-info);
  --bs-btn-hover-bg: color-mix(in srgb, var(--au-info) 6%, transparent);
  --bs-btn-hover-border-color: var(--au-info);
  color: var(--au-info);
  border-color: color-mix(in srgb, var(--au-info) 50%, transparent);
}
.btn-link {
  --bs-btn-color: var(--au-primary);
  --bs-btn-hover-color: var(--au-primary-dark);
  color: var(--au-primary);
  text-decoration: none;
}
.btn.btn-link:hover {
  color: var(--au-primary);
  background-color: color-mix(in srgb, var(--au-primary) 6%, transparent);
  text-decoration: none;
}
:root[data-theme='dark'] .btn-close {
  filter: invert(1) grayscale(100%) brightness(200%);
}

/* --- forms --------------------------------------------------------------- */
.form-control,
.form-select {
  min-height: 40px;
  padding: 7.5px 14px;
  color: var(--au-text);
  background-color: var(--au-input-bg);
  border: 1px solid var(--au-input-border);
  border-radius: var(--au-r-sm);
  font-size: 1rem;
  line-height: 1.5;
  transition: border-color var(--au-t) ease, box-shadow var(--au-t) ease;
}
.form-select {
  padding-right: 36px;
  background-image: var(--au-select-chevron);
  background-repeat: no-repeat;
  background-position: right 8px center;
  background-size: 22px;
}
.form-select[multiple],
.form-select[size]:not([size='1']) {
  background-image: none;
  padding-right: 14px;
}
.form-control::placeholder {
  color: color-mix(in srgb, var(--au-text) 42%, transparent);
  opacity: 1;
}
.form-control:hover,
.form-select:hover {
  border-color: var(--au-input-border-hover);
}
.form-control:focus,
.form-select:focus {
  color: var(--au-text);
  background-color: var(--au-input-bg);
  border-color: var(--au-primary);
  box-shadow: var(--au-focus-ring);
  outline: 0;
}
.form-control:disabled,
.form-select:disabled,
.form-control[readonly] {
  color: var(--au-text-2);
  background-color: var(--au-hover);
}
.form-control-sm,
.form-select-sm {
  min-height: 34px;
  padding-top: 4px;
  padding-bottom: 4px;
  font-size: 0.875rem;
  border-radius: var(--au-r-sm);
}
textarea.form-control {
  min-height: 96px;
}
.form-label,
.col-form-label,
.control-label {
  margin-bottom: 6px;
  color: var(--au-text);
  font-size: 0.8125rem;
  font-weight: 600;
}
.form-text,
.help-block {
  color: var(--au-text-2);
  font-size: 0.75rem;
}
.form-check-input {
  border-color: var(--au-input-border);
  background-color: var(--au-input-bg);
}
.form-check-input:checked {
  background-color: var(--au-primary);
  border-color: var(--au-primary);
}
.form-check-input:focus {
  border-color: var(--au-primary);
  box-shadow: var(--au-focus-ring);
}
.form-check-label {
  font-size: 0.875rem;
}
.input-group-text {
  color: var(--au-text-2);
  background-color: var(--au-table-head-bg);
  border-color: var(--au-input-border);
  border-radius: var(--au-r-sm);
}
.form-floating > label {
  color: var(--au-text-2);
}
.form-floating > .form-control:focus ~ label::after,
.form-floating > .form-control:not(:placeholder-shown) ~ label::after,
.form-floating > .form-select ~ label::after {
  background-color: transparent !important;
}
.is-invalid,
.form-control.is-invalid,
.form-select.is-invalid {
  border-color: var(--au-error) !important;
  background-image: none;
}
.is-invalid:focus {
  box-shadow: var(--au-focus-ring-error) !important;
}
.is-valid,
.form-control.is-valid {
  background-image: none;
}
.invalid-feedback,
.text-danger.field-validation-error {
  color: var(--au-error);
  font-size: 0.75rem;
  font-weight: 500;
}
/* ASP.NET validation summary (<div asp-validation-summary> + .text-danger) */
.validation-summary-errors {
  margin: 8px 0 16px;
  padding: 10px 16px;
  border-radius: var(--au-r-md);
  background-color: color-mix(in srgb, var(--au-error) 10%, var(--au-mix-bg));
  color: color-mix(in srgb, var(--au-error) 40%, var(--au-mix-fg)) !important;
  font-weight: 500;
}

/* --- cards ---------------------------------------------------------------- */
.card {
  --bs-card-bg: var(--au-paper);
  --bs-card-color: var(--au-text);
  --bs-card-border-color: var(--au-divider);
  --bs-card-border-radius: var(--au-r-card);
  --bs-card-inner-border-radius: calc(var(--au-r-card) - 1px);
  --bs-card-cap-bg: transparent;
  --bs-card-spacer-x: 20px;
  --bs-card-spacer-y: 20px;
  color: var(--au-text);
  background-color: var(--au-paper);
  background-image: var(--au-sheen);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-card);
  box-shadow: var(--au-shadow-paper);
}
.card-header,
.card-footer {
  padding: 14px 20px;
  background-color: transparent;
  border-color: var(--au-divider);
}
.card-header {
  font-size: 0.875rem;
  font-weight: 700;
}
.card-body {
  padding: 20px;
}
.card-title {
  font-size: 0.875rem;
  font-weight: 700;
}
.card-subtitle,
.card-text {
  color: var(--au-text-2);
}

/* --- tables ---------------------------------------------------------------
   Wrap a page's <table class="table"> in <div class="au-table-card"> (markup
   only) to get the card + rounded clip; the rules below do the rest. */
.table {
  --bs-table-bg: transparent;
  --bs-table-color: var(--au-text);
  --bs-table-border-color: var(--au-divider);
  --bs-table-striped-bg: color-mix(in srgb, var(--au-text) 2.5%, transparent);
  --bs-table-striped-color: var(--au-text);
  --bs-table-hover-bg: color-mix(in srgb, var(--au-primary) 5%, transparent);
  --bs-table-hover-color: var(--au-text);
  --bs-table-active-bg: color-mix(in srgb, var(--au-primary) 7%, transparent);
  --bs-table-active-color: var(--au-text);
  margin-bottom: 0;
  color: var(--au-text);
  border-color: var(--au-divider);
  font-size: 0.875rem;
  font-variant-numeric: tabular-nums;
  vertical-align: middle;
}
.table > :not(caption) > * > * {
  height: 44px;
  padding: 6px 10px;
  color: var(--au-text);
  border-bottom-color: var(--au-divider);
}
.table > thead > tr > th,
.table > thead > tr > td {
  position: sticky;
  top: 0;
  z-index: 2;
  background-color: var(--au-table-head-bg);
  color: var(--au-text-2);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  white-space: nowrap;
}
.table > thead > tr > th a {
  color: inherit;
  text-decoration: none;
}
.table > tbody > tr:last-child > * {
  border-bottom-width: 0;
}
.table-light,
.table-dark,
.thead-light,
.thead-dark,
.table > thead.table-light > tr > th,
.table > thead.table-dark > tr > th {
  --bs-table-bg: var(--au-table-head-bg);
  --bs-table-color: var(--au-text-2);
  background-color: var(--au-table-head-bg);
  color: var(--au-text-2);
}
.table-bordered > :not(caption) > * > * {
  border-color: var(--au-divider);
}
.table-responsive {
  border-radius: inherit;
}
.au-table-card > .table,
.au-table-card .table-responsive > .table {
  margin: 0;
}

/* --- badges -> chips --------------------------------------------------------- */
.badge {
  display: inline-flex;
  align-items: center;
  height: 22px;
  padding: 0 8px;
  border-radius: var(--au-r-xs);
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1;
}
.badge.rounded-pill {
  border-radius: 999px;
}
.badge.bg-primary,
.badge.text-bg-primary {
  color: var(--au-on-primary) !important;
  background-color: var(--au-primary) !important;
}
.badge.bg-success,
.badge.text-bg-success {
  color: var(--au-on-success) !important;
  background-color: var(--au-success) !important;
}
.badge.bg-warning,
.badge.text-bg-warning {
  color: var(--au-on-warning) !important;
  background-color: var(--au-warning) !important;
}
.badge.bg-danger,
.badge.text-bg-danger {
  color: var(--au-on-error) !important;
  background-color: var(--au-error) !important;
}
.badge.bg-info,
.badge.text-bg-info {
  color: var(--au-on-info) !important;
  background-color: var(--au-info) !important;
}
.badge.bg-secondary,
.badge.text-bg-secondary,
.badge.bg-dark,
.badge.text-bg-dark {
  color: var(--au-text) !important;
  background-color: var(--au-selected) !important;
}
.badge.bg-light,
.badge.text-bg-light {
  color: var(--au-text) !important;
  background-color: transparent !important;
  border: 1px solid var(--au-input-border);
}

/* --- alerts ---------------------------------------------------------------- */
.alert {
  --c: var(--au-info);
  padding: 14px 16px;
  border: 0;
  border-radius: var(--au-r-md);
  background-color: color-mix(in srgb, var(--c) 10%, var(--au-mix-bg));
  color: color-mix(in srgb, var(--c) 40%, var(--au-mix-fg));
  font-weight: 500;
}
.alert-primary {
  --c: var(--au-primary);
}
.alert-success {
  --c: var(--au-success);
}
.alert-warning {
  --c: var(--au-warning);
}
.alert-danger {
  --c: var(--au-error);
}
.alert-secondary,
.alert-light,
.alert-dark {
  --c: var(--au-text-2);
}
.alert .alert-link {
  color: inherit;
  font-weight: 700;
}
.alert-heading {
  color: inherit;
  font-weight: 700;
}

/* --- modals ---------------------------------------------------------------- */
.modal-content {
  color: var(--au-text);
  background-color: var(--au-paper);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-dialog);
  box-shadow: var(--au-shadow-pop);
}
.modal-header {
  padding: 16px 24px;
  border-bottom: 0;
}
.modal-title {
  font-size: 1.02rem;
  font-weight: 750;
  letter-spacing: -0.01em;
}
.modal-body {
  padding: 0 24px 20px;
}
.modal-footer {
  gap: 8px;
  padding: 8px 24px 16px;
  border-top: 0;
}
.modal-footer > * {
  margin: 0;
}
.modal-backdrop {
  background-color: var(--au-backdrop);
  backdrop-filter: blur(5px);
}
.modal-backdrop.show {
  opacity: 1;
}

/* --- dropdowns ------------------------------------------------------------- */
.dropdown-menu {
  --bs-dropdown-bg: var(--au-paper);
  --bs-dropdown-color: var(--au-text);
  --bs-dropdown-border-color: var(--au-divider);
  --bs-dropdown-link-color: var(--au-text);
  --bs-dropdown-link-hover-color: var(--au-text);
  --bs-dropdown-link-hover-bg: var(--au-hover);
  --bs-dropdown-link-active-color: var(--au-text);
  --bs-dropdown-link-active-bg: color-mix(in srgb, var(--au-primary) 10%, transparent);
  padding: 6px;
  color: var(--au-text);
  background-color: color-mix(in srgb, var(--au-paper) 92%, transparent);
  backdrop-filter: blur(14px);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-md);
  box-shadow: var(--au-shadow-pop);
  font-size: 0.875rem;
}
.dropdown-item {
  padding: 6px 10px;
  color: var(--au-text);
  border-radius: var(--au-r-xs);
}
.dropdown-item:hover,
.dropdown-item:focus {
  color: var(--au-text);
  background-color: var(--au-hover);
}
.dropdown-item.active,
.dropdown-item:active {
  color: var(--au-text);
  background-color: color-mix(in srgb, var(--au-primary) 10%, transparent);
}
.dropdown-divider {
  border-color: var(--au-divider);
}
.dropdown-header {
  color: var(--au-text-3);
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

/* --- nav tabs / pills ------------------------------------------------------ */
.nav-tabs {
  gap: 0;
  border-bottom: 1px solid var(--au-divider);
}
.nav-tabs .nav-link {
  position: relative;
  min-height: 44px;
  margin-bottom: 0;
  padding: 8px 16px;
  color: var(--au-text-2);
  background: none;
  border: 0;
  border-radius: 0;
  font-weight: 650;
}
.nav-tabs .nav-link:hover,
.nav-tabs .nav-link:focus {
  color: var(--au-text);
  border: 0;
  isolation: auto;
}
.nav-tabs .nav-link.active,
.nav-tabs .nav-item.show .nav-link {
  color: var(--au-primary);
  background: none;
  border: 0;
}
.nav-tabs .nav-link.active::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 3px;
  border-radius: 3px;
  background-image: var(--au-gradient);
}
.nav-pills {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  border-radius: var(--au-r-sm);
  background: var(--au-segment-bg);
}
.nav-pills .nav-link {
  padding: 4px 11px;
  color: var(--au-text-2);
  border-radius: var(--au-r-xs);
  font-size: 0.8125rem;
  font-weight: 600;
}
.nav-pills .nav-link.active,
.nav-pills .show > .nav-link {
  color: var(--au-primary);
  background: var(--au-segment-selected);
  box-shadow: var(--au-segment-shadow);
}

/* --- pagination ------------------------------------------------------------ */
.pagination {
  gap: 6px;
  margin: 0;
}
.page-link {
  display: grid;
  place-items: center;
  min-width: 34px;
  height: 34px;
  padding: 0 8px;
  color: var(--au-text);
  background-color: transparent;
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-sm) !important;
  font-size: 0.875rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}
.page-link:hover {
  color: var(--au-text);
  background-color: var(--au-hover);
  border-color: var(--au-divider);
}
.page-link:focus {
  box-shadow: var(--au-focus-ring);
}
.page-item.active .page-link,
.active > .page-link {
  color: var(--au-primary);
  background-color: color-mix(in srgb, var(--au-primary) 10%, transparent);
  border-color: color-mix(in srgb, var(--au-primary) 40%, transparent);
}
.page-item.disabled .page-link,
.disabled > .page-link {
  color: var(--au-disabled);
  background-color: transparent;
  border-color: var(--au-divider);
}

/* --- breadcrumb, list group, accordion, misc ------------------------------- */
.breadcrumb {
  margin: 0;
  font-size: 12px;
}
.breadcrumb-item a {
  color: var(--au-text-2);
  text-decoration: none;
}
.breadcrumb-item a:hover {
  color: var(--au-text);
}
.breadcrumb-item.active {
  color: var(--au-text);
}
.breadcrumb-item + .breadcrumb-item::before {
  content: '\203A';
  color: var(--au-text-3);
}
.list-group-item {
  color: var(--au-text);
  background-color: var(--au-paper);
  border-color: var(--au-divider);
}
.list-group-item-action:hover,
.list-group-item-action:focus {
  color: var(--au-text);
  background-color: var(--au-hover);
}
.list-group-item.active {
  color: var(--au-primary);
  background-color: color-mix(in srgb, var(--au-primary) 10%, var(--au-paper));
  border-color: var(--au-divider);
}
.accordion-item {
  color: var(--au-text);
  background-color: var(--au-paper);
  border: 1px solid var(--au-divider);
}
.accordion-item:first-of-type {
  border-top-left-radius: var(--au-r-md);
  border-top-right-radius: var(--au-r-md);
}
.accordion-item:last-of-type {
  border-bottom-left-radius: var(--au-r-md);
  border-bottom-right-radius: var(--au-r-md);
}
.accordion-button {
  color: var(--au-text);
  background-color: var(--au-paper);
  font-weight: 650;
}
.accordion-button:not(.collapsed) {
  color: var(--au-primary);
  background-image: var(--au-gradient-soft);
  box-shadow: none;
}
.accordion-button:focus {
  box-shadow: var(--au-focus-ring);
}
.progress {
  height: 6px;
  border-radius: 999px;
  background-color: color-mix(in srgb, var(--au-primary) 12%, transparent);
}
.progress-bar {
  background-color: var(--au-primary);
  background-image: var(--au-gradient);
  border-radius: 999px;
}
.tooltip {
  --bs-tooltip-bg: var(--au-tooltip-bg);
  --bs-tooltip-color: #ffffff;
  --bs-tooltip-font-size: 12px;
  --bs-tooltip-border-radius: var(--au-r-xs);
  --bs-tooltip-padding-x: 10px;
  --bs-tooltip-padding-y: 6px;
}
.toast {
  color: var(--au-text);
  background-color: var(--au-paper);
  border: 1px solid var(--au-divider);
  border-radius: var(--au-r-md);
  box-shadow: var(--au-shadow-pop);
}
.offcanvas {
  color: var(--au-text);
  background-color: var(--au-paper);
  border-color: var(--au-divider);
  box-shadow: var(--au-shadow-pop);
}
.spinner-border,
.spinner-grow {
  color: var(--au-primary);
}

/* --- utility re-mapping (so dark mode stays correct) ----------------------- */
.text-muted,
.text-secondary,
.text-body-secondary {
  color: var(--au-text-2) !important;
}
.text-primary {
  color: var(--au-primary) !important;
}
.text-success {
  color: var(--au-success) !important;
}
.text-warning {
  color: var(--au-warning) !important;
}
.text-danger {
  color: var(--au-error) !important;
}
.text-info {
  color: var(--au-info) !important;
}
.text-dark,
.text-body,
.text-black {
  color: var(--au-text) !important;
}
.bg-white,
.bg-light,
.bg-body,
.bg-body-tertiary {
  background-color: var(--au-paper) !important;
  color: var(--au-text);
}
.border,
.border-top,
.border-bottom,
.border-start,
.border-end {
  border-color: var(--au-divider) !important;
}
.shadow-sm {
  box-shadow: var(--au-shadow-paper) !important;
}
.shadow,
.shadow-lg {
  box-shadow: var(--au-shadow-pop) !important;
}
.rounded {
  border-radius: var(--au-r-sm) !important;
}
hr {
  color: var(--au-divider);
  opacity: 1;
}
````

### Appendix C — `wwwroot/js/aurora.js`

<!-- au-file: wwwroot/js/aurora.js -->
````js
/* ==========================================================================
   AURORA — UI behaviours. Vanilla JS, no dependencies, no build step.
   Every feature is OPT-IN by markup (data-au-* attributes); a page that does
   not use a feature is untouched. Never put business logic here.
   Public API: window.Aurora.{theme, toast, confirm, copy, progress, dialog}
   ========================================================================== */
(function () {
  'use strict';

  var root = document.documentElement;
  var APP = root.getAttribute('data-au-app') || 'aurora';
  var KEY = {
    theme: APP + '.themeMode',
    collapsed: APP + '.sidebarCollapsed',
    favs: APP + '.nav.favorites',
    recent: APP + '.nav.recent',
  };
  var reduceMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  var desktop = window.matchMedia ? window.matchMedia('(min-width: 900px)') : { matches: true };

  // --- tiny helpers --------------------------------------------------------
  function $(sel, ctx) {
    return (ctx || document).querySelector(sel);
  }
  function $$(sel, ctx) {
    return Array.prototype.slice.call((ctx || document).querySelectorAll(sel));
  }
  function store(key, value) {
    try {
      if (value === undefined) return localStorage.getItem(key);
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch (e) {
      /* private mode / blocked storage: degrade silently */
    }
    return null;
  }
  function readJson(key, fallback) {
    try {
      var raw = store(key);
      return raw ? JSON.parse(raw) : fallback;
    } catch (e) {
      return fallback;
    }
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function icon(name, cls) {
    return '<span class="au-icon' + (cls ? ' ' + cls : '') + '" aria-hidden="true">' + esc(name) + '</span>';
  }
  function isTyping(el) {
    return !!el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));
  }
  function debounce(fn, ms) {
    var t;
    return function () {
      var args = arguments,
        self = this;
      clearTimeout(t);
      t = setTimeout(function () {
        fn.apply(self, args);
      }, ms);
    };
  }

  // A click whose target is the <dialog> itself AND lands outside its box is a
  // backdrop click (a click on empty space inside the dialog also targets it).
  function isBackdropClick(e, dlg) {
    if (e.target !== dlg) return false;
    var r = dlg.getBoundingClientRect();
    return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom;
  }

  // --- theme (light / dark) -------------------------------------------------
  var theme = {
    get: function () {
      return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    },
    set: function (mode) {
      root.setAttribute('data-theme', mode);
      root.setAttribute('data-bs-theme', mode); // Bootstrap 5.3+ follows along
      store(KEY.theme, mode);
      $$('[data-au-theme-toggle]').forEach(syncThemeToggle);
      document.dispatchEvent(new CustomEvent('au:themechange', { detail: { mode: mode } }));
    },
    toggle: function () {
      theme.set(theme.get() === 'dark' ? 'light' : 'dark');
    },
  };
  function syncThemeToggle(btn) {
    var dark = theme.get() === 'dark';
    var label = dark ? 'Switch to light mode' : 'Switch to dark mode';
    btn.setAttribute('aria-label', label);
    if (btn.hasAttribute('data-au-tip')) btn.setAttribute('data-au-tip', label);
    var glyph = btn.querySelector('.au-icon');
    if (glyph) {
      glyph.textContent = dark ? 'light_mode' : 'dark_mode';
      glyph.classList.remove('au-animate-in');
      void glyph.offsetWidth; // restart the animation
      glyph.classList.add('au-animate-in');
    }
  }

  // --- top progress bar (navigation / submit in flight) ---------------------
  var progress = {
    el: null,
    start: function () {
      if (progress.el) progress.el.classList.add('is-active');
    },
    stop: function () {
      if (progress.el) progress.el.classList.remove('is-active');
    },
  };

  // --- toasts ----------------------------------------------------------------
  var TOAST_MS = 4500;
  var TOAST_MAX = 4;
  var TOAST_ICON = { success: 'check_circle', error: 'error', warning: 'warning', info: 'info' };
  function toastHost() {
    var host = $('.au-toasts');
    if (!host) {
      host = document.createElement('div');
      host.className = 'au-toasts';
      host.setAttribute('aria-live', 'polite');
      document.body.appendChild(host);
    }
    return host;
  }
  function showToast(severity, message) {
    if (!message) return;
    var sev = TOAST_ICON[severity] ? severity : 'info';
    var host = toastHost();
    while (host.children.length >= TOAST_MAX) host.removeChild(host.firstElementChild);
    var text = String(message);
    if (text.length > 300) text = text.slice(0, 297) + '…'; // the full error lives in the page panel
    var el = document.createElement('div');
    el.className = 'au-toast au-toast--' + sev;
    el.setAttribute('role', sev === 'error' ? 'alert' : 'status');
    el.innerHTML =
      icon(TOAST_ICON[sev]) +
      '<div class="au-toast__msg">' +
      esc(text) +
      '</div>' +
      '<button type="button" class="au-icon-btn au-icon-btn--sm" aria-label="Dismiss">' +
      icon('close') +
      '</button>';
    var dismiss = function () {
      if (!el.parentNode) return;
      el.classList.add('is-leaving');
      setTimeout(function () {
        if (el.parentNode) el.parentNode.removeChild(el);
      }, 200);
    };
    el.querySelector('button').addEventListener('click', dismiss);
    host.appendChild(el);
    setTimeout(dismiss, TOAST_MS);
  }
  var toast = {
    success: function (m) {
      showToast('success', m);
    },
    error: function (m) {
      showToast('error', m);
    },
    warning: function (m) {
      showToast('warning', m);
    },
    info: function (m) {
      showToast('info', m);
    },
  };

  // --- clipboard (falls back to execCommand on plain-http origins) ----------
  function copy(text, container) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {
      var host = container || document.body; // inside a <dialog>, pass the dialog
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      host.appendChild(ta);
      ta.select();
      var ok = false;
      try {
        ok = document.execCommand('copy');
      } catch (e) {
        ok = false;
      }
      host.removeChild(ta);
      if (ok) resolve();
      else reject(new Error('copy failed'));
    });
  }

  // --- dialogs / confirm ----------------------------------------------------
  var dialog = {
    open: function (target) {
      var d = typeof target === 'string' ? $(target) : target;
      if (d && typeof d.showModal === 'function' && !d.open) d.showModal();
      return d;
    },
    close: function (target) {
      var d = typeof target === 'string' ? $(target) : target;
      if (d && d.open) d.close();
    },
  };
  var confirmEl = null;
  function confirmDialog(opts) {
    opts = opts || {};
    if (!confirmEl) {
      confirmEl = document.createElement('dialog');
      confirmEl.className = 'au-dialog';
      confirmEl.setAttribute('aria-labelledby', 'au-confirm-title');
      confirmEl.innerHTML =
        '<h2 class="au-dialog__title" id="au-confirm-title"></h2>' +
        '<div class="au-dialog__content"><div class="au-dialog__text"></div></div>' +
        '<div class="au-dialog__actions">' +
        '<button type="button" class="au-btn" data-au-cancel>Cancel</button>' +
        '<button type="button" class="au-btn" data-au-ok>Confirm</button>' +
        '</div>';
      document.body.appendChild(confirmEl);
      confirmEl.addEventListener('click', function (e) {
        if (isBackdropClick(e, confirmEl)) confirmEl.close('cancel');
      });
    }
    var tone = opts.tone || 'primary';
    confirmEl.querySelector('.au-dialog__title').textContent = opts.title || 'Are you sure?';
    var text = confirmEl.querySelector('.au-dialog__text');
    if (opts.html) text.innerHTML = opts.html;
    else text.textContent = opts.message || '';
    var ok = confirmEl.querySelector('[data-au-ok]');
    ok.className = 'au-btn au-btn--' + tone;
    ok.textContent = opts.confirmLabel || 'Confirm';
    confirmEl.querySelector('[data-au-cancel]').textContent = opts.cancelLabel || 'Cancel';
    return new Promise(function (resolve) {
      function done(result) {
        ok.removeEventListener('click', onOk);
        cancel.removeEventListener('click', onCancel);
        confirmEl.removeEventListener('close', onClose);
        resolve(result);
      }
      var cancel = confirmEl.querySelector('[data-au-cancel]');
      function onOk() {
        confirmEl.close('ok');
      }
      function onCancel() {
        confirmEl.close('cancel');
      }
      function onClose() {
        done(confirmEl.returnValue === 'ok');
      }
      ok.addEventListener('click', onOk);
      cancel.addEventListener('click', onCancel);
      confirmEl.addEventListener('close', onClose);
      confirmEl.returnValue = '';
      confirmEl.showModal();
      (tone === 'danger' ? cancel : ok).focus(); // destructive: default focus is Cancel
    });
  }
  function confirmOptions(el, fallback) {
    var src = el && el.hasAttribute('data-au-confirm') ? el : fallback;
    return {
      message: src.getAttribute('data-au-confirm'),
      title: src.getAttribute('data-au-confirm-title') || undefined,
      confirmLabel: src.getAttribute('data-au-confirm-label') || undefined,
      tone: src.getAttribute('data-au-confirm-tone') || undefined,
    };
  }

  // --- sidebar: collapse rail, mobile drawer, filter, favourites, recent ----
  function initSidebar() {
    var sidebar = $('.au-sidebar');
    $$('[data-au-sidebar-toggle]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        if (desktop.matches) {
          var next = !root.classList.contains('au-side-collapsed');
          root.classList.toggle('au-side-collapsed', next);
          store(KEY.collapsed, next ? '1' : '0');
        } else {
          root.classList.toggle('au-side-open');
        }
      });
    });
    var scrim = $('.au-scrim');
    if (scrim)
      scrim.addEventListener('click', function () {
        root.classList.remove('au-side-open');
      });
    if (!sidebar) return;

    // Collapsed rail: a group click expands the rail instead of toggling.
    sidebar.addEventListener('click', function (e) {
      var summary = e.target.closest('.au-nav-group > summary');
      if (summary && desktop.matches && root.classList.contains('au-side-collapsed')) {
        e.preventDefault();
        root.classList.remove('au-side-collapsed');
        store(KEY.collapsed, '0');
        summary.parentElement.open = true;
      }
    });

    initNavFilter(sidebar);
    initFavorites(sidebar);
    recordVisit(sidebar);
  }

  function initNavFilter(sidebar) {
    var box = $('[data-au-nav-filter]', sidebar);
    if (!box) return;
    var input = box.querySelector('input');
    var clear = box.querySelector('button');
    var empty = $('.au-nav-empty', sidebar);
    var tree = $('[data-au-nav-tree]', sidebar) || sidebar;
    var filtering = false;
    function apply() {
      var raw = input.value.trim();
      var q = raw.toLowerCase();
      var now = q.length > 0;
      if (now !== filtering) {
        // a closed <details> hides its children no matter what CSS says,
        // so open every group while filtering and restore afterwards
        $$('details.au-nav-group', tree).forEach(function (d) {
          if (now) {
            d.setAttribute('data-au-was-open', d.open ? '1' : '0');
            d.open = true;
          } else {
            d.open = d.getAttribute('data-au-was-open') === '1';
            d.removeAttribute('data-au-was-open');
          }
        });
        filtering = now;
      }
      box.classList.toggle('has-value', now);
      sidebar.classList.toggle('is-filtering', now);
      var shown = 0;
      $$('.au-nav-row', tree).forEach(function (row) {
        var link = row.querySelector('.au-nav-item');
        var hay = ((link.getAttribute('data-trail') || '') + ' ' + link.textContent).toLowerCase();
        var match = !now || hay.indexOf(q) !== -1;
        row.classList.toggle('is-filtered-out', !match);
        if (now && match) shown++;
      });
      if (empty) {
        empty.hidden = !(now && shown === 0);
        empty.textContent = 'No pages match “' + raw + '”.';
      }
    }
    input.addEventListener('input', apply);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        input.value = '';
        apply();
      }
    });
    if (clear)
      clear.addEventListener('click', function () {
        input.value = '';
        apply();
        input.focus();
      });
  }

  function initFavorites(sidebar) {
    var host = $('[data-au-favorites]', sidebar);
    var tree = $('[data-au-nav-tree]', sidebar) || sidebar;
    function render() {
      var favs = readJson(KEY.favs, []);
      var byPath = {};
      // skip the filter-only child rows: they share their group row's href
      $$('.au-nav-row:not(.au-nav-row--search-only) > .au-nav-item', tree).forEach(function (a) {
        byPath[a.getAttribute('href')] = a;
      });
      $$('.au-nav-star', sidebar).forEach(function (b) {
        var on = favs.some(function (f) {
          return f.path === b.getAttribute('data-path');
        });
        b.classList.toggle('is-on', on);
        b.setAttribute('aria-pressed', on ? 'true' : 'false');
        var label = b.getAttribute('data-label') || '';
        b.setAttribute('aria-label', (on ? 'Unpin ' : 'Pin ') + label);
        b.title = on ? 'Unpin from favorites' : 'Pin to favorites';
      });
      if (!host) return;
      var rows = favs
        .filter(function (f) {
          return byPath[f.path];
        })
        .map(function (f) {
          var row = byPath[f.path].closest('.au-nav-row').cloneNode(true);
          row.classList.remove('is-filtered-out');
          var star = row.querySelector('.au-nav-star');
          if (star) star.classList.add('is-on');
          return row;
        });
      host.innerHTML = '';
      if (!rows.length) return;
      var label = document.createElement('div');
      label.className = 'au-side-section';
      label.innerHTML = icon('star', 'au-icon--fill') + 'Favorites';
      host.appendChild(label);
      rows.forEach(function (r) {
        host.appendChild(r);
      });
    }
    sidebar.addEventListener('click', function (e) {
      var star = e.target.closest('.au-nav-star');
      if (!star) return;
      e.preventDefault();
      e.stopPropagation();
      var path = star.getAttribute('data-path');
      var favs = readJson(KEY.favs, []);
      var exists = favs.some(function (f) {
        return f.path === path;
      });
      favs = exists
        ? favs.filter(function (f) {
            return f.path !== path;
          })
        : favs.concat([{ path: path, label: star.getAttribute('data-label') || path }]);
      store(KEY.favs, JSON.stringify(favs));
      render();
    });
    render();
  }

  function recordVisit(sidebar) {
    var active = $('[data-au-nav-tree] .au-nav-row:not(.au-nav-row--search-only) > .au-nav-item.is-active', sidebar);
    if (!active) return;
    var path = active.getAttribute('href');
    var label = (active.querySelector('.au-nav-item__label') || active).textContent.trim();
    var recent = readJson(KEY.recent, []).filter(function (r) {
      return r.path !== path;
    });
    recent.unshift({ path: path, label: label, at: Date.now() });
    store(KEY.recent, JSON.stringify(recent.slice(0, 8)));
  }

  // --- command palette (Ctrl/⌘+K quick navigation over the nav tree) -------
  function initPalette() {
    var dlg = $('dialog.au-palette');
    var dataEl = $('#au-nav-data');
    if (!dlg || !dataEl) return;
    var pages = [];
    try {
      pages = JSON.parse(dataEl.textContent || '[]');
    } catch (e) {
      pages = [];
    }
    var input = dlg.querySelector('input');
    var list = dlg.querySelector('.au-palette__list');
    var results = [];
    var index = 0;

    function byPath(path) {
      for (var i = 0; i < pages.length; i++) if (pages[i].path === path) return pages[i];
      return null;
    }
    function compute() {
      var q = input.value.trim().toLowerCase();
      var groups = [];
      if (!q) {
        var favs = readJson(KEY.favs, []).map(function (f) {
          return byPath(f.path);
        }).filter(Boolean);
        var recent = readJson(KEY.recent, []).map(function (r) {
          return byPath(r.path);
        }).filter(Boolean);
        if (favs.length) groups.push({ title: 'Favorites', items: favs });
        if (recent.length) groups.push({ title: 'Recent', items: recent.slice(0, 6) });
        groups.push({ title: 'All pages', items: pages });
      } else {
        var terms = q.split(/\s+/);
        groups.push({
          title: 'Pages',
          items: pages
            .filter(function (p) {
              var hay = ((p.trail || []).join(' ') + ' ' + p.label).toLowerCase();
              return terms.every(function (t) {
                return hay.indexOf(t) !== -1;
              });
            })
            .slice(0, 40),
        });
      }
      return groups;
    }
    function render() {
      var groups = compute();
      results = [];
      var html = '';
      groups.forEach(function (g) {
        if (!g.items.length) return;
        html += '<li class="au-palette__group" role="presentation">' + esc(g.title) + '</li>';
        g.items.forEach(function (p) {
          var i = results.length;
          results.push(p);
          html +=
            '<li class="au-palette__item" role="option" id="au-pal-' +
            i +
            '" data-i="' +
            i +
            '" aria-selected="' +
            (i === index) +
            '">' +
            icon(p.icon || 'article') +
            '<span>' +
            esc(p.label) +
            '</span><span class="au-palette__trail">' +
            esc((p.trail || []).join(' › ')) +
            '</span></li>';
        });
      });
      if (!results.length) html = '<li class="au-palette__group">No pages match.</li>';
      list.innerHTML = html;
      var sel = list.querySelector('[aria-selected="true"]');
      if (sel) sel.scrollIntoView({ block: 'nearest' });
    }
    function go(i) {
      var p = results[i];
      if (!p) return;
      dlg.close();
      progress.start();
      window.location.href = p.path;
    }
    function open() {
      input.value = '';
      index = 0;
      render();
      dlg.showModal();
      input.focus();
    }
    input.addEventListener('input', function () {
      index = 0;
      render();
    });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (!results.length) return;
        index = (index + (e.key === 'ArrowDown' ? 1 : -1) + results.length) % results.length;
        render();
      } else if (e.key === 'Enter') {
        e.preventDefault();
        go(index);
      }
    });
    list.addEventListener('click', function (e) {
      var item = e.target.closest('.au-palette__item');
      if (item) go(Number(item.getAttribute('data-i')));
    });
    dlg.addEventListener('click', function (e) {
      if (isBackdropClick(e, dlg)) dlg.close();
    });
    document.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        if (!dlg.open) open();
      }
    });
    $$('[data-au-palette-open]').forEach(function (b) {
      b.addEventListener('click', open);
    });
  }

  // --- global scope selector (one entity, e.g. Clinic) ----------------------
  // Server contract: GET {endpoint}&q=&skip=&take= → { items:[{id,name,isActive,sub}], hasMore }
  // Picking sets ?{param}=<id> (or ?{param}= for "All") and reloads; the server
  // persists the choice (cookie) and every scope-aware page reads it.
  function initScope() {
    $$('[data-au-scope]').forEach(function (host) {
      var input = host.querySelector('.au-combobox__input');
      var list = host.querySelector('.au-combobox__list');
      if (!input || !list) return;
      var endpoint = host.getAttribute('data-endpoint');
      var param = host.getAttribute('data-param') || 'scope';
      var allLabel = host.getAttribute('data-all-label') || 'All';
      var currentId = host.getAttribute('data-current-id') || '';
      var currentName = host.getAttribute('data-current-name') || '';
      var resetParams = (host.getAttribute('data-reset-params') || 'page').split(',');
      var PAGE = 25;
      var items = [];
      var hasMore = false;
      var loading = false;
      var q = '';
      var active = -1;
      var seq = 0;

      // keep the active scope in the URL so a copied link reproduces the view
      if (currentId) {
        var here = new URL(window.location.href);
        if (!here.searchParams.has(param)) {
          here.searchParams.set(param, currentId);
          history.replaceState(history.state, '', here.toString());
        }
      }

      function options() {
        return [{ id: '', name: allLabel, isActive: true, all: true }].concat(items);
      }
      function render(status) {
        var opts = options();
        var html = opts
          .map(function (o, i) {
            var cls = 'au-combobox__opt' + (o.all ? ' au-combobox__opt--all' : '') + (o.id === currentId ? ' is-current' : '');
            return (
              '<li class="' + cls + '" role="option" id="' + list.id + '-' + i + '" data-i="' + i + '" aria-selected="' + (i === active) + '">' +
              '<span class="au-combobox__opt-name"><span>' + esc(o.name || 'Unnamed') + '</span>' +
              (!o.all && o.isActive === false ? '<span class="au-chip au-chip--outlined" style="height:18px;font-size:10px">inactive</span>' : '') +
              '</span>' +
              (o.sub ? '<span class="au-combobox__opt-sub">' + esc(o.sub) + '</span>' : '') +
              '</li>'
            );
          })
          .join('');
        if (status) html += '<li class="au-combobox__status" role="presentation">' + esc(status) + '</li>';
        list.innerHTML = html;
        input.setAttribute('aria-activedescendant', active >= 0 ? list.id + '-' + active : '');
      }
      function load(reset) {
        if (!endpoint || (loading && !reset)) return;
        if (reset) {
          items = [];
          hasMore = false;
        }
        loading = true;
        var my = ++seq;
        render('Loading…');
        var url = endpoint + (endpoint.indexOf('?') === -1 ? '?' : '&') + 'q=' + encodeURIComponent(q) + '&skip=' + items.length + '&take=' + PAGE;
        fetch(url, { headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
          .then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
          })
          .then(function (data) {
            if (my !== seq) return;
            items = items.concat(data.items || []);
            hasMore = !!data.hasMore;
            loading = false;
            render(items.length ? null : q ? 'No matches' : null);
          })
          .catch(function (err) {
            if (my !== seq) return;
            loading = false;
            render('Could not load the list (' + err.message + ')');
          });
      }
      function openList() {
        if (!list.hidden) return;
        list.hidden = false;
        input.setAttribute('aria-expanded', 'true');
        active = -1;
        q = '';
        load(true);
      }
      function closeList(restore) {
        list.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        if (restore) input.value = currentName;
      }
      function pick(o) {
        closeList(false);
        if ((o.id || '') === currentId) {
          input.value = currentName;
          return;
        }
        var url = new URL(window.location.href);
        url.searchParams.set(param, o.id || ''); // empty = explicit "All" (server clears)
        resetParams.forEach(function (p) {
          if (p) url.searchParams.delete(p.trim());
        });
        progress.start();
        window.location.assign(url.toString());
      }
      var search = debounce(function () {
        q = input.value.trim();
        active = -1;
        load(true);
      }, 300);

      input.addEventListener('focus', function () {
        input.select();
        openList();
      });
      input.addEventListener('click', openList);
      input.addEventListener('input', function () {
        if (list.hidden) openList();
        search();
      });
      input.addEventListener('keydown', function (e) {
        var count = options().length;
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
          e.preventDefault();
          openList();
          active = (active + (e.key === 'ArrowDown' ? 1 : -1) + count) % count;
          render();
          var el = document.getElementById(list.id + '-' + active);
          if (el) el.scrollIntoView({ block: 'nearest' });
        } else if (e.key === 'Enter') {
          if (!list.hidden && active >= 0) {
            e.preventDefault();
            pick(options()[active]);
          }
        } else if (e.key === 'Escape') {
          closeList(true);
        }
      });
      input.addEventListener('blur', function () {
        setTimeout(function () {
          if (!host.contains(document.activeElement)) closeList(true);
        }, 150);
      });
      list.addEventListener('mousedown', function (e) {
        e.preventDefault(); // keep focus in the input
        var li = e.target.closest('.au-combobox__opt');
        if (li) pick(options()[Number(li.getAttribute('data-i'))]);
      });
      list.addEventListener('scroll', function () {
        if (hasMore && !loading && list.scrollTop + list.clientHeight >= list.scrollHeight - 48) load(false);
      });
    });
  }

  // --- forms: confirm gate, busy state, auto-submit filters, date range -----
  function initForms() {
    // Bubble phase on document: jQuery-unobtrusive validation runs first on the
    // form and stops an invalid submit, so we never confirm an invalid form.
    document.addEventListener('submit', function (e) {
      if (e.defaultPrevented) return;
      var form = e.target;
      if (form.method === 'dialog') return; // closes a <dialog>; not a navigation, no busy state
      var submitter = e.submitter || null;
      var gate = (submitter && submitter.hasAttribute('data-au-confirm')) || form.hasAttribute('data-au-confirm');
      if (gate && !form.__auConfirmed) {
        e.preventDefault();
        confirmDialog(confirmOptions(submitter, form)).then(function (ok) {
          if (!ok) return;
          form.__auConfirmed = true;
          // requestSubmit(submitter) keeps the submitter's name/value + formaction
          // (Razor's asp-page-handler on a button depends on it)
          if (form.requestSubmit) form.requestSubmit(submitter || undefined);
          else form.submit();
        });
        return;
      }
      form.__auConfirmed = false;
      if (form.target === '_blank' || form.hasAttribute('data-au-no-busy')) return;
      // NEVER disable the submitter here: a disabled button is dropped from the
      // form data and the page handler would not be selected.
      if (submitter && submitter.classList.contains('au-btn')) submitter.setAttribute('aria-busy', 'true');
      progress.start();
    });

    document.addEventListener('change', function (e) {
      var el = e.target;
      var form = el.form;
      if (!form || !form.hasAttribute('data-au-autosubmit')) return;
      if (el.hasAttribute('data-au-range-select') && el.value === 'custom') return; // wait for Apply
      if (el.closest('[data-au-range-custom]')) return;
      var isText = el.tagName === 'TEXTAREA' || (el.tagName === 'INPUT' && /^(text|search|email|tel|url|number)$/i.test(el.type));
      if (isText) return; // text filters submit on Enter, never on every keystroke
      resetPage(form);
      if (form.requestSubmit) form.requestSubmit();
      else form.submit();
    });

    $$('[data-au-range]').forEach(function (range) {
      var select = range.querySelector('[data-au-range-select]');
      var custom = range.querySelector('[data-au-range-custom]');
      if (!select || !custom) return;
      var sync = function () {
        custom.hidden = select.value !== 'custom';
      };
      select.addEventListener('change', sync);
      sync();
    });
  }
  function resetPage(form) {
    var name = form.getAttribute('data-au-page-param') || 'page';
    var first = form.getAttribute('data-au-page-first') || '1';
    var input = form.querySelector('[name="' + name + '"]');
    if (input) input.value = first;
  }

  // --- clicks: dialogs, drawers, copy, row links, confirm links, refresh ----
  function initClicks() {
    document.addEventListener('click', function (e) {
      var t = e.target;

      var opener = t.closest('[data-au-open]');
      if (opener) {
        e.preventDefault();
        dialog.open(opener.getAttribute('data-au-open'));
        return;
      }
      var closer = t.closest('[data-au-close]');
      if (closer) {
        var d = closer.closest('dialog');
        if (d) d.close();
        return;
      }
      // click on the backdrop of a dismissable dialog / drawer
      if (t.tagName === 'DIALOG' && (t.classList.contains('au-drawer') || t.hasAttribute('data-au-dismissable')) && isBackdropClick(e, t)) {
        t.close();
        return;
      }

      var copyBtn = t.closest('[data-au-copy]');
      if (copyBtn) {
        e.preventDefault();
        e.stopPropagation();
        var value = copyBtn.getAttribute('data-au-copy');
        var src = copyBtn.getAttribute('data-au-copy-target');
        if (src) {
          var srcEl = $(src);
          value = srcEl ? srcEl.textContent : '';
        }
        copy(value || '', copyBtn.closest('dialog') || undefined).then(
          function () {
            toast.success(copyBtn.getAttribute('data-au-copy-message') || 'Copied');
          },
          function () {
            toast.error('Could not copy to clipboard');
          }
        );
        return;
      }

      var refresh = t.closest('[data-au-refresh]');
      if (refresh) {
        e.preventDefault();
        progress.start();
        window.location.reload();
        return;
      }

      var link = t.closest('a[data-au-confirm]');
      if (link) {
        e.preventDefault();
        confirmDialog(confirmOptions(link, link)).then(function (ok) {
          if (ok) {
            progress.start();
            window.location.href = link.href;
          }
        });
        return;
      }

      // row → drawer (partial HTML) or row → page
      var row = t.closest('[data-au-drawer-url], [data-au-href]');
      if (row && !t.closest('a, button, input, select, textarea, label, summary, [data-au-stop]')) {
        if (row.hasAttribute('data-au-drawer-url')) {
          e.preventDefault();
          openRemoteDrawer(row.getAttribute('data-au-drawer-url'), row.getAttribute('data-au-drawer') || '#au-drawer');
          return;
        }
        var href = row.getAttribute('data-au-href');
        if (e.ctrlKey || e.metaKey || e.button === 1) window.open(href, '_blank');
        else {
          progress.start();
          window.location.href = href;
        }
        return;
      }

      // ordinary same-tab navigation → show the progress strip
      var a = t.closest('a[href]');
      if (
        a &&
        !e.defaultPrevented &&
        !e.ctrlKey &&
        !e.metaKey &&
        !e.shiftKey &&
        (!a.target || a.target === '_self') &&
        !a.hasAttribute('download') &&
        a.origin === window.location.origin &&
        !(a.pathname === window.location.pathname && a.search === window.location.search && a.hash)
      ) {
        progress.start();
      }
    });

    // keyboard: Enter on a focused clickable row
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter') return;
      var row = e.target.closest && e.target.closest('tr[data-au-href], tr[data-au-drawer-url]');
      if (row && e.target === row) row.click();
    });
  }

  function openRemoteDrawer(url, selector) {
    var drawer = $(selector);
    if (!drawer) return;
    var body = drawer.querySelector('[data-au-drawer-body]') || drawer;
    body.innerHTML =
      '<span class="au-skeleton au-skeleton--text" style="width:60%"></span>' +
      '<span class="au-skeleton" style="height:120px;margin-top:16px"></span>';
    dialog.open(drawer);
    fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
      .then(function (r) {
        return r.text().then(function (html) {
          if (!r.ok) throw { status: r.status, html: html };
          return html;
        });
      })
      .then(function (html) {
        body.innerHTML = html; // a server-rendered partial (already HTML-encoded by Razor)
      })
      .catch(function (err) {
        body.innerHTML =
          '<div class="au-alert au-alert--error">' + icon('error', 'au-alert__icon') +
          '<div class="au-alert__body"><div class="au-alert__title">Could not load the details</div>' +
          esc(err && err.status ? 'The server answered HTTP ' + err.status + ' for ' + url : 'The request did not reach the server: ' + url) +
          '</div></div>';
      });
  }

  // --- dropdowns (<details class="au-dropdown">): one open at a time --------
  function initDropdowns() {
    document.addEventListener(
      'toggle',
      function (e) {
        var d = e.target;
        if (!d.classList || !d.classList.contains('au-dropdown') || !d.open) return;
        $$('details.au-dropdown[open]').forEach(function (o) {
          if (o !== d) o.open = false;
        });
      },
      true
    );
    document.addEventListener('click', function (e) {
      $$('details.au-dropdown[open]').forEach(function (d) {
        if (!d.contains(e.target) || e.target.closest('.au-menu__item')) d.open = false;
      });
    });
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Escape') return;
      $$('details.au-dropdown[open]').forEach(function (d) {
        d.open = false;
        var s = d.querySelector('summary');
        if (s) s.focus();
      });
      root.classList.remove('au-side-open');
    });
  }

  // --- boot -------------------------------------------------------------------
  function boot() {
    progress.el = $('.au-topbar-progress');
    $$('[data-au-theme-toggle]').forEach(function (btn) {
      syncThemeToggle(btn);
      btn.addEventListener('click', theme.toggle);
    });
    initSidebar();
    initPalette();
    initScope();
    initForms();
    initClicks();
    initDropdowns();

    // server-rendered toasts (e.g. from TempData after Post-Redirect-Get)
    $$('[data-au-toast]').forEach(function (el) {
      showToast(el.getAttribute('data-au-toast'), el.textContent.trim());
      el.parentNode.removeChild(el);
    });

    // keep the active section tab visible on narrow screens
    var tab = $('.au-section-tabs .au-tab.is-active');
    if (tab && tab.scrollIntoView) tab.scrollIntoView({ block: 'nearest', inline: 'center' });

    // soft page-enter motion (visual only)
    var main = $('.au-main');
    if (main && main.animate && !reduceMotion) {
      main.animate(
        [
          { opacity: 0.3, transform: 'translateY(6px)' },
          { opacity: 1, transform: 'none' },
        ],
        { duration: 260, easing: 'cubic-bezier(0.2, 0.8, 0.3, 1)' }
      );
    }
  }

  // back/forward-cache restore: clear the busy state the page was frozen with
  window.addEventListener('pageshow', function (e) {
    if (!e.persisted) return;
    progress.stop();
    $$('[aria-busy="true"]').forEach(function (b) {
      b.removeAttribute('aria-busy');
    });
  });

  window.Aurora = { theme: theme, toast: toast, confirm: confirmDialog, copy: copy, progress: progress, dialog: dialog };

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
````

