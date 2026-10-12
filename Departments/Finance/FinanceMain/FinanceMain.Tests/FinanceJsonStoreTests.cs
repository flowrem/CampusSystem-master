using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using FinanceMain.Contracts;
using FinanceMain.Services;

namespace FinanceMain.Tests;

public sealed class FinanceJsonStoreTests : IDisposable
{
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "users": [
          { "id": 5, "student_id_number": "2023-01045", "name": "Alyssa Mendoza", "role": "student" },
          { "id": 8, "student_id_number": "2024-1086767", "name": "Benjamin Dizon", "role": "student" },
          { "id": 2, "student_id_number": "FIN-2024-002", "name": "Roberto Gomez", "role": "finance" }
        ],
        "student_profiles": [
          { "user_id": 5, "program": "BS Information Technology" },
          { "user_id": 8, "program": "BS Information Technology" }
        ],
        "subjects": [ { "id": 101, "subject_code": "IT311", "has_lab": true, "lab_fee_amount": 1800 } ],
        "enrollments": [
          { "id": 1001, "student_id": 5, "school_year": "2025-2026", "semester": "1st Semester", "total_units": 9, "created_at": "2025-08-10T14:20:00Z" },
          { "id": 1002, "student_id": 8, "school_year": "2026-2027", "semester": "1st Semester", "total_units": 6, "created_at": "2026-09-29T13:47:36Z" }
        ],
        "enrolled_subjects": [
          { "id": 2001, "enrollment_id": 1002, "student_id": 8, "class_offering_id": 501, "subject_code": "IT311", "status": "enrolled" }
        ],
        "class_offerings": [
          { "id": 501, "subject_id": 101, "section_code": "IT31S1", "room": "CLAB-302", "days_of_week": "MW", "start_time": "08:00:00", "end_time": "10:30:00" }
        ],
        "tuition_fee_matrix": [
          { "id": 1, "program_code": "BSIT", "academic_year": "2025-2026", "tuition_per_unit": 1500, "base_miscellaneous_fee": 6500, "energy_it_fee": 2500, "library_fee": 1200, "registration_fee": 800 }
        ],
        "fee_assessments": [
          { "id": 8001, "assessment_number": "OAO-2025-001045", "enrollment_id": 1001, "student_id": 5, "school_year": "2025-2026", "semester": "1st Semester",
            "total_units": 9, "tuition_fee": 13500, "miscellaneous_fees": 11000, "laboratory_fees": 3600, "total_net_assessment": 28100,
            "total_paid": 10000, "remaining_balance": 18100, "status": "Partially Paid" },
          { "id": 8002, "enrollment_id": 1002, "student_id": 8, "total_net_assessment": 10000, "total_paid": 10000, "remaining_balance": 0, "status": "Paid" }
        ],
        "installment_schedules": [
          { "id": 9001, "assessment_id": 8001, "student_id": 5, "term_type": "Downpayment", "due_date": "2025-08-15", "amount_due": 10000, "amount_paid": 10000, "status": "Paid", "cleared_for_exam": true },
          { "id": 9002, "assessment_id": 8001, "student_id": 5, "term_type": "Midterm", "due_date": "2025-10-15", "amount_due": 9050, "amount_paid": 0, "status": "Pending", "cleared_for_exam": false },
          { "id": 9003, "assessment_id": 8001, "student_id": 5, "term_type": "Final", "due_date": "2025-12-10", "amount_due": 9050, "amount_paid": 0, "status": "Pending", "cleared_for_exam": false }
        ],
        "student_payments": [
          { "id": 3001, "student_id": 5, "assessment_id": 8001, "installment_id": 9001, "receipt_no": "OR-2025-99812", "amount": 10000, "pay_date": "2025-08-14T11:20:00Z", "payment_method": "Maya", "payment_category": "Downpayment", "status": "Paid" },
          { "id": 3002, "student_id": 5, "receipt_no": "LIB-1", "amount": 40, "pay_date": "2025-09-01T08:00:00Z", "payment_method": "Cash", "payment_category": "Library Fine", "status": "Paid" }
        ],
        "student_clearance": [
          { "id": 4001, "student_id": 5, "department_name": "Finance Office", "school_year": "2025-2026", "semester": "1st Semester", "status": "Cleared" },
          { "id": 4002, "student_id": 5, "department_name": "Registrar Office", "school_year": "2025-2026", "semester": "1st Semester", "status": "Cleared" }
        ],
        "transcript_requests": [],
        "scholarships": [ { "id": 1, "code": "ACAD-50", "name": "University Academic Honors Scholarship", "coverage": "50% Tuition", "budget": 1000, "status": "Active" } ],
        "scholarship_applications": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FinanceJsonTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly CanonicalJsonStore _json;
    private readonly FinanceJsonStore _finance;

    public FinanceJsonStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, CanonicalJsonStore.FileName);
        File.WriteAllText(_path, Fixture);
        _json = new CanonicalJsonStore(_path);
        _finance = new FinanceJsonStore(_json);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    private JsonObject Row(string collection, int id) =>
        CanonicalJsonStore.GetArray(_json.ReadRoot(), collection).OfType<JsonObject>().First(r => r["id"]!.GetValue<int>() == id);

    private string ClearanceOf(int studentId) => _finance.GetStudentClearance(studentId)!.Status;

    [Fact]
    public void Assessment_IsBuiltFromJsonRow_WithStudentNameAndPaymentSpread()
    {
        var assessment = _finance.GetAssessmentById(8001)!;

        Assert.Equal("Alyssa Mendoza", assessment.StudentDisplayName);
        Assert.Equal("BS Information Technology", assessment.Program);
        Assert.Equal("Partial", assessment.Status);
        Assert.Equal(18100m, assessment.Balance);
        Assert.Equal(3, assessment.Items.Count);
        Assert.Equal(10000m, assessment.Items.Sum(i => i.AmountPaid));
        Assert.Equal(10000m, assessment.Items[0].AmountPaid); // spread in order, never above a line's amount
    }

    [Fact]
    public void Payments_ExcludeLibraryFines()
    {
        var payments = _finance.GetPayments(new PaymentListFilter(null, 1, 20));

        var only = Assert.Single(payments.Items);
        Assert.Equal("OR-2025-99812", only.PaymentNumber);
        Assert.Equal("OAO-2025-001045", only.AssessmentNumber);
    }

    [Fact]
    public void RecordPayment_UpdatesAssessment_Installments_AndPaymentRow()
    {
        var record = _finance.RecordPayment(new RecordPaymentRequest(8001, 9050m, "GCash", "REF-1"));

        var assessment = Row("fee_assessments", 8001);
        Assert.Equal(19050m, assessment["total_paid"]!.GetValue<decimal>());
        Assert.Equal(9050m, assessment["remaining_balance"]!.GetValue<decimal>());
        Assert.Equal("Partially Paid", assessment["status"]!.GetValue<string>());
        Assert.Equal("Paid", Row("installment_schedules", 9002)["status"]!.GetValue<string>());
        Assert.True(Row("installment_schedules", 9002)["cleared_for_exam"]!.GetValue<bool>());
        Assert.Equal("Pending", Row("installment_schedules", 9003)["status"]!.GetValue<string>());

        var payment = _finance.GetPaymentByNumber(record.PaymentNumber)!;
        Assert.Equal("REF-1", payment.ReferenceNumber);
        Assert.Equal(8001, payment.FeeAssessmentId);
        Assert.Equal("Cleared", ClearanceOf(5)); // a partial payment never downgrades a cleared student
    }

    [Fact]
    public void RecordPayment_InFull_ClearsFinanceRow()
    {
        _finance.RecordPayment(new RecordPaymentRequest(8001, 18100m, "Over-the-counter", null));

        Assert.Equal("Paid", Row("fee_assessments", 8001)["status"]!.GetValue<string>());
        Assert.Equal("Cleared", Row("student_clearance", 4001)["status"]!.GetValue<string>());
        Assert.NotNull(Row("student_clearance", 4001)["cleared_at"]);
    }

    [Fact]
    public void RecordPayment_RejectsBadAmounts_AndLeavesFileUntouched()
    {
        var before = File.ReadAllText(_path);

        Assert.Throws<FinanceValidationException>(() => _finance.RecordPayment(new RecordPaymentRequest(8001, 18100.01m, "GCash", null)));
        Assert.Throws<FinanceValidationException>(() => _finance.RecordPayment(new RecordPaymentRequest(8001, 0m, "GCash", null)));
        Assert.Throws<FinanceValidationException>(() => _finance.RecordPayment(new RecordPaymentRequest(9999, 10m, "GCash", null)));

        Assert.Equal(before, File.ReadAllText(_path));
    }

    [Fact]
    public void ConcurrentPayments_AllLand_WithUniqueReceipts()
    {
        Parallel.For(0, 6, new ParallelOptions { MaxDegreeOfParallelism = 6 }, _ =>
        {
            var finance = new FinanceJsonStore(new CanonicalJsonStore(_path));
            for (var i = 0; i < 5; i++) finance.RecordPayment(new RecordPaymentRequest(8001, 100m, "GCash", null));
        });

        Assert.Equal(13000m, Row("fee_assessments", 8001)["total_paid"]!.GetValue<decimal>());
        var receipts = _finance.GetPayments(new PaymentListFilter(null, 1, 100)).Items.Select(p => p.PaymentNumber).ToList();
        Assert.Equal(31, receipts.Count);
        Assert.Equal(receipts.Count, receipts.Distinct().Count());
    }

    [Fact]
    public void AssessEnrollee_ReplacesRegistrarPlaceholder_OnceWithMatrixFees()
    {
        var request = new EnrolleeAssessmentRequest(8, "", "", "", "2026-2027", "1st Semester", 0);

        var first = _finance.AssessEnrollee(request);
        var second = _finance.AssessEnrollee(request);

        // The matrix only has 2025-2026, so BSIT's newest year applies: 6 units x 1500 + 11000 misc + 1800 lab.
        Assert.False(first.IsExisting);
        Assert.True(second.IsExisting);
        Assert.Equal(8002, first.Assessment.Id);
        Assert.Equal(9000m + 11000m + 1800m, first.Assessment.TotalAmount);
        Assert.Equal(10000m, first.Assessment.TotalPaid);
        Assert.Equal(first.Assessment.TotalAmount - 10000m, first.Assessment.Balance);
        Assert.Equal("Partial", first.Assessment.Status);
        Assert.Equal(first.Assessment.TotalAmount, second.Assessment.TotalAmount);
        Assert.Equal("NotCleared", ClearanceOf(8));
    }

    [Fact]
    public void AssessEnrollee_Throws_ForUnknownStudent_OrMissingUnits()
    {
        Assert.Throws<FinanceValidationException>(() =>
            _finance.AssessEnrollee(new EnrolleeAssessmentRequest(0, "NOPE", "", "", "2026-2027", "1st Semester", 9)));
        Assert.Throws<FinanceValidationException>(() =>
            _finance.AssessEnrollee(new EnrolleeAssessmentRequest(5, "", "", "", "2030-2031", "1st Semester", 0)));
    }

    [Fact]
    public void BatchIssue_CreatesMissingClearanceRows_ForSettledAssessments()
    {
        Assert.Equal(1, _finance.BatchIssueClearance());
        Assert.Equal(0, _finance.BatchIssueClearance());
        Assert.Equal("Cleared", ClearanceOf(8));
    }

    [Fact]
    public void ClearanceList_CanBeSearchedByStudentNumber()
    {
        var byNumber = _finance.GetClearances(new ClearanceListFilter("", "", null, "2023-01045", 1, 20));
        var byName = _finance.GetClearances(new ClearanceListFilter("", "", null, "alyssa", 1, 20));

        Assert.Equal(4001, Assert.Single(byNumber.Items).Id);
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal(0, _finance.GetClearances(new ClearanceListFilter("", "", null, "nobody", 1, 20)).TotalCount);
    }

    [Fact]
    public void SetConditional_OnlyTouchesFinanceRows()
    {
        _finance.SetConditional(4001, "Promissory note");

        Assert.Equal("Conditional", ClearanceOf(5));
        Assert.Equal("Cleared", Row("student_clearance", 4002)["status"]!.GetValue<string>());
        Assert.Throws<FinanceValidationException>(() => _finance.SetConditional(4002, "x"));
    }

    [Fact]
    public void DocumentRequest_IsCreatedThenPaidOnce()
    {
        var created = _finance.CreateRequest(new CreateRequestRequest(5, "Transcript of Records", 1, 2, "Job"));

        var before = Assert.Single(_finance.GetRequestsForStudent(5));
        Assert.Equal(created.RequestNumber, before.RequestNumber);
        Assert.Equal(550m, before.ProcessingFee); // 2 x 250 + 50
        Assert.Empty(before.Payments);

        _finance.PayRequestFee(created.Id, "Over-the-counter");
        Assert.Equal("Paid", Assert.Single(Assert.Single(_finance.GetRequestsForStudent(5)).Payments).Status);
        Assert.Throws<FinanceValidationException>(() => _finance.PayRequestFee(created.Id, "Over-the-counter"));
        Assert.Throws<FinanceValidationException>(() => _finance.CreateRequest(new CreateRequestRequest(5, "x", 99, 1, null)));
    }

    [Fact]
    public void Scholarship_CanBeGrantedOncePerStudent()
    {
        _finance.GrantScholarship("2023-01045", "University Academic Honors Scholarship", 5000m);

        var grantee = Assert.Single(_finance.GetScholarshipGrantees());
        Assert.Equal("Alyssa Mendoza", grantee.StudentName);
        Assert.Equal(1, Assert.Single(_finance.GetScholarshipPrograms()).RecipientsCount);
        Assert.Throws<FinanceValidationException>(() => _finance.GrantScholarship("2023-01045", "University Academic Honors Scholarship", 5000m));
        Assert.Throws<FinanceValidationException>(() => _finance.GrantScholarship("FIN-2024-002", "University Academic Honors Scholarship", 5000m));
    }

    [Fact]
    public void Roster_ListsStudentsEnrolledInSection()
    {
        var roster = _finance.GetRoster("it31s1")!;

        Assert.Equal(8, Assert.Single(roster.Students).StudentId);
        Assert.Contains("IT31S1", roster.SectionLabel);
        Assert.Null(_finance.GetRoster("NOPE"));
    }

    [Fact]
    public void RepositoryData_IsReadableByEveryFinanceQuery()
    {
        var finance = new FinanceJsonStore(new CanonicalJsonStore(CanonicalJsonStore.ResolvePath()));

        Assert.NotEmpty(finance.GetAssessments(new AssessmentListFilter("", "", null, null, 1, 50)).Items);
        Assert.NotEmpty(finance.GetPayments(new PaymentListFilter(null, 1, 50)).Items);
        Assert.NotEmpty(finance.GetClearances(new ClearanceListFilter("", "", null, null, 1, 50)).Items);
        Assert.NotNull(finance.GetCurrentAssessmentForStudent(5));
        Assert.NotEmpty(finance.GetScholarshipPrograms());
        Assert.NotEmpty(finance.GetRoster("IT31S1")!.Students);
    }
}
