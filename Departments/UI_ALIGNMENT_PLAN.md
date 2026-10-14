# UI alignment across departments

Handoff spec for Codex. The 7 department apps look like 7 different products. This plan gives them one look while keeping each department's own pages and navigation.

## What differs today (measured from the repo)

| App | Shell | Stylesheet | Palette | Font |
| --- | --- | --- | --- | --- |
| Registrar | topbar + dashboard layout | `dashboard.css` (891 lines) + `site.css` | `--primary #0a1d4d`, `--accent #0f2a6b` | Inter |
| Guidance | static HTML pages | `styles.css` (1201 lines) | same navy as Registrar (copied) | Inter |
| Student Portal | left sidebar + topbar | `portal.css` (601) | `--nu-blue #344495` | system sans |
| Faculty Portal | left sidebar + topbar | `site.css` (810) | `--nu-blue #344495` (copy of Student Portal's) | system sans |
| Finance | three layouts (admin, faculty, student) | `finance-pages.css` (744) + Bootstrap | `--fin-navy #0d1b3d`, gold `#eab308` | Inter |
| Library | default Bootstrap navbar | `site.css` (241) | green-grey `--campus-ink`, paper | Segoe UI |
| Testing | default Bootstrap navbar | `site.css` (78) | Bootstrap blue | Bootstrap |

Other problems found:
- Three different blues, three fonts, and three navigation patterns (sidebar, topbar, Bootstrap navbar).
- Each app draws its own sign-in page with inline `<style>` and its own colors.
- Library and Testing still look like the default ASP.NET template.
- `Library/.../site.css` and `StudentPortal/.../site.css` are byte-identical (template leftovers).
- Page titles differ: "NU Lipa Finance", "National University Lipa Faculty Portal", "Library", "TestingMain".
- Guidance is static HTML, not Razor, so it can't share a Razor layout. It can share CSS.

## Goal

Same brand, tokens, typography, components and page structure in every app. A user moving from the Student Portal to Finance to Library should feel they are in one system. Do not change data, routes, roles or behavior.

## Decisions (change them here if the user disagrees)

1. **One palette: the NU navy.** Use Registrar/Guidance's `--primary #0a1d4d`, `--primary-light #0f2a6b`, `--accent #1e4bb8`. Student and Faculty's `#344495` goes away. Finance keeps gold only as a small accent (badges, highlights), not as a second brand color.
2. **One font: Inter** with a system fallback. Bundle `Inter` woff2 locally in the shared assets, because the Google Fonts link depends on the network.
3. **One shell: left sidebar + topbar**, as in Student and Faculty. Registrar, Finance, Library and Testing adopt it. Guidance uses the same markup and CSS in its HTML pages.
4. **Bootstrap stays** where it is already used, but through the shared stylesheet's tokens, not Bootstrap's default blue.

## Plan

### Phase 1: shared design system (no app changes yet)
- Create a shared static asset set. Because the repo has no Razor class library, prefer a new `Shared/CampusSystem.Ui` Razor class library that serves static web assets at `_content/CampusSystem.Ui/`. Fallback if that is too heavy: one `campus-ui.css` file copied into each app by a script, with a test that fails if the copies differ.
- `campus-tokens.css`: colors, spacing, radii, shadows, font, status colors (success, warning, danger, info), dark-sidebar colors.
- `campus-base.css`: reset, typography, the sidebar, topbar, content container, page heading (`.page-heading`, `.eyebrow`), cards (`.surface`), tables, badges, buttons, form fields, alerts/flash, empty states, pagination.
- `_CampusShell.cshtml` partials: sidebar, topbar (user name, role, sign-out), flash message. Each app passes its nav items, department name and logo.
- `_CampusSignIn.cshtml`: one sign-in page layout so the 7 inline-styled login pages become the same page with a department title.
- A small style guide page (`/styleguide`, Development only) that shows every component, to review visually.

### Phase 2: migrate apps, simplest first
For each app: switch to the shared shell and CSS, delete the app CSS that duplicates it, keep only genuinely app-specific rules in a small `app.css`.

1. **Testing** (3 pages): replace the template navbar with the shell. Smallest, proves the shell.
2. **Library** (7 pages): same. Do this after the Library borrower work in `Library/LIBRARY_BORROWERS_PLAN.md` is merged, to avoid conflicts on the same pages.
3. **Student Portal** and **Faculty Portal**: already a sidebar shell. Swap `--nu-blue` for the shared tokens and remove the duplicated CSS. Faculty `site.css` is 810 lines and most of it duplicates Student Portal.
4. **Registrar**: move from `dashboard.css` to the shared shell. Keep its table-heavy pages working.
5. **Finance**: collapse the three layouts (admin, faculty, student) into one layout that picks the nav by role. Move `finance-pages.css` content that is app-specific into `app.css`.
6. **Guidance** (static HTML): link the shared CSS and use the same sidebar/topbar markup. Replace the copy of the Registrar palette in `styles.css`.

### Phase 3: consistency pass
- Unify page titles to `<Page> · <Department> · NU Lipa`.
- Unify headings, empty states, button labels (Save, Cancel, Check in, etc.), date format (`MMM d, yyyy`) and currency format (`₱1,234.50`).
- Same flash and error presentation everywhere.
- Same 404 and Error pages.
- Accessibility: visible focus ring, contrast of at least 4.5:1 on text, labels on every input, keyboard-reachable sidebar.
- Responsive: sidebar collapses to a menu under 768px in every app.

### Phase 4: guard against drift
- Remove the duplicate `site.css` template leftovers and any now-unused CSS.
- Add a check (script or test) that each app's layout links the shared stylesheet and that no app CSS defines its own `--primary`-style brand variables.
- Update `README.md` and each department's AI-context file with where the shared UI lives and the rule "new pages use shared components".

## How to verify
- Build and run all 7 apps on a temp copy of the data (`CAMPUS_SYSTEM_JSON_PATH`), sign in with each demo account (`Campus@2026`), open every page and confirm: 200, CSS and JS requests all 200 (check the browser network list), no console errors.
- Take before/after screenshots of every page at desktop and phone width and compare them.
- Run all 8 test projects. UI changes must not break any.
- The user reviews it in their own browser before anything is committed. Do not commit.

## Risks
- Registrar and Finance pages have dense tables and inline styles. Expect the most visual regressions there, so migrate them one page at a time.
- Library has an active change in `LIBRARY_BORROWERS_PLAN.md`. Sequence them, don't run both at once.
- A shared Razor class library adds a project reference to every app. If that is a problem for the Docker/bat start scripts, use the copied-CSS fallback.

## Out of scope
Rebranding or a new logo, new features, changes to routes, roles, data or business rules, and rewriting Guidance into Razor.
