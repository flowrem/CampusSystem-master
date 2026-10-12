# Registrar AI Context

## Current state (read first)

Data layer: this department reads and writes the shared JSON datastore (`campus_system_unified_data_schema.json`) through `CanonicalJsonStore`. There is no SQL, EF Core, MySQL, `DbContext`, migration or connection string. Rules and collection ownership: `Shared/CampusSystem.Data/Json/README.md`. This department's collections and code: the `*-Database-Tables.md` file in this folder. Sign-in: the shared `AuthStore` (see the root README for roles and demo accounts).

Staff pages need a registrar or admin sign-in (cookie `.CampusSystem.Registrar`). The student REST API (`/api/courses`, `/api/registrations`, `/api/transcript`, `/api/records`, `/api/verifications`) runs on `Services/RegistrarApiStore.cs`; it has no production sign-in, only the development test login (`DevelopmentAuthentication:StudentId`, set to 5). Approving an add/drop, and registering or dropping through the API, recalculates the student's fees through `FeeCalculator.Recalculate` and records a `fee_adjustments` row. The Integration Hub pings the department apps and probes real endpoints. `RegistrarDbContext`, the `Models/` entities and `Migrations/` were deleted. Tests: `dotnet test ..\RegistrarMain.Tests`. Older text below that mentions `RegistrarDbContext`, migrations or SQL is out of date.

## Ownership

Registrar owns course browsing, registration, academic records, verification, and records-request interfaces in this project. Keep changes local to `RegistrarMain` unless a shared contract or platform change is required.

## How to edit this project

Use the task type to decide the correct layer:

- AI model rule: if the request does not clearly ask for backend logic, assume it is a UI edit and do not add persistence, form submission, or live academic-data processing.

## AI maintenance manual

This file is the project guide for future AI sessions and developer handoffs. It should stay current as the project changes.

### When to update this file

Update this context whenever any of the following changes:

- a new Razor Page, folder, or route is added under `Pages/`
- a new controller, service, contract, model, or DTO is created
- a page starts depending on real registration, transcript, or records data
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


### Files to edit for UI changes

- Razor Pages in `Pages/`
- Shared layout in `Pages/Shared/_Layout.cshtml`
- Styling in `wwwroot/css/site.css`
- Supporting front-end assets in `wwwroot/`

### What not to add while editing the interface

- Do not wire Razor Page forms directly to the API without an explicit UI task and review.
- Do not move API request validation or claims-derived identity checks into client-side code.
- Do not accept `StudentId` from Razor Page form fields or browser request bodies.

### Approved UI behavior

- Use the Registrar API for live course, enrollment, transcript, verification, and records data.
- Keep claims-derived identity and authorization enforcement on the server; the browser UI must not become the source of truth for student ownership.
- Preserve the existing project structure and keep future UI changes focused on presentation and API integration.

## Department UI

The following Razor Pages are the Registrar UI surface and are wired to the Registrar API for catalog browsing, enrollment, transcript display, verification requests, and records requests:

- `/Courses`: course catalog and browse page
- `/Registration`: registration and schedule builder
- `/Transcript`: semester-grouped transcript view
- `/Verification`: enrollment verification request form
- `/Records`: records request and status tracker

The pages use the existing Bootstrap layout and local styles in `Pages/Shared/_Layout.cshtml` and `wwwroot/css/site.css`.

## API Endpoints

The Registrar API is available for database and authorization testing. All endpoints require authentication; in Development, the `DevelopmentTest` scheme supplies the configured `StudentId` claim and defaults to the `Student` role. Send `X-Test-Role: Admin` only when testing the course seed endpoint.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/courses?search=` | Browse the course catalog |
| `GET` | `/api/courses/{id}` | Read one course |
| `POST` | `/api/courses` | Create a course; Admin role only |
| `GET` | `/api/registrations/mine` | List the authenticated student's enrollments |
| `POST` | `/api/registrations` | Enroll the authenticated student in a course |
| `DELETE` | `/api/registrations/{id}?rowVersion=` | Drop an owned enrollment with optimistic concurrency |
| `GET` | `/api/transcript/mine` | Read the authenticated student's semester-grouped transcript |
| `GET` | `/api/verifications/mine` | List the student's verification requests |
| `POST` | `/api/verifications` | Create a pending verification request |
| `GET` | `/api/records/mine` | List the student's records requests |
| `POST` | `/api/records` | Create a pending document request |


## Change Rules

- Registrar does not modify `CampusSystem.Data` except when the shared `Student` model itself needs a field, which requires cross-team coordination.
- Derive `StudentId` from authenticated claims; never accept it from request payloads.
- Protect academic records and enrollment verification with authorization and audit controls as the API expands; the current endpoints enforce authentication and student ownership, while administrative workflows remain deferred.
- Treat `Enrollment.RowVersion` as a concurrency token and handle update conflicts.
- Preserve existing Razor Pages behavior and department namespace.
- Run `dotnet build RegistrarMain.csproj` after UI changes, API changes, or data changes.
- From the workspace root, run `.\Check-Departments.ps1 -ProjectPath .\Departments\Registrar\RegistrarMain` for Registrar, or `.\Check-Departments.ps1 -ProjectPath . -AllDepartments -Build` for the full health check.

##