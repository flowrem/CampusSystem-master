using System.Globalization;
using System.Text.Json.Nodes;

namespace CampusSystem.Data.Json;

/// <summary>Raised when a library action breaks a rule. The message is safe to show to the user.</summary>
public sealed class LibraryException(string message) : Exception(message);

/// <summary>
/// The library's lending rules in one place, so the Library app (librarian desk) and Student Portal agree.
/// Every method works on the root inside <see cref="CanonicalJsonStore.UpdateRoot"/> and throws
/// <see cref="LibraryException"/> when a rule is broken, which leaves the file unchanged.
/// Owns <c>library_books</c>, <c>book_loans</c>, <c>library_fines</c> and <c>library_reservations</c>.
/// </summary>
public static class LibraryCirculation
{
    public const int DefaultLoanDays = 7;
    public const int DefaultMaxLoans = 5;
    public const int ReservationHoldDays = 7;

    public static readonly string[] Conditions = ["Good", "Damaged", "Lost"];

    public sealed record ReturnResult(decimal TotalFine, IReadOnlyList<string> Reasons);

    // ── Settings ────────────────────────────────────────────────────────────

    public static decimal OverduePerDay(JsonObject root) => Setting(root, "library_overdue_fine_per_day", 20m);
    public static decimal DamagedFine(JsonObject root) => Setting(root, "library_damaged_book_fine", 500m);
    public static decimal LostFine(JsonObject root) => Setting(root, "library_lost_book_fine", 1200m);
    public static int MaxLoans(JsonObject root) => (int)Setting(root, "library_max_loans", DefaultMaxLoans);

    // ── Lending ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks a book out. Refused when no copy is free, the borrower has unpaid fines or an overdue book, already holds
    /// this title, or is at the loan limit. A matching active reservation by the borrower is marked fulfilled.
    /// </summary>
    public static int Borrow(JsonObject root, int studentId, int bookId, DateTime now, int? loanDays = null, string source = "Library desk")
    {
        var student = Rows(root, "users").FirstOrDefault(u => Int(u, "id") == studentId && IsBorrower(Str(u, "role")))
                      ?? throw new LibraryException("That borrower does not exist.");
        var book = Rows(root, "library_books").FirstOrDefault(b => Int(b, "id") == bookId)
                   ?? throw new LibraryException("That book is not in the catalog.");
        if (Int(book, "available_copies") <= 0) throw new LibraryException("No copy of this book is available right now.");

        var outstanding = OutstandingFines(root, studentId, now);
        if (outstanding > 0)
            throw new LibraryException($"{Str(student, "name")} has ₱{outstanding:N2} in unpaid library fines. Fines must be settled before borrowing.");

        var open = Rows(root, "book_loans").Where(l => Int(l, "student_id") == studentId && IsOpen(l)).ToList();
        if (open.Any(l => Int(l, "book_id") == bookId)) throw new LibraryException("This borrower already has this book on loan.");
        if (open.Count >= MaxLoans(root)) throw new LibraryException($"This borrower is at the limit of {MaxLoans(root)} books on loan.");

        var loans = CanonicalJsonStore.GetOrCreateArray(root, "book_loans");
        var loanId = CanonicalJsonStore.NextId(loans);
        var due = now.AddDays(Math.Clamp(loanDays ?? DefaultLoanDays, 1, 30));
        loans.Add(new JsonObject
        {
            ["id"] = loanId,
            ["book_id"] = bookId,
            ["student_id"] = studentId,
            ["borrow_date"] = Iso(now),
            ["due_date"] = Iso(due),
            ["return_date"] = null,
            ["condition_on_return"] = null,
            ["status"] = "Borrowed",
            ["fine_amount"] = 0m,
            ["fine_status"] = "None",
            ["remarks"] = $"Checked out at {source}"
        });
        book["available_copies"] = Int(book, "available_copies") - 1;

        var reservation = Rows(root, "library_reservations").FirstOrDefault(r =>
            Int(r, "student_id") == studentId && Int(r, "book_id") == bookId && Same(Str(r, "status"), "Active"));
        if (reservation is not null)
        {
            reservation["status"] = "Fulfilled";
            reservation["fulfilled_at"] = Iso(now);
        }
        return loanId;
    }

    /// <summary>
    /// Checks a book in. Overdue days cost the daily rate; Damaged and Lost add their fixed fines. Every fine becomes a
    /// pending <c>library_fines</c> row. A Good return puts the copy back on the shelf; Damaged and Lost do not.
    /// </summary>
    public static ReturnResult Return(JsonObject root, int loanId, string condition, DateTime now, int? onlyForStudent = null)
    {
        if (!Conditions.Contains(condition, StringComparer.OrdinalIgnoreCase)) throw new LibraryException("Condition must be Good, Damaged or Lost.");
        condition = Conditions.First(c => Same(c, condition));

        var loan = Rows(root, "book_loans").FirstOrDefault(l => Int(l, "id") == loanId && (onlyForStudent is null || Int(l, "student_id") == onlyForStudent))
                   ?? throw new LibraryException("That loan was not found.");
        if (!IsOpen(loan)) throw new LibraryException("This book has already been returned.");

        var fines = new List<(string Reason, decimal Amount)>();
        var overdueDays = OverdueDays(loan, now);
        if (overdueDays > 0) fines.Add(($"Overdue {overdueDays} day(s)", overdueDays * OverduePerDay(root)));
        if (condition == "Damaged") fines.Add(("Damaged", DamagedFine(root)));
        if (condition == "Lost") fines.Add(("Lost", LostFine(root)));

        var total = fines.Sum(f => f.Amount);
        loan["return_date"] = Iso(now);
        loan["condition_on_return"] = condition;
        loan["status"] = condition == "Good" ? "Returned" : condition;
        loan["fine_amount"] = total;
        loan["fine_status"] = total > 0 ? "Pending" : "None";
        loan["remarks"] = total > 0 ? $"Returned {condition.ToLowerInvariant()}. Fines: {string.Join("; ", fines.Select(f => $"{f.Reason} ₱{f.Amount:N2}"))}" : "Returned in good condition.";

        if (total > 0)
        {
            var table = CanonicalJsonStore.GetOrCreateArray(root, "library_fines");
            foreach (var (reason, amount) in fines)
                table.Add(new JsonObject
                {
                    ["id"] = CanonicalJsonStore.NextId(table),
                    ["loan_id"] = loanId,
                    ["student_id"] = Int(loan, "student_id"),
                    ["amount"] = amount,
                    ["reason"] = reason,
                    ["status"] = "Pending",
                    ["assessed_at"] = Iso(now),
                    ["paid_at"] = null,
                    ["payment_id"] = null
                });
        }

        if (condition == "Good")
        {
            var book = Rows(root, "library_books").FirstOrDefault(b => Int(b, "id") == Int(loan, "book_id"));
            if (book is not null)
            {
                var total_ = Int(book, "total_copies");
                book["available_copies"] = Math.Min(total_ > 0 ? total_ : int.MaxValue, Int(book, "available_copies") + 1);
            }
        }
        return new ReturnResult(total, fines.Select(f => f.Reason).ToList());
    }

    // ── Fines ───────────────────────────────────────────────────────────────

    public static int OverdueDays(JsonObject loan, DateTime now) =>
        IsOpen(loan) && Date(loan, "due_date") is { } due && due < now ? (int)Math.Ceiling((now - due).TotalDays) : 0;

    /// <summary>Pending fines plus what open overdue loans have accrued so far. Waived and paid fines do not count.</summary>
    public static decimal OutstandingFines(JsonObject root, int studentId, DateTime now)
    {
        var pending = Rows(root, "library_fines").Where(f => Int(f, "student_id") == studentId && Same(Str(f, "status"), "Pending")).Sum(f => Dec(f, "amount"));
        var accrued = Rows(root, "book_loans").Where(l => Int(l, "student_id") == studentId).Sum(l => OverdueDays(l, now) * OverduePerDay(root));
        // A loan returned before library_fines existed carries its fine on the loan itself.
        var legacy = Rows(root, "book_loans")
            .Where(l => Int(l, "student_id") == studentId && Same(Str(l, "fine_status"), "Pending")
                        && !Rows(root, "library_fines").Any(f => Int(f, "loan_id") == Int(l, "id")))
            .Sum(l => Dec(l, "fine_amount"));
        return pending + accrued + legacy;
    }

    /// <summary>Records payment of a pending fine at the library desk or from Student Portal. Returns the receipt number.</summary>
    public static string PayFine(JsonObject root, int fineId, string method, DateTime now, int? processedBy = null, int? onlyForStudent = null)
    {
        var fine = Rows(root, "library_fines").FirstOrDefault(f => Int(f, "id") == fineId && (onlyForStudent is null || Int(f, "student_id") == onlyForStudent))
                   ?? throw new LibraryException("That fine was not found.");
        if (!Same(Str(fine, "status"), "Pending")) throw new LibraryException("This fine is not pending.");

        var payments = CanonicalJsonStore.GetOrCreateArray(root, "student_payments");
        var paymentId = CanonicalJsonStore.NextId(payments);
        var receipt = $"LIB-{now:yyyy}-{paymentId:D5}";
        // A library fine is not tuition, so no fee assessment is touched.
        payments.Add(new JsonObject
        {
            ["id"] = paymentId,
            ["student_id"] = Int(fine, "student_id"),
            ["assessment_id"] = null,
            ["receipt_no"] = receipt,
            ["amount"] = Dec(fine, "amount"),
            ["pay_date"] = Iso(now),
            ["payment_method"] = method,
            ["payment_category"] = "Library Fine",
            ["status"] = "Paid",
            ["description"] = $"Library fine ({Str(fine, "reason")}) for loan #{Int(fine, "loan_id")}",
            ["processed_by_user_id"] = processedBy
        });
        fine["status"] = "Paid";
        fine["paid_at"] = Iso(now);
        fine["payment_id"] = paymentId;
        SyncLoanFineStatus(root, Int(fine, "loan_id"));
        return receipt;
    }

    /// <summary>
    /// Pays every pending fine on a loan in one step, as Student Portal does. A loan that was returned before
    /// <c>library_fines</c> existed has its fine converted into a row first. Returns the receipt numbers.
    /// </summary>
    public static IReadOnlyList<string> PayLoanFines(JsonObject root, int loanId, string method, DateTime now, int? onlyForStudent = null)
    {
        var loan = Rows(root, "book_loans").FirstOrDefault(l => Int(l, "id") == loanId && (onlyForStudent is null || Int(l, "student_id") == onlyForStudent))
                   ?? throw new LibraryException("That loan was not found.");

        var table = CanonicalJsonStore.GetOrCreateArray(root, "library_fines");
        if (Same(Str(loan, "fine_status"), "Pending") && Dec(loan, "fine_amount") > 0 && !table.OfType<JsonObject>().Any(f => Int(f, "loan_id") == loanId))
            table.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(table), ["loan_id"] = loanId, ["student_id"] = Int(loan, "student_id"),
                ["amount"] = Dec(loan, "fine_amount"), ["reason"] = "Library fine", ["status"] = "Pending", ["assessed_at"] = Iso(now)
            });

        var pending = table.OfType<JsonObject>().Where(f => Int(f, "loan_id") == loanId && Same(Str(f, "status"), "Pending")).Select(f => Int(f, "id")).ToList();
        if (pending.Count == 0) throw new LibraryException("No outstanding fine exists for this loan.");
        return pending.Select(id => PayFine(root, id, method, now, null, onlyForStudent)).ToList();
    }

    /// <summary>A librarian forgives a fine. The reason is kept on the row.</summary>
    public static void WaiveFine(JsonObject root, int fineId, string reason, int waivedBy, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new LibraryException("Give a reason for waiving the fine.");
        var fine = Rows(root, "library_fines").FirstOrDefault(f => Int(f, "id") == fineId) ?? throw new LibraryException("That fine was not found.");
        if (!Same(Str(fine, "status"), "Pending")) throw new LibraryException("This fine is not pending.");

        fine["status"] = "Waived";
        fine["waived_by"] = waivedBy;
        fine["waived_reason"] = reason.Trim();
        fine["paid_at"] = Iso(now);
        SyncLoanFineStatus(root, Int(fine, "loan_id"));
    }

    private static void SyncLoanFineStatus(JsonObject root, int loanId)
    {
        var loan = Rows(root, "book_loans").FirstOrDefault(l => Int(l, "id") == loanId);
        if (loan is null) return;
        var remaining = Rows(root, "library_fines").Any(f => Int(f, "loan_id") == loanId && Same(Str(f, "status"), "Pending"));
        if (!remaining && Dec(loan, "fine_amount") > 0)
            loan["fine_status"] = Rows(root, "library_fines").Any(f => Int(f, "loan_id") == loanId && Same(Str(f, "status"), "Paid")) ? "Paid" : "Waived";
    }

    // ── Reservations ────────────────────────────────────────────────────────

    /// <summary>Puts a student in the queue for a title. Only for books with no free copy.</summary>
    public static int Reserve(JsonObject root, int studentId, int bookId, DateTime now)
    {
        var book = Rows(root, "library_books").FirstOrDefault(b => Int(b, "id") == bookId) ?? throw new LibraryException("That book is not in the catalog.");
        if (Int(book, "available_copies") > 0) throw new LibraryException("A copy is available. Borrow it instead of reserving.");
        if (Rows(root, "book_loans").Any(l => Int(l, "student_id") == studentId && Int(l, "book_id") == bookId && IsOpen(l)))
            throw new LibraryException("You already have this book on loan.");

        var reservations = CanonicalJsonStore.GetOrCreateArray(root, "library_reservations");
        if (reservations.OfType<JsonObject>().Any(r => Int(r, "student_id") == studentId && Int(r, "book_id") == bookId && IsActiveReservation(r, now)))
            throw new LibraryException("You already reserved this book.");

        var id = CanonicalJsonStore.NextId(reservations);
        reservations.Add(new JsonObject
        {
            ["id"] = id,
            ["student_id"] = studentId,
            ["book_id"] = bookId,
            ["book_title"] = Str(book, "title"),
            ["book_author"] = Str(book, "author"),
            ["reservation_date"] = Iso(now),
            ["due_date"] = Iso(now.AddDays(ReservationHoldDays)),
            ["status"] = "Active"
        });
        return id;
    }

    public static void CancelReservation(JsonObject root, int reservationId, int? onlyForStudent = null)
    {
        var row = Rows(root, "library_reservations").FirstOrDefault(r => Int(r, "id") == reservationId && (onlyForStudent is null || Int(r, "student_id") == onlyForStudent))
                  ?? throw new LibraryException("That reservation was not found.");
        if (!Same(Str(row, "status"), "Active")) throw new LibraryException("Only an active reservation can be cancelled.");
        row["status"] = "Cancelled";
    }

    /// <summary>Active means not cancelled, not fulfilled and not past its hold date.</summary>
    public static bool IsActiveReservation(JsonObject reservation, DateTime now) =>
        Same(Str(reservation, "status"), "Active") && (Date(reservation, "due_date") is not { } due || due >= now);

    // ── Helpers ─────────────────────────────────────────────────────────────

    public static bool IsOpen(JsonObject loan) => loan["return_date"] is null || Str(loan, "return_date").Length == 0;

    private static decimal Setting(JsonObject root, string key, decimal fallback) =>
        Rows(root, "settings").FirstOrDefault(s => Str(s, "key") == key) is { } row
        && decimal.TryParse(Str(row, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Iso(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? Date(JsonObject o, string key) =>
        DateTimeOffset.TryParse(Str(o, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : null;

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsBorrower(string? role) => Same(role, "student") || Same(role, "faculty");

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
