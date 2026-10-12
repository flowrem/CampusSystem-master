# Guidance data collections

Guidance uses the shared canonical JSON datastore.

| Collection | Key fields | Purpose |
| --- | --- | --- |
| `guidance_requests` | `id`, `student_id`, `subject`, `details`, `urgency`, `status`, `assigned_counselor_id`, `row_version` | Intake cases and referrals. |
| `counseling_case_notes` | `id`, `student_id`, `counselor`, `category`, `content`, `is_confidential`, `created_at` | Restricted case notes. |
| `guidance_appointments` | `id`, `student_id`, `counselor`, `appointment_at`, `status`, `purpose`, `remarks`, `created_at` | Scheduled counseling consultations. |

Guidance writes its own `Guidance Office` row in `student_clearance` and appends audit entries to `audit_events`.

## Endpoints

- `GET /api/guidance/students`
- `GET|POST /api/guidance/requests`
- `POST /api/guidance/requests/{id}/assign`
- `POST /api/guidance/requests/{id}/status`
- `GET|POST /api/guidance/case-notes`
- `GET|POST /api/guidance/appointments`
- `POST /api/guidance/clearance`
- `POST /api/v1/incoming-referral`
