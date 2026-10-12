# Guidance JSON persistence

Guidance stores its records in the repository-wide canonical JSON file.

## Owned collections

- `guidance_requests` for intake, triage, referrals, assignment, and status.
- `counseling_case_notes` for confidential counselor notes.
- `guidance_appointments` for counseling consultations.

Guidance also updates its own `Guidance Office` sign-off in shared `student_clearance` and appends events to `audit_events`.

## Write rules

Use `CanonicalJsonStore.UpdateRoot` for every change. Allocate integer ids with `CanonicalJsonStore.NextId`. Never edit a result returned by `ReadRoot` and expect it to persist.

The live data file is `campus_system_unified_data_schema.json`; `campus_system_seed_data.json` is the reset source. Add every new collection to both files, the shared ownership documentation, and the datastore collection test.
