using System.Text.Json.Nodes;
using CampusSystem.Data.Json;

namespace CampusSystem.Data.Tests;

public sealed class FeeCalculatorTests : IDisposable
{
    // Student 5 is assessed for 9 units (3 subjects, 2 with labs) and has paid 10,000 of 28,100.
    private const string Fixture = """
    {
      "system": { "schema_version": 1 },
      "unified_seed_data": {
        "student_profiles": [ { "user_id": 5, "program": "BS Information Technology" } ],
        "subjects": [
          { "id": 101, "subject_code": "IT311", "units": 3, "has_lab": true, "lab_fee_amount": 1800 },
          { "id": 102, "subject_code": "IT312", "units": 3, "has_lab": true, "lab_fee_amount": 1800 },
          { "id": 103, "subject_code": "IT313", "units": 3, "has_lab": false, "lab_fee_amount": 0 },
          { "id": 104, "subject_code": "IT314", "units": 3, "has_lab": false, "lab_fee_amount": 0 }
        ],
        "tuition_fee_matrix": [
          { "id": 1, "program_code": "BSIT", "academic_year": "2025-2026", "tuition_per_unit": 1500, "base_miscellaneous_fee": 6500, "energy_it_fee": 2500, "library_fee": 1200, "registration_fee": 800 }
        ],
        "enrollments": [ { "id": 1001, "student_id": 5, "school_year": "2025-2026", "semester": "1st Semester", "total_units": 9 } ],
        "enrolled_subjects": [
          { "id": 2001, "enrollment_id": 1001, "student_id": 5, "subject_code": "IT311", "units": 3, "status": "enrolled" },
          { "id": 2002, "enrollment_id": 1001, "student_id": 5, "subject_code": "IT312", "units": 3, "status": "enrolled" },
          { "id": 2003, "enrollment_id": 1001, "student_id": 5, "subject_code": "IT313", "units": 3, "status": "enrolled" }
        ],
        "fee_assessments": [
          { "id": 8001, "enrollment_id": 1001, "student_id": 5, "school_year": "2025-2026", "total_units": 9, "tuition_fee": 13500, "miscellaneous_fees": 11000,
            "laboratory_fees": 3600, "discounts_or_scholarship": 0, "total_net_assessment": 28100, "total_paid": 10000, "remaining_balance": 18100, "status": "Partially Paid" }
        ],
        "installment_schedules": [
          { "id": 9001, "assessment_id": 8001, "student_id": 5, "due_date": "2025-08-15", "amount_due": 10000, "amount_paid": 10000, "status": "Paid", "cleared_for_exam": true },
          { "id": 9002, "assessment_id": 8001, "student_id": 5, "due_date": "2025-10-15", "amount_due": 9050, "amount_paid": 0, "status": "Pending", "cleared_for_exam": false },
          { "id": 9003, "assessment_id": 8001, "student_id": 5, "due_date": "2025-12-10", "amount_due": 9050, "amount_paid": 0, "status": "Pending", "cleared_for_exam": false }
        ],
        "fee_adjustments": []
      }
    }
    """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FeeCalcTests", Guid.NewGuid().ToString("N"));
    private readonly CanonicalJsonStore _store;

    public FeeCalculatorTests()
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

    private JsonObject Row(string collection, int id) =>
        CanonicalJsonStore.GetArray(_store.ReadRoot(), collection).OfType<JsonObject>().First(r => r["id"]!.GetValue<int>() == id);

    private decimal Due(int installmentId) => Row("installment_schedules", installmentId)["amount_due"]!.GetValue<decimal>();

    private void AddSubject(string code, decimal units) =>
        _store.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "enrolled_subjects").Add(new JsonObject
        {
            ["id"] = 2004, ["enrollment_id"] = 1001, ["student_id"] = 5, ["subject_code"] = code, ["units"] = units, ["status"] = "enrolled"
        }));

    private void Drop(int enrolledSubjectId) =>
        _store.UpdateRoot(root => CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>()
            .First(e => e["id"]!.GetValue<int>() == enrolledSubjectId)["status"] = "dropped");

    private FeeCalculator.Adjustment? Recalculate(string reason)
    {
        FeeCalculator.Adjustment? result = null;
        _store.UpdateRoot(root => result = FeeCalculator.Recalculate(root, 1001, reason));
        return result;
    }

    [Fact]
    public void Recalculate_WithNoChange_AdjustsNothing()
    {
        Assert.Null(Recalculate("check"));

        Assert.Equal(28100m, Row("fee_assessments", 8001)["total_net_assessment"]!.GetValue<decimal>());
        Assert.Empty(CanonicalJsonStore.GetArray(_store.ReadRoot(), "fee_adjustments"));
    }

    [Fact]
    public void Adding_A_Subject_RaisesFees_AndTheLastUnpaidInstallment()
    {
        AddSubject("IT314", 3);
        var result = Recalculate("Added IT314");

        // 12 units x 1500 = 18000 + 11000 misc + 3600 lab = 32600
        Assert.Equal(4500m, result!.Delta);
        var assessment = Row("fee_assessments", 8001);
        Assert.Equal(32600m, assessment["total_net_assessment"]!.GetValue<decimal>());
        Assert.Equal(22600m, assessment["remaining_balance"]!.GetValue<decimal>());
        Assert.Equal(12m, assessment["total_units"]!.GetValue<decimal>());
        Assert.Equal(12m, Row("enrollments", 1001)["total_units"]!.GetValue<decimal>());
        Assert.Equal(9050m, Due(9002));
        Assert.Equal(13550m, Due(9003));
        var log = Row("fee_adjustments", 1);
        Assert.Equal((28100m, 32600m, "Added IT314"), (log["old_total"]!.GetValue<decimal>(), log["new_total"]!.GetValue<decimal>(), log["reason"]!.GetValue<string>()));
    }

    [Fact]
    public void Dropping_A_LabSubject_LowersFees_IncludingItsLabFee()
    {
        Drop(2001);
        var result = Recalculate("Dropped IT311");

        // 6 units x 1500 = 9000 + 11000 + 1800 lab = 21800
        Assert.Equal(-6300m, result!.Delta);
        var assessment = Row("fee_assessments", 8001);
        Assert.Equal(21800m, assessment["total_net_assessment"]!.GetValue<decimal>());
        Assert.Equal(11800m, assessment["remaining_balance"]!.GetValue<decimal>());
        Assert.Equal("Partially Paid", assessment["status"]!.GetValue<string>());
        Assert.Equal(21800m, Due(9001) + Due(9002) + Due(9003));
        Assert.All(new[] { 9001, 9002, 9003 }, id => Assert.True(Due(id) >= 0));
        Assert.Equal(10000m, Due(9001)); // what was already paid is never reduced
    }

    [Fact]
    public void Dropping_Below_What_Was_Paid_KeepsACredit_NotANegativeBalance()
    {
        Drop(2001); Drop(2002); Drop(2003);
        Recalculate("Dropped everything");

        // Only the 11000 miscellaneous fees remain, so 10000 paid still leaves 1000 outstanding.
        var assessment = Row("fee_assessments", 8001);
        Assert.Equal(11000m, assessment["total_net_assessment"]!.GetValue<decimal>());
        Assert.Equal(1000m, assessment["remaining_balance"]!.GetValue<decimal>());
        Assert.Equal(0m, assessment["credit_balance"]!.GetValue<decimal>());

        // A student who has paid more than the new total has a credit instead.
        _store.UpdateRoot(root => FeeCalculator.ApplyTotals(
            CanonicalJsonStore.GetArray(root, "fee_assessments").OfType<JsonObject>().First(), 9000m, 10000m));
        var credited = Row("fee_assessments", 8001);
        Assert.Equal(0m, credited["remaining_balance"]!.GetValue<decimal>());
        Assert.Equal(1000m, credited["credit_balance"]!.GetValue<decimal>());
        Assert.Equal("Paid", credited["status"]!.GetValue<string>());
    }

    [Fact]
    public void A_Fully_Paid_Assessment_Gets_An_Adjustment_Installment_When_Fees_Rise()
    {
        _store.UpdateRoot(root =>
        {
            foreach (var installment in CanonicalJsonStore.GetArray(root, "installment_schedules").OfType<JsonObject>())
            {
                installment["amount_paid"] = installment["amount_due"]!.DeepClone();
                installment["status"] = "Paid";
            }
        });
        AddSubject("IT314", 3);
        Recalculate("Added IT314");

        var adjustment = CanonicalJsonStore.GetArray(_store.ReadRoot(), "installment_schedules").OfType<JsonObject>().Last();
        Assert.Equal("Adjustment", adjustment["term_type"]!.GetValue<string>());
        Assert.Equal(4500m, adjustment["amount_due"]!.GetValue<decimal>());
        Assert.Equal("Pending", adjustment["status"]!.GetValue<string>());
    }

    [Fact]
    public void A_Placeholder_Assessment_Is_Left_For_Finance_But_Units_Are_Updated()
    {
        _store.UpdateRoot(root =>
        {
            var assessment = CanonicalJsonStore.GetArray(root, "fee_assessments").OfType<JsonObject>().First();
            assessment.Remove("tuition_fee");
            assessment["total_net_assessment"] = 10000;
        });
        AddSubject("IT314", 3);

        Assert.Null(Recalculate("Added IT314"));
        Assert.Equal(12m, Row("enrollments", 1001)["total_units"]!.GetValue<decimal>());
        Assert.Equal(10000m, Row("fee_assessments", 8001)["total_net_assessment"]!.GetValue<decimal>());
    }

    [Fact]
    public void Compute_ReturnsNull_ForAProgramWithNoMatrix_AndFallsBackToTheNewestYear()
    {
        var root = _store.ReadRoot();

        Assert.Null(FeeCalculator.Compute(root, "BS Nursing", "2025-2026", 9, 1001));
        Assert.Equal(28100m, FeeCalculator.Compute(root, "BS Information Technology", "2030-2031", 9, 1001)!.Total);
        Assert.Equal(9m, FeeCalculator.CountUnits(root, 1001));
    }
}
