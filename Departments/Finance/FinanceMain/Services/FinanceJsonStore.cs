using System.Globalization;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using FinanceMain.Contracts;
using FinanceMain.Contracts.Dtos;

namespace FinanceMain.Services;

/// <summary>
/// Finance's view of the shared JSON datastore. Owns <c>fee_assessments</c>, <c>installment_schedules</c>,
/// <c>student_payments</c>, <c>scholarships</c> and <c>scholarship_applications</c>; writes the Finance
/// row of <c>student_clearance</c> and the payment fields of <c>transcript_requests</c>.
/// Reads are copies; every write happens inside one <see cref="CanonicalJsonStore.UpdateRoot"/> call.
/// </summary>
public sealed class FinanceJsonStore(CanonicalJsonStore store)
{
    public const string FinanceDepartment = "Finance Office";
    public const decimal BaseProcessingFee = 50.00m;

    private const string Cleared = "Cleared";
    private const string Conditional = "Conditional";
    private const string NotCleared = "NotCleared";

    public static readonly IReadOnlyList<DocumentFeeTypeDto> DocumentFeeTypes =
    [
        new(1, "Transcript of Records", 250m),
        new(2, "Certificate of Enrollment", 150m),
        new(3, "Diploma Copy", 350m),
        new(4, "Good Moral Certificate", 120m),
        new(5, "TOR with Authentication", 400m)
    ];

    // ── Assessments ─────────────────────────────────────────────────────────

    public AssessmentListResult GetAssessments(AssessmentListFilter filter)
    {
        var root = store.ReadRoot();
        var query = Rows(root, "fee_assessments").Select(a => ToAssessmentDto(root, a));

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(a => a.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            query = query.Where(a => a.StudentDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || a.StudentNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || a.AssessmentNumber.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var list = query.OrderByDescending(a => a.Id).ToList();
        return new AssessmentListResult(list.Count, list);
    }

    public FeeAssessmentDto? GetAssessmentById(int id)
    {
        var root = store.ReadRoot();
        var match = Rows(root, "fee_assessments").FirstOrDefault(a => Int(a, "id") == id);
        return match is null ? null : ToAssessmentDto(root, match);
    }

    /// <summary>The student's newest assessment. The term arguments are not used: pages still pass a fixed term.</summary>
    public FeeAssessmentDto? GetCurrentAssessmentForStudent(int studentId)
    {
        var root = store.ReadRoot();
        var match = Rows(root, "fee_assessments")
            .Where(a => Int(a, "student_id") == studentId)
            .OrderByDescending(a => Int(a, "id"))
            .FirstOrDefault();
        return match is null ? null : ToAssessmentDto(root, match);
    }

    public EnrolleeAssessmentResult AssessEnrollee(EnrolleeAssessmentRequest req)
    {
        EnrolleeAssessmentResult? result = null;
        store.UpdateRoot(root =>
        {
            var user = FindStudent(root, req.StudentId, req.StudentNumber)
                ?? throw new FinanceValidationException("Student not found in the campus records.");
            var studentId = Int(user, "id");

            var enrollment = Rows(root, "enrollments").FirstOrDefault(e =>
                Int(e, "student_id") == studentId
                && Same(Str(e, "school_year"), req.AcademicYear)
                && Same(Str(e, "semester"), req.Semester));

            var assessments = CanonicalJsonStore.GetOrCreateArray(root, "fee_assessments");
            var existing = assessments.OfType<JsonObject>().FirstOrDefault(a =>
            {
                var term = AssessmentTerm(root, a);
                return Int(a, "student_id") == studentId && Same(term.Year, req.AcademicYear) && Same(term.Semester, req.Semester);
            });

            // Registrar creates a flat placeholder row on application. Finance replaces it with the real
            // fees but only once, so a second call never charges the student again.
            if (existing is not null && existing["tuition_fee"] is not null)
            {
                result = new EnrolleeAssessmentResult(true, ToAssessmentDto(root, existing));
                return;
            }

            var units = req.TotalUnits > 0 ? req.TotalUnits : Int(enrollment, "total_units");
            if (units <= 0)
                throw new FinanceValidationException("The enrollment has no units to assess.");

            var program = ProgramOf(root, studentId, req.Program);
            var fees = FeeCalculator.Compute(root, program, req.AcademicYear, units, enrollment is null ? 0 : Int(enrollment, "id"))
                ?? throw new FinanceValidationException($"No tuition fee matrix exists for {program} in {req.AcademicYear}.");
            var tuition = fees.Tuition;
            var misc = fees.Miscellaneous;
            var lab = fees.Laboratory;
            var total = fees.Total;

            JsonObject row;
            if (existing is not null)
            {
                row = existing;
            }
            else
            {
                row = new JsonObject
                {
                    ["id"] = CanonicalJsonStore.NextId(assessments),
                    ["student_id"] = studentId,
                    ["created_at"] = Now()
                };
                assessments.Add(row);
            }

            var paid = Dec(row, "total_paid");
            row["assessment_number"] ??= $"OAO-{DateTime.UtcNow:yyyy}-{Int(row, "id"):D6}";
            if (enrollment is not null) row["enrollment_id"] = Int(enrollment, "id");
            row["school_year"] = req.AcademicYear;
            row["semester"] = req.Semester;
            row["total_units"] = units;
            row["tuition_fee"] = tuition;
            row["miscellaneous_fees"] = misc;
            row["laboratory_fees"] = lab;
            row["discounts_or_scholarship"] ??= 0m;
            row["payment_plan"] ??= "Full Payment";
            FeeCalculator.ApplyTotals(row, total, paid);

            if (Rows(root, "student_clearance").All(c => !IsFinanceRowFor(c, studentId, req.AcademicYear, req.Semester)))
                UpsertFinanceClearance(root, studentId, req.AcademicYear, req.Semester, NotCleared,
                    "Awaiting matriculation settlement upon enrollment validation");

            result = new EnrolleeAssessmentResult(false, ToAssessmentDto(root, row));
        });
        return result!;
    }

    // ── Payments ────────────────────────────────────────────────────────────

    public PaymentListResult GetPayments(PaymentListFilter filter)
    {
        var root = store.ReadRoot();
        var query = TuitionPayments(root).Select(p => ToPaymentRow(root, p));

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            query = query.Where(p => p.StudentDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || p.PaymentNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || p.AssessmentNumber.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var list = query.OrderByDescending(p => p.PaymentDate).ToList();
        return new PaymentListResult(list.Count, list);
    }

    public IReadOnlyList<PaymentHistoryEntryDto> GetPaymentHistoryForStudent(int studentId)
    {
        var root = store.ReadRoot();
        return TuitionPayments(root)
            .Where(p => Int(p, "student_id") == studentId)
            .Select(p => ToPaymentRow(root, p))
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => new PaymentHistoryEntryDto(p.PaymentNumber, p.PaymentDate, p.AmountPaid, p.PaymentMethod, p.ReferenceNumber, p.Status))
            .ToList();
    }

    public PaymentRowDto? GetPaymentByNumber(string paymentNumber)
    {
        var root = store.ReadRoot();
        var match = TuitionPayments(root).FirstOrDefault(p => Same(Str(p, "receipt_no"), paymentNumber));
        return match is null ? null : ToPaymentRow(root, match);
    }

    public PaymentRecordDto RecordPayment(RecordPaymentRequest request)
    {
        PaymentRecordDto? record = null;
        store.UpdateRoot(root =>
        {
            var assessment = Rows(root, "fee_assessments").FirstOrDefault(a => Int(a, "id") == request.FeeAssessmentId)
                ?? throw new FinanceValidationException("Assessment record not found.");

            if (request.AmountPaid <= 0)
                throw new FinanceValidationException("Payment amount must be greater than zero.");

            var total = Dec(assessment, "total_net_assessment");
            var balance = total - Dec(assessment, "total_paid");
            if (request.AmountPaid > balance)
                throw new FinanceValidationException($"Payment exceeds the outstanding balance of ₱{Math.Max(0, balance):N2}.");

            var studentId = Int(assessment, "student_id");
            var payments = CanonicalJsonStore.GetOrCreateArray(root, "student_payments");
            var paymentId = CanonicalJsonStore.NextId(payments);
            var receipt = $"PAY-{DateTime.UtcNow:yyMM}-{paymentId:D4}";

            var installmentId = ApplyToInstallments(root, Int(assessment, "id"), request.AmountPaid);
            payments.Add(new JsonObject
            {
                ["id"] = paymentId,
                ["student_id"] = studentId,
                ["assessment_id"] = Int(assessment, "id"),
                ["installment_id"] = installmentId,
                ["receipt_no"] = receipt,
                ["amount"] = request.AmountPaid,
                ["pay_date"] = Now(),
                ["payment_method"] = request.PaymentMethod,
                ["payment_category"] = "Tuition",
                ["status"] = "Paid",
                ["description"] = $"Tuition payment for assessment {AssessmentNumber(assessment)}",
                ["reference_number"] = request.ReferenceNumber
            });

            var newPaid = Dec(assessment, "total_paid") + request.AmountPaid;
            FeeCalculator.ApplyTotals(assessment, total, newPaid);

            var (year, semester) = AssessmentTerm(root, assessment);
            if (total - newPaid <= 0)
            {
                UpsertFinanceClearance(root, studentId, year, semester, Cleared, "Cleared upon full payment settlement.");
            }
            else
            {
                // A partial payment never downgrades a student who was already cleared.
                var row = Rows(root, "student_clearance").FirstOrDefault(c => IsFinanceRowFor(c, studentId, year, semester));
                if (row is null || !Same(Str(row, "status"), Cleared))
                    UpsertFinanceClearance(root, studentId, year, semester, Conditional,
                        $"Partial payment of ₱{request.AmountPaid:N2} received.");
            }

            record = new PaymentRecordDto(receipt, request.AmountPaid, request.PaymentMethod);
        });
        return record!;
    }

    // ── Clearance ───────────────────────────────────────────────────────────

    public ClearanceSummaryDto GetClearanceSummary()
    {
        var statuses = FinanceClearances(store.ReadRoot()).Select(c => NormalizeClearance(Str(c, "status"))).ToList();
        return new ClearanceSummaryDto(
            statuses.Count,
            statuses.Count(s => s == Cleared),
            statuses.Count(s => s == Conditional),
            statuses.Count(s => s == NotCleared));
    }

    public ClearanceListResult GetClearances(ClearanceListFilter filter)
    {
        var root = store.ReadRoot();
        var query = FinanceClearances(root).Select(c =>
        {
            var studentId = Int(c, "student_id");
            var student = StudentInfo(root, studentId);
            var assessment = Rows(root, "fee_assessments")
                .Where(a => Int(a, "student_id") == studentId)
                .OrderByDescending(a => Int(a, "id"))
                .FirstOrDefault();
            var record = new ClearanceRecordDto(
                Int(c, "id"),
                student.Name,
                student.Program,
                assessment is null ? null : Dec(assessment, "total_net_assessment"),
                assessment is null ? null : BalanceOf(assessment),
                NormalizeClearance(Str(c, "status")),
                ParseDate(c, "cleared_at"));
            return (Record: record, student.Number);
        });

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(c => c.Record.Status.Equals(filter.Status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var term = filter.SearchText.Trim();
            query = query.Where(c => c.Record.StudentDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                     || c.Number.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var list = query.Select(c => c.Record).ToList();
        return new ClearanceListResult(list.Count, list);
    }

    public void SetConditional(int clearanceId, string remarks)
    {
        store.UpdateRoot(root =>
        {
            var row = Rows(root, "student_clearance").FirstOrDefault(c => Int(c, "id") == clearanceId && IsFinance(c))
                ?? throw new FinanceValidationException("Finance clearance record not found.");
            row["status"] = Conditional;
            row["remarks"] = remarks;
            row["cleared_by"] = FinanceDepartment;
            row["cleared_at"] = null;
        });
    }

    public IReadOnlyList<StudentClearanceDto> GetStudentClearances(IReadOnlyCollection<int> studentIds)
    {
        return FinanceClearances(store.ReadRoot())
            .Where(c => studentIds.Contains(Int(c, "student_id")))
            .GroupBy(c => Int(c, "student_id"))
            .Select(g => g.OrderBy(c => Int(c, "id")).Last())
            .Select(c => new StudentClearanceDto(Int(c, "student_id"), NormalizeClearance(Str(c, "status"))))
            .ToList();
    }

    public StudentClearanceDto? GetStudentClearance(int studentId) =>
        GetStudentClearances([studentId]).FirstOrDefault();

    /// <summary>Marks every fully settled assessment Cleared, creating the Finance row when none exists yet.</summary>
    public int BatchIssueClearance()
    {
        var count = 0;
        store.UpdateRoot(root =>
        {
            foreach (var assessment in Rows(root, "fee_assessments").ToList())
            {
                if (Dec(assessment, "total_net_assessment") <= 0 || BalanceOf(assessment) > 0) continue;

                var studentId = Int(assessment, "student_id");
                var (year, semester) = AssessmentTerm(root, assessment);
                var row = Rows(root, "student_clearance").FirstOrDefault(c => IsFinanceRowFor(c, studentId, year, semester));
                if (row is not null && Same(Str(row, "status"), Cleared)) continue;

                UpsertFinanceClearance(root, studentId, year, semester, Cleared, "Batch clearance issued — balance fully settled.");
                count++;
            }
        });
        return count;
    }

    // ── Document request fees (transcript_requests) ─────────────────────────

    public IReadOnlyList<StudentRequestViewDto> GetRequestsForStudent(int studentId) =>
        Rows(store.ReadRoot(), "transcript_requests")
            .Where(r => Int(r, "student_id") == studentId)
            .OrderByDescending(r => Int(r, "id"))
            .Select(r => new StudentRequestViewDto(
                Int(r, "id"),
                Str(r, "tracking_token"),
                Str(r, "document_type"),
                Dec(r, "fee_amount"),
                Str(r, "processing_status"),
                Same(Str(r, "payment_status"), "Paid") ? [new StudentRequestPaymentDto("Paid")] : []))
            .ToList();

    public RequestCreateResultDto CreateRequest(CreateRequestRequest request)
    {
        RequestCreateResultDto? created = null;
        store.UpdateRoot(root =>
        {
            if (FindStudent(root, request.StudentId, null) is null)
                throw new FinanceValidationException("Student not found in the campus records.");
            var docType = DocumentFeeTypes.FirstOrDefault(d => d.Id == request.DocumentFeeTypeId)
                ?? throw new FinanceValidationException("Select a valid document type.");
            if (request.Copies is < 1 or > 10)
                throw new FinanceValidationException("Copies must be between 1 and 10.");

            var requests = CanonicalJsonStore.GetArray(root, "transcript_requests");
            var id = CanonicalJsonStore.NextId(requests);
            var token = $"REQ-{DateTime.UtcNow:yyMMdd}-{id:D3}";
            requests.Add(new JsonObject
            {
                ["id"] = id,
                ["student_id"] = request.StudentId,
                ["tracking_token"] = token,
                ["document_type"] = docType.Name,
                ["purpose"] = request.Purpose,
                ["copies"] = request.Copies,
                ["fee_amount"] = docType.Amount * request.Copies + BaseProcessingFee,
                ["payment_status"] = "Unpaid",
                ["payment_receipt_no"] = null,
                ["processing_status"] = "Pending",
                ["request_date"] = Now(),
                ["released_date"] = null
            });
            created = new RequestCreateResultDto(id, token);
        });
        return created!;
    }

    public void PayRequestFee(int requestId, string paymentMethod)
    {
        store.UpdateRoot(root =>
        {
            var row = Rows(root, "transcript_requests").FirstOrDefault(r => Int(r, "id") == requestId)
                ?? throw new FinanceValidationException("Document request not found.");
            if (Same(Str(row, "payment_status"), "Paid"))
                throw new FinanceValidationException("This request has already been paid.");

            row["payment_status"] = "Paid";
            row["payment_receipt_no"] = $"OR-DOC-{DateTime.UtcNow:yyyy}-{requestId:D4}";
            row["payment_method"] = paymentMethod;
        });
    }

    // ── Scholarships ────────────────────────────────────────────────────────

    public IReadOnlyList<ScholarshipProgramDto> GetScholarshipPrograms()
    {
        var root = store.ReadRoot();
        var applications = Rows(root, "scholarship_applications").Where(IsActiveGrant).ToList();
        return Rows(root, "scholarships")
            .Select(s => new ScholarshipProgramDto(
                Str(s, "code"),
                Str(s, "name"),
                Str(s, "coverage"),
                applications.Count(a => Int(a, "scholarship_id") == Int(s, "id")),
                Dec(s, "budget")))
            .ToList();
    }

    public IReadOnlyList<ScholarshipGranteeDto> GetScholarshipGrantees()
    {
        var root = store.ReadRoot();
        var scholarships = Rows(root, "scholarships").ToList();
        return Rows(root, "scholarship_applications")
            .OrderByDescending(a => Int(a, "id"))
            .Select(a =>
            {
                var student = StudentInfo(root, Int(a, "student_id"));
                var scholarship = scholarships.FirstOrDefault(s => Int(s, "id") == Int(a, "scholarship_id"));
                return new ScholarshipGranteeDto(
                    student.Name, student.Number, student.Program,
                    scholarship is null ? "Unknown scholarship" : Str(scholarship, "name"),
                    Dec(a, "discount_amount"),
                    Str(a, "status"));
            })
            .ToList();
    }

    public void GrantScholarship(string studentNumber, string scholarshipName, decimal discountAmount)
    {
        store.UpdateRoot(root =>
        {
            var user = FindStudent(root, 0, studentNumber)
                ?? throw new FinanceValidationException($"No student with number {studentNumber.Trim()} exists.");
            var scholarship = Rows(root, "scholarships").FirstOrDefault(s => Same(Str(s, "name"), scholarshipName))
                ?? throw new FinanceValidationException("Select a scholarship program.");
            if (discountAmount <= 0)
                throw new FinanceValidationException("The discount must be greater than zero.");

            var studentId = Int(user, "id");
            var applications = CanonicalJsonStore.GetOrCreateArray(root, "scholarship_applications");
            if (applications.OfType<JsonObject>().Any(a => IsActiveGrant(a)
                    && Int(a, "student_id") == studentId && Int(a, "scholarship_id") == Int(scholarship, "id")))
                throw new FinanceValidationException("This student already holds that scholarship.");

            applications.Add(new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(applications),
                ["student_id"] = studentId,
                ["scholarship_id"] = Int(scholarship, "id"),
                ["discount_amount"] = discountAmount,
                ["status"] = "Active",
                ["granted_at"] = Now()
            });
        });
    }

    // ── Class roster (read-only, from Registrar's enrollment data) ──────────

    public ClassRosterDto? GetRoster(string sectionCode)
    {
        var root = store.ReadRoot();
        var offering = Rows(root, "class_offerings").FirstOrDefault(o => Same(Str(o, "section_code"), sectionCode));
        if (offering is null) return null;

        var subject = Rows(root, "subjects").FirstOrDefault(s => Int(s, "id") == Int(offering, "subject_id"));
        var label = $"{Str(offering, "section_code")} · {(subject is null ? "" : Str(subject, "subject_code") + " · ")}"
                    + $"{Str(offering, "days_of_week")} {Short(Str(offering, "start_time"))}–{Short(Str(offering, "end_time"))} · {Str(offering, "room")}";

        var students = Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "class_offering_id") == Int(offering, "id") && !Same(Str(e, "status"), "dropped"))
            .Select(e => Int(e, "student_id"))
            .Distinct()
            .Select(id => StudentInfo(root, id))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new ClassRosterStudentDto(s.Id, s.Name, s.Number))
            .ToList();

        return new ClassRosterDto(label, students);
    }

    // ── Row → DTO ───────────────────────────────────────────────────────────

    private static FeeAssessmentDto ToAssessmentDto(JsonObject root, JsonObject a)
    {
        var studentId = Int(a, "student_id");
        var student = StudentInfo(root, studentId);
        var total = Dec(a, "total_net_assessment");
        var paid = Dec(a, "total_paid");

        var parts = new List<(string Name, decimal Amount)>();
        AddPart(parts, $"Tuition Fee ({Int(a, "total_units")} units)", Dec(a, "tuition_fee"));
        AddPart(parts, "Miscellaneous Fees", Dec(a, "miscellaneous_fees"));
        AddPart(parts, "Laboratory Fees", Dec(a, "laboratory_fees"));
        if (parts.Count == 0) AddPart(parts, "Assessed Fees", total);

        // Payments are recorded against the assessment as a whole, so spread them over the lines in order.
        var unapplied = paid;
        var items = parts.Select(p =>
        {
            var applied = Math.Min(p.Amount, unapplied);
            unapplied -= applied;
            return new FeeAssessmentItemDto(p.Name, p.Amount, applied);
        }).ToList();

        var status = total > 0 && paid >= total ? "Paid" : paid > 0 ? "Partial" : "Unpaid";
        var assessed = ParseDate(a, "created_at")
            ?? ParseDate(Rows(root, "enrollments").FirstOrDefault(e => Int(e, "id") == Int(a, "enrollment_id")), "created_at")
            ?? default;

        return new FeeAssessmentDto(
            Int(a, "id"), AssessmentNumber(a), student.Name, student.Number, student.Program,
            AssessmentTerm(root, a).Year, AssessmentTerm(root, a).Semester,
            total, paid, BalanceOf(a), status, assessed, items);
    }

    private static PaymentRowDto ToPaymentRow(JsonObject root, JsonObject p)
    {
        var student = StudentInfo(root, Int(p, "student_id"));
        var assessment = Rows(root, "fee_assessments").FirstOrDefault(a => Int(a, "id") == Int(p, "assessment_id"));
        return new PaymentRowDto(
            Str(p, "receipt_no"),
            ParseDate(p, "pay_date") ?? default,
            Dec(p, "amount"),
            Str(p, "payment_method"),
            NullIfEmpty(Str(p, "reference_number")) ?? NullIfEmpty(Str(p, "verification_token")),
            Same(Str(p, "status"), "Paid") ? "Success" : Str(p, "status"),
            student.Name,
            student.Program,
            assessment is null ? 0 : Int(assessment, "id"),
            assessment is null ? "" : AssessmentNumber(assessment));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        CanonicalJsonStore.GetArray(root, collection).OfType<JsonObject>();

    private static IEnumerable<JsonObject> TuitionPayments(JsonObject root) =>
        Rows(root, "student_payments").Where(p => !Same(Str(p, "payment_category"), "Library Fine"));

    private static IEnumerable<JsonObject> FinanceClearances(JsonObject root) =>
        Rows(root, "student_clearance").Where(IsFinance);

    private static bool IsFinance(JsonObject c) =>
        Str(c, "department_name").Contains("Finance", StringComparison.OrdinalIgnoreCase);

    private static bool IsFinanceRowFor(JsonObject c, int studentId, string year, string semester) =>
        IsFinance(c) && Int(c, "student_id") == studentId
        && Same(Str(c, "school_year"), year) && Same(Str(c, "semester"), semester);

    private static bool IsActiveGrant(JsonObject a) => Same(Str(a, "status"), "Active");

    private static string NormalizeClearance(string status) =>
        Same(status, Cleared) ? Cleared : Same(status, Conditional) ? Conditional : NotCleared;

    private static JsonObject UpsertFinanceClearance(JsonObject root, int studentId, string year, string semester, string status, string remarks)
    {
        var rows = CanonicalJsonStore.GetArray(root, "student_clearance");
        var row = rows.OfType<JsonObject>().FirstOrDefault(c => IsFinanceRowFor(c, studentId, year, semester));
        if (row is null)
        {
            row = new JsonObject
            {
                ["id"] = CanonicalJsonStore.NextId(rows),
                ["student_id"] = studentId,
                ["department_name"] = FinanceDepartment,
                ["school_year"] = year,
                ["semester"] = semester
            };
            rows.Add(row);
        }

        row["status"] = status;
        row["remarks"] = remarks;
        row["cleared_by"] = FinanceDepartment;
        row["cleared_at"] = status == Cleared ? Now() : null;
        return row;
    }

    /// <summary>Spreads a payment over the assessment's installments by due date. Returns the first one touched.</summary>
    private static int? ApplyToInstallments(JsonObject root, int assessmentId, decimal amount)
    {
        int? first = null;
        var installments = Rows(root, "installment_schedules")
            .Where(i => Int(i, "assessment_id") == assessmentId)
            .OrderBy(i => Str(i, "due_date"), StringComparer.Ordinal);

        foreach (var installment in installments)
        {
            if (amount <= 0) break;
            var open = Dec(installment, "amount_due") - Dec(installment, "amount_paid");
            if (open <= 0) continue;

            var applied = Math.Min(open, amount);
            amount -= applied;
            installment["amount_paid"] = Dec(installment, "amount_paid") + applied;
            var settled = applied >= open;
            installment["status"] = settled ? "Paid" : "Partial";
            if (settled) installment["cleared_for_exam"] = true;
            first ??= Int(installment, "id");
        }
        return first;
    }

    private static decimal BalanceOf(JsonObject assessment) =>
        assessment["remaining_balance"] is null
            ? Math.Max(0m, Dec(assessment, "total_net_assessment") - Dec(assessment, "total_paid"))
            : Dec(assessment, "remaining_balance");

    private static string AssessmentNumber(JsonObject a) =>
        NullIfEmpty(Str(a, "assessment_number")) ?? $"OAO-{Int(a, "id"):D6}";

    /// <summary>The term of an assessment, upper-cased for comparison where needed. Registrar's rows carry it on the enrollment.</summary>
    private static (string Year, string Semester) AssessmentTerm(JsonObject root, JsonObject a)
    {
        var year = Str(a, "school_year");
        var semester = Str(a, "semester");
        if (year.Length == 0 || semester.Length == 0)
        {
            var enrollment = Rows(root, "enrollments").FirstOrDefault(e => Int(e, "id") == Int(a, "enrollment_id"));
            if (enrollment is not null)
            {
                if (year.Length == 0) year = Str(enrollment, "school_year");
                if (semester.Length == 0) semester = Str(enrollment, "semester");
            }
        }
        return (year, semester);
    }

    private static string ProgramOf(JsonObject root, int studentId, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        var profile = Rows(root, "student_profiles").FirstOrDefault(p => Int(p, "user_id") == studentId);
        return profile is null ? "" : Str(profile, "program");
    }

    private static JsonObject? FindStudent(JsonObject root, int studentId, string? studentNumber) =>
        Rows(root, "users").FirstOrDefault(u =>
            Same(Str(u, "role"), "student")
            && (studentId > 0 ? Int(u, "id") == studentId : Same(Str(u, "student_id_number"), studentNumber?.Trim())));

    private sealed record Student(int Id, string Name, string Number, string Program);

    private static Student StudentInfo(JsonObject root, int studentId)
    {
        var user = Rows(root, "users").FirstOrDefault(u => Int(u, "id") == studentId);
        return new Student(studentId, user is null ? "Unknown student" : Str(user, "name"),
            user is null ? "" : Str(user, "student_id_number"), ProgramOf(root, studentId, null));
    }

    private static void AddPart(List<(string, decimal)> parts, string name, decimal amount)
    {
        if (amount > 0) parts.Add((name, amount));
    }

    private static string Short(string time) => time.Length >= 5 ? time[..5] : time;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    // Nodes read from disk hold JsonElements, but nodes built in this process hold the C# type
    // they were created with, so each reader accepts every numeric type.
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

    private static DateTime? ParseDate(JsonObject? o, string key) =>
        DateTimeOffset.TryParse(Str(o, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime
            : null;
}
