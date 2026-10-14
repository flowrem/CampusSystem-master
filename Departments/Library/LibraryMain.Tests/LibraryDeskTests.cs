using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using LibraryMain.Services;

namespace LibraryMain.Tests;

public sealed class LibraryDeskTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 2 },
      "unified_seed_data": {
        "settings": [],
        "users": [
          { "id": 5, "student_id_number": "2023-01045", "name": "Alyssa Mendoza", "role": "student" },
          { "id": 6, "student_id_number": "2023-01088", "name": "Juan Dela Cruz", "role": "student" },
          { "id": 7, "student_id_number": "FAC-2024-003", "name": "Prof. Santos", "role": "faculty" },
          { "id": 10, "student_id_number": "ADM-1", "name": "Admin User", "role": "admin" },
          { "id": 11, "student_id_number": "LIB-1", "name": "Ms. Reyes", "role": "librarian" }
        ],
        "library_books": [
          { "id": 1, "isbn": "111", "title": "Clean Code", "author": "Martin", "category": "Software Engineering", "call_number": "QA 76", "shelf_location": "B4", "total_copies": 2, "available_copies": 2 },
          { "id": 2, "isbn": "222", "title": "Patterns", "author": "GoF", "category": "Software Engineering", "call_number": "QA 77", "shelf_location": "B5", "total_copies": 1, "available_copies": 0 },
          { "id": 3, "isbn": "333", "title": "Kapitan", "author": "Rizal", "category": "Literature", "call_number": "PR 1", "shelf_location": "C1", "total_copies": 1, "available_copies": 1 }
        ],
        "book_loans": [], "library_fines": [], "library_reservations": [], "student_payments": [], "activity_log": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LibraryDeskTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _json;
    private readonly LibraryDesk _desk;

    public LibraryDeskTests()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(path, Fixture);
        _json = new CanonicalJsonStore(path);
        _desk = new LibraryDesk(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private int Count(string collection) => CanonicalJsonStore.GetArray(_json.ReadRoot(), collection).Count;

    [Fact]
    public void Catalog_SearchesTitleAuthorIsbnAndCallNumber_AndFiltersByCategoryAndAvailability()
    {
        Assert.Equal(3, _desk.Catalog(null, null, false).Count);
        Assert.Equal("Clean Code", Assert.Single(_desk.Catalog("martin", null, false)).Title);
        Assert.Equal("Patterns", Assert.Single(_desk.Catalog("222", null, false)).Title);
        Assert.Equal("Kapitan", Assert.Single(_desk.Catalog("PR 1", null, false)).Title);
        Assert.Equal(2, _desk.Catalog(null, "Software Engineering", false).Count);
        Assert.Equal(["Clean Code", "Kapitan"], _desk.Catalog(null, null, true).Select(b => b.Title));
        Assert.Equal(["Literature", "Software Engineering"], _desk.Categories());
    }

    [Fact]
    public void ALibrarian_ChecksOutByStudentNumber_AndTheLoanShowsInHistory()
    {
        var loanId = _desk.Checkout(" 2023-01045 ", 1, null, staffId: 11);

        var loan = Assert.Single(_desk.Loans(null, "open", null));
        Assert.Equal((loanId, "Alyssa Mendoza", "Clean Code", "Borrowed"), (loan.Id, loan.StudentName, loan.BookTitle, loan.Status));
        Assert.Equal(1, _desk.Book(1)!.Available);
        var log = CanonicalJsonStore.GetArray(_json.ReadRoot(), "activity_log")[0]!;
        Assert.Equal("Ms. Reyes", log["user_name"]!.GetValue<string>());
    }

    [Fact]
    public void Checkout_IsRefused_ForUnknownNonBorrowers_AndNoCopies()
    {
        Assert.Throws<LibraryException>(() => _desk.Checkout("0000", 1, null, 11));
        Assert.Throws<LibraryException>(() => _desk.Checkout("LIB-1", 1, null, 11));
        Assert.Throws<LibraryException>(() => _desk.Checkout("ADM-1", 1, null, 11));
        Assert.Throws<LibraryException>(() => _desk.Checkout("2023-01045", 2, null, 11));
        Assert.Equal(0, Count("book_loans"));
        Assert.Equal(0, Count("activity_log"));
    }

    [Fact]
    public void CurrentBorrowers_OnlyShowsOpenLoans_OrdersOverdueFirst_AndRespectsTop()
    {
        var overdue = _desk.Checkout("2023-01045", 1, null, 11);
        var current = _desk.Checkout("2023-01088", 3, null, 11);
        _json.UpdateRoot(root =>
        {
            var loans = CanonicalJsonStore.GetArray(root, "book_loans").OfType<JsonObject>();
            loans.First(l => l["id"]!.GetValue<int>() == overdue)["due_date"] = DateTime.UtcNow.AddDays(-2).ToString("O");
            loans.First(l => l["id"]!.GetValue<int>() == current)["due_date"] = DateTime.UtcNow.AddDays(1).ToString("O");
        });
        _desk.CheckIn(current, "Good", 11);

        var borrowers = _desk.CurrentBorrowers(1);

        var borrower = Assert.Single(borrowers);
        Assert.Equal((overdue, "Alyssa Mendoza", "student"), (borrower.LoanId, borrower.Name, borrower.Role));
        Assert.True(borrower.OverdueDays > 0);
    }

    [Fact]
    public void FacultyCanBorrow_AppearsAsFaculty_AndUnpaidFinesBlockBorrowing()
    {
        var loan = _desk.Checkout("FAC-2024-003", 1, null, 11);
        var borrower = Assert.Single(_desk.CurrentBorrowers());
        Assert.Equal(("Prof. Santos", "faculty"), (borrower.Name, borrower.Role));
        _desk.CheckIn(loan, "Damaged", 11);

        Assert.Throws<LibraryException>(() => _desk.Checkout("FAC-2024-003", 3, null, 11));
    }

    [Fact]
    public void RecentReturns_AreNewestFirst()
    {
        var first = _desk.Checkout("2023-01045", 1, null, 11);
        _desk.CheckIn(first, "Good", 11);
        var second = _desk.Checkout("2023-01088", 3, null, 11);
        _desk.CheckIn(second, "Good", 11);

        var returns = _desk.RecentReturns();

        Assert.Equal([second, first], returns.Select(r => r.LoanId));
    }

    [Fact]
    public void ALoanIsSeenByItsOwner_AndStaff_ButNotByAnotherStudent()
    {
        _desk.Checkout("2023-01045", 1, null, 11);

        Assert.Single(_desk.Loans(5, null, null));
        Assert.Empty(_desk.Loans(6, null, null));
        Assert.Single(_desk.Loans(null, null, "alyssa"));
        Assert.Empty(_desk.Loans(null, null, "nobody"));
    }

    [Fact]
    public void ALateCheckIn_CreatesAFine_ThatBlocksBorrowing_UntilTheDeskRecordsPayment()
    {
        var loanId = _desk.Checkout("2023-01045", 1, 1, 11);
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "book_loans").OfType<JsonObject>().First()["due_date"] = DateTime.UtcNow.AddDays(-3).AddHours(1).ToString("O"));

        var result = _desk.CheckIn(loanId, "Good", 11);

        Assert.Equal(60m, result.TotalFine);
        var fine = Assert.Single(_desk.Fines(5, "Pending"));
        Assert.Equal((60m, "Alyssa Mendoza", "Clean Code"), (fine.Amount, fine.StudentName, fine.BookTitle));
        Assert.Equal(60m, _desk.Outstanding(5));
        Assert.Throws<LibraryException>(() => _desk.Checkout("2023-01045", 3, null, 11));

        var receipt = _desk.RecordFinePayment(fine.Id, "Cash (Library)", 11);

        Assert.StartsWith("LIB-", receipt);
        Assert.Equal(0m, _desk.Outstanding(5));
        Assert.Single(_desk.Fines(null, "Paid"));
        _desk.Checkout("2023-01045", 3, null, 11);
        Assert.Throws<LibraryException>(() => _desk.RecordFinePayment(fine.Id, "Cash (Library)", 11));
        Assert.Throws<LibraryException>(() => _desk.RecordFinePayment(fine.Id, "Barter", 11));
    }

    [Fact]
    public void AWaivedFine_IsKeptWithItsReason_AndCharged_NoPayment()
    {
        var loanId = _desk.Checkout("2023-01045", 1, null, 11);
        _desk.CheckIn(loanId, "Damaged", 11);

        _desk.WaiveFine(_desk.Fines(5, "Pending").Single().Id, "Water damage from the flood", 11);

        var fine = Assert.Single(_desk.Fines(5, "Waived"));
        Assert.Equal("Water damage from the flood", fine.Note);
        Assert.Equal(0, Count("student_payments"));
        Assert.Equal(0m, _desk.Outstanding(5));
    }

    [Fact]
    public void Stats_CountTitlesCopiesLoansOverdueAndPendingMoney()
    {
        var loanId = _desk.Checkout("2023-01045", 1, null, 11);
        _desk.Checkout("2023-01088", 3, null, 11);
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "book_loans").OfType<JsonObject>().First(l => l["id"]!.GetValue<int>() == loanId)["due_date"] = DateTime.UtcNow.AddDays(-2).AddHours(1).ToString("O"));

        var stats = _desk.GetStats();

        Assert.Equal((3, 1, 2, 1), (stats.Titles, stats.CopiesOnShelf, stats.OpenLoans, stats.OverdueLoans));
        Assert.Equal(40m, stats.PendingFines);
    }

    [Fact]
    public void ReservingAndCancelling_FollowTheRules_AndStudentsCannotCancelOthers()
    {
        Assert.Throws<LibraryException>(() => _desk.Reserve(5, 1)); // copies free
        var id = _desk.Reserve(5, 2);

        var row = Assert.Single(_desk.Reservations(5, "Active"));
        Assert.Equal((id, "Patterns", 1), (row.Id, row.BookTitle, _desk.Book(2)!.Reservations));
        Assert.Empty(_desk.Reservations(6, null));
        Assert.Throws<LibraryException>(() => _desk.CancelReservation(id, 6, 6));

        _desk.CancelReservation(id, 5, 5);
        Assert.Single(_desk.Reservations(null, "Cancelled"));
        Assert.Equal(0, _desk.Book(2)!.Reservations);
    }

    [Fact]
    public void AnOldActiveReservation_IsShownAsExpired()
    {
        _desk.Reserve(5, 2);
        _json.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "library_reservations").OfType<JsonObject>().First()["due_date"] = DateTime.UtcNow.AddDays(-1).ToString("O"));

        Assert.Equal("Expired", Assert.Single(_desk.Reservations(5, null)).Status);
        Assert.Empty(_desk.Reservations(5, "Active"));
    }

    [Fact]
    public void RepositoryData_IsReadableByEveryLibraryQuery()
    {
        var desk = new LibraryDesk(new CanonicalJsonStore(CanonicalJsonStore.ResolvePath()));

        Assert.NotEmpty(desk.Catalog(null, null, false));
        Assert.NotEmpty(desk.Loans(null, null, null));
        Assert.True(desk.GetStats().Titles > 0);
        _ = desk.Fines(null, null);
        _ = desk.Reservations(null, null);
    }
}
