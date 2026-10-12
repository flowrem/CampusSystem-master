# Library Data

All department apps share one JSON datastore at the repository root (`campus_system_unified_data_schema.json`, seeded from `campus_system_seed_data.json`). There is no SQL database. Read `Shared/CampusSystem.Data/Json/README.md` first: it defines the locking rules, id rules, schema version and the ownership table for every collection.

## Collections Library owns

| Collection | Seed rows | Fields |
| --- | --- | --- |
| `library_books` | 5 | id, isbn, title, author, call_number, category, total_copies, available_copies, shelf_location |
| `book_loans` | 2 | id, book_id, student_id, borrow_date, due_date, return_date, condition_on_return, status, fine_amount, fine_status, remarks |
| `library_fines` | 0 | id, loan_id, student_id, amount, reason (Overdue n day(s), Damaged, Lost), status (Pending, Paid, Waived), assessed_at, paid_at, payment_id, waived_by, waived_reason |
| `library_reservations` | 0 | id, student_id, book_title, book_author, reservation_date, due_date, status |

## Lending rules

The rules are in `CampusSystem.Data.Json.LibraryCirculation`, shared by the Library app and Student Portal:

- **Check out.** Needs a free copy. Refused when the student has unpaid fines (pending fines plus overdue books still out), already has the title, or is at the loan limit (`library_max_loans`, default 5). Loans default to 7 days, 1 to 30 allowed. A matching active reservation is marked fulfilled.
- **Check in.** Condition Good, Damaged or Lost. Overdue days cost `library_overdue_fine_per_day` each (default ₱20). Damaged adds `library_damaged_book_fine` (₱500) and Lost adds `library_lost_book_fine` (₱1,200). Every fine is a pending `library_fines` row. A Good return puts the copy back; Damaged and Lost do not.
- **Fines.** Paying one writes a `student_payments` row with category "Library Fine" (no tuition assessment is touched) and marks the fine Paid. A librarian can also waive a fine, which needs a reason. Loans fined before `library_fines` existed are handled too.
- **Reservations.** Only for a title with no free copy, once per student, held 7 days, then shown as Expired.

## The Library app

Roles: librarian and admin run the desk (check out, check in, record payments, waive fines, see everyone's loans and fines); students see the catalog and only their own loans, fines and reservations (`CampusAuth`, cookie `.CampusSystem.Library`). Code: `Services/LibraryDesk.cs`. Tests: `dotnet test Departments\Library\LibraryMain.Tests` and `LibraryCirculationTests` in the shared store tests.
