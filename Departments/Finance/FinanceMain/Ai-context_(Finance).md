# Finance AI Context

## Current state (read first)

Data layer: this department reads and writes the shared JSON datastore (`campus_system_unified_data_schema.json`) through `CanonicalJsonStore`. There is no SQL, EF Core, MySQL, `DbContext`, migration or connection string. Rules and collection ownership: `Shared/CampusSystem.Data/Json/README.md`. This department's collections and code: the `*-Database-Tables.md` file in this folder. Sign-in: the shared `AuthStore` (see the root README for roles and demo accounts).

Finance is **not** UI-only. These pages run on real data through `Services/FinanceJsonStore.cs`: Dashboard, Assessments, Billing, Payments, Clearance, FacultyClearance, Aid (scholarships), Pay, OAO, Invoice, Receipt, RequestFees and StudentAccount. Staff pages need finance or admin; a student opens only their own documents (another student's id gives "not found"); faculty see the clearance roster. Fees come from the shared `FeeCalculator`, which Registrar also uses to recalculate fees on add/drop (`fee_adjustments`). `/api/finance/*` accepts the service key or finance/admin/registrar sign-in. Tests: `dotnet test FinanceMain.Tests`. The "UI-only" rules further down are older text; ignore them for these pages.

## Ownership

Finance owns billing, payment, and financial-aid interfaces in this project. Keep changes local to `FinanceMain` unless a shared contract or platform change is required.

## How to edit this project

Use the task type to decide the correct layer:

- Front-end / interface work: edit only Razor Pages in `Pages/`, the shared layout in `Pages/Shared/_Layout.cshtml`, and styling in `wwwroot/css/site.css`. Keep forms and actions presentation-only until a real contract and approval are in place.
- Back-end / data work: edit `Controllers/`, `Services/`, `Contracts/`, models, DTOs, `Program.cs`, and other server-side files only when the task explicitly requires payment, invoice, or aid integration.
- AI model rule: if the request does not clearly ask for backend logic, assume it is a UI edit and do not add persistence, payment processing, or external API calls.

## AI maintenance manual

This file is the project guide for future AI sessions and developer handoffs. It should stay current as the project changes.

### When to update this file

Update this context whenever any of the following changes:

- a new Razor Page, folder, or route is added under `Pages/`
- a new controller, service, contract, model, or DTO is created
- a page starts depending on real billing, payment, or aid data
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

- `Pages/` = UI pages and presentation logic
- `Pages/Shared/_Layout.cshtml` = shared shell and global styling entry point
- `wwwroot/css/site.css` = visual theme and component styling
- `Controllers/` = request handling and endpoint behavior
- `Services/` = business logic and integrations
- `Contracts/` = interfaces and shared DTOs
- `Models/` = domain objects and data contracts

## Interface-only editing guidance

This department is a frontend-only dashboard shell. If the task is to modify the interface, keep it in the presentation layer and do not add backend behavior.

### Files to edit for UI changes

- Razor Pages in `Pages/`
- Shared layout in `Pages/Shared/_Layout.cshtml`
- Styling in `wwwroot/css/site.css`
- Supporting front-end assets in `wwwroot/`

### What not to add while editing the interface

- No live financial calculations tied to production data
- No hidden API calls that would process real payment data
- Direct calls into another department's controllers, services, or API endpoints remain unapproved.

### Approved UI behavior

- Payment forms, invoice downloads and aid actions are live. Keep amount validation (a payment must be positive and no larger than the balance) when changing them.
- Preserve the existing Bootstrap structure and page flow.
- Update labels, cards, filters, and styling without introducing live workflow behavior.

## Department UI

Razor Pages (all now wired to the datastore; the descriptions are the original UI intent):

- `/Billing`: billing and invoice list
- `/Invoice`: invoice detail, line items, totals, and download placeholder
- `/Payments`: payment history table
- `/Pay`: make-a-payment form; UI only
- `/Aid`: financial aid and scholarship status card

The pages use the existing Bootstrap layout and local styles in `Pages/Shared/_Layout.cshtml` and `wwwroot/css/site.css`. Keep payment and download controls non-functional until approved integrations exist.

## Change Rules

- Add authorization before using Finance with real students. The current student is a fixed demo id.
- Protect financial and aid information with appropriate access checks before backend wiring.
- Run `dotnet build FinanceMain.csproj` after UI changes.
