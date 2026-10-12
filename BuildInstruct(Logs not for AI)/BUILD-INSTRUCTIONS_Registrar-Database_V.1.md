# Build Instructions: Split Shared DbContext into Per-Department Contexts V1

**Supersedes:** `BUILD-INSTRUCTIONS_Shared-Campus-Database.md` and `INSTRUCTIONS_Update-AI-Context-Files.md`. Both described a single `CampusSystemDbContext` owning every department's tables and one shared migration history. That design is being corrected here because it forces every department team to edit a shared file outside their own project — the opposite of what was asked for.

**What stays the same:** one physical database (`CampusSystemDb`), one LocalDB instance, one connection string shared by all six departments, per-department SQL schemas (`registrar`, `finance`, `library`, etc.).

**What changes:** each department gets **its own `DbContext`, its own entity models, and its own migration history**, living entirely inside that department's own project folder. The shared project shrinks to just the one thing that's genuinely cross-department: student identity.

---

## Part A — Refactor the shared project down to identity only

### A1. Trim `CampusSystem.Data`

Keep only:
- `Models/Student.cs` (unchanged — `Id`, `StudentNumber`, `FullName`, `Email`, mapped to `dbo.Students`)

Remove from `CampusSystem.Data`:
- `CampusSystemDbContext.cs`
- `Course.cs`, `Enrollment.cs`, `TranscriptEntry.cs`, `VerificationRequest.cs`, `RecordsRequest.cs`
- The existing `Migrations/` folder

`CampusSystem.Data` should end up small enough that it rarely changes once this refactor is done — it's a shared vocabulary (currently just "what is a student"), not a shared database layer.

### A2. Keep the `dbo.Students` table itself

The `Students` table already exists in `CampusSystemDb` from the current migration. Rather than dropping and recreating the whole database, the cleanest path is:

```powershell
cd Shared\CampusSystem.Data
dotnet ef migrations add IdentityOnly --context CampusSystemDbContext
```
Review the generated migration — it should show the department-specific tables (`Courses`, `Enrollments`, etc.) being dropped, since their `DbSet<T>` properties are being removed from this context. Apply it:
```powershell
dotnet ef database update --context CampusSystemDbContext
```
Then delete `CampusSystemDbContext.cs` itself and its `Migrations/` folder from the project, since Part B replaces it with Registrar's own context (which will re-create the `registrar` schema tables independently).

---

## Part B — Give Registrar its own DbContext

### B1. Add packages directly to RegistrarMain (if not already present after the reference removal)

```powershell
cd Departments\Registrar\RegistrarMain
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.EntityFrameworkCore.Tools
```

Keep the existing project reference to `CampusSystem.Data` — Registrar still needs it for the shared `Student` type.

### B2. Move the five entity models into RegistrarMain

Move `Course`, `Enrollment`, `TranscriptEntry`, `VerificationRequest`, `RecordsRequest` from `CampusSystem.Data` into `RegistrarMain/Models/`, keeping their `[Table(..., Schema = "registrar")]` attributes and their `Student` foreign keys (referencing the shared type from `CampusSystem.Data`).

### B3. Create `RegistrarDbContext` inside RegistrarMain

Add `RegistrarMain/Data/RegistrarDbContext.cs`:

```csharp
public class RegistrarDbContext : DbContext
{
    public RegistrarDbContext(DbContextOptions<RegistrarDbContext> options) : base(options) { }

    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<TranscriptEntry> TranscriptEntries => Set<TranscriptEntry>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<RecordsRequest> RecordsRequests => Set<RecordsRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Enrollment>()
            .Property(e => e.RowVersion)
            .IsRowVersion();

        // This context does not own dbo.Students — it only references it as a foreign key target.
        modelBuilder.Entity<Student>().ToTable("Students", "dbo", t => t.ExcludeFromMigrations());
    }
}
```

The `ExcludeFromMigrations()` call is the important part — it tells EF Core that `RegistrarDbContext` can query and join against `dbo.Students`, but must never try to create, alter, or drop that table. `CampusSystem.Data`'s own context (if one still exists for identity) or a dedicated identity-migration step owns that table exclusively.

### B4. Register RegistrarDbContext with its own migrations-history table

In `RegistrarMain/Program.cs`:

```csharp
builder.Services.AddDbContext<RegistrarDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CampusSystemDb"),
        x => x.MigrationsHistoryTable("__EFMigrationsHistory_Registrar", "registrar")));
```

Note: connection string key stays `CampusSystemDb` and points at the same physical database as before — only the `DbContext` type and the migrations-history table name change. Remove any leftover `AddDbContext<CampusSystemDbContext>` registration.

### B5. Create Registrar's own migration

```powershell
cd Departments\Registrar\RegistrarMain
dotnet ef migrations add InitialCreate --context RegistrarDbContext
dotnet ef database update --context RegistrarDbContext
```

This recreates the `registrar` schema tables, now owned by Registrar's own migration history, stored under `registrar.__EFMigrationsHistory_Registrar` — completely separate from any other department's history table, even though all of them live in `CampusSystemDb`.

### B6. Verify

```powershell
dotnet build RegistrarMain.csproj
dotnet build CampusSystem.Data.csproj
```
Then inspect `CampusSystemDb` directly (SSMS or the SQL Server extension) to confirm: `dbo.Students` exists once, `registrar.*` tables exist, and there are two separate `__EFMigrationsHistory*` tables, not one shared one.

---

## Part C — The pattern for the next department (template, not yet executed)

When Finance, Library, FacultyPortal, GuidanceDepartment, or StudentPortal are wired up later, each one repeats Part B inside its own project: its own models under its own schema, its own `{Department}DbContext`, its own `MigrationsHistoryTable("__EFMigrationsHistory_{Department}", "{schema}")`, its own migration. None of them touch `CampusSystem.Data` unless the shared `Student` model itself needs a new field — and even then, only that one shared file changes, not another department's context.

---

## Part D — Corrected AI-context file updates

The previous `INSTRUCTIONS_Update-AI-Context-Files.md` described the single-`CampusSystemDbContext` design and needs correcting wherever it was already applied. Re-open each file below and fix the following:

### D1. Workspace-level AI context

Replace any statement that `CampusSystemDbContext` is "the shared database layer used by all departments" with:

> "`Shared/CampusSystem.Data` contains only the shared `Student` identity model. Each department owns its own `DbContext`, its own entity models, and its own migration history inside its own project, scoped to its own SQL schema, all pointed at the one physical `CampusSystemDb` database. No department should need to edit another department's `DbContext` or another department's migrations. The only shared file teams routinely reference is the `Student` model itself."

Add to the Repository Map table:
| Area | Purpose |
|---|---|
| `Shared/CampusSystem.Data` | Shared `Student` identity model only — not a shared database layer |

### D2. `PROJECT_MAP.md`

No structural change needed beyond what was already added (`Shared/CampusSystem.Data` still exists as a folder) — just correct any description implying it holds all schema/migrations.

### D3. Registrar's `Ai-context_(Registrar).md`

- Replace every reference to `CampusSystemDbContext` with `RegistrarDbContext`.
- Update Change Rules: "Registrar owns `RegistrarDbContext`, its own `Migrations/` folder, and the `registrar` schema inside the shared `CampusSystemDb` database. Registrar does not modify `CampusSystem.Data` except when the shared `Student` model itself needs a field, which requires cross-team coordination."
- Note the migrations-history table name (`__EFMigrationsHistory_Registrar`) so a future session doesn't assume a single shared history exists.

### D4. FacultyPortal, Finance, Library, StudentPortal — `Ai-context_(*).md`

Replace the earlier note (which pointed to a shared `CampusSystemDbContext`) with:

> "A shared campus database (`CampusSystemDb`) exists, along with a shared `Student` identity model in `CampusSystem.Data`. When this department's persistence is built, it gets its **own** `{Department}DbContext`, its own models, and its own migration history inside this project — following the pattern already used by Registrar (see `Ai-context_(Registrar).md` for the concrete example). Do not add a `DbSet` for this department's tables into `CampusSystem.Data` or into another department's context."

### D5. GuidanceDepartment's `Ai-context_(GuidanceDepartment).md`

Same correction as D4, plus keep the existing caution that case notes and audit events need an explicit authorization/durability decision before migrating off `InMemoryGuidanceRequestStore` — that part of the earlier guidance was correct regardless of which `DbContext` design is used.

### D6. StudentPortal's cross-department read language

Keep the earlier clarification (a source department's data is only readable once that department has actually built its own schema), but correct the mechanism description: cross-department reads mean querying another department's schema in the same physical database (e.g., StudentPortal's context reading `library.*` tables, marked `ExcludeFromMigrations()` the same way Registrar excludes `dbo.Students`) — not going through one shared `CampusSystemDbContext`, which no longer exists as a concept.

---

## Verification checklist

- [ ] `CampusSystem.Data` contains only `Student` — no department-specific entities or `DbContext`
- [ ] `RegistrarDbContext` exists inside `RegistrarMain`, owns only `registrar.*` tables, and excludes `dbo.Students` from its own migrations
- [ ] Two distinct migrations-history tables exist in `CampusSystemDb` (one for identity/shared, one for Registrar), not one shared history
- [ ] All six department `.csproj` files still reference `CampusSystem.Data`, but only Registrar has a second, department-owned `DbContext` so far
- [ ] Every AI-context file edited under the previous instruction set has been re-corrected per Part D above — search specifically for the string `CampusSystemDbContext` in all six department files and the workspace-level file; any remaining occurrence outside this document is stale