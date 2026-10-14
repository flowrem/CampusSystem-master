# Testing AI Context

## Current state (read first)

Data layer: this department reads and writes the shared JSON datastore (`campus_system_unified_data_schema.json`) through `CanonicalJsonStore`. There is no SQL, EF Core, MySQL, `DbContext`, migration or connection string. Rules and collection ownership: `Shared/CampusSystem.Data/Json/README.md`. This department's collections and code: the `*-Database-Tables.md` file in this folder.

Testing is a browser and editor for every collection in the datastore (`Services/JsonCollectionBrowser.cs`, `Pages/Index.cshtml`). Edits are live. Tests: `dotnet test ..\TestingMain.Tests`.

## Ownership

Testing owns QA workflows, test-case tracking, regression planning, and defect triage in this project. Keep changes local to `TestingMain` unless a shared contract or platform change is required.

## How to edit this project

Use the task type to decide the correct layer:

- Front-end / interface work: edit only Razor Pages in `Pages/`, the shared layout in `Pages/Shared/_Layout.cshtml`, and styling in `wwwroot/css/site.css`.
- Back-end / data work: edit `Controllers/`, `Services/`, `Contracts/`, models, DTOs, `Program.cs`, and other server-side files only when the task explicitly requires test management functionality.
- AI model rule: if the request does not clearly ask for backend logic, assume it is a UI edit and do not add persistence or external test-system integration.

## AI maintenance manual

This file is the project guide for future AI sessions and developer handoffs. It should stay current as the project changes.

### Project map

- `Pages/` = UI pages and presentation logic
- `Pages/Shared/_Layout.cshtml` = shared shell and global styling entry point
- `wwwroot/css/site.css` = visual theme and component styling
- `Controllers/` = request handling and endpoint behavior
- `Services/` = business logic and integrations
- `Contracts/` = interfaces and shared DTOs
- `Models/` = domain objects and data contracts

## Department UI

The following Razor Pages are conceptual placeholders until a full testing workflow is defined:

- `/Dashboard`: summary of testing status and release health
- `/TestCases`: test case list and assignment board
- `/Bugs`: defect queue and triage states
- `/Reports`: validation and regression reporting
- `/Runs`: test execution and environment tracking

## Change Rules

- Use `Shared/CampusSystem.Ui` for all new page shell and shared component styling; do not introduce local brand tokens.

- Keep testing workflows presentation-first unless a clear contract is approved.
- Do not add persistence or service calls without an approved design.
- Protect sensitive test and defect data with appropriate access checks before backend wiring.
- Run `dotnet build TestingMain.csproj` after UI changes.
