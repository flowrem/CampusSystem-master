# Fix plan: Student Records throws when a student ID is blank

> This file used to hold the Guidance fix plan. Codex finished that work and it passed the full test run on 2026-10-13, so the file now holds the next fix. The old plan is no longer needed.

For whoever is editing `Departments/Registrar/RegistrarMain` (currently Codex). Nobody else is working in that folder.

Read first: `README.md` and `Shared/CampusSystem.Data/Json/README.md`. Do not add SQL, EF Core or MySQL code. Write only inside `CanonicalJsonStore.UpdateRoot`. Ids are `int`.

## The problem

On `http://localhost:5005/StudentRecords` (signed in as `REG-2024-001`), these handlers throw `System.NullReferenceException` when the student ID or email field is empty:

| Handler | Line in `Pages/StudentRecords.cshtml.cs` | What is null |
| --- | --- | --- |
| `OnPostRecordPayment` | ~333, via `FindUser` (~417) | `studentIdNumber.Trim()` |
| `OnPostRegister` | ~384, via `FindUser` (~417) | `studentIdNumber.Trim()` |
| `OnPostCreate` (and `OnPostCreateStudent`) | ~243 and ~244 | `email.Trim()` and `studentIdNumber.Trim()` |

The page catches the exception and shows `Object reference not set to an instance of an object.` to the registrar, and the server logs `Student Records JSON payment failed` or `... registration failed`.

**Why it happens.** Razor Pages binds an empty form field to `null`, not to `""`. The handler parameters are declared as non-nullable `string`, so nothing stops the null from reaching `.Trim()`.

**How to see it.** The "Create student" form on this page has a field labelled **"Student ID (optional)"** (`name="studentIdNumber"`, `StudentRecords.cshtml` line ~15). Leave it blank and submit: the create fails with the null-reference message even though the label says the field is optional. The code below that line already handles a blank ID (it generates `2026-<id>`), so the intent is clear and only the null check is missing.

The Record Payment and Register buttons in the table always send an ID, so they only fail when called without one (for example a stale form or a hand-built request). They need to fail with a clear message, not a stack trace.

## Fix

Keep the changes inside `Pages/StudentRecords.cshtml.cs`. Do not change the form markup unless a check below needs it.

1. **Create: blank ID and email.**
   - Declare `studentIdNumber`, `contactNumber` and `gender` as nullable (`string?`) where they are optional, and use `(studentIdNumber ?? "").Trim()` so a blank ID falls through to the existing auto-generated `2026-<id>` branch.
   - Name, email, program and year level are required. Check them before the `UpdateRoot` call and set `Error` to a specific message ("Name is required.", "A valid email address is required.", and so on) instead of letting a null reach `.Trim()`. Reuse the rules from `EnrollmentApplication.cshtml.cs` (email must contain `@`, program must be in the program list) so both forms agree.
   - Keep the duplicate check (same ID number or email) and keep it case-insensitive.
2. **Record payment and register: blank ID.**
   - Make `studentIdNumber` nullable. At the start of each handler, if it is null or whitespace, set `Error = "Choose a student first."` and return the page. Do not call `UpdateRoot`.
   - Make `FindUser` null-safe: `(studentIdNumber ?? "").Trim()`. An unknown ID should still throw the existing "Student account was not found." message.
3. **Other handlers on the page.** Read `OnPostUpdateStudentStatusAsync` and `OnPostUpdateDocumentAsync` and any other `OnPost*` in the file. Anywhere a `string` parameter is used with `.Trim()`, `.Equals(...)` or `.ToLower()` without a null check, apply the same pattern. Do not change behaviour that already works.
4. **Do not hide real errors.** Keep the `try/catch` that logs and shows `Error`, but the messages the registrar sees for blank input must be the specific ones above, never "Object reference not set...".

Leave the 10,000 minimum down payment rule, the "paid payment required before registration" rule, and the COR creation exactly as they are.

## Tests

The page model is hard to unit test directly, so cover this in two layers:

1. **Unit tests** in `Departments/Registrar/RegistrarMain.Tests`. If the logic you changed can be moved into a small helper (for example a `StudentInput.Validate(...)` that returns an error message or the cleaned values, and a null-safe lookup), put that helper in the Registrar project and test it with these cases:
   - blank ID is allowed and produces an auto ID;
   - blank name, blank email, email without `@`, unknown program each return their own message;
   - duplicate email and duplicate ID number (different letter case) are refused;
   - lookup with `null`, `""`, `"   "` and an unknown ID returns the right message and never throws a `NullReferenceException`.
2. **HTTP check** against a temp copy of the datastore (never your live file). Start Registrar with `CAMPUS_SYSTEM_JSON_PATH` pointing at a copy of `campus_system_unified_data_schema.json`, sign in, and post the three handlers with an empty ID. A script that does this with `curl` and an antiforgery token is acceptable; keep it out of the repo (use the Windows temp folder).

Before and after, run the Registrar log check: `grep -i NullReference` on the app's console output must show nothing for these requests.

## Check when done

1. `dotnet build Departments\Registrar\RegistrarMain\RegistrarMain.csproj` gives 0 warnings and 0 errors.
2. `dotnet test Departments\Registrar\RegistrarMain.Tests` passes (13 tests before your change; add yours on top).
3. With Registrar running on a temp datastore copy:
   - Create student with the ID field blank: a new student appears in the list with an ID like `2026-00014`, and the page shows a success message, not an error.
   - Create student with the email blank: the message is "A valid email address is required." and no user is added.
   - Create student with an existing email: the message says it already exists.
   - POST `RecordPayment` and `Register` with no `studentIdNumber`: the message is "Choose a student first." and the file is unchanged.
   - Create a student, record the payment, register: still works as before, and the student can sign in to Student Portal with the temporary password.
4. Run the whole suite before you stop: `dotnet test` for every test project, and `.\Check-Departments.ps1 -ProjectPath . -AllDepartments -Build` reports 7 of 7 healthy.

## Out of scope

- The Enrollment Application form (`/EnrollmentApplication`). It already validates every field before use.
- Showing or downloading the uploaded document files. They are stored under `wwwroot/uploads/documents/<student id>/` but no page lists them yet. That is a separate task.
- Any other department's folder, and `Shared/CampusSystem.Data`.

## Docs

If you add a helper or change messages, add one line to `Departments/Registrar/RegistrarMain/Registrar-Database-Tables.md` or `Ai-context_(Registrar).md` only if it describes behaviour a future editor needs to know (for example "blank ID on Create generates `2026-<id>`"). Do not rewrite those files.
