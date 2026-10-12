using System.Globalization;
using System.Text.Json.Nodes;

namespace CampusSystem.Data.Json;

/// <summary>
/// The fee rules, in one place so Finance (assessing an enrollee) and Registrar (approving an add/drop or changing a
/// registration) always agree. Every method works on the root inside <see cref="CanonicalJsonStore.UpdateRoot"/>.
/// </summary>
public static class FeeCalculator
{
    public sealed record Fees(decimal Units, decimal Tuition, decimal Miscellaneous, decimal Laboratory)
    {
        public decimal Total => Tuition + Miscellaneous + Laboratory;
    }

    public sealed record Adjustment(int AssessmentId, decimal OldTotal, decimal NewTotal)
    {
        public decimal Delta => NewTotal - OldTotal;
    }

    /// <summary>Units counted for fees: every subject on the enrollment that has not been dropped.</summary>
    public static decimal CountUnits(JsonObject root, int enrollmentId) =>
        Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "enrollment_id") == enrollmentId && !Same(Str(e, "status"), "dropped"))
            .Sum(e => Dec(e, "units"));

    /// <summary>
    /// Fees for the given units from <c>tuition_fee_matrix</c> (the program's newest year if the exact one is missing)
    /// plus laboratory fees of the enrollment's subjects. Null when no matrix row exists for the program.
    /// </summary>
    public static Fees? Compute(JsonObject root, string program, string academicYear, decimal units, int enrollmentId)
    {
        var matrix = FindMatrix(root, program, academicYear);
        if (matrix is null) return null;

        var subjects = Rows(root, "subjects").ToList();
        var lab = Rows(root, "enrolled_subjects")
            .Where(e => Int(e, "enrollment_id") == enrollmentId && !Same(Str(e, "status"), "dropped"))
            .Sum(e => subjects.FirstOrDefault(s => Str(s, "subject_code") == Str(e, "subject_code")) is { } subject
                      && subject["has_lab"] is JsonValue flag && flag.TryGetValue<bool>(out var hasLab) && hasLab
                ? Dec(subject, "lab_fee_amount")
                : 0m);

        return new Fees(
            units,
            Math.Round(units * Dec(matrix, "tuition_per_unit"), 2),
            Dec(matrix, "base_miscellaneous_fee") + Dec(matrix, "energy_it_fee") + Dec(matrix, "library_fee") + Dec(matrix, "registration_fee"),
            lab);
    }

    /// <summary>Matches "BS Information Technology" to matrix code "BSIT".</summary>
    public static JsonObject? FindMatrix(JsonObject root, string program, string academicYear)
    {
        var code = "BS" + string.Concat(program.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(w => char.ToUpperInvariant(w[0])));
        var forProgram = Rows(root, "tuition_fee_matrix")
            .Where(m => Same(Str(m, "program_code"), code) || Same(Str(m, "program_code"), program))
            .ToList();
        return forProgram.FirstOrDefault(m => Same(Str(m, "academic_year"), academicYear))
               ?? forProgram.OrderByDescending(m => Str(m, "academic_year"), StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Sets the totals on an assessment row. Anything paid beyond the total is kept as <c>credit_balance</c>.</summary>
    public static void ApplyTotals(JsonObject assessment, decimal total, decimal paid)
    {
        assessment["total_net_assessment"] = total;
        assessment["total_paid"] = paid;
        assessment["remaining_balance"] = Math.Max(0m, total - paid);
        assessment["credit_balance"] = Math.Max(0m, paid - total);
        // Same vocabulary as Registrar ("Pending") and Student Portal ("Partially Paid").
        assessment["status"] = total > 0 && paid >= total ? "Paid" : paid > 0 ? "Partially Paid" : "Pending";
    }

    /// <summary>
    /// Brings an enrollment's units and fees in line with its current subjects after an add, drop or change.
    /// Always updates the enrollment's <c>total_units</c>. When Finance has already assessed the enrollment
    /// (the row has <c>tuition_fee</c>), it also recomputes the assessment, spreads the difference over the unpaid
    /// installments and records a <c>fee_adjustments</c> row. A placeholder assessment is left for Finance to assess.
    /// Returns the adjustment, or null when nothing was adjusted or the total did not change.
    /// </summary>
    public static Adjustment? Recalculate(JsonObject root, int enrollmentId, string reason)
    {
        var enrollment = Rows(root, "enrollments").FirstOrDefault(e => Int(e, "id") == enrollmentId);
        if (enrollment is null) return null;

        var units = CountUnits(root, enrollmentId);
        enrollment["total_units"] = units;

        var assessment = Rows(root, "fee_assessments").FirstOrDefault(a => Int(a, "enrollment_id") == enrollmentId);
        if (assessment is null || assessment["tuition_fee"] is null) return null;

        var studentId = Int(assessment, "student_id");
        var profile = Rows(root, "student_profiles").FirstOrDefault(p => Int(p, "user_id") == studentId);
        var year = Str(assessment, "school_year") is { Length: > 0 } y ? y : Str(enrollment, "school_year");
        var fees = Compute(root, Str(profile, "program"), year, units, enrollmentId);
        if (fees is null) return null;

        var oldTotal = Dec(assessment, "total_net_assessment");
        var newTotal = fees.Total - Dec(assessment, "discounts_or_scholarship");
        if (newTotal == oldTotal && Dec(assessment, "tuition_fee") == fees.Tuition) return null;

        assessment["total_units"] = units;
        assessment["tuition_fee"] = fees.Tuition;
        assessment["miscellaneous_fees"] = fees.Miscellaneous;
        assessment["laboratory_fees"] = fees.Laboratory;
        ApplyTotals(assessment, newTotal, Dec(assessment, "total_paid"));
        SpreadOverInstallments(root, Int(assessment, "id"), studentId, newTotal - oldTotal);

        var adjustments = CanonicalJsonStore.GetOrCreateArray(root, "fee_adjustments");
        adjustments.Add(new JsonObject
        {
            ["id"] = CanonicalJsonStore.NextId(adjustments),
            ["assessment_id"] = Int(assessment, "id"),
            ["enrollment_id"] = enrollmentId,
            ["student_id"] = studentId,
            ["reason"] = reason,
            ["old_total"] = oldTotal,
            ["new_total"] = newTotal,
            ["delta"] = newTotal - oldTotal,
            ["created_at"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
        return new Adjustment(Int(assessment, "id"), oldTotal, newTotal);
    }

    /// <summary>
    /// A higher total goes on the last unpaid installment (or a new "Adjustment" installment if all are paid).
    /// A lower total comes off the unpaid installments from the end, never below what is already paid on each.
    /// </summary>
    private static void SpreadOverInstallments(JsonObject root, int assessmentId, int studentId, decimal delta)
    {
        if (delta == 0) return;
        var installments = CanonicalJsonStore.GetOrCreateArray(root, "installment_schedules");
        var mine = installments.OfType<JsonObject>()
            .Where(i => Int(i, "assessment_id") == assessmentId)
            .OrderBy(i => Str(i, "due_date"), StringComparer.Ordinal)
            .ToList();
        if (mine.Count == 0) return;

        if (delta > 0)
        {
            var target = mine.LastOrDefault(i => Dec(i, "amount_paid") < Dec(i, "amount_due"));
            if (target is null)
            {
                installments.Add(new JsonObject
                {
                    ["id"] = CanonicalJsonStore.NextId(installments),
                    ["assessment_id"] = assessmentId,
                    ["student_id"] = studentId,
                    ["term_type"] = "Adjustment",
                    ["due_date"] = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["amount_due"] = delta,
                    ["amount_paid"] = 0m,
                    ["status"] = "Pending",
                    ["cleared_for_exam"] = false
                });
                return;
            }
            target["amount_due"] = Dec(target, "amount_due") + delta;
            target["status"] = "Pending";
            target["cleared_for_exam"] = false;
            return;
        }

        var toRemove = -delta;
        for (var index = mine.Count - 1; index >= 0 && toRemove > 0; index--)
        {
            var installment = mine[index];
            var due = Dec(installment, "amount_due");
            var room = due - Dec(installment, "amount_paid");
            if (room <= 0) continue;

            var cut = Math.Min(room, toRemove);
            installment["amount_due"] = due - cut;
            toRemove -= cut;
            if (Dec(installment, "amount_paid") >= Dec(installment, "amount_due"))
            {
                installment["status"] = "Paid";
                installment["cleared_for_exam"] = true;
            }
        }
    }

    // A missing collection counts as empty so the calculator works on a file that predates it.
    private static IEnumerable<JsonObject> Rows(JsonObject root, string collection) =>
        (root["unified_seed_data"]?[collection] as JsonArray ?? new JsonArray()).OfType<JsonObject>();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
