# Library: borrower list on the admin home page + full function check

Handoff spec for Codex. Work in `Departments/Library/`. Build and test as you go.

## Context

- App: `LibraryMain` (Razor Pages, net10.0). Data is the shared JSON store (`CanonicalJsonStore`), reached only through `Services/LibraryDesk.cs`. Lending rules live in `Shared/CampusSystem.Data/Json/LibraryCirculation.cs` and are shared with Student Portal, so changes there must keep Student Portal tests green.
- `/Index` (home) shows only counts. `/History` already has a Student column for staff, but nothing on the home page says who has books.
- Loans are in `book_loans`. The borrower column is `student_id` and it holds a `users.id`. Faculty are `users` rows with `role: "faculty"` and an ID number in the same `student_id_number` field (e.g. `FAC-2024-003`), so they fit the existing column.
- Roles: `librarian` and `admin` are staff (`IsStaff`). `student` sees only their own records. Keep it that way.
- Demo accounts (password `Campus@2026`): `LIB-2024-005` librarian, `ADM-2024-006` admin, `2023-01045` student, `FAC-2024-003` faculty.

## Part 1: borrower list on the home page (students and faculty)

### 1a. Let faculty borrow
- `LibraryCirculation.Borrow` (line ~40) and `LibraryDesk.Checkout` (line ~156) only accept `role == "student"`. Accept `student` or `faculty`.
- Keep every other rule: unpaid fines block borrowing, no free copy, already holds this title, loan limit.
- Update the error wording so it isn't student-only (e.g. "That borrower does not exist", "No student or faculty member has the ID number X").
- Rename the form label on `Pages/Resource.cshtml` to "Student or faculty ID number" and the placeholder to show both formats.
- Do not rename the `student_id` column or `student_id_number` field. That would break the other apps.

### 1b. Service method
In `LibraryDesk` add:

```csharp
public sealed record BorrowerRow(int LoanId, int UserId, string Name, string IdNumber, string Role,
    int BookId, string BookTitle, DateTime BorrowDate, DateTime DueDate, int OverdueDays);

public IReadOnlyList<BorrowerRow> CurrentBorrowers(int top = 10)
```

- Open loans only (`LibraryCirculation.IsOpen`).
- Sort: overdue first (most days late first), then soonest due date.
- `Role` comes from the `users` row, so the UI can show a Student or Faculty badge.
- Also add `RecentReturns(int top = 5)`: the latest returned loans, newest `return_date` first, with the same name, book and role fields.
- Add a `TotalOpen` count (or reuse `Stats.OpenLoans`) so the UI can say "Showing 10 of 23".

### 1c. Home page
`Pages/Index.cshtml` and `.cshtml.cs`, staff only (`Model.IsStaff`):
- Add a "Who has books right now" table under the stat cards. Columns: Borrower (name, ID number, Student/Faculty badge), Book (links to `/Resource`), Borrowed, Due, Status (Borrowed or Overdue, with "N day(s) late").
- Footer link "View all loans" goes to `/History?filter=open`.
- Add a smaller "Recently returned" table.
- Make the "Books on loan" card link to `/History?filter=open` and "Overdue" link to `/History?filter=overdue`.
- Empty state text when there are no open loans.
- Students must not get the borrower list. Populate it in `OnGet` only when `IsStaff`, and never render it for students.
- Match the existing markup (`surface`, `eyebrow`, `badge text-bg-*`, `table-responsive`).

### 1d. Loans page
`LibraryDesk.Loans` search matches name/ID number already. Add a Student/Faculty badge next to the borrower in `Pages/History.cshtml` so the two kinds are distinguishable. Add a role to `LoanRow` if needed.

### 1e. Tests (`Departments/Library/LibraryMain.Tests/LibraryDeskTests.cs`)
- `CurrentBorrowers` returns open loans only, excludes returned loans, orders overdue first, and respects `top`.
- Faculty can borrow and appear in `CurrentBorrowers` with role `faculty`.
- A user with another role (admin, librarian) still cannot borrow.
- Faculty with unpaid fines is blocked.
- `RecentReturns` ordering.
- Check Student Portal tests still pass (`StudentPortalMain.Tests`), since `Borrow` is shared.

## Part 2: check every other function works

Run the app on a temp copy of the data, never the real file:

```bash
export PATH="/c/Program Files/dotnet:$PATH"
cp campus_system_unified_data_schema.json <temp>/data.json
CAMPUS_SYSTEM_JSON_PATH=<temp>/data.json ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-launch-profile --urls http://localhost:5228   # in Departments/Library/LibraryMain
```

Sign in at `/Login` (antiforgery token required). Test as librarian, admin and student, and fix anything that fails, with a test for each fix.

| Area | Check |
| --- | --- |
| Catalog | search by title and author, category filter, available-only filter |
| Book page `/Resource?id=N` | check-out form (ID number, loan days 1 to 30), reserve button. Loading `/Resource` with no id returns 404, which is expected |
| Check-out | refused for unpaid fines, no free copy, unknown ID, duplicate title, loan limit; loan days bounds |
| Check-in | Good, Damaged and Lost; late fine is days late x daily rate; damage and loss fees; copy count goes back up |
| Loans `/History` | all, open, overdue, returned filters; search by book, name, ID number |
| Fines | pay with each of `LibraryDesk.PaymentMethods`; waive with a reason; receipt created; paid fine no longer blocks borrowing |
| Reservations | reserve only when no copy is on the shelf; cancel; fulfilled when the reserver borrows |
| Student role | sees only own loans, fines and reservations; POSTs for check-in, waive and check-out are refused |
| Cross-app | the same loans and fines show in Student Portal's Library page |

Report each row as pass or fail with the evidence. Fix failures in the smallest place that fixes them.

## Finish
1. `dotnet test` for `LibraryMain.Tests`, `StudentPortalMain.Tests` and `CampusSystem.Data.Tests`. Everything must pass.
2. Update `Departments/Library/LibraryMain/Ai-context_(Library).md` with the borrower list and faculty borrowing.
3. Do not commit. The user will review it in the browser first.

## Out of scope
Renaming data columns, new collections, file uploads, changes to other departments' pages.
