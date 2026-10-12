# Faculty Portal Data

All department apps share one JSON datastore at the repository root (`campus_system_unified_data_schema.json`, seeded from `campus_system_seed_data.json`). There is no SQL database. Read `Shared/CampusSystem.Data/Json/README.md` first: it defines the locking rules, id rules, schema version and the ownership table for every collection.

## Collections Faculty owns

| Collection | Seed rows | Fields |
| --- | --- | --- |
| `grades` | 3 | id, student_id, enrolled_subject_id, subject_code, school_year, semester, midterm_grade, final_grade, is_inc, inc_reason, remarks, encoded_by_faculty_id, encoded_at |
| `attendance_logs` | 0 | id, class_offering_id, student_id, session_date (yyyy-MM-dd), status (Present, Late, Absent, Excused), topic, recorded_by, recorded_at |
| `assignments` | 0 | id, class_offering_id, title, instructions, type (Activity, Quiz, Lab, Project, Exam), max_score, due_date, status (Draft, Published, Closed), created_by, created_at, published_at |
| `assignment_submissions` | 0 | id, assignment_id, student_id, text, submitted_at, is_late, status, score, feedback, graded_at, graded_by |

## Grade lifecycle

1. Faculty saves a grade as `status: "draft"`, only while the setting `grade_encoding_open` is "1" and only for an enrolled (not dropped) student. The range is 1.00 to 5.00; 3.00 or lower is "Passed".
2. Registrar verifies it, then locks it in Grade Control.
3. Verified or locked grades cannot be changed from Faculty. Grades with no `status` predate this and count as final.
4. Only non-draft grades appear on the student transcript API.

Each save also appends an `activity_log` row.

## What Faculty reads

`class_offerings` and `subjects` for the current term (`settings.current_school_year` and `current_semester`), `enrolled_subjects` and `users` and `student_profiles` for the roster, and the Finance row of `student_clearance` for the exam permit. A student with no Finance row is shown as not cleared.

## Failing grades and INC

Saving a grade above 3.00, or marking a subject Incomplete, opens one `ReferredIn` request in `guidance_requests` for that enrolled subject (idempotency key `grade-referral:<enrolled subject id>`). Re-grading never adds a second one and never changes a counselor's triage.

## Coursework

- **Attendance.** One row per student per session. Saving a date again replaces the earlier marks. Future dates and students not enrolled in the section are refused. The page also totals present, late, absent and excused per student.
- **Assignments.** An instructor creates a draft, sets a due date (required to publish), publishes it, and closes it when done. Closed assignments cannot be edited. A draft with no submissions can be deleted. Maximum score is 1 to 1000.
- **Submissions.** Student Portal shows published and closed assignments to students enrolled in the section. A student turns in a text answer (up to 10,000 characters), can update it until it is scored, and late answers are flagged. Faculty give a score (0 to the maximum) and feedback; the Feedback page edits feedback only.
- **Scope.** Faculty see only their own sections (`class_offerings.instructor_id` equals their `users.id`). Administrators see all. File uploads are not built.

## Sign-in

Faculty and admin accounts only (`CampusAuth`, cookie `.CampusSystem.Faculty`).

## Code

`Services/FacultyJsonStore.cs` (roster, grades, referral) and `Services/CourseworkStore.cs` (attendance, assignments, submissions)

## Tests

`dotnet test Departments\FacultyPortal\FacultyPortalMain.Tests`
