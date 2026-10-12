# Student Portal Data

All department apps share one JSON datastore at the repository root (`campus_system_unified_data_schema.json`, seeded from `campus_system_seed_data.json`). There is no SQL database. Read `Shared/CampusSystem.Data/Json/README.md` first: it defines the locking rules, id rules, schema version and the ownership table for every collection.

Student Portal owns no collections. It reads every department's data and makes the agreed writes below.

## Writes Student Portal makes

| Collection | What it writes |
| --- | --- |
| `student_payments`, `fee_assessments` | A payment row, and the assessment's `total_paid`, `remaining_balance` and status ("Partially Paid" or "Paid") |
| `enrollments`, `enrolled_subjects`, `class_offerings` | Subject reservations and the offering's `enrolled_count` |
| `add_drop_requests`, `overload_waiver_requests`, `course_shifting_requests`, `completion_revision_requests` | New requests with status "pending" |
| `document_credentials`, `student_profiles` | Uploads, and contact fields on the student's own profile |
| `assignment_submissions` | A student's answer text and lateness. Faculty add the score and feedback |
| `transcript_requests` | Document requests |
| `guidance_requests` | Guidance requests (urgency Normal, Priority or Urgent) |
| `book_loans`, `library_books`, `library_fines`, `library_reservations`, `student_payments` | Borrowing, returning, reserving and paying library fines, all through the shared `LibraryCirculation` rules |
| `scholarship_applications` | Scholarship applications |
| `activity_log` | One line per student action |

## Code

`Services/StudentPortalDbService.cs` holds the reads and writes. A student signs in with their ID number or email and the shared password check (`AuthStore`, lockout after five wrong passwords). The session then supplies the current student (`CurrentStudentId`).

## Coursework

A student sees assignments that are Published or Closed for the sections they are enrolled in, writes a text answer, and turns it in. They can update it until Faculty score it. Closed or scored work is read-only, and the score and feedback show beside it.

## Tests

`dotnet test Departments\StudentPortal\StudentPortalMain.Tests`
