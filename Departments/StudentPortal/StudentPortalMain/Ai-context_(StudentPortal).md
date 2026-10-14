# StudentPortal AI Context V4

## Shared UI

New pages use `Shared/CampusSystem.Ui` tokens and components. Do not add a separate portal brand palette.

## Current state (read first)

Data layer: this department reads and writes the shared JSON datastore (`campus_system_unified_data_schema.json`) through `CanonicalJsonStore`. There is no SQL, EF Core, MySQL, `DbContext`, migration or connection string. Rules and collection ownership: `Shared/CampusSystem.Data/Json/README.md`. This department's collections and code: the `*-Database-Tables.md` file in this folder. Sign-in: the shared `AuthStore` (see the root README for roles and demo accounts).

Student Portal owns no collections. It reads every department's data and makes the agreed writes listed in `Student-Database-Tables.md`. Students sign in with the shared password check (`AuthStore`, lockout after five misses); the session holds the current student. Coursework shows published assignments for the student's sections and takes text answers; library borrowing, returning and fine payment use `LibraryCirculation`; guidance requests go to `guidance_requests`. Names below such as `user`, `student_profile`, `MySqlConnector` and `Dapper` describe the old MySQL layout; the datastore uses `users` and `student_profiles`. Tests: `dotnet test ..\StudentPortalMain.Tests`.

## Ownership

StudentPortal owns the student-facing experience: Dashboard, Schedule, Grades, Enrollment & COR, Document Requests (201 Vault), Guidance Requests, and Profile/Settings interfaces. It is connected to the collegiate database and tightly integrated with the Registrar System (`Departments/Registrar/RegistrarMain`).

## System Architecture & Connectivity

StudentPortal reads and writes the shared JSON datastore. The collections it uses:
- **Registrar data**:
  - `users`: Authenticated student accounts and active sessions.
  - `student_profiles`: Program, curriculum year, academic standing, completed units, clearance.
  - `document_credentials`: The student's 201 File (Form 137, 138, PSA Birth Certificate, Good Moral, Medical Clearance).
  - `class_offerings` & `subjects`: Class schedules, rooms, instructors, lecture/lab credits.
  - `enrollments` & `enrolled_subjects`: Official course enrollments and printable Certificate of Registration (COR).
  - `grades`: Final grades encoded/controlled by Registrar; real-time GWA calculation.
  - `transcript_requests`: Live document requests with security tracking tokens (`MSU-[TYPE]-YEAR-[TOKEN]`), synchronized with Registrar's `DocumentProcessing.cshtml`.
  - `add_drop_requests`: Add/Drop petitions submitted to Registrar's `EnrollmentValidation.cshtml`.
  - `overload_waiver_requests`: Overload petitions submitted to Registrar.
  - `completion_revision_requests`: INC completion and grade revision petitions submitted to Registrar's `GradeControl.cshtml`.
  - `activity_log`: Audit logs of student transactions.
  - `settings`: Institutional settings (`current_school_year`, `current_semester`).
- **Guidance**: `guidance_requests`, filed from `/Requests` and triaged by the Guidance API.

## Department UI & Implemented Modules

1. **Dashboard (`/Dashboard`)**:
   - Welcome banner with student name, ID number, program, year level, academic status, and enrollment status.
   - Live KPI cards: Cumulative GWA, Degree Progress bar (Completed vs Remaining Units), Enrolled Load, Pending Requests.
   - Class schedule preview for the current term and recent grades preview.
   - Live Registrar Document & Petition Tracker.
   - Quick action shortcuts (Document Request, COR, Schedule, Guidance).

2. **My Schedule & Classes (`/Schedule`)**:
   - Term schedule selector (active term and previous enrolled semesters).
   - Enrolled class table with units, lecture/lab hours, days, time, room, and instructor.
   - Weekly visual schedule timetable matrix (Monday to Saturday).
   - Official printable Study Load sheet with NU Lipa header.

3. **Grades & Scholastic Record (`/Grades`)**:
   - Term-by-term grade breakdown with term GWA, total units, passing status, and remarks.
   - Cumulative GWA and Latin honors eligibility indicators.
   - INC completion and Grade Revision modal petition form directly submitting to Registrar.
   - Printable Grade Evaluation Slip.

4. **Enrollment & Official COR (`/Enrollment`)**:
   - Official collegiate Certificate of Registration (COR) viewer with Registrar validation badge.
   - Subject breakdown, section codes, time/room, and unit load.
   - Add/Drop subject petition form with live status tracker.
   - Overload / Unit Waiver petition form for graduating seniors.
   - Printable Official COR sheet.

5. **Document Requests & 201 Vault (`/DocumentRequests`)**:
   - Document request form (TOR, COE, Good Moral, Certified True Copy, Course Description).
   - Generates official cryptographic tracking token (`MSU-[PREFIX]-2026-[HEX]`).
   - Live queue tracker synchronized with Registrar's `DocumentProcessing.cshtml`.
   - 201 File Credential Vault tracking compliance of Form 137, Form 138, Birth Certificate, etc.

6. **Guidance Requests (`/Requests`)**:
   - Confidential appointment booking with Registered Guidance Counselors.
   - Advisory categories (academic, career, personal/mental wellbeing, shifting).
   - Request history and office details.

7. **Profile & Settings (`/Profile`)**:
   - Master student academic profile, curriculum catalog, and admission date.
   - Institutional & government records: NSTP Serial Number, CHED Special Order (S.O.).
   - Editable contact number and permanent residential address syncing to `student_profile`.

8. **Student Switcher (`/SwitchStudent`)**:
   - Fast switcher dropdown in topbar allowing evaluation and testing across students (e.g. Alyssa Bea Mendoza [Graduating], Joshua Fernandez, Kirsten Reyes [3rd Year], Christian Paul Tan [Transferee], Jasmine Navarro [Probation]).

## Verification & Build

Run `dotnet build StudentPortalMain.csproj` to compile.
All endpoints are compiled with 0 Warnings and 0 Errors.
