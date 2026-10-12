# Finance Data

All department apps share one JSON datastore at the repository root (`campus_system_unified_data_schema.json`, seeded from `campus_system_seed_data.json`). There is no SQL database. Read `Shared/CampusSystem.Data/Json/README.md` first: it defines the locking rules, id rules, schema version and the ownership table for every collection.

## Collections Finance owns

| Collection | Seed rows | Fields |
| --- | --- | --- |
| `tuition_fee_matrix` | 3 | id, program_code, academic_year, tuition_per_unit, base_miscellaneous_fee, energy_it_fee, library_fee, registration_fee |
| `fee_assessments` | 4 | id, assessment_number, enrollment_id, student_id, school_year, semester, total_units, tuition_fee, miscellaneous_fees, laboratory_fees, discounts_or_scholarship, total_net_assessment, total_paid, remaining_balance, payment_plan, status |
| `installment_schedules` | 3 | id, assessment_id, student_id, term_type, due_date, amount_due, amount_paid, status, cleared_for_exam |
| `student_payments` | 4 | id, student_id, assessment_id, installment_id, receipt_no, amount, pay_date, payment_method, payment_category, status, description, processed_by_user_id, verification_token |
| `scholarships` | 5 | id, code, name, coverage, budget, status |
| `scholarship_applications` | 0 | id, student_id, scholarship_id, discount_amount, status, granted_at |
| `fee_adjustments` | 0 | id, assessment_id, enrollment_id, student_id, reason, old_total, new_total, delta, created_at |

## Shared collections Finance writes

- `student_clearance`: only its own row (department name contains "Finance"). Statuses: Cleared, Conditional, NotCleared.
- `transcript_requests`: the payment fields of document-fee requests created on the Request Fees page.

## Rules the code enforces

- A payment must be greater than zero and cannot exceed the outstanding balance.
- A payment updates the assessment, the installments (earliest due date first), a `student_payments` row and the clearance row in one locked write.
- A full payment marks the Finance clearance Cleared. A partial payment never downgrades a student who is already Cleared.
- Assessing an enrollee replaces Registrar's placeholder once, using `tuition_fee_matrix` for the program and year (the newest year if the exact one is missing) plus laboratory fees from the enrolled subjects. A second call returns the existing assessment.
- Library fines (`payment_category: "Library Fine"`) are not tuition and do not appear in Finance's payment lists.
- Fees come from `FeeCalculator` (shared with Registrar): tuition per unit, miscellaneous fees and the laboratory fee of each enrolled lab subject. Add/drop recalculation raises or lowers the last unpaid installment, adds an "Adjustment" installment when all were paid, and turns overpayment into `credit_balance`.
- Pages still pass a fixed term (2026-2027); the services ignore it and use the student's latest assessment.
- Sign-in: finance and admin see the ledgers; a student sees only their own invoice, OAO, receipt and account; faculty see the clearance roster. A student asking for another student's assessment id gets "not found". Staff view a student's pages with `?studentId=`.

## Code

- `Services/FinanceJsonStore.cs`: all reads and writes.
- `Services/FinanceServices.cs`: thin adapters behind the page-facing interfaces in `Contracts/`.
- API (service key or finance, admin or registrar sign-in): `POST /api/finance/assess` (body: `studentId` or `studentNumber`, `academicYear`, `semester`, optional `totalUnits`), `GET /api/finance/assessment?studentNumber=`, `GET /api/finance/clearance?studentNumber=`.

## Tests

`dotnet test Departments\Finance\FinanceMain\FinanceMain.Tests`
