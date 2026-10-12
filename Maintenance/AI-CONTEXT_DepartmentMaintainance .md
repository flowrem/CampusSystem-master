# Campus System AI Context

## Mission

Campus System is a multi-department ASP.NET Core solution. Each department is an independent .NET web project. All of them read and write one shared JSON datastore (`campus_system_unified_data_schema.json`) through `CanonicalJsonStore`. There is no SQL database, EF Core or MySQL.

When modifying this workspace, preserve department ownership of collections and follow the rules in `Shared/CampusSystem.Data/Json/README.md`. Prefer small, local changes, and validate the affected project with `Check-Departments.ps1`, `dotnet build` and its tests.

## Repository Map

The workspace root is `D:\CampusSystem-master`.

| Area | Purpose |
| --- | --- |
| `Departments` | Independent department applications |
| `Requirements` | Product and technical requirements |
| `BackupFolder` | Datastore backups and `before-phase5.zip`; git-ignored, not active source |
| `Maintenance` | Local health dashboard, launcher, and AI context |
| `Shared/CampusSystem.Data` | `CanonicalJsonStore` and its README (rules and collection ownership) |
| `Shared/CampusSystem.Data.Tests` | Store tests |
| `campus_system_unified_data_schema.json` | Live datastore |
| `campus_system_seed_data.json` | Pristine seed |
| `Reset-CampusJsonData.ps1` | Back up and reset the datastore |
| `Check-Departments.ps1` | Health check for one project or all departments |
| `Shared/CampusSystem.Data/Auth` | `AuthStore` and `CampusAuth`: sign-in, roles, service key |

## Departments

Every department targets `net10.0`, uses nullable reference types and implicit usings, and has a project folder ending in `Main`.

| Department | Project path | Assembly/namespace |
| --- | --- | --- |
| FacultyPortal | `Departments/FacultyPortal/FacultyPortalMain` | `FacultyPortalMain` |
| Finance | `Departments/Finance/FinanceMain` | `FinanceMain` |
| GuidanceDepartment | `Departments/GuidanceDepartment/GuidanceDepartmentMain` | `GuidanceDepartmentMain` |
| Library | `Departments/Library/LibraryMain` | `LibraryMain` |
| Registrar | `Departments/Registrar/RegistrarMain` | `RegistrarMain` |
| StudentPortal | `Departments/StudentPortal/StudentPortalMain` | `StudentPortalMain` |
| Testing | `Departments/Testing/TestingMain` | `TestingMain` |

Each department owns specific collections (see the ownership table) and does its own reads and writes through `CanonicalJsonStore`. Departments do not call each other's controllers, services or APIs. Workflows between departments run on status fields in the shared data. The one exception is Finance's `POST /api/finance/assess`.

The Guidance service stack (auth, request, triage, CSV, PII masking, notification) lives only in `GuidanceDepartmentMain`. The copies that used to be installed in the other departments were removed, along with `Install-GuidanceServices.ps1`.

## Maintenance Tools

### Health checker

`Check-Departments.ps1` replaces the old `Check-GuidanceServices.ps1`. Run one project from its project directory:

```powershell
.\Check-Departments.ps1 -ProjectPath .
```

Run all departments from the workspace root, optionally with compilation:

```powershell
.\Check-Departments.ps1 -ProjectPath . -AllDepartments
.\Check-Departments.ps1 -ProjectPath . -AllDepartments -Build
```

For each project it checks that sign-in is configured (`AddCampusAuth`, or the app's own AuthStore sign-in for Student Portal and Guidance), that `Program.cs` registers `CanonicalJsonStore`, that no SQL, EF Core or MySQL package is referenced, that no `ConnectionStrings` section is in `appsettings*.json`, that the datastore file loads and reports a schema version, and (with `-Build`) that the project builds. Exit code `0` means all checks pass; `1` means one or more failed. A full run reports seven projects, all HEALTHY.

It does not run tests. Run the test projects listed in the README separately.

### Maintenance dashboard

Files in `Maintenance`:

- `index.html`: browser dashboard with Campus and department terminal tabs, department cards, green/red process indicators, clickable department web-address links, Start/Stop buttons, bulk Start all/Stop all controls, error list, and terminal-style output. Process states and the selected department log refresh every `500 ms`; full builds run only when requested.
- `Start-MaintenanceDashboard.ps1`: local-only `HttpListener` host with health and department lifecycle APIs. Failed department processes with non-zero exit codes are removed from the running set and remain stopped. Process status includes `Running`, `Managed`, `CanStop`, and `Error` so the UI can distinguish Maintenance-owned processes from externally launched processes.
- `Run-MaintenanceMonitoring.bat`: starts the host on port `5080` and opens the browser.
- `AI-CONTEXT_DepartmentMaintainance .md`: this document.

Start it with:

```powershell
cd D:\CampusSystem-master
.\Maintenance\Start-MaintenanceDashboard.ps1
```

Open `http://localhost:5080/`, then select **Run full debug**. The dashboard runs `Check-Departments.ps1` for all departments with `-Build` and displays failures in the terminal panel. Green dots mean a department process is running; red dots mean it is stopped. Each card can start or stop its department with `dotnet run`, and a running Maintenance-owned process has an **Open site** link for its assigned localhost address. Logs are written to `Maintenance/logs`. Maintenance-owned processes can be stopped from the dashboard; processes launched by Visual Studio are displayed as running but are not stopped by Maintenance.

The dashboard API is:

- `GET /api/health?build=true`: run the structure and build checks (`Check-Departments.ps1`) for all departments.
- `GET /api/departments`: return current process states.
- `POST /api/departments/start-all`: start every department process.
- `POST /api/departments/stop-all`: stop every department process.
- `POST /api/departments/{name}/start`: start one department project.
- `POST /api/departments/{name}/stop`: stop one department process tree.
- `GET /api/departments/{name}/log`: return the latest output and error log for a department terminal tab.

Maintenance assigns distinct high localhost ports to department processes started from the dashboard:

| Department project | Maintenance port |
| --- | ---: |
| `FacultyPortalMain` | `52141` |
| `FinanceMain` | `52142` |
| `GuidanceDepartmentMain` | `52143` |
| `LibraryMain` | `52144` |
| `RegistrarMain` | `52145` |
| `StudentPortalMain` | `52146` |

TestingMain is not managed by the dashboard.

These ports are passed through `dotnet run --urls` and are intended to reduce collisions with Visual Studio launch ports. They are not a security boundary and may still be changed if another process already uses one.

The **Open site** links use the assigned Maintenance URL for Maintenance-owned processes. For processes launched externally, including Visual Studio, the host checks the process's listening localhost port and uses that actual port when available. External processes are reported as running but their Maintenance Stop button remains disabled; stop them from Visual Studio or terminate their process tree explicitly before starting the same project through Maintenance.

When a department process exits with a non-zero exit code, the host treats it as an error, removes it from the active process set, and reports it as stopped. Do not restart it automatically until the underlying error is investigated.

If Visual Studio is already running a department, do not start that same project through Maintenance. Visual Studio owns the generated executable in `bin/Debug/net10.0`, and a Maintenance build can fail with `MSB3027` or `MSB3021` because the executable is locked. Use the Visual Studio process, or stop it in Visual Studio before starting it from Maintenance. A typical lock message names the process and ends with `The file is locked by: "DepartmentMain (PID)"`.

The host handles an occupied port. If the requested port already serves this dashboard, it reports the existing URL. If another process owns it, start on another port, for example:

```powershell
.\Maintenance\Start-MaintenanceDashboard.ps1 -Port 5081
```

Stop the host with `Ctrl+C`. Stopping the host also stops department processes started by that host.

The dashboard is intentionally local-only. Keep it bound to `localhost`; do not expose it to a network without authentication and authorization. The `Campus` terminal tab shows dashboard/debug activity; each department tab shows that project's latest `dotnet run` output and error log. If a department is already running from Visual Studio, the dashboard reports it as running, does not rebuild it, and disables its Maintenance Stop button; stop that process from Visual Studio before using Maintenance for that project.

The default dashboard port is `5080`. If an older dashboard listener remains on that port, start the corrected host on another local port, such as `5081`, and open that URL instead.

## Security and Production Boundaries

- Keep JWT signing keys out of JSON and source control. Use `dotnet user-secrets` locally and a managed secret provider such as Azure Key Vault in production.
- The JSON file is a development datastore. It is one file locked by a machine-local mutex, so it does not support more than one machine. Plan a real database before deploying beyond a single host.
- Replace `UnavailableOutboundMessageTransport` with an approved email, SMS, or messaging transport.
- Sign-in is real (shared `AuthStore`). The demo accounts all share one password and the service key in `appsettings.json` is a development value. Change both, and keep real keys out of source control.
- Registrar's student REST API has no production sign-in yet.
- Keep rate limiting, HTTPS, authentication, authorization, PII masking, and audit behavior enabled where applicable.
- Run the dependency vulnerability check before merging package changes:

```powershell
dotnet list package --vulnerable
```

## AI Change Rules

1. Identify the owning department project and the owning collections before editing.
2. Read data with `ReadRoot()` and write only inside `UpdateRoot(root => ...)`. Ids are `int` from `CanonicalJsonStore.NextId`.
3. A new collection must be added to the ownership table, the seed file, and `OwnedCollections` in `Shared/CampusSystem.Data.Tests`. Bump `CurrentSchemaVersion` when the file's shape changes.
4. Do not add SQL, EF Core or MySQL code or packages, and do not add connection strings.
5. Do not edit generated `bin`, `obj`, or `.vs` output.
6. Do not overwrite user changes or backups without inspection.
7. After edits, run the narrowest relevant check first (`Check-Departments.ps1` for the project), then build and test the affected project.
8. If changing checker output labels or the dashboard API shape, update both the parser in `Start-MaintenanceDashboard.ps1` and the HTML consumer.
9. Treat a passing health check as structural health only. It does not prove real authentication, production persistence, or external messaging.

## Current Validation Expectations

A complete workspace validation reports seven department projects, all HEALTHY, builds with 0 warnings, and eight passing test projects (store and shared rules 51, Finance 15, Registrar API 13, Faculty 33, Student Portal 14, Guidance 30, Library 10, Testing 9). Build failures should be reported with the department and the compiler error.
