# Faculty Portal

Faculty module of CampusSystem. It reads and writes the shared JSON datastore through `Services/FacultyJsonStore.cs`. See `Faculty-Database-Tables.md` for the collections and the grade lifecycle.

## Run and check

```powershell
dotnet run --project FacultyPortalMain.csproj        # http://localhost:5115
dotnet test ..\FacultyPortalMain.Tests
.\..\..\..\Check-Departments.ps1 -ProjectPath .   # structure check; add -Build to compile
```

## Faculty system progress

Faculty module of CampusSystem. It must integrate with the Student and Registrar systems.

**Current status:** Roster, Gradebook, Schedule, Attendance, Assignments, Assignment Detail and Feedback all run on real data behind a faculty/admin sign-in. Grades are saved as drafts for the Registrar to verify and lock, and a failing grade or INC opens a Guidance referral. Students turn in text answers in Student Portal. File uploads are not built.

### Data flow

| Direction | What moves | Status |
|---|---|---|
| Registrar to Faculty | Sections assigned to the teacher, enrollment (class rosters), schedule | In diagram |
| Student to Faculty | Assignment submissions, student IDs | In diagram |
| Faculty to Student | Grades, feedback, attendance, announcements | In diagram |
| Faculty to Registrar | Final grades and attendance | Not in diagram, needs adding |
| Faculty and Payroll | Teaching load and employment type out, payslips (read-only) in | Not in diagram, needs deciding |

Faculty owns: `Faculty`, `Teacher`, `Authentication`, `Assignments`, `Grades`, `Feedback`, `Attendance_Sessions`, `Attendance_Record`, `Announcement`. Registrar and Student data is read-only in Faculty. Ownership is an assumption to confirm. `Feedback` stays a data table tied to a submission, but has no page of its own.

### Design

- [x] Faculty data-flow diagram (ERD) drafted
- [ ] Confirm data ownership with the Registrar and Student teams
- [ ] Fix data model: one identity key for Faculty/Teacher, `Attendance_Record` needs `Status` instead of repeated times and topic, remove Schedule/Sections overlap, rename `ViewUpdate_Grades` to `Grades`, fix `Statuis`, drop `Announcement.PostedBy`
- [ ] Add the Faculty to Registrar return flow (final grades, attendance)
- [ ] Decide payroll/HR integration: Payroll owns pay, portal shows payslips pulled on request, no salary or bank details in the faculty database, hours from `Attendance_Sessions` only if pay is hourly
- [ ] Decide on other missing items: leave requests, time records, evaluations, documents

### Front end

FacultyPortal is an ASP.NET Core Razor Pages project. The interface is UI-only until a backend contract is approved: no database, service, API, or form-submit behavior.

- [x] Standalone HTML prototype (`faculty-portal.html`) used to plan the screens
- [x] Faculty layout, `site.css` and pages rebuilt on the Student portal theme (sidebar, top bar, `portal-*` classes, NU blue and gold)
- [x] UI-only pages drafted: Dashboard (`Index`), `/Roster`, `/Gradebook`, `/Attendance`, `/Schedule`
- [x] Assignments pages drafted with demo data: `/Assignments` (list and create) and `/AssignmentDetail` (submissions, grading, feedback), plus an Assignments sidebar item
- [x] Feedback tab removed: teachers write feedback per submission in `/AssignmentDetail`. Delete `Pages/Feedback.cshtml` from the project
- [ ] Copy into the project (back up first): `Pages/Shared/_Layout.cshtml`, the `Pages/*.cshtml` files (including `Assignments` and `AssignmentDetail`, without `Feedback`), `wwwroot/css/site.css`, `wwwroot/img/logo3.png`
- [ ] Run `dotnet build FacultyPortalMain.csproj`
- [ ] Open each page in the browser and check it looks right
- [x] Update the AI context file: pages now use the Student-style portal layout in `site.css`, not Bootstrap; it also lists the Assignments pages and the Registrar and Student connections

### Back end

Only start once the owning teams approve a contract.

- [ ] Get approved Student and Registrar contracts (rosters, schedule, submissions). No direct calls into other departments' controllers, services or endpoints until then.
- [x] Attendance, Assignments and Feedback are backed by `attendance_logs`, `assignments` and `assignment_submissions` (`Services/CourseworkStore.cs`)
- [ ] Assignments backend: contract with Student for assignments, multi-file submissions, grades and feedback (replaces the per-student `student_assignments` placeholder), then file storage and upload validation
- [x] Teachers are linked to sections by id (`class_offerings.instructor_id`)
- [ ] Add validation and authorization before enabling grade, attendance, feedback, roster or schedule actions
- [ ] Replace demo content with real data, one page at a time
- [ ] `Check-Departments.ps1` reports this project HEALTHY
