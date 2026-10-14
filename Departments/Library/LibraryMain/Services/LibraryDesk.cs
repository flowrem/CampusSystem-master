using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace LibraryMain.Services;

/// <summary>
/// What the Library pages read and do. The lending rules live in <see cref="LibraryCirculation"/>, which Student Portal
/// uses too. Every action here runs in one locked write and appends a line to <c>activity_log</c>.
/// </summary>
public sealed class LibraryDesk(CanonicalJsonStore store)
{
    public sealed record BookRow(int Id, string Isbn, string Title, string Author, string Category, string CallNumber, string Shelf, int Total, int Available, int Reservations);
    public sealed record LoanRow(int Id, int StudentId, string StudentName, string StudentNumber, int BookId, string BookTitle,
        DateTime BorrowDate, DateTime DueDate, DateTime? ReturnDate, string Status, int OverdueDays, decimal AccruedFine, string Condition, string Role);
    public sealed record BorrowerRow(int LoanId, int UserId, string Name, string IdNumber, string Role,
        int BookId, string BookTitle, DateTime BorrowDate, DateTime DueDate, int OverdueDays);
    public sealed record RecentReturnRow(int LoanId, int UserId, string Name, string IdNumber, string Role,
        int BookId, string BookTitle, DateTime BorrowDate, DateTime DueDate, DateTime ReturnDate);
    public sealed record FineRow(int Id, int LoanId, int StudentId, string StudentName, string StudentNumber, string BookTitle, decimal Amount,
        string Reason, string Status, DateTime? AssessedAt, string Note);
    public sealed record ReservationRow(int Id, int StudentId, string StudentName, int BookId, string BookTitle, string BookAuthor,
        DateTime ReservedAt, DateTime HoldUntil, string Status, int CopiesFree);
    public sealed record Stats(int Titles, int CopiesOnShelf, int OpenLoans, int OverdueLoans, decimal PendingFines, int ActiveReservations);

    public static readonly string[] PaymentMethods = ["Cash (Library)", "GCash", "Maya", "Bank Transfer"];

    // ── Reading ─────────────────────────────────────────────────────────────

    public Stats GetStats()
    {
        var root = store.ReadRoot();
        var now = DateTime.UtcNow;
        var books = Rows(root, "library_books").ToList();
        var open = Rows(root, "book_loans").Where(LibraryCirculation.IsOpen).ToList();
        return new Stats(
            books.Count,
            books.Sum(b => Int(b, "available_copies")),
            open.Count,
            open.Count(l => LibraryCirculation.OverdueDays(l, now) > 0),
            Rows(root, "library_fines").Where(f => Same(Str(f, "status"), "Pending")).Sum(f => Dec(f, "amount"))
                + open.Sum(l => LibraryCirculation.OverdueDays(l, now) * LibraryCirculation.OverduePerDay(root)),
            Rows(root, "library_reservations").Count(r => LibraryCirculation.IsActiveReservation(r, now)));
    }

    public IReadOnlyList<string> Categories() =>
        Rows(store.ReadRoot(), "library_books").Select(b => Str(b, "category")).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();

    public IReadOnlyList<BookRow> Catalog(string? search, string? category, bool availableOnly)
    {
        var root = store.ReadRoot();
        var now = DateTime.UtcNow;
        var term = (search ?? "").Trim();
        return Rows(root, "library_books")
            .Where(b => term.Length == 0 || Str(b, "title").Contains(term, StringComparison.OrdinalIgnoreCase)
                        || Str(b, "author").Contains(term, StringComparison.OrdinalIgnoreCase)
                        || Str(b, "isbn").Contains(term, StringComparison.OrdinalIgnoreCase)
                        || Str(b, "call_number").Contains(term, StringComparison.OrdinalIgnoreCase))
            .Where(b => string.IsNullOrWhiteSpace(category) || Same(Str(b, "category"), category))
            .Where(b => !availableOnly || Int(b, "available_copies") > 0)
            .Select(b => ToBook(root, b, now))
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public BookRow? Book(int id)
    {
        var root = store.ReadRoot();
        var book = Rows(root, "library_books").FirstOrDefault(b => Int(b, "id") == id);
        return book is null ? null : ToBook(root, book, DateTime.UtcNow);
    }

    /// <summary>Loans for one student (studentId set) or for everyone. Filter is "open", "overdue", "returned" or empty for all.</summary>
    public IReadOnlyList<LoanRow> Loans(int? studentId, string? filter, string? search)
    {
        var root = store.ReadRoot();
        var now = DateTime.UtcNow;
        var term = (search ?? "").Trim();
        var users = Rows(root, "users").ToList();
        var books = Rows(root, "library_books").ToList();
        var rate = LibraryCirculation.OverduePerDay(root);

        return Rows(root, "book_loans")
            .Where(l => studentId is null || Int(l, "student_id") == studentId)
            .Select(l =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(l, "student_id"));
                var book = books.FirstOrDefault(b => Int(b, "id") == Int(l, "book_id"));
                var overdue = LibraryCirculation.OverdueDays(l, now);
                var open = LibraryCirculation.IsOpen(l);
                return new LoanRow(Int(l, "id"), Int(l, "student_id"), Str(user, "name"), Str(user, "student_id_number"), Int(l, "book_id"), Str(book, "title"),
                    Date(l, "borrow_date") ?? default, Date(l, "due_date") ?? default, Date(l, "return_date"),
                    open ? (overdue > 0 ? "Overdue" : "Borrowed") : Str(l, "status"), overdue, overdue * rate, Str(l, "condition_on_return"), Str(user, "role"));
            })
            .Where(l => filter switch
            {
                "open" => l.ReturnDate is null,
                "overdue" => l.Status == "Overdue",
                "returned" => l.ReturnDate is not null,
                _ => true
            })
            .Where(l => term.Length == 0 || l.BookTitle.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || l.StudentName.Contains(term, StringComparison.OrdinalIgnoreCase) || l.StudentNumber.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.BorrowDate)
            .ToList();
    }

    public IReadOnlyList<BorrowerRow> CurrentBorrowers(int top = 10)
    {
        var root = store.ReadRoot();
        var now = DateTime.UtcNow;
        var users = Rows(root, "users").ToList();
        var books = Rows(root, "library_books").ToList();
        return Rows(root, "book_loans")
            .Where(LibraryCirculation.IsOpen)
            .Select(loan =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(loan, "student_id"));
                var book = books.FirstOrDefault(b => Int(b, "id") == Int(loan, "book_id"));
                return new BorrowerRow(Int(loan, "id"), Int(loan, "student_id"), Str(user, "name"), Str(user, "student_id_number"), Str(user, "role"),
                    Int(loan, "book_id"), Str(book, "title"), Date(loan, "borrow_date") ?? default, Date(loan, "due_date") ?? default, LibraryCirculation.OverdueDays(loan, now));
            })
            .OrderByDescending(row => row.OverdueDays > 0)
            .ThenByDescending(row => row.OverdueDays)
            .ThenBy(row => row.DueDate)
            .Take(Math.Max(0, top))
            .ToList();
    }

    public IReadOnlyList<RecentReturnRow> RecentReturns(int top = 5)
    {
        var root = store.ReadRoot();
        var users = Rows(root, "users").ToList();
        var books = Rows(root, "library_books").ToList();
        return Rows(root, "book_loans")
            .Where(loan => !LibraryCirculation.IsOpen(loan) && Date(loan, "return_date") is not null)
            .Select(loan =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(loan, "student_id"));
                var book = books.FirstOrDefault(b => Int(b, "id") == Int(loan, "book_id"));
                return new RecentReturnRow(Int(loan, "id"), Int(loan, "student_id"), Str(user, "name"), Str(user, "student_id_number"), Str(user, "role"),
                    Int(loan, "book_id"), Str(book, "title"), Date(loan, "borrow_date") ?? default, Date(loan, "due_date") ?? default, Date(loan, "return_date")!.Value);
            })
            .OrderByDescending(row => row.ReturnDate)
            .Take(Math.Max(0, top))
            .ToList();
    }

    public IReadOnlyList<FineRow> Fines(int? studentId, string? status)
    {
        var root = store.ReadRoot();
        var users = Rows(root, "users").ToList();
        var loans = Rows(root, "book_loans").ToList();
        var books = Rows(root, "library_books").ToList();
        return Rows(root, "library_fines")
            .Where(f => studentId is null || Int(f, "student_id") == studentId)
            .Where(f => string.IsNullOrWhiteSpace(status) || Same(Str(f, "status"), status))
            .Select(f =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(f, "student_id"));
                var loan = loans.FirstOrDefault(l => Int(l, "id") == Int(f, "loan_id"));
                var book = books.FirstOrDefault(b => Int(b, "id") == Int(loan, "book_id"));
                return new FineRow(Int(f, "id"), Int(f, "loan_id"), Int(f, "student_id"), Str(user, "name"), Str(user, "student_id_number"), Str(book, "title"),
                    Dec(f, "amount"), Str(f, "reason"), Str(f, "status"), Date(f, "assessed_at"), Str(f, "waived_reason"));
            })
            .OrderByDescending(f => f.AssessedAt)
            .ToList();
    }

    /// <summary>What a student owes: pending fines plus accrued overdue on books still out.</summary>
    public decimal Outstanding(int studentId) =>
        LibraryCirculation.OutstandingFines(store.ReadRoot(), studentId, DateTime.UtcNow);

    public IReadOnlyList<ReservationRow> Reservations(int? studentId, string? status)
    {
        var root = store.ReadRoot();
        var now = DateTime.UtcNow;
        var users = Rows(root, "users").ToList();
        var books = Rows(root, "library_books").ToList();
        return Rows(root, "library_reservations")
            .Where(r => studentId is null || Int(r, "student_id") == studentId)
            .Select(r =>
            {
                var user = users.FirstOrDefault(u => Int(u, "id") == Int(r, "student_id"));
                var book = books.FirstOrDefault(b => Int(b, "id") == Int(r, "book_id"));
                var shown = Same(Str(r, "status"), "Active") && !LibraryCirculation.IsActiveReservation(r, now) ? "Expired" : Str(r, "status");
                return new ReservationRow(Int(r, "id"), Int(r, "student_id"), Str(user, "name"), Int(r, "book_id"), Str(r, "book_title"), Str(r, "book_author"),
                    Date(r, "reservation_date") ?? default, Date(r, "due_date") ?? default, shown, Int(book, "available_copies"));
            })
            .Where(r => string.IsNullOrWhiteSpace(status) || Same(r.Status, status))
            .OrderByDescending(r => r.ReservedAt)
            .ToList();
    }

    // ── Doing ───────────────────────────────────────────────────────────────

    /// <summary>Librarian checks a book out to a student or faculty borrower with this ID number. Returns the new loan id.</summary>
    public int Checkout(string studentNumber, int bookId, int? loanDays, int staffId)
    {
        var loanId = 0;
        store.UpdateRoot(root =>
        {
            var student = Rows(root, "users").FirstOrDefault(u => Same(Str(u, "student_id_number"), (studentNumber ?? "").Trim()) && IsBorrower(Str(u, "role")))
                          ?? throw new LibraryException($"No student or faculty member has the ID number {(studentNumber ?? "").Trim()}.");
            loanId = LibraryCirculation.Borrow(root, Int(student, "id"), bookId, DateTime.UtcNow, loanDays, "Library desk");
            Log(root, staffId, $"Checked out book #{bookId} to {Str(student, "name")} (loan #{loanId})");
        });
        return loanId;
    }

    public LibraryCirculation.ReturnResult CheckIn(int loanId, string condition, int staffId)
    {
        LibraryCirculation.ReturnResult? result = null;
        store.UpdateRoot(root =>
        {
            result = LibraryCirculation.Return(root, loanId, condition, DateTime.UtcNow);
            Log(root, staffId, $"Checked in loan #{loanId} as {condition}. Fines: ₱{result.TotalFine:N2}");
        });
        return result!;
    }

    public string RecordFinePayment(int fineId, string method, int staffId)
    {
        var receipt = "";
        store.UpdateRoot(root =>
        {
            if (!PaymentMethods.Contains(method)) throw new LibraryException("Choose a payment method.");
            receipt = LibraryCirculation.PayFine(root, fineId, method, DateTime.UtcNow, staffId);
            Log(root, staffId, $"Recorded payment of library fine #{fineId} (receipt {receipt})");
        });
        return receipt;
    }

    public void WaiveFine(int fineId, string reason, int staffId) =>
        store.UpdateRoot(root =>
        {
            LibraryCirculation.WaiveFine(root, fineId, reason, staffId, DateTime.UtcNow);
            Log(root, staffId, $"Waived library fine #{fineId}: {reason.Trim()}");
        });

    public int Reserve(int studentId, int bookId)
    {
        var id = 0;
        store.UpdateRoot(root =>
        {
            id = LibraryCirculation.Reserve(root, studentId, bookId, DateTime.UtcNow);
            Log(root, studentId, $"Reserved library book #{bookId}");
        });
        return id;
    }

    /// <summary>Students may cancel their own. Staff pass null to cancel anyone's.</summary>
    public void CancelReservation(int reservationId, int? onlyForStudent, int actorId) =>
        store.UpdateRoot(root =>
        {
            LibraryCirculation.CancelReservation(root, reservationId, onlyForStudent);
            Log(root, actorId, $"Cancelled library reservation #{reservationId}");
        });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static BookRow ToBook(JsonObject root, JsonObject b, DateTime now) =>
        new(Int(b, "id"), Str(b, "isbn"), Str(b, "title"), Str(b, "author"), Str(b, "category"), Str(b, "call_number"), Str(b, "shelf_location"),
            Int(b, "total_copies"), Int(b, "available_copies"),
            Rows(root, "library_reservations").Count(r => Int(r, "book_id") == Int(b, "id") && LibraryCirculation.IsActiveReservation(r, now)));

    private static void Log(JsonObject root, int userId, string message)
    {
        var log = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
        var user = Rows(root, "users").FirstOrDefault(u => Int(u, "id") == userId);
        log.Add(new JsonObject
        {
            ["id"] = CanonicalJsonStore.NextId(log),
            ["user_id"] = userId,
            ["user_name"] = Str(user, "name"),
            ["message"] = message,
            ["created_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsBorrower(string? role) => Same(role, "student") || Same(role, "faculty");

    private static DateTime? Date(JsonObject? o, string key) =>
        DateTimeOffset.TryParse(Str(o, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;

    private static int Int(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0
        : v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<long>(out var l) ? (int)l
        : v.TryGetValue<decimal>(out var d) ? (int)d
        : 0;

    private static decimal Dec(JsonObject? o, string key) =>
        o?[key] is not JsonValue v ? 0m
        : v.TryGetValue<decimal>(out var d) ? d
        : v.TryGetValue<double>(out var f) ? (decimal)f
        : v.TryGetValue<long>(out var l) ? l
        : v.TryGetValue<int>(out var n) ? n
        : 0m;

    private static string Str(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}
