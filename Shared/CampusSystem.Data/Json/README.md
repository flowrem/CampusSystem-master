# Canonical JSON datastore

All department apps share one database file at the repository root:

```text
campus_system_unified_data_schema.json   live data (read and written by every app)
campus_system_seed_data.json             pristine seed (never written at runtime)
```

Every app reaches it through `CanonicalJsonStore`, registered as a singleton:

```csharp
builder.Services.AddSingleton<CanonicalJsonStore>();
```

Shared rules that need more than one department live here as static helpers, so both sides always agree: `FeeCalculator` (fees and add/drop recalculation, used by Finance and Registrar) and `LibraryCirculation` (lending, fines and reservations, used by Library and Student Portal). `CampusSystem.Data.Auth` has `AuthStore` (sign-in against `users`) and `CampusAuth` (cookie sign-in, role attributes, service key).

Set `CAMPUS_SYSTEM_JSON_PATH` to point an app at a different file (for example, a test copy).

## Rules

- **Read** with `ReadRoot()`. The returned `JsonObject` is a private copy, so editing it changes nothing on disk.
- **Write** only inside `UpdateRoot(root => ...)`. The callback reads, changes and saves the file in one step, holding a lock shared by every department process. Never call `ReadRoot()`, edit the result, and then write it back separately: another app's change will be lost.
- **New IDs** come from `CanonicalJsonStore.NextId(root, "collection")`, called inside `UpdateRoot`.
- **IDs are `int`.** Use `users.id` as the student, faculty and staff key everywhere.
- **Collections** live under `unified_seed_data`. Use `GetArray` when the collection must exist, and `GetOrCreateArray` when writing to one that may not exist yet.
- Keep each `UpdateRoot` callback short and free of I/O or HTTP calls. Every other app waits while it runs.
- **Users carry sign-in state.** `password_hash` (BCrypt), `failed_logins`, `locked_until`, `last_login`, `password_changed_at`. Never write a plain password. Roles: registrar, finance, faculty, guidance, librarian, admin, student.
- **Schema version 2** added `fee_adjustments`, the sign-in fields on `users`, and the librarian and admin accounts. `system.schema_version` must not exceed `CanonicalJsonStore.CurrentSchemaVersion`. If it does, the store refuses to read or write the file, so an out-of-date app cannot drop fields it does not know about. When you change the file's shape, bump the constant and rebuild every app.

## Collection ownership

The owner defines a collection's fields and status values, and does all writes except the agreed ones listed here. Any department may read any collection. Every collection below exists in the seed file, possibly as an empty array. When you add a collection, add it here, to the seed, and to `OwnedCollections` in the tests.

| Owner | Collections | Agreed writes by other departments |
| --- | --- | --- |
| Registrar | `users`, `student_profiles`, `subjects`, `class_offerings`, `enrollments`, `enrolled_subjects`, `certificate_of_registrations`, `document_credentials`, `transcript_requests`, `add_drop_requests`, `overload_waiver_requests`, `course_shifting_requests`, `completion_revision_requests`, `transferee_credited_subjects`, `nstp_serial_numbers`, `ched_special_orders` | Student Portal creates request rows with `status: "pending"`, uploads to `document_credentials`, and edits contact fields on its own `student_profiles` row |
| Finance | `tuition_fee_matrix`, `fee_assessments`, `installment_schedules`, `student_payments`, `scholarships`, `scholarship_applications`, `fee_adjustments` | Registrar appends `fee_adjustments` (through `FeeCalculator.Recalculate`) and updates the assessment and installments when it approves an add/drop or a student registers or drops through the API. Student Portal adds `student_payments` and `scholarship_applications`. Registrar creates a flat placeholder `fee_assessments` row on application. Finance replaces it with the matrix fees when it assesses the enrollment (`POST /api/finance/assess`). Finance writes its own row of `student_clearance` (department name contains "Finance") and the payment fields of `transcript_requests` |
| Faculty Portal | `grades`, `attendance_logs`, `assignments`, `assignment_submissions` | Registrar writes `grades` through Grade Control. Student Portal adds `assignment_submissions` (the answer text, `is_late`); Faculty adds the `score` and `feedback` |
| Guidance | `guidance_requests`, `student_guidance_tickets`, `counseling_case_notes`, `guidance_appointments` | Student Portal creates requests. Faculty creates one `ReferredIn` request (idempotency key `grade-referral:<enrolled subject id>`) when it saves a failing grade or INC |
| Library | `library_books`, `book_loans`, `library_fines`, `library_reservations` | Student Portal borrows, returns, reserves and pays fines through `LibraryCirculation`. A fine payment is a `student_payments` row with category "Library Fine" and no assessment |
| Shared | `settings`, `activity_log`, `audit_events`, `refresh_tokens`, `student_clearance` | Any department appends to logs. Each department updates only its own sign-off on `student_clearance` |

Registrar's student REST API (`/api/courses`, `/api/registrations`, `/api/transcript`, `/api/records`, `/api/verifications`) is a view over these collections, not separate tables: courses are `subjects`, registrations are `enrolled_subjects` (dropping one sets `status: "dropped"`), the transcript is `grades`, and records and verification requests are `transcript_requests`. Its `rowVersion` is a hash of the row.

Grades have a lifecycle: Faculty Portal saves a grade as `status: "draft"` (only while `grade_encoding_open` is "1"), Registrar verifies it and then locks it, and only verified or locked grades appear on a student's transcript. Grades with no `status` predate this and count as final.

Guidance requests live only in `guidance_requests`, with int `id` and `student_id`. Student Portal files them, and Guidance's counselor API reads, triages and audits them (`audit_events`, `refresh_tokens`). `student_guidance_tickets` is no longer written by anyone. The Testing app is a browser and editor for every collection, so it can change any department's data.

Cross-department workflows run on these status fields, not on HTTP calls between apps. For example, Student Portal reserves an enrollment, Finance assesses it, and Registrar validates it.

## Resetting and backups

From the repository root:

```powershell
.\Reset-CampusJsonData.ps1              # back up the live file, then restore it from the seed
.\Reset-CampusJsonData.ps1 -BackupOnly  # timestamped backup only
```

Backups go to `BackupFolder\json\` (git-ignored). The same operations are available in code as `CanonicalJsonStore.ResetToSeed()` and `Backup()`.

To change the seed data on purpose, edit `campus_system_seed_data.json` and then run the reset.

## Tests

```powershell
dotnet test Shared\CampusSystem.Data.Tests
```

The tests cover locking under concurrent writers, `NextId`, failed updates, the schema-version guard, reset and backup, DI registration, and whether the live and seed files contain every collection listed above.
