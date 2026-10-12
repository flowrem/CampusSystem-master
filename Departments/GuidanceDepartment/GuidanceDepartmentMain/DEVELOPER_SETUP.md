# Guidance Department developer guide

## Requirements

- Windows and the .NET 10 SDK.
- The repository root must contain `campus_system_unified_data_schema.json`.
- No external data service is required.

## Build and test

From this project directory:

```powershell
dotnet build .\GuidanceDepartmentMain.csproj
dotnet test .\GuidanceDepartmentMain.Tests\GuidanceDepartmentMain.Tests.csproj
```

If the app is running and locks its output files, stop that instance first or build into `BuildTemp`.

## Run locally

```powershell
dotnet run --project .\GuidanceDepartmentMain.csproj --urls http://localhost:5149
```

Open `http://localhost:5149/Login`. The development counselor account is `GUI-2024-004` with password `Campus@2026`.

## Configuration

The canonical data file is located automatically from the repository root. Set `CAMPUS_SYSTEM_JSON_PATH` only when using a temporary test copy. The development service key is in `appsettings.json`; production deployments must provide an environment-specific value.

## Development rules

- Read data through `CanonicalJsonStore.ReadRoot()`.
- Change data only inside `CanonicalJsonStore.UpdateRoot(...)`.
- Use integer identifiers from `CanonicalJsonStore.NextId(...)`.
- Keep the shared cookie roles as `guidance` and `admin`; static counselor pages use `.CampusSystem.Guidance.Auth`.
- Add focused tests whenever a controller, workflow, or collection changes.
- Do not edit generated `bin` or `obj` output.
