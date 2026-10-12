using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace CampusSystem.Data.Tests;

public sealed class LibraryCirculationTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private const string Fixture = """
    {
      "system": { "schema_version": 2 },
      "unified_seed_data": {
        "settings": [
          { "key": "library_overdue_fine_per_day", "value": "20.00" },
          { "key": "library_damaged_book_fine", "value": "500.00" },
          { "key": "library_lost_book_fine", "value": "1200.00" },
          { "key": "library_max_loans", "value": "2" }
        ],
        "users": [
          { "id": 5, "name": "Alyssa", "role": "student" },
          { "id": 6, "name": "Juan", "role": "student" },
          { "id": 3, "name": "Prof", "role": "faculty" }
        ],
        "library_books": [
          { "id": 1, "title": "Clean Code", "author": "Martin", "total_copies": 2, "available_copies": 1 },
          { "id": 2, "title": "Patterns", "author": "GoF", "total_copies": 1, "available_copies": 0 },
          { "id": 3, "title": "Refactoring", "author": "Fowler", "total_copies": 3, "available_copies": 3 },
          { "id": 4, "title": "DDD", "author": "Evans", "total_copies": 3, "available_copies": 3 }
        ],
        "book_loans": [],
        "library_fines": [],
        "library_reservations": [],
        "student_payments": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LibraryTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _store;

    public LibraryCirculationTests()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(path, Fixture);
        _store = new CanonicalJsonStore(path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private T Run<T>(Func<JsonObject, T> action)
    {
        T result = default!;
        _store.UpdateRoot(root => result = action(root));
        return result;
    }

    private void RunVoid(Action<JsonObject> action) => _store.UpdateRoot(action);

    private int Borrow(int student, int book, DateTime? at = null) => Run(root => LibraryCirculation.Borrow(root, student, book, at ?? Now));

    private JsonObject Row(string collection, int id) =>
        CanonicalJsonStore.GetArray(_store.ReadRoot(), collection).OfType<JsonObject>().First(r => r["id"]!.GetValue<int>() == id);

    private int Copies(int bookId) => Row("library_books", bookId)["available_copies"]!.GetValue<int>();

    [Fact]
    public void Borrow_CreatesALoanDueInSevenDays_AndTakesACopyOffTheShelf()
    {
        var id = Borrow(5, 1);

        var loan = Row("book_loans", id);
        Assert.Equal("Borrowed", loan["status"]!.GetValue<string>());
        Assert.Equal(Now.AddDays(7), DateTime.Parse(loan["due_date"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        Assert.Equal(0, Copies(1));
    }

    [Fact]
    public void Borrow_IsRefused_ForNoCopy_UnknownPeople_Titles_Duplicates_AndTheLoanLimit()
    {
        Assert.Throws<LibraryException>(() => Borrow(5, 2));        // no copy free
        Assert.Throws<LibraryException>(() => Borrow(99, 1));       // unknown
        Assert.Throws<LibraryException>(() => Borrow(3, 1));        // faculty is not a student
        Assert.Throws<LibraryException>(() => Borrow(5, 99));       // unknown book
        Borrow(5, 3);
        Assert.Throws<LibraryException>(() => Borrow(5, 3));        // same title twice
        Borrow(5, 4);
        Assert.Throws<LibraryException>(() => Borrow(5, 1));        // limit of 2

        Assert.Equal(2, CanonicalJsonStore.GetArray(_store.ReadRoot(), "book_loans").Count);
        Assert.Equal(1, Copies(1));
    }

    [Fact]
    public void ALateGoodReturn_CostsTheDailyRate_AndRestoresTheCopy()
    {
        var loan = Borrow(5, 3, Now.AddDays(-10)); // due 3 days ago

        var result = Run(root => LibraryCirculation.Return(root, loan, "good", Now));

        Assert.Equal(60m, result.TotalFine); // ceil(3 days) x 20... due was 3 days before Now
        var fine = Assert.Single(CanonicalJsonStore.GetArray(_store.ReadRoot(), "library_fines"))!.AsObject();
        Assert.Equal(("Pending", 60m, loan), (fine["status"]!.GetValue<string>(), fine["amount"]!.GetValue<decimal>(), fine["loan_id"]!.GetValue<int>()));
        Assert.Equal("Pending", Row("book_loans", loan)["fine_status"]!.GetValue<string>());
        Assert.Equal("Returned", Row("book_loans", loan)["status"]!.GetValue<string>());
        Assert.Equal(3, Copies(3));
    }

    [Fact]
    public void AnOnTimeGoodReturn_HasNoFine()
    {
        var loan = Borrow(5, 3);

        var result = Run(root => LibraryCirculation.Return(root, loan, "Good", Now.AddDays(3)));

        Assert.Equal(0m, result.TotalFine);
        Assert.Empty(CanonicalJsonStore.GetArray(_store.ReadRoot(), "library_fines"));
        Assert.Equal("None", Row("book_loans", loan)["fine_status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Damaged", 500)]
    [InlineData("Lost", 1200)]
    public void DamagedAndLostBooks_AddTheirFixedFine_AndStayOffTheShelf(string condition, int expected)
    {
        var loan = Borrow(5, 3);

        var result = Run(root => LibraryCirculation.Return(root, loan, condition, Now.AddDays(2)));

        Assert.Equal(expected, result.TotalFine);
        Assert.Equal([condition], result.Reasons);
        Assert.Equal(2, Copies(3));
        Assert.Equal(condition, Row("book_loans", loan)["status"]!.GetValue<string>());
    }

    [Fact]
    public void ALateAndDamagedReturn_CreatesOneFineRowForEachReason()
    {
        var loan = Borrow(5, 3, Now.AddDays(-9)); // due 2 days ago

        var result = Run(root => LibraryCirculation.Return(root, loan, "Damaged", Now));

        Assert.Equal(540m, result.TotalFine);
        Assert.Equal(2, CanonicalJsonStore.GetArray(_store.ReadRoot(), "library_fines").Count);
    }

    [Fact]
    public void ABadReturn_IsRefused_ForUnknownConditionOrLoan_OrAReturnedLoan_OrAnotherStudent()
    {
        var loan = Borrow(5, 3);

        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Return(root, loan, "Chewed", Now)));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Return(root, 99, "Good", Now)));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Return(root, loan, "Good", Now, onlyForStudent: 6)));
        Run(root => LibraryCirculation.Return(root, loan, "Good", Now));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Return(root, loan, "Good", Now)));
        Assert.Equal(3, Copies(3));
    }

    [Fact]
    public void UnpaidFines_AndOverdueBooks_BlockNewBorrowing_UntilSettled()
    {
        var overdue = Borrow(5, 3, Now.AddDays(-20)); // due 13 days ago, still out
        Assert.Equal(260m, Run(root => LibraryCirculation.OutstandingFines(root, 5, Now)));
        Assert.Throws<LibraryException>(() => Borrow(5, 4));

        Run(root => LibraryCirculation.Return(root, overdue, "Good", Now));
        Assert.Throws<LibraryException>(() => Borrow(5, 4)); // fine now pending

        var receipts = Run(root => LibraryCirculation.PayLoanFines(root, overdue, "Cash (Library)", Now));
        Assert.Single(receipts);
        Assert.Equal(0m, Run(root => LibraryCirculation.OutstandingFines(root, 5, Now)));
        Borrow(5, 4);
    }

    [Fact]
    public void PayingAFine_WritesALibraryPayment_AndLeavesTuitionAlone()
    {
        var loan = Borrow(5, 3, Now.AddDays(-9));
        Run(root => LibraryCirculation.Return(root, loan, "Good", Now));

        var receipt = Run(root => LibraryCirculation.PayFine(root, 1, "GCash", Now, processedBy: 11));

        Assert.StartsWith("LIB-2026-", receipt);
        var payment = Row("student_payments", 1);
        Assert.Equal(("Library Fine", "Paid", 40m, 5), (payment["payment_category"]!.GetValue<string>(), payment["status"]!.GetValue<string>(), payment["amount"]!.GetValue<decimal>(), payment["student_id"]!.GetValue<int>()));
        Assert.Null(payment["assessment_id"]);
        Assert.Equal("Paid", Row("library_fines", 1)["status"]!.GetValue<string>());
        Assert.Equal("Paid", Row("book_loans", loan)["fine_status"]!.GetValue<string>());
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.PayFine(root, 1, "GCash", Now)));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.PayFine(root, 99, "GCash", Now)));
    }

    [Fact]
    public void AStudent_CannotPayAnotherStudentsFine()
    {
        var loan = Borrow(5, 3, Now.AddDays(-9));
        Run(root => LibraryCirculation.Return(root, loan, "Good", Now));

        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.PayFine(root, 1, "GCash", Now, onlyForStudent: 6)));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.PayLoanFines(root, loan, "GCash", Now, onlyForStudent: 6)));
    }

    [Fact]
    public void AWaivedFine_NoLongerBlocks_AndNeedsAReason()
    {
        var loan = Borrow(5, 3, Now.AddDays(-9));
        Run(root => LibraryCirculation.Return(root, loan, "Good", Now));

        Assert.Throws<LibraryException>(() => RunVoid(root => LibraryCirculation.WaiveFine(root, 1, " ", 11, Now)));
        RunVoid(root => LibraryCirculation.WaiveFine(root, 1, "Library was closed", 11, Now));

        Assert.Equal(0m, Run(root => LibraryCirculation.OutstandingFines(root, 5, Now)));
        Assert.Equal(("Waived", "Library was closed"), (Row("library_fines", 1)["status"]!.GetValue<string>(), Row("library_fines", 1)["waived_reason"]!.GetValue<string>()));
        Assert.Equal("Waived", Row("book_loans", loan)["fine_status"]!.GetValue<string>());
        Assert.Empty(CanonicalJsonStore.GetArray(_store.ReadRoot(), "student_payments"));
    }

    [Fact]
    public void ALegacyFineOnALoan_IsStillCounted_AndCanBePaid()
    {
        _store.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "book_loans").Add(new JsonObject
        {
            ["id"] = 1, ["book_id"] = 3, ["student_id"] = 5, ["borrow_date"] = "2026-08-01T00:00:00Z", ["due_date"] = "2026-08-08T00:00:00Z",
            ["return_date"] = "2026-08-20T00:00:00Z", ["status"] = "Returned", ["fine_amount"] = 240m, ["fine_status"] = "Pending"
        }));

        Assert.Equal(240m, Run(root => LibraryCirculation.OutstandingFines(root, 5, Now)));
        Run(root => LibraryCirculation.PayLoanFines(root, 1, "Maya", Now, onlyForStudent: 5));

        Assert.Equal(0m, Run(root => LibraryCirculation.OutstandingFines(root, 5, Now)));
        Assert.Equal("Paid", Row("book_loans", 1)["fine_status"]!.GetValue<string>());
    }

    [Fact]
    public void Reservations_AreOnlyForBooksWithNoFreeCopy_AndOncePerStudent()
    {
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Reserve(root, 5, 3, Now)));  // copies free
        var id = Run(root => LibraryCirculation.Reserve(root, 5, 2, Now));
        Assert.Throws<LibraryException>(() => Run(root => LibraryCirculation.Reserve(root, 5, 2, Now)));

        var row = Row("library_reservations", id);
        Assert.Equal(("Active", "Patterns", 2), (row["status"]!.GetValue<string>(), row["book_title"]!.GetValue<string>(), row["book_id"]!.GetValue<int>()));
        Assert.True(Run(root => LibraryCirculation.IsActiveReservation(Row("library_reservations", id), Now.AddDays(3))));
        Assert.False(Run(root => LibraryCirculation.IsActiveReservation(Row("library_reservations", id), Now.AddDays(8))));
    }

    [Fact]
    public void ACopyReturned_ThenBorrowedByTheReserver_FulfilsTheReservation()
    {
        var holder = Run(root => LibraryCirculation.Borrow(root, 6, 1, Now));      // takes the last copy of Clean Code
        var reservation = Run(root => LibraryCirculation.Reserve(root, 5, 1, Now));
        Run(root => LibraryCirculation.Return(root, holder, "Good", Now.AddDays(1)));

        Borrow(5, 1, Now.AddDays(1));

        Assert.Equal("Fulfilled", Row("library_reservations", reservation)["status"]!.GetValue<string>());
    }

    [Fact]
    public void Cancelling_NeedsAnActiveReservation_OfTheRightStudent()
    {
        var id = Run(root => LibraryCirculation.Reserve(root, 5, 2, Now));

        Assert.Throws<LibraryException>(() => RunVoid(root => LibraryCirculation.CancelReservation(root, id, onlyForStudent: 6)));
        RunVoid(root => LibraryCirculation.CancelReservation(root, id, 5));
        Assert.Equal("Cancelled", Row("library_reservations", id)["status"]!.GetValue<string>());
        Assert.Throws<LibraryException>(() => RunVoid(root => LibraryCirculation.CancelReservation(root, id)));
    }

    [Fact]
    public void RulesComeFromSettings_WithDefaults()
    {
        _store.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "settings").Clear());

        var root = _store.ReadRoot();
        Assert.Equal((20m, 500m, 1200m, 5), (LibraryCirculation.OverduePerDay(root), LibraryCirculation.DamagedFine(root), LibraryCirculation.LostFine(root), LibraryCirculation.MaxLoans(root)));
    }
}
