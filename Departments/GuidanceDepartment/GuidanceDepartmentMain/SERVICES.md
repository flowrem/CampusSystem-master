# Guidance services

- `JsonStudentDirectory` lists and validates users with the `student` role.
- `JsonGuidanceRequestStore` reads and writes Guidance requests with integer ids and optimistic row versions.
- `CounselorTriageService` validates case transitions, records audit events, and accepts referrals only for real students.
- `GuidanceOperationsStore` owns confidential case notes, appointments, and the Guidance Office clearance sign-off.
- `JsonAuditLogService` appends Guidance activity to the shared audit collection.

The controller exposes the counselor workflow at `/api/guidance`. Counselors and administrators use the Guidance cookie; trusted department integrations use `X-Campus-Service-Key` where the endpoint explicitly supports it.

All services use the shared canonical JSON file. Keep writes short and inside one `UpdateRoot` callback.
