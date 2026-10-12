# GuidanceDepartment AI Context

## Ownership

GuidanceDepartment owns the student-support and counselor workflow interfaces in this project. This project uses static HTML under `wwwroot` alongside its API and controllers.

## How to edit this project

Use the task type to decide the correct layer:

- Front-end / interface work: edit static pages in `wwwroot/`, shared styling in `wwwroot/styles.css`, and navigation/layout structure in the HTML files. Keep forms and workflow cards visual placeholders unless an approved contract exists.
- Back-end / data work: edit `Controllers/`, `Services/`, `Contracts/`, DTOs, models, `Program.cs`, and other server files only when the task explicitly requires guidance data, case note logic, or a live API contract.
- AI model rule: if the request does not clearly include backend requirements, assume it is an interface edit and do not add persistence, API calls, or live student-record submission.

## AI maintenance manual

This file is the project guide for future AI sessions and developer handoffs. It should stay current as the project changes.

### When to update this file

Update this context whenever any of the following changes:

- a new static page, route, or folder is added under `wwwroot/`
- a new controller, service, contract, model, or DTO is created
- a page starts depending on real guidance data, case notes, or counselor workflows
- department ownership or security responsibilities change
- a page moves from mock UI to real backend behavior

### Required update pattern

When the project structure changes, revise these sections in order:

1. `Ownership` — confirm which team and application area owns the feature
2. `How to edit this project` — confirm whether the work is UI-only or backend
3. `Department UI` — add or remove page names and responsibilities
4. `Files to edit for UI changes` and `What not to add while editing the interface` — keep them aligned with the current files
5. `Change Rules` — add any new auth, validation, or contract requirements

### AI session rule

Before making a change in a future session, read this file first and compare it to the current project structure. If the app has new pages, services, policies, or contract files, update this file to match the real state before continuing the task.

### Project map

- `wwwroot/` = static front-end pages and assets
- `wwwroot/styles.css` = visual theme and front-end styling
- `Controllers/` = request handling and API endpoints
- `Services/` = business logic and guidance integrations
- `Contracts/` = interfaces and shared DTOs
- `Models/` = domain objects and data contracts

## Interface-only editing guidance

This project includes a UI layer and a backend API surface, but the static interface pages are intentionally presentation-only unless the owning team has approved the contract. When editing the interface, keep the work in the front-end layer.

### Files to edit for UI changes

- Static pages in `wwwroot/`
- Shared styling in `wwwroot/styles.css`
- Navigation and page structure in the static HTML files
- App shell and page flow in `wwwroot/index.html`

### What not to add while editing the interface

- No database writes or persistence
- No service calls that create or update real guidance records
- No form submissions to live endpoints without an approved contract
- No bypass of the existing authorization boundaries for case notes or counselor workflows

### Approved UI behavior

- Keep forms, cards, and triage lanes as visual placeholders until the backend contract is approved.
- Preserve the static-file structure and security boundaries around sensitive student data.
- Use the existing `StudentRequestService` DTO shape only as a design reference; do not connect the form to real storage without approval.

## Department UI

The following static pages are UI-only presentation placeholders with no database, service, API, or form-submit behavior:

- `/request.html`: student request submission form
- `/triage.html`: counselor queue with new, in-progress, and resolved lanes
- `/appointments.html`: appointment calendar and picker
- `/case-notes.html`: restricted-access case notes panel
- `/resources.html`: college and career resources list

The pages reuse `wwwroot/styles.css` and are linked from the Guidance home page. The request form should remain aligned with the `StudentRequestService` DTO shape, but it must not submit until the owning team connects it to the approved contract. Case notes require real authorization and restricted storage before use.

## Change Rules

- Preserve the static-file/controller architecture; do not convert to Razor Pages without an explicit decision.
- Do not add persistence or service calls to placeholder pages without an approved contract.
- Preserve security boundaries around student requests, counselor triage, and case notes.
- Read `DEVELOPER_SETUP.md` and `SERVICES.md` before service or API changes.
- Run `dotnet build GuidanceDepartmentMain.csproj` and focused tests after changes.
