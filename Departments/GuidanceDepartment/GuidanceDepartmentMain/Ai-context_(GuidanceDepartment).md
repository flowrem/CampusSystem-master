# Guidance Department current context

## Shared UI

Static Guidance pages import `Shared/CampusSystem.Ui`; new pages must use those shared tokens and components.

Guidance is a static HTML counselor portal backed by the shared canonical JSON datastore.

## Current state

- `Program.cs` registers the canonical store and shared cookie sign-in for `guidance` and `admin` roles.
- `JsonGuidanceStores.cs` contains the student directory, request store, refresh-token store, and audit store.
- `GuidanceOperationsStore.cs` manages notes, appointments, and Guidance Office clearance sign-off.
- `GuidanceStudentController.cs` exposes counselor and approved integration endpoints.
- `wwwroot/guidance-app.js` connects the counselor pages to the live endpoints.

## Rules

- Read through `ReadRoot`; write only through `UpdateRoot`; identifiers are integers.
- Guidance owns `guidance_requests`, `counseling_case_notes`, and `guidance_appointments`.
- Guidance updates only its matching `student_clearance` row, identified by a department name containing `Guidance`.
- Counselor pages require `.CampusSystem.Guidance.Auth`; students must not receive counselor-only records.
- Use status values `Requested`, `InProgress`, `Resolved`, `ReferredOut`, and `ReferredIn`; urgency values are `Normal`, `Priority`, and `Urgent`.
