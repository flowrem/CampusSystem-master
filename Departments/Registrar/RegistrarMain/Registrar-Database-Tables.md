# Registrar Data

All department apps share one JSON datastore at the repository root (`campus_system_unified_data_schema.json`, seeded from `campus_system_seed_data.json`). There is no SQL database. Read `Shared/CampusSystem.Data/Json/README.md` first: it defines the locking rules, id rules, schema version and the ownership table for every collection.

## Collections Registrar owns

| Collection | Seed rows | Fields |
| --- | --- | --- |
| `users` | 12 | id, student_id_number, name, email, role, status, created_at, password_hash, account_state |
| `student_profiles` | 6 | user_id, program, year_level, curriculum_year, average_grade, academic_status, enrollment_status, completed_units, units_remaining, guidance_clearance_status, guidance_notes, contact_number, address, birth_date, gender |
| `subjects` | 3 | id, subject_code, subject_name, units, lec_hours, lab_hours, has_lab, lab_fee_amount, prerequisites, curriculum_year, department |
| `class_offerings` | 3 | id, subject_id, section_code, room, days_of_week, start_time, end_time, instructor_id, instructor_name, max_capacity, enrolled_count, school_year, semester |
| `enrollments` | 4 | id, student_id, school_year, semester, total_units, total_assessment_amount, status, validated_by_registrar_id, validated_at, created_at, registered_at |
| `enrolled_subjects` | 3 | id, enrollment_id, student_id, class_offering_id, subject_code, subject_name, units, status |
| `certificate_of_registrations` | 3 | id, student_id, school_year, semester, total_units, payment_status, registration_date, status |
| `document_credentials` | 22 | id, student_id, document_name, status, submission_date, verification_status, verified_by_user_id, remarks, document_type |
| `transcript_requests` | 1 | id, student_id, tracking_token, document_type, purpose, fee_amount, payment_status, payment_receipt_no, processing_status, request_date, released_date |
| `add_drop_requests` | 0 | written by Registrar and Student Portal pages; see Registrar/RegistrarMain/Pages/EnrollmentValidation.cshtml.cs |
| `overload_waiver_requests` | 0 | written by Student Portal, decided in Registrar Enrollment Validation |
| `course_shifting_requests` | 0 | written by Student Portal, decided in Registrar Curriculum Evaluation |
| `completion_revision_requests` | 0 | written by Student Portal, decided in Registrar Grade Control |
| `transferee_credited_subjects` | 0 | written by Registrar Curriculum Evaluation |
| `nstp_serial_numbers` | 0 | written by Registrar Government Compliance |
| `ched_special_orders` | 0 | written by Registrar Government Compliance |

Rows written by the apps can carry fields the seed rows lack. The seed shows the usual shape, not a strict schema.

## Writes Registrar does for other departments

- Creates a flat placeholder `fee_assessments` row when a student applies. Finance replaces it with real fees (`POST /api/finance/assess`).
- Saves, verifies and locks `grades` in Grade Control. Faculty Portal writes drafts first.
- Reads `student_clearance` for the clearance dashboard.
- When it approves an add/drop, or a student registers or drops through the API, it recalculates the enrollment's fees through the shared `FeeCalculator` (see Finance) and appends a `fee_adjustments` row.
- The Integration Hub pings the other department apps and probes their endpoints (Finance and Guidance with the service key).

## Student REST API

`/api/courses`, `/api/registrations`, `/api/transcript`, `/api/records` and `/api/verifications` are views over these collections, implemented in `Services/RegistrarApiStore.cs`:

| Endpoint | Collection |
| --- | --- |
| courses | `subjects` |
| registrations | `enrolled_subjects` (dropping sets `status: "dropped"`) |
| transcript | `grades` (drafts are hidden) |
| records, verifications | `transcript_requests` (verifications use document type "Enrollment Verification") |

`rowVersion` on a registration is a hash of its row. The signed-in student comes from the `StudentId` claim, which must be a `users.id` with role student. Only the development test login issues that claim today, so these endpoints are not ready for production use.

Staff pages need a registrar or admin sign-in (`CampusAuth`, cookie `.CampusSystem.Registrar`).

## Code

- `Services/RegistrarApiStore.cs`: the API above.
- `Services/DatabaseService.cs`: session helpers, settings, activity log, and `RegisterStudentInJsonStoreAsync`.
- `Services/StudentInput.cs`: create-applicant validation; a blank optional ID is assigned the existing `2026-<id>` value during creation.
- Razor Pages read and write the store directly through `CanonicalJsonStore`.

## Tests

`dotnet test Departments\Registrar\RegistrarMain.Tests`
