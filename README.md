# CampusSystem

CampusSystem is a multi-department ASP.NET Core solution for campus operations. Each department is its own web application. All of them read and write one shared JSON datastore, so a payment recorded in Finance, a grade saved in Faculty Portal and an enrollment validated in Registrar all show up for the other departments immediately.

There is no SQL database. SQL Server, MySQL and Entity Framework Core were removed in the JSON migration (see [Migration history](#migration-history)).

## Applications

| Department | Project | Dev port | Primary responsibility |
| --- | --- | --- | --- |
| Registrar | `Departments/Registrar/RegistrarMain` | 5005 | Enrollment validation, student records, class scheduling, grades control, documents, plus a student REST API |
| Finance | `Departments/Finance/FinanceMain` | 5124 | Fee assessments, payments, clearance, scholarships, document fees |
| Faculty Portal | `Departments/FacultyPortal/FacultyPortalMain` | 5115 | Class roster with exam permits, gradebook (draft grades, INC), schedule, attendance, assignments, submissions and feedback |
| Student Portal | `Departments/StudentPortal/StudentPortalMain` | 5069 | Student enrollment, payments, library, requests, coursework (turn in work, read scores) |
| Guidance Department | `Departments/GuidanceDepartment/GuidanceDepartmentMain` | 5149 | Counselor portal: sign-in, requests, triage, referrals, case notes, appointments, clearance sign-off and audit trail |
| Library | `Departments/Library/LibraryMain` | 5228 | Catalog, circulation desk (check out and in), fines, reservations |
| Testing | `Departments/Testing/TestingMain` | 5284 | Browser and editor for every collection in the datastore |

All applications target `net10.0` with nullable reference types and implicit usings.

## The datastore

```text
campus_system_unified_data_schema.json   live data, read and written by every app
campus_system_seed_data.json             pristine seed, never written at runtime
```

Every app reaches it through `CanonicalJsonStore` in `Shared/CampusSystem.Data/Json`, registered as a singleton:

```csharp
builder.Services.AddSingleton<CanonicalJsonStore>();
```

The store keeps writes safe across processes. A write reads, changes and saves the file while holding a named mutex that every department process (and `Reset-CampusJsonData.ps1`) shares, and it replaces the file through a temp file so a crash cannot leave half a file. The file carries a `schema_version`, and the store refuses to touch a file newer than it understands.

**Read [`Shared/CampusSystem.Data/Json/README.md`](Shared/CampusSystem.Data/Json/README.md) before changing any data code.** It has the rules (read with `ReadRoot`, write only inside `UpdateRoot`, ids are `int`), the ownership table for all 38 collections, and which departments may write which collections.

Set `CAMPUS_SYSTEM_JSON_PATH` to point an app at a different file, for example a test copy.

### Resetting and backups

```powershell
.\Reset-CampusJsonData.ps1              # back up the live file, then restore it from the seed
.\Reset-CampusJsonData.ps1 -BackupOnly  # timestamped backup only
```

Backups go to `BackupFolder\json\`, which is git-ignored. The script always resets the root file and ignores `CAMPUS_SYSTEM_JSON_PATH`.

## Signing in

Every app asks for a sign-in. Accounts are the `users` rows, passwords are BCrypt hashes (`password_hash`), and five wrong passwords lock an account for 15 minutes. Which accounts an app admits depends on the role:

| App | Roles admitted |
| --- | --- |
| Registrar | registrar, admin |
| Finance | finance, admin, student, faculty (each role sees only its own pages) |
| Faculty Portal | faculty (their own sections), admin (all sections) |
| Student Portal | student |
| Guidance | guidance, admin |
| Library | librarian, admin, student (students see only their own loans and fines) |
| Testing | admin |

**Demo accounts.** Every seeded account uses the password `Campus@2026`. Change it with `/ChangePassword`. The seed is for development only.

| ID number | Role | Name |
| --- | --- | --- |
| REG-2024-001 | registrar | Dr. Rosalinda Santos |
| FIN-2024-002 | finance | Mr. Roberto Gomez |
| FAC-2024-003 | faculty | Prof. Alan Turing |
| GUI-2024-004 | guidance | Ms. Maria Clara |
| LIB-2024-005 | librarian | Ms. Corazon Reyes |
| ADM-2024-006 | admin | System Administrator |
| 2023-01045 | student | Alyssa Bea Mendoza |

Sign-in cookies have a different name in each app (browsers share cookies across ports on localhost). Calls between apps use a service key instead of a user: send `X-Campus-Service-Key` with the value of `CampusSystem:ServiceKey` (a development value is in each `appsettings.json`; override it with an environment variable or user-secrets anywhere real). Finance's `/api/finance/*` endpoints and Guidance's `/api/guidance/*` accept either the key or a signed-in staff user.

## Architecture

- **Ownership.** Each department owns specific collections and defines their fields and status values. Other departments may read any collection and make only the agreed writes listed in the ownership table.
- **No calls between departments.** Cross-department workflows run on status fields in the shared data, not on HTTP calls. For example, Student Portal reserves an enrollment, Finance assesses it, and Registrar validates it. The one exception is `POST /api/finance/assess`.
- **Identity.** Students, faculty and staff are `users` rows with an `int` `id`. Student ids elsewhere are `users.id`.
- **Shared library.** `Shared/CampusSystem.Data` holds only the JSON store. It has no department models.

## Prerequisites

- Windows with PowerShell.
- .NET SDK 10.

Nothing else is needed. There is no database server to install.

## Build, run and test

Build every app:

```powershell
Get-ChildItem .\Departments -Recurse -Filter *.csproj |
    Where-Object { $_.Name -notlike '*.Tests.csproj' } |
    ForEach-Object { dotnet build $_.FullName }
```

Run one app:

```powershell
cd Departments\Finance\FinanceMain
dotnet run --project FinanceMain.csproj
```

Run the tests:

```powershell
dotnet test Shared\CampusSystem.Data.Tests
dotnet test Departments\Finance\FinanceMain\FinanceMain.Tests
dotnet test Departments\Registrar\RegistrarMain.Tests
dotnet test Departments\FacultyPortal\FacultyPortalMain.Tests
dotnet test Departments\StudentPortal\StudentPortalMain.Tests
dotnet test Departments\Library\LibraryMain.Tests
dotnet test Departments\GuidanceDepartment\GuidanceDepartmentMain\GuidanceDepartmentMain.Tests
dotnet test Departments\Testing\TestingMain.Tests
```

If the dev certificate is not trusted, run `dotnet dev-certs https --trust`.

## Health checks

```powershell
.\Check-Departments.ps1 -ProjectPath . -AllDepartments          # structure checks
.\Check-Departments.ps1 -ProjectPath . -AllDepartments -Build   # plus a build of each app
```

For each app it checks that sign-in is configured, that `Program.cs` registers `CanonicalJsonStore`, that no SQL, EF Core or MySQL package or connection string is present, that the datastore file loads and has a supported schema version, and (with `-Build`) that the project compiles.

## Maintenance dashboard

```powershell
.\Maintenance\Start-MaintenanceDashboard.ps1
```

Open `http://localhost:5080/`. The dashboard starts and stops the department apps and runs the health check above. It is local-only. Do not expose it to a network without adding authentication.

`Start-Maintenance-And-Docker.bat` still exists so old shortcuts work. It no longer starts Docker. It checks the datastore file exists and then opens the dashboard.

## Repository map

- [Shared JSON datastore rules and collection ownership](Shared/CampusSystem.Data/Json/README.md)
- [Project map](CampusSystem-Project-Map.md)
- [Architecture and status](AI_CONTEXT_MASTER_RESOLUTION_GUIDE.md)
- [Maintenance AI context](Maintenance/AI-CONTEXT_DepartmentMaintainance%20.md)
- [Maintenance dashboard](Maintenance)

Each department has an `Ai-context_*.md` file and a `*-Database-Tables.md` file in its project folder. Read the owning department's files before changing its code.

## Development rules

- Read data with `ReadRoot()`. Write only inside `UpdateRoot(root => ...)`, and keep the callback short with no I/O or HTTP calls.
- Take new ids from `CanonicalJsonStore.NextId` inside `UpdateRoot`. Ids are `int`.
- Stay inside your department's collections, except the agreed writes in the ownership table. A new collection must be added to the ownership table, the seed file, and `OwnedCollections` in `Shared/CampusSystem.Data.Tests`.
- When you change the file's shape, bump `CanonicalJsonStore.CurrentSchemaVersion` and rebuild every app.
- Do not add SQL, EF Core or MySQL code or packages.
- Derive student identity from authenticated claims on the backend. Do not trust a student id from a request body.
- Do not edit generated `bin`, `obj` or `.vs` output.

## Current status

Every department runs on the shared JSON datastore with real sign-in, all 7 apps build with no warnings, and eight test projects (175 tests) pass.

What works across departments:

- **Fees follow the schedule.** Approving an add/drop, or registering and dropping through Registrar's API, recomputes the student's fee assessment and unpaid installments in the same write and records a `fee_adjustments` row. Overpayment becomes a credit, never a negative balance.
- **Failing or incomplete grades open a Guidance referral.** Faculty saving a grade above 3.00, or marking INC, adds one `ReferredIn` request per enrolled subject for the counselors.
- **Coursework is live.** Faculty record attendance, publish assignments, score submissions and write feedback. Students see published assignments for their sections in Student Portal, turn in a text answer (late work is flagged), and read the score and feedback.
- **Library is live.** Librarians check books out and in, assess overdue/damaged/lost fines, record fine payments or waive them, and manage reservations. Student Portal uses the same lending rules.

Known gaps:

- **Registrar's student REST API has no production sign-in.** Students sign in to Student Portal, not Registrar, so `/api/registrations` and the other student endpoints only work with the development test login. Decide how students should authenticate there before exposing it.
- **No file uploads.** Assignment answers are text only.
- **The JSON file only works on one machine.** It is locked by a machine-local mutex. A real database is needed before deploying across machines.
- **Terms.** Several Finance pages still pass a fixed term (2026-2027); the services ignore it.
- **Historical documents** (`SESSION_HANDOFF_REGISTRAR_STUDENT_PORTAL.txt`, `BuildStruct/`, `Requirements/SERVICES.md`, `Departments/Finance/Finance-Department-Documentation.md`, `Departments/GuidanceDepartment/operation.md`, `AI_CONTEXT_SYSTEM_ISSUES.md`) describe the earlier SQL design and carry a notice saying so.

## Migration history

The project moved from per-department SQL databases to the single JSON datastore in phases:

0. Hardened the store: cross-process lock, atomic writes, schema version, tests.
1. Finance onto the store.
2. Registrar's student REST API onto the store.
3. Faculty Portal onto the store (draft grades).
4. Guidance (int ids), Student Portal guidance requests, Library, Testing.
5. Removed EF Core, SQL Server and MySQL packages, connection strings, the SQL connector, and the per-department copies of the Guidance service stack; rewrote the docs.

A zip of the files Phase 5 deleted or rewrote is in `BackupFolder\before-phase5.zip`.

After the migration the remaining stubs were replaced: shared sign-in, the Faculty-to-Guidance referral, add/drop fee recalculation, Faculty coursework, the Library app, and the Integration Hub (the old PHP/XAMPP fallback is gone; it now pings the department apps and probes their real endpoints).

## Shared UI

All department interfaces use `Shared/CampusSystem.Ui` for campus tokens and common components. New pages must link the shared stylesheet and use its shell, heading, surface, alert, and empty-state classes. Run `powershell -ExecutionPolicy Bypass -File scripts/verify-ui-alignment.ps1` to detect missing links or hard-coded brand token definitions.
