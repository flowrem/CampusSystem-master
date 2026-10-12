using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampusSystem.Data.Json;
using StudentPortalMain.Models;

namespace StudentPortalMain.Services;

public enum PasswordChangeResult
{
    Changed,
    Ineligible,
    InvalidCurrentPassword,
    Failed
}

public class StudentPortalDbService
{
    private readonly IHttpContextAccessor _ctx;
    private readonly ILogger<StudentPortalDbService>? _logger;
    private readonly CanonicalJsonStore _jsonStore;
    private static readonly object _jsonLock = new();

    public StudentPortalDbService(IHttpContextAccessor ctx, CanonicalJsonStore jsonStore, ILogger<StudentPortalDbService>? logger = null)
    {
        _ctx = ctx;
        _jsonStore = jsonStore;
        _logger = logger;
    }

    private ISession? Session => _ctx.HttpContext?.Session;

    public static readonly string[] VaultDocumentTypes = [
        "Form 137",
        "Form 138",
        "Birth Certificate",
        "Good Moral",
        "Transcript from Previous School",
        "Medical Clearance"
    ];

    // ─── JSON File Locator & Loader ──────────────────────────────────────────

        // ─── JSON File Loader ────────────────────────────────────────────────────
        // Removed obsolete JSON file locator method.

    private JsonElement? GetSeedDataElement(string tableName)
    {
        lock (_jsonLock)
        {
            try
            {
                using var doc = JsonDocument.Parse(_jsonStore.ReadRoot().ToJsonString());
                if (doc.RootElement.TryGetProperty("unified_seed_data", out var seedData) &&
                    seedData.TryGetProperty(tableName, out var tableElement))
                    return tableElement.Clone();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to parse JSON seed data: {Msg}", ex.Message);
            }
            return null;
        }
    }

    // ─── Current Session Helpers ─────────────────────────────────────────────

    /// <summary>Returns the authenticated student's ID from the session (0 if not signed in).</summary>
    public int CurrentStudentId
    {
        get => Session?.GetInt32("student_user_id") ?? 0;
        set => Session?.SetInt32("student_user_id", value);
    }

    public bool IsAuthenticated => (Session?.GetInt32("is_authenticated") ?? 0) == 1;

    /// <summary>Signs a student in through the shared AuthStore (BCrypt check, lockout after five wrong passwords).</summary>
    public Task<StudentUser?> AuthenticateStudentAsync(string identifier, string password)
    {
        var result = new CampusSystem.Data.Auth.AuthStore(_jsonStore).Login(identifier, password, ["student"]);
        LastLoginError = result.Error;
        return Task.FromResult<StudentUser?>(result.User is null ? null : new StudentUser
        {
            Id = result.User.Id,
            StudentIdNumber = result.User.IdNumber,
            Name = result.User.Name,
            Email = result.User.Email,
            Role = "student",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>Why the last sign-in failed, so the login page can show a lockout message.</summary>
    public string? LastLoginError { get; private set; }

    private static JsonObject? FindStudent(JsonObject root, int studentId) =>
        CanonicalJsonStore.GetArray(root, "users").OfType<JsonObject>().FirstOrDefault(user =>
            user["id"]?.GetValue<int>() == studentId &&
            string.Equals(user["role"]?.GetValue<string>(), "student", StringComparison.OrdinalIgnoreCase));

    private static bool IsEligibleForPasswordChange(JsonObject root, JsonObject user, int studentId)
    {
        if (!string.Equals(user["status"]?.GetValue<string>(), "active", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(user["account_state"]?.GetValue<string>(), "account_created", StringComparison.OrdinalIgnoreCase))
            return true;

        return CanonicalJsonStore.GetArray(root, "enrollments").OfType<JsonObject>().Any(enrollment =>
        {
            if (enrollment["student_id"]?.GetValue<int>() != studentId) return false;
            var status = enrollment["status"]?.GetValue<string>();
            return string.Equals(status, "registered", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "enrolled", StringComparison.OrdinalIgnoreCase);
        });
    }

    public bool CanChangeStudentPassword(int studentId)
    {
        if (studentId <= 0) return false;

        try
        {
            var root = _jsonStore.ReadRoot();
            var user = FindStudent(root, studentId);
            return user != null && IsEligibleForPasswordChange(root, user, studentId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Could not determine password-change eligibility: {Message}", ex.Message);
            return false;
        }
    }

    private sealed class PasswordChangeRejectedException(PasswordChangeResult result) : Exception
    {
        public PasswordChangeResult Result { get; } = result;
    }

    public PasswordChangeResult ChangeStudentPassword(int studentId, string currentPassword, string newPassword)
    {
        if (studentId <= 0) return PasswordChangeResult.Ineligible;

        try
        {
            _jsonStore.UpdateRoot(root =>
            {
                var user = FindStudent(root, studentId);
                if (user == null || !IsEligibleForPasswordChange(root, user, studentId))
                    throw new PasswordChangeRejectedException(PasswordChangeResult.Ineligible);

                var storedHash = user["password_hash"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(storedHash) || !BCrypt.Net.BCrypt.Verify(currentPassword, storedHash))
                    throw new PasswordChangeRejectedException(PasswordChangeResult.InvalidCurrentPassword);

                user["password_hash"] = BCrypt.Net.BCrypt.HashPassword(newPassword);
            });

            return PasswordChangeResult.Changed;
        }
        catch (PasswordChangeRejectedException ex)
        {
            return ex.Result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Student password update failed for student ID {StudentId}", studentId);
            return PasswordChangeResult.Failed;
        }
    }

    public async Task<List<StudentUser>> GetAllStudentsAsync()
    {
        var list = new List<StudentUser>();
        var usersEl = GetSeedDataElement("users");
        if (usersEl.HasValue)
        {
            foreach (var u in usersEl.Value.EnumerateArray())
            {
                if (u.TryGetProperty("role", out var r) && r.GetString() == "student")
                {
                    list.Add(new StudentUser
                    {
                        Id = u.GetProperty("id").GetInt32(),
                        StudentIdNumber = u.GetProperty("student_id_number").GetString() ?? "",
                        Name = u.GetProperty("name").GetString() ?? "",
                        Email = u.GetProperty("email").GetString() ?? "",
                        Role = "student",
                        Status = u.GetProperty("status").GetString() ?? "active",
                        CreatedAt = DateTime.TryParse(u.GetProperty("created_at").GetString(), out var dt) ? dt : DateTime.UtcNow
                    });
                }
            }
        }

        if (!list.Any())
        {
            _logger?.LogWarning("GetAllStudentsAsync: No students found in JSON seed data. Please add students via the Registrar system.");
        }
        return list;
    }

    private StudentUser? GetJsonStudentUser(int studentId)
    {
        var usersEl = GetSeedDataElement("users");
        if (!usersEl.HasValue) return null;

        foreach (var user in usersEl.Value.EnumerateArray())
        {
            if (!user.TryGetProperty("id", out var id) || id.GetInt32() != studentId) continue;
            if (!user.TryGetProperty("role", out var role) || role.GetString() != "student") continue;

            var createdAt = user.TryGetProperty("created_at", out var createdAtElement)
                && DateTime.TryParse(createdAtElement.GetString(), out var parsedCreatedAt)
                    ? parsedCreatedAt
                    : DateTime.UtcNow;

            return new StudentUser
            {
                Id = studentId,
                StudentIdNumber = user.TryGetProperty("student_id_number", out var idNumber) ? idNumber.GetString() ?? "" : "",
                Name = user.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Email = user.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
                Role = "student",
                Status = user.TryGetProperty("status", out var status) ? status.GetString() ?? "active" : "active",
                CreatedAt = createdAt
            };
        }

        return null;
    }

    public Task<StudentUser?> GetStudentUserAsync(int studentId)
        => Task.FromResult(GetJsonStudentUser(studentId));

    // ─── Academic Term & Settings ────────────────────────────────────────────

    public async Task<string> GetSettingAsync(string key, string defaultValue = "")
    {
        var settingsEl = GetSeedDataElement("settings");
        if (settingsEl.HasValue)
        {
            foreach (var s in settingsEl.Value.EnumerateArray())
            {
                if (s.TryGetProperty("key", out var k) && k.GetString() == key &&
                    s.TryGetProperty("value", out var v))
                {
                    return v.GetString() ?? defaultValue;
                }
            }
        }
        return defaultValue;
    }

    public async Task<(string SchoolYear, string Semester)> GetActiveTermAsync()
    {
        var sy = await GetSettingAsync("current_school_year", "2025-2026");
        var sem = await GetSettingAsync("current_semester", "1st Semester");
        return (sy, sem);
    }

    public Task LogActivityAsync(int? userId, string message)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var activities = CanonicalJsonStore.GetOrCreateArray(root, "activity_log");
            var nextId = activities.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            activities.Add(new JsonObject
            {
                ["id"] = nextId,
                ["user_id"] = userId.HasValue ? JsonValue.Create(userId.Value) : null,
                ["message"] = message,
                ["created_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        return Task.CompletedTask;
    }

    // ─── Student Profile ─────────────────────────────────────────────────────

    private StudentProfileData? GetJsonStudentProfile(int studentId)
    {
        var profilesEl = GetSeedDataElement("student_profiles");
        if (profilesEl.HasValue)
        {
            foreach (var p in profilesEl.Value.EnumerateArray())
            {
                if (p.TryGetProperty("user_id", out var uid) && uid.GetInt32() == studentId)
                {
                    return new StudentProfileData
                    {
                        UserId = studentId,
                        Program = p.TryGetProperty("program", out var program) ? program.GetString() ?? "BS Information Technology" : "BS Information Technology",
                        YearLevel = p.TryGetProperty("year_level", out var yearLevel) ? yearLevel.GetString() ?? "3rd Year" : "3rd Year",
                        CurriculumYear = p.TryGetProperty("curriculum_year", out var curriculumYear) ? curriculumYear.GetString() ?? "2023-2027" : "2023-2027",
                        AverageGrade = p.TryGetProperty("average_grade", out var avg) ? avg.GetDecimal() : 1.33m,
                        AcademicStatus = p.TryGetProperty("academic_status", out var academicStatus) ? academicStatus.GetString() ?? "Good Standing" : "Good Standing",
                        EnrollmentStatus = p.TryGetProperty("enrollment_status", out var enrollmentStatus) ? enrollmentStatus.GetString() ?? "Regular / Enrolled" : "Regular / Enrolled",
                        CompletedUnits = p.TryGetProperty("completed_units", out var completedUnits) ? completedUnits.GetInt32() : 0,
                        UnitsRemaining = p.TryGetProperty("units_remaining", out var unitsRemaining) ? unitsRemaining.GetInt32() : 144,
                        GuidanceClearanceStatus = p.TryGetProperty("guidance_clearance_status", out var clearance) ? clearance.GetString() ?? "Cleared" : "Cleared",
                        GuidanceNotes = p.TryGetProperty("guidance_notes", out var gn) ? gn.GetString() : "",
                        ContactNumber = p.TryGetProperty("contact_number", out var cn) ? cn.GetString() : null,
                        Address = p.TryGetProperty("address", out var addr) ? addr.GetString() : null,
                        Gender = p.TryGetProperty("gender", out var g) ? g.GetString() ?? "Unspecified" : "Unspecified"
                    };
                }
            }
        }

        return null;
    }

    public async Task<StudentProfileData> GetStudentProfileAsync(int studentId)
    {
        return GetJsonStudentProfile(studentId) ?? new StudentProfileData { UserId = studentId };
    }

    public async Task UpdateContactInfoAsync(int studentId, string contactNumber, string address)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var profiles = CanonicalJsonStore.GetOrCreateArray(root, "student_profiles");
            var profile = profiles.OfType<JsonObject>().FirstOrDefault(item => item["user_id"]?.GetValue<int>() == studentId);
            if (profile == null)
            {
                profile = new JsonObject { ["user_id"] = studentId };
                profiles.Add(profile);
            }
            profile["contact_number"] = contactNumber;
            profile["address"] = address;
        });
        await LogActivityAsync(studentId, "Updated personal contact details via Student Portal");
    }

    // ─── Enrollment Overview & History ───────────────────────────────────────

    public async Task<StudentEnrollmentOverview> GetActiveEnrollmentOverviewAsync(int studentId, string? schoolYear = null, string? semester = null)
    {
        if (string.IsNullOrEmpty(schoolYear) || string.IsNullOrEmpty(semester))
        {
            var term = await GetActiveTermAsync();
            schoolYear = term.SchoolYear;
            semester = term.Semester;
        }

        var enrollmentElement = GetSeedDataElement("enrollments")?.EnumerateArray()
            .FirstOrDefault(item => item.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId &&
                                    item.TryGetProperty("school_year", out var sy) && sy.GetString() == schoolYear &&
                                    item.TryGetProperty("semester", out var sem) && sem.GetString() == semester);
        var enrollment = enrollmentElement.GetValueOrDefault();
        var hasEnrollment = enrollmentElement.HasValue && enrollment.ValueKind != JsonValueKind.Undefined;
            var subjects = hasEnrollment && enrollment.TryGetProperty("status", out var status) && (status.GetString() == "registered" || status.GetString() == "enrolled")
            ? await GetEnrolledClassesAsync(studentId, schoolYear, semester)
            : new List<EnrolledClassItem>();
        var enrolledAt = DateTime.MinValue;
        if (hasEnrollment && enrollment.TryGetProperty("created_at", out var created) && DateTime.TryParse(created.GetString(), out var parsedCreatedAt))
            enrolledAt = parsedCreatedAt;
        return new StudentEnrollmentOverview
        {
            Id = hasEnrollment && enrollment.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            StudentId = studentId,
            SchoolYear = schoolYear,
            Semester = semester,
            EnrollmentType = "Regular",
            Status = hasEnrollment && enrollment.TryGetProperty("status", out var enrollmentStatus) ? enrollmentStatus.GetString() ?? "pending" : "pending",
            EnrolledAt = enrolledAt,
            TotalUnits = subjects.Sum(s => s.Units),
            SubjectCount = subjects.Count,
            Subjects = subjects
        };
    }

    public async Task<List<StudentEnrollmentOverview>> GetEnrollmentHistoryAsync(int studentId)
    {
        var current = await GetActiveEnrollmentOverviewAsync(studentId);
        return new List<StudentEnrollmentOverview> { current };
    }

    // ─── Schedule & Enrolled Classes ─────────────────────────────────────────

    public async Task<List<EnrolledClassItem>> GetEnrolledClassesAsync(int studentId, string? schoolYear = null, string? semester = null)
    {
        if (string.IsNullOrEmpty(schoolYear) || string.IsNullOrEmpty(semester))
        {
            var term = await GetActiveTermAsync();
            schoolYear = term.SchoolYear;
            semester = term.Semester;
        }

        var result = new List<EnrolledClassItem>();
        var offeringsEl = GetSeedDataElement("class_offerings");
        var enrolledSubsEl = GetSeedDataElement("enrolled_subjects");

        if (enrolledSubsEl.HasValue)
        {
            foreach (var es in enrolledSubsEl.Value.EnumerateArray())
            {
                if (es.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    var offeringId = es.GetProperty("class_offering_id").GetInt32();
                    var item = new EnrolledClassItem
                    {
                        EnrolledSubjectId = es.GetProperty("id").GetInt32(),
                        ClassOfferingId = offeringId,
                        SubjectCode = es.GetProperty("subject_code").GetString() ?? "",
                        SubjectName = es.GetProperty("subject_name").GetString() ?? "",
                        Units = es.GetProperty("units").GetInt32(),
                        Status = es.GetProperty("status").GetString() ?? "enrolled",
                        SchoolYear = schoolYear,
                        Semester = semester
                    };

                    if (offeringsEl.HasValue)
                    {
                        foreach (var off in offeringsEl.Value.EnumerateArray())
                        {
                            if (off.GetProperty("id").GetInt32() == offeringId)
                            {
                                item.SectionCode = off.GetProperty("section_code").GetString() ?? "IT31S1";
                                item.Room = off.GetProperty("room").GetString() ?? "CLAB-302";
                                item.InstructorName = off.GetProperty("instructor_name").GetString() ?? "Prof. Alan Turing";
                                item.DaysOfWeek = off.GetProperty("days_of_week").GetString() ?? "MW";
                                if (TimeSpan.TryParse(off.GetProperty("start_time").GetString(), out var st)) item.StartTime = st;
                                if (TimeSpan.TryParse(off.GetProperty("end_time").GetString(), out var et)) item.EndTime = et;
                                break;
                            }
                        }
                    }

                    result.Add(item);
                }
            }
        }

        return result;
    }

    public async Task<List<EnrolledClassItem>> GetAvailableOfferingsAsync(string? schoolYear = null, string? semester = null)
    {
        if (string.IsNullOrEmpty(schoolYear) || string.IsNullOrEmpty(semester))
        {
            var term = await GetActiveTermAsync();
            schoolYear = term.SchoolYear;
            semester = term.Semester;
        }

        var result = new List<EnrolledClassItem>();
        var offeringsEl = GetSeedDataElement("class_offerings");
        var subjectsEl = GetSeedDataElement("subjects");

        if (offeringsEl.HasValue)
        {
            foreach (var off in offeringsEl.Value.EnumerateArray())
            {
                var subId = off.GetProperty("subject_id").GetInt32();
                var item = new EnrolledClassItem
                {
                    ClassOfferingId = off.GetProperty("id").GetInt32(),
                    SectionCode = off.GetProperty("section_code").GetString() ?? "",
                    Room = off.GetProperty("room").GetString() ?? "",
                    DaysOfWeek = off.GetProperty("days_of_week").GetString() ?? "",
                    InstructorName = off.GetProperty("instructor_name").GetString() ?? "",
                    SchoolYear = schoolYear,
                    Semester = semester
                };

                if (TimeSpan.TryParse(off.GetProperty("start_time").GetString(), out var st)) item.StartTime = st;
                if (TimeSpan.TryParse(off.GetProperty("end_time").GetString(), out var et)) item.EndTime = et;

                if (subjectsEl.HasValue)
                {
                    foreach (var s in subjectsEl.Value.EnumerateArray())
                    {
                        if (s.GetProperty("id").GetInt32() == subId)
                        {
                            item.SubjectCode = s.GetProperty("subject_code").GetString() ?? "";
                            item.SubjectName = s.GetProperty("subject_name").GetString() ?? "";
                            item.Units = s.GetProperty("units").GetInt32();
                            break;
                        }
                    }
                }

                result.Add(item);
            }
        }

        return result;
    }

    // ─── Grades & GWA ────────────────────────────────────────────────────────

    public async Task<List<TermGradeGroup>> GetGradesGroupedByTermAsync(int studentId)
    {
        var groups = new List<TermGradeGroup>();
        var gradesEl = GetSeedDataElement("grades");
        var subjectsEl = GetSeedDataElement("subjects");

        if (gradesEl.HasValue)
        {
            var studentGrades = new List<StudentGradeItem>();
            foreach (var g in gradesEl.Value.EnumerateArray())
            {
                if (g.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    var code = g.GetProperty("subject_code").GetString() ?? "";
                    var item = new StudentGradeItem
                    {
                        GradeId = g.GetProperty("id").GetInt32(),
                        SubjectCode = code,
                        SchoolYear = g.GetProperty("school_year").GetString() ?? "2025-2026",
                        Semester = g.GetProperty("semester").GetString() ?? "1st Semester",
                        Grade = g.TryGetProperty("final_grade", out var fg) ? fg.GetDecimal() : 1.25m,
                        IsInc = g.TryGetProperty("is_inc", out var inc) && inc.GetBoolean(),
                        Remarks = g.TryGetProperty("remarks", out var rem) ? rem.GetString() : "Passed",
                        Units = 3
                    };

                    if (subjectsEl.HasValue)
                    {
                        foreach (var sub in subjectsEl.Value.EnumerateArray())
                        {
                            if (sub.GetProperty("subject_code").GetString() == code)
                            {
                                item.SubjectName = sub.GetProperty("subject_name").GetString() ?? code;
                                item.Units = sub.GetProperty("units").GetInt32();
                                break;
                            }
                        }
                    }

                    studentGrades.Add(item);
                }
            }

            groups = studentGrades.GroupBy(x => new { x.SchoolYear, x.Semester })
                .Select(grp => new TermGradeGroup
                {
                    SchoolYear = grp.Key.SchoolYear,
                    Semester = grp.Key.Semester,
                    Grades = grp.ToList(),
                    TotalUnits = grp.Sum(x => x.Units),
                    SemesterGpa = grp.Where(x => x.Grade.HasValue).Select(x => x.Grade!.Value * x.Units).Sum() / Math.Max(1, grp.Where(x => x.Grade.HasValue).Sum(x => x.Units))
                }).ToList();
        }

        return groups;
    }

    public async Task<decimal?> CalculateCumulativeGwaAsync(int studentId)
    {
        var termGroups = await GetGradesGroupedByTermAsync(studentId);
        var allGrades = termGroups.SelectMany(g => g.Grades).Where(g => g.Grade.HasValue && !g.IsInc).ToList();
        if (!allGrades.Any()) return null;

        var totalUnits = allGrades.Sum(g => g.Units);
        if (totalUnits == 0) return null;

        var weighted = allGrades.Sum(g => g.Grade!.Value * g.Units);
        return Math.Round(weighted / totalUnits, 2);
    }

    // ─── Financials & Ledger ─────────────────────────────────────────────────

    public async Task<decimal> GetTotalAssessmentAsync(int studentId)
    {
        var feeEl = GetSeedDataElement("fee_assessments");
        if (feeEl.HasValue)
        {
            foreach (var fa in feeEl.Value.EnumerateArray())
            {
                if (fa.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    return fa.GetProperty("total_net_assessment").GetDecimal();
                }
            }
        }
        return 0m;
    }

    public async Task<List<PaymentRecord>> GetPaymentsAsync(int studentId)
    {
        var listFallback = new List<PaymentRecord>();
        var paymentsEl = GetSeedDataElement("student_payments");
        if (paymentsEl.HasValue)
        {
            foreach (var p in paymentsEl.Value.EnumerateArray())
            {
                if (p.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    listFallback.Add(new PaymentRecord
                    {
                        Id = p.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        StudentId = studentId,
                        ReceiptNo = p.TryGetProperty("receipt_no", out var receipt) ? receipt.GetString() ?? "" : "",
                        Amount = p.TryGetProperty("amount", out var amount) ? amount.GetDecimal() : 0m,
                        PayDate = p.TryGetProperty("pay_date", out var payDate) && DateTime.TryParse(payDate.GetString(), out var dt) ? dt : DateTime.UtcNow,
                        PaymentMethod = p.TryGetProperty("payment_method", out var method) ? method.GetString() ?? "Maya" : "Maya",
                        PaymentCategory = p.TryGetProperty("payment_category", out var pc) ? pc.GetString() ?? "Tuition" : "Tuition",
                        Status = p.TryGetProperty("status", out var status) ? status.GetString() ?? "Paid" : "Paid",
                        Description = p.TryGetProperty("description", out var description) ? description.GetString() ?? "Tuition Payment" : "Tuition Payment"
                    });
                }
            }
        }

        return listFallback;
    }

    public async Task<string> SubmitPaymentAsync(int studentId, decimal amount, string paymentMethod, string description, string category = "Tuition")
    {
        var rcpt = "OR-" + DateTime.UtcNow.ToString("yyyy") + "-" + RandomNumberGenerator.GetInt32(10000, 99999);

        _jsonStore.UpdateRoot(root =>
        {
            var payments = CanonicalJsonStore.GetOrCreateArray(root, "student_payments");
            var paymentId = payments.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            var assessment = CanonicalJsonStore.GetArray(root, "fee_assessments").OfType<JsonObject>().FirstOrDefault(item =>
                item["student_id"]?.GetValue<int>() == studentId);
            payments.Add(new JsonObject
            {
                ["id"] = paymentId,
                ["student_id"] = studentId,
                ["assessment_id"] = assessment?["id"]?.GetValue<int>(),
                ["receipt_no"] = rcpt,
                ["amount"] = amount,
                ["pay_date"] = DateTime.UtcNow.ToString("O"),
                ["payment_method"] = paymentMethod,
                ["payment_category"] = category,
                ["status"] = "Paid",
                ["description"] = description
            });

            if (assessment != null && !category.Equals("Library Fine", StringComparison.OrdinalIgnoreCase))
            {
                var totalPaid = (assessment["total_paid"]?.GetValue<decimal>() ?? 0m) + amount;
                var totalAssessment = assessment["total_net_assessment"]?.GetValue<decimal>() ?? 0m;
                var remaining = Math.Max(0m, totalAssessment - totalPaid);
                assessment["total_paid"] = totalPaid;
                assessment["remaining_balance"] = remaining;
                assessment["status"] = remaining == 0m ? "Paid" : "Partially Paid";
            }
        });

        await LogActivityAsync(studentId, $"Settled {category} payment of ₱{amount:N2} (Receipt #{rcpt}) via {paymentMethod}");
        return rcpt;
    }

    // ─── Comprehensive Library System ────────────────────────────────────────

    public async Task<List<LibraryBookItem>> GetLibraryCatalogAsync(string? search = null, string? category = null)
    {
        var books = new List<LibraryBookItem>();
        var booksEl = GetSeedDataElement("library_books");

        if (booksEl.HasValue)
        {
            foreach (var b in booksEl.Value.EnumerateArray())
            {
                var title = b.GetProperty("title").GetString() ?? "";
                var author = b.GetProperty("author").GetString() ?? "";
                var cat = b.GetProperty("category").GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(search))
                {
                    if (!title.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                        !author.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                        !cat.Contains(search, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(category) && category != "All")
                {
                    if (!cat.Equals(category, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                books.Add(new LibraryBookItem
                {
                    Id = b.GetProperty("id").GetInt32(),
                    Isbn = b.GetProperty("isbn").GetString() ?? "",
                    Title = title,
                    Author = author,
                    CallNumber = b.GetProperty("call_number").GetString() ?? "",
                    Category = cat,
                    TotalCopies = b.GetProperty("total_copies").GetInt32(),
                    AvailableCopies = b.GetProperty("available_copies").GetInt32(),
                    ShelfLocation = b.GetProperty("shelf_location").GetString() ?? "Main Stacks"
                });
            }
        }

        return books;
    }

    public async Task<List<BookLoanRecord>> GetStudentBookLoansAsync(int studentId)
    {
        var overdueRate = LibraryCirculation.OverduePerDay(_jsonStore.ReadRoot());
        var loans = new List<BookLoanRecord>();
        var loansEl = GetSeedDataElement("book_loans");
        var booksEl = GetSeedDataElement("library_books");

        if (loansEl.HasValue)
        {
            foreach (var l in loansEl.Value.EnumerateArray())
            {
                if (l.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    var bookId = l.GetProperty("book_id").GetInt32();
                    var loan = new BookLoanRecord
                    {
                        Id = l.GetProperty("id").GetInt32(),
                        BookId = bookId,
                        StudentId = studentId,
                        BorrowDate = DateTime.TryParse(l.GetProperty("borrow_date").GetString(), out var bd) ? bd : DateTime.UtcNow,
                        DueDate = DateTime.TryParse(l.GetProperty("due_date").GetString(), out var dd) ? dd : DateTime.UtcNow.AddDays(7),
                        ReturnDate = l.TryGetProperty("return_date", out var rd) && !string.IsNullOrEmpty(rd.GetString()) && DateTime.TryParse(rd.GetString(), out var rdt) ? rdt : null,
                        ConditionOnReturn = l.TryGetProperty("condition_on_return", out var cr) ? cr.GetString() : null,
                        Status = l.GetProperty("status").GetString() ?? "Borrowed",
                        FineAmount = l.TryGetProperty("fine_amount", out var fa) ? fa.GetDecimal() : 0.00m,
                        FineStatus = l.TryGetProperty("fine_status", out var fs) ? fs.GetString() ?? "None" : "None",
                        Remarks = l.TryGetProperty("remarks", out var rem) ? rem.GetString() : ""
                    };

                    if (loan.ReturnDate == null && loan.DueDate < DateTime.UtcNow && loan.Status == "Borrowed")
                    {
                        loan.Status = "Overdue";
                        var overdueDays = (int)Math.Ceiling((DateTime.UtcNow - loan.DueDate).TotalDays);
                        if (overdueDays > 0)
                        {
                            loan.FineAmount = overdueDays * overdueRate;
                            loan.FineStatus = "Pending";
                        }
                    }

                    if (booksEl.HasValue)
                    {
                        foreach (var b in booksEl.Value.EnumerateArray())
                        {
                            if (b.GetProperty("id").GetInt32() == bookId)
                            {
                                loan.BookTitle = b.GetProperty("title").GetString() ?? "Library Book";
                                loan.BookAuthor = b.GetProperty("author").GetString() ?? "University Library";
                                break;
                            }
                        }
                    }

                    loans.Add(loan);
                }
            }
        }

        return loans;
    }

    public async Task<int> BorrowBookAsync(int studentId, int bookId, int loanDays = 7)
    {
        var loanId = 0;
        var title = "";
        _jsonStore.UpdateRoot(root =>
        {
            loanId = LibraryCirculation.Borrow(root, studentId, bookId, DateTime.UtcNow, loanDays, "Student Portal");
            title = CanonicalJsonStore.GetArray(root, "library_books").OfType<JsonObject>()
                .FirstOrDefault(b => b["id"]?.GetValue<int>() == bookId)?["title"]?.GetValue<string>() ?? "";
        });
        await LogActivityAsync(studentId, $"Borrowed library book '{title}'");
        return loanId;
    }

    public async Task ReturnBookAsync(int loanId, int studentId, string condition = "Good")
    {
        LibraryCirculation.ReturnResult? result = null;
        _jsonStore.UpdateRoot(root => result = LibraryCirculation.Return(root, loanId, condition, DateTime.UtcNow, studentId));
        await LogActivityAsync(studentId, $"Returned library loan #{loanId}. Condition: {condition}. Fine: ₱{result!.TotalFine:N2}");
    }

    public Task<decimal> GetStudentOutstandingFineAmountAsync(int studentId) =>
        Task.FromResult(LibraryCirculation.OutstandingFines(_jsonStore.ReadRoot(), studentId, DateTime.UtcNow));

    public async Task PayLibraryFineAsync(int studentId, int loanId, string paymentMethod = "Maya")
    {
        IReadOnlyList<string> receipts = [];
        _jsonStore.UpdateRoot(root => receipts = LibraryCirculation.PayLoanFines(root, loanId, paymentMethod, DateTime.UtcNow, studentId));
        await LogActivityAsync(studentId, $"Paid library fine for loan #{loanId} (OR #{string.Join(", ", receipts)})");
    }

    // ─── Clearance System ────────────────────────────────────────────────────

    public async Task<List<ClearanceRecord>> GetClearanceRecordsAsync(int studentId, string? schoolYear = null, string? semester = null)
    {
        return await GetStudentClearanceAsync(studentId, schoolYear, semester);
    }

    public async Task<List<ClearanceRecord>> GetStudentClearanceAsync(int studentId, string? schoolYear = null, string? semester = null)
    {
        if (string.IsNullOrEmpty(schoolYear) || string.IsNullOrEmpty(semester))
        {
            var term = await GetActiveTermAsync();
            schoolYear = term.SchoolYear;
            semester = term.Semester;
        }

        var list = new List<ClearanceRecord>();
        var clearanceEl = GetSeedDataElement("student_clearance");
        var outstandingFines = await GetStudentOutstandingFineAmountAsync(studentId);

        if (clearanceEl.HasValue)
        {
            foreach (var c in clearanceEl.Value.EnumerateArray())
            {
                if (c.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    var deptName = c.GetProperty("department_name").GetString() ?? "";
                    var status = c.GetProperty("status").GetString() ?? "Cleared";
                    var remarks = c.TryGetProperty("remarks", out var rem) ? rem.GetString() : "";

                    if (deptName.Contains("Library", StringComparison.OrdinalIgnoreCase) && outstandingFines > 0)
                    {
                        status = "Deficient";
                        remarks = $"Unpaid library fines: ₱{outstandingFines:N2}. Please settle at Library or Cashier.";
                    }

                    list.Add(new ClearanceRecord
                    {
                        Id = c.GetProperty("id").GetInt32(),
                        StudentId = studentId,
                        DepartmentName = deptName,
                        SchoolYear = c.GetProperty("school_year").GetString() ?? schoolYear,
                        Semester = c.GetProperty("semester").GetString() ?? semester,
                        Status = status,
                        ClearedBy = c.TryGetProperty("cleared_by", out var cb) ? cb.GetString() ?? "Officer" : "Officer",
                        Remarks = remarks
                    });
                }
            }
        }

        return list;
    }

    // ─── Document Requests & 201 Vault ───────────────────────────────────────

    public async Task<List<DocumentCredentialItem>> GetDocumentVaultAsync(int studentId)
    {
        return await GetDocumentCredentialsAsync(studentId);
    }

    public async Task<List<DocumentCredentialItem>> GetDocumentCredentialsAsync(int studentId)
    {
        var list = new List<DocumentCredentialItem>();
        var docsEl = GetSeedDataElement("document_credentials");
        if (docsEl.HasValue)
        {
            foreach (var d in docsEl.Value.EnumerateArray())
            {
                if (d.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    var documentType = d.TryGetProperty("document_name", out var documentName)
                        ? documentName.GetString() ?? ""
                        : d.TryGetProperty("document_type", out var legacyDocumentType) ? legacyDocumentType.GetString() ?? "" : "";
                    list.Add(new DocumentCredentialItem
                    {
                        Id = d.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        StudentId = studentId,
                        DocumentType = documentType,
                        Status = d.TryGetProperty("status", out var status) ? status.GetString() ?? "Verified" : "Verified",
                        Remarks = d.TryGetProperty("remarks", out var r) ? r.GetString() : "Authenticated file"
                    });
                }
            }
        }
        return list;
    }

    public async Task SubmitVaultFileAsync(int credentialId, int studentId, string fileName)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var credential = CanonicalJsonStore.GetArray(root, "document_credentials").OfType<JsonObject>().FirstOrDefault(item =>
                item["id"]?.GetValue<int>() == credentialId && item["student_id"]?.GetValue<int>() == studentId);
            if (credential == null) throw new InvalidOperationException("The selected document checklist item was not found.");

            credential["status"] = "Submitted";
            credential["submission_date"] = DateTime.UtcNow.ToString("O");
            credential["remarks"] = fileName;
        });
        await LogActivityAsync(studentId, $"Uploaded 201 credential file '{fileName}' (Ref #{credentialId})");
    }

    public async Task<List<DocumentRequestRecord>> GetDocumentRequestsAsync(int studentId)
    {
        var list = new List<DocumentRequestRecord>();
        var requestsEl = GetSeedDataElement("transcript_requests");
        if (requestsEl.HasValue)
        {
            foreach (var r in requestsEl.Value.EnumerateArray())
            {
                if (r.TryGetProperty("student_id", out var sid) && sid.GetInt32() == studentId)
                {
                    list.Add(new DocumentRequestRecord
                    {
                        Id = r.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                        StudentId = studentId,
                        DocumentType = r.TryGetProperty("document_type", out var type) ? type.GetString() ?? "Transcript of Records" : "Transcript of Records",
                        Purpose = r.TryGetProperty("purpose", out var purpose) ? purpose.GetString() ?? "General Purpose" : "General Purpose",
                        Status = r.TryGetProperty("processing_status", out var status) ? status.GetString() ?? "Pending" : "Pending",
                        QrCodeToken = r.TryGetProperty("tracking_token", out var token) ? token.GetString() ?? "" : "",
                        RequestedAt = r.TryGetProperty("request_date", out var requestedAt) && DateTime.TryParse(requestedAt.GetString(), out var dt) ? dt : DateTime.UtcNow
                    });
                }
            }
        }
        return list;
    }

    public async Task<string> SubmitDocumentRequestAsync(int studentId, string documentType, string purpose, int copies = 1)
    {
        var token = "MSU-DOC-" + DateTime.UtcNow.ToString("yyyy") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        _jsonStore.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "transcript_requests");
            var nextId = requests.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            requests.Add(new JsonObject
            {
                ["id"] = nextId,
                ["student_id"] = studentId,
                ["tracking_token"] = token,
                ["document_type"] = documentType,
                ["purpose"] = purpose,
                ["copies"] = Math.Max(1, copies),
                ["fee_amount"] = 0m,
                ["payment_status"] = "Pending",
                ["processing_status"] = "Pending",
                ["request_date"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Submitted request for '{documentType}' (Tracking #{token})");
        return token;
    }

    // ─── Add/Drop, Overload & Shifting Petitions ─────────────────────────────

    public async Task<List<AddDropRecord>> GetAddDropRequestsAsync(int studentId)
    {
        var list = new List<AddDropRecord>();
        var requests = GetSeedDataElement("add_drop_requests");
        if (requests.HasValue)
        {
            foreach (var request in requests.Value.EnumerateArray())
            {
                if (!request.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
                list.Add(new AddDropRecord
                {
                    Id = request.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    StudentId = studentId,
                    EnrollmentId = request.TryGetProperty("enrollment_id", out var enrollmentId) ? enrollmentId.GetInt32() : 0,
                    RequestType = request.TryGetProperty("request_type", out var type) ? type.GetString() ?? "add" : "add",
                    ClassOfferingId = request.TryGetProperty("class_offering_id", out var classId) && classId.ValueKind == JsonValueKind.Number ? classId.GetInt32() : null,
                    TargetClassOfferingId = request.TryGetProperty("target_class_offering_id", out var targetId) && targetId.ValueKind == JsonValueKind.Number ? targetId.GetInt32() : null,
                    Reason = request.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    Status = request.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending",
                    CreatedAt = request.TryGetProperty("requested_at", out var created) && DateTime.TryParse(created.GetString(), out var date) ? date : DateTime.UtcNow
                });
            }
        }
        return list;
    }

    public async Task SubmitAddDropRequestAsync(int studentId, int enrollmentId, string requestType, int? classOfferingId, int? targetClassOfferingId, string reason)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "add_drop_requests");
            var nextId = requests.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            requests.Add(new JsonObject
            {
                ["id"] = nextId,
                ["student_id"] = studentId,
                ["enrollment_id"] = enrollmentId,
                ["request_type"] = requestType,
                ["class_offering_id"] = classOfferingId,
                ["target_class_offering_id"] = targetClassOfferingId,
                ["reason"] = reason,
                ["status"] = "pending",
                ["requested_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Submitted {requestType} subject request (Offering #{classOfferingId})");
    }

    public async Task<List<OverloadRecord>> GetOverloadRequestsAsync(int studentId)
    {
        var list = new List<OverloadRecord>();
        var requests = GetSeedDataElement("overload_waiver_requests");
        if (requests.HasValue)
        {
            foreach (var request in requests.Value.EnumerateArray())
            {
                if (!request.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
                list.Add(new OverloadRecord
                {
                    Id = request.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    StudentId = studentId,
                    SchoolYear = request.TryGetProperty("school_year", out var schoolYear) ? schoolYear.GetString() ?? "" : "",
                    Semester = request.TryGetProperty("semester", out var semester) ? semester.GetString() ?? "" : "",
                    RequestType = request.TryGetProperty("request_type", out var type) ? type.GetString() ?? "overload" : "overload",
                    RequestedUnits = request.TryGetProperty("requested_units", out var units) ? units.GetInt32() : 0,
                    Reason = request.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    Status = request.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending",
                    RequestedAt = request.TryGetProperty("requested_at", out var requestedAt) && DateTime.TryParse(requestedAt.GetString(), out var date) ? date : DateTime.UtcNow
                });
            }
        }
        return list;
    }

    public async Task SubmitOverloadRequestAsync(int studentId, string schoolYear, string semester, string requestType, int requestedUnits, string reason)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "overload_waiver_requests");
            var nextId = requests.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            requests.Add(new JsonObject
            {
                ["id"] = nextId,
                ["student_id"] = studentId,
                ["school_year"] = schoolYear,
                ["semester"] = semester,
                ["request_type"] = requestType,
                ["requested_units"] = requestedUnits,
                ["reason"] = reason,
                ["status"] = "pending",
                ["requested_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Submitted {requestType} waiver for {requestedUnits} units ({schoolYear} {semester})");
    }

    public async Task<List<CourseShiftingRecord>> GetCourseShiftingRequestsAsync(int studentId)
    {
        var list = new List<CourseShiftingRecord>();
        var requests = GetSeedDataElement("course_shifting_requests");
        if (requests.HasValue)
        {
            foreach (var request in requests.Value.EnumerateArray())
            {
                if (!request.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
                list.Add(new CourseShiftingRecord
                {
                    Id = request.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    StudentId = studentId,
                    FromProgram = request.TryGetProperty("from_program", out var from) ? from.GetString() ?? "" : "",
                    ToProgram = request.TryGetProperty("to_program", out var to) ? to.GetString() ?? "" : "",
                    Reason = request.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "" : "",
                    Status = request.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending",
                    RequestedAt = request.TryGetProperty("requested_at", out var requestedAt) && DateTime.TryParse(requestedAt.GetString(), out var date) ? date : DateTime.UtcNow,
                    ApprovedAt = request.TryGetProperty("approved_at", out var approvedAt) && DateTime.TryParse(approvedAt.GetString(), out var approvedDate) ? approvedDate : null,
                    EvaluatedBy = request.TryGetProperty("evaluated_by", out var evaluator) ? evaluator.GetString() : null
                });
            }
        }
        return list;
    }

    public async Task SubmitCourseShiftingRequestAsync(int studentId, string fromProgram, string toProgram, string reason)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "course_shifting_requests");
            var nextId = requests.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            requests.Add(new JsonObject
            {
                ["id"] = nextId,
                ["student_id"] = studentId,
                ["from_program"] = fromProgram,
                ["to_program"] = toProgram,
                ["reason"] = reason,
                ["status"] = "pending",
                ["requested_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Submitted Program Shifting petition ({fromProgram} -> {toProgram})");
    }

    public async Task<List<CompletionRevisionRecord>> GetCompletionRevisionRequestsAsync(int studentId)
    {
        var list = new List<CompletionRevisionRecord>();
        var requests = GetSeedDataElement("completion_revision_requests");
        if (requests.HasValue)
        {
            foreach (var request in requests.Value.EnumerateArray())
            {
                if (!request.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
                list.Add(new CompletionRevisionRecord
                {
                    Id = request.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    StudentId = studentId,
                    GradeId = request.TryGetProperty("grade_id", out var gradeId) ? gradeId.GetInt32() : 0,
                    SubjectCode = request.TryGetProperty("subject_code", out var code) ? code.GetString() ?? "" : "",
                    SubjectName = request.TryGetProperty("subject_name", out var name) ? name.GetString() ?? "" : "",
                    RequestType = request.TryGetProperty("request_type", out var type) ? type.GetString() ?? "completion" : "completion",
                    RequestedGrade = request.TryGetProperty("requested_grade", out var requestedGrade) ? requestedGrade.GetDecimal() : 0m,
                    Reason = request.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    Status = request.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending",
                    RequestedAt = request.TryGetProperty("requested_at", out var requestedAt) && DateTime.TryParse(requestedAt.GetString(), out var date) ? date : DateTime.UtcNow
                });
            }
        }
        return list;
    }

    public async Task SubmitCompletionRevisionAsync(int studentId, int gradeId, string subjectCode, string requestType, decimal requestedGrade, string reason)
    {
        var subjectName = GetSeedDataElement("subjects")?.EnumerateArray()
            .Where(subject => subject.TryGetProperty("subject_code", out var code) && code.GetString() == subjectCode)
            .Select(subject => subject.TryGetProperty("subject_name", out var name) ? name.GetString() ?? subjectCode : subjectCode)
            .FirstOrDefault() ?? subjectCode;
        _jsonStore.UpdateRoot(root =>
        {
            var requests = CanonicalJsonStore.GetOrCreateArray(root, "completion_revision_requests");
            var nextId = requests.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            requests.Add(new JsonObject
            {
                ["id"] = nextId,
                ["student_id"] = studentId,
                ["grade_id"] = gradeId,
                ["subject_code"] = subjectCode,
                ["subject_name"] = subjectName,
                ["request_type"] = requestType,
                ["requested_grade"] = requestedGrade,
                ["reason"] = reason,
                ["status"] = "pending",
                ["requested_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Submitted {requestType} petition for {subjectCode} (Grade: {requestedGrade})");
    }

    // ─── Guidance & Reservations ─────────────────────────────────────────────

    public async Task<List<LibraryReservationRecord>> GetLibraryReservationsAsync(int studentId)
    {
        var reservations = new List<LibraryReservationRecord>();
        var data = GetSeedDataElement("library_reservations");
        if (!data.HasValue) return reservations;

        foreach (var item in data.Value.EnumerateArray())
        {
            if (!item.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
            reservations.Add(new LibraryReservationRecord
            {
                Id = item.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                StudentId = studentId,
                BookTitle = item.TryGetProperty("book_title", out var title) ? title.GetString() ?? "" : "",
                BookAuthor = item.TryGetProperty("book_author", out var author) ? author.GetString() ?? "" : "",
                ReservationDate = item.TryGetProperty("reservation_date", out var reserved) && DateTime.TryParse(reserved.GetString(), out var reservationDate) ? reservationDate : DateTime.UtcNow,
                DueDate = item.TryGetProperty("due_date", out var due) && DateTime.TryParse(due.GetString(), out var dueDate) ? dueDate : DateTime.UtcNow.AddDays(7),
                Status = item.TryGetProperty("status", out var status) ? status.GetString() ?? "Active" : "Active"
            });
        }
        return reservations;
    }

    public async Task SubmitLibraryReservationAsync(int studentId, string bookTitle, string bookAuthor)
    {
        _jsonStore.UpdateRoot(root =>
        {
            var book = CanonicalJsonStore.GetArray(root, "library_books").OfType<JsonObject>().FirstOrDefault(b =>
                string.Equals(b["title"]?.GetValue<string>(), bookTitle, StringComparison.OrdinalIgnoreCase))
                ?? throw new LibraryException("That book is not in the catalog.");
            LibraryCirculation.Reserve(root, studentId, book["id"]!.GetValue<int>(), DateTime.UtcNow);
        });
        await LogActivityAsync(studentId, $"Placed library reservation for '{bookTitle}'");
    }

    // ─── Scholarships ────────────────────────────────────────────────────────

    public List<ScholarshipItem> GetAvailableScholarships()
    {
        _jsonStore.UpdateRoot(root =>
        {
            var scholarships = CanonicalJsonStore.GetOrCreateArray(root, "scholarships");
            if (scholarships.Count != 0) return;
            scholarships.Add(new JsonObject { ["id"] = 1, ["name"] = "NU President's Academic Scholarship", ["description"] = "100% Tuition discount for students with GWA 1.00 - 1.25", ["slots_available"] = 15 });
            scholarships.Add(new JsonObject { ["id"] = 2, ["name"] = "Collegiate Dean's Lister Grant", ["description"] = "50% Tuition discount for students with GWA 1.26 - 1.50", ["slots_available"] = 30 });
            scholarships.Add(new JsonObject { ["id"] = 3, ["name"] = "CHED Tulong Dunong Program (TDP-TES)", ["description"] = "Government subsidy allowance for qualified collegiate students", ["slots_available"] = 50 });
            scholarships.Add(new JsonObject { ["id"] = 4, ["name"] = "NU Athletic & Cultural Varsity Grant", ["description"] = "Full athletic waiver for university varsity team members", ["slots_available"] = 20 });
        });

        var scholarshipsData = GetSeedDataElement("scholarships");
        if (!scholarshipsData.HasValue) return new List<ScholarshipItem>();
        return scholarshipsData.Value.EnumerateArray().Select(scholarship => new ScholarshipItem
        {
            Id = scholarship.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Name = scholarship.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Description = scholarship.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
            SlotsAvailable = scholarship.TryGetProperty("slots_available", out var slots) ? slots.GetInt32() : 0
        }).ToList();
    }

    public async Task<List<ScholarshipApplicationRecord>> GetScholarshipApplicationsAsync(int studentId)
    {
        var list = new List<ScholarshipApplicationRecord>();
        var applications = GetSeedDataElement("scholarship_applications");
        if (applications.HasValue)
        {
            foreach (var application in applications.Value.EnumerateArray())
            {
                if (!application.TryGetProperty("student_id", out var owner) || owner.GetInt32() != studentId) continue;
                list.Add(new ScholarshipApplicationRecord
                {
                    Id = application.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    StudentId = studentId,
                    ScholarshipName = application.TryGetProperty("scholarship_name", out var name) ? name.GetString() ?? "" : "",
                    SchoolYear = application.TryGetProperty("school_year", out var schoolYear) ? schoolYear.GetString() ?? "" : "",
                    Semester = application.TryGetProperty("semester", out var semester) ? semester.GetString() ?? "" : "",
                    RequirementsSubmitted = application.TryGetProperty("requirements_submitted", out var submitted) && submitted.GetBoolean(),
                    Status = application.TryGetProperty("status", out var status) ? status.GetString() ?? "pending" : "pending",
                    RequestedAt = application.TryGetProperty("requested_at", out var requestedAt) && DateTime.TryParse(requestedAt.GetString(), out var date) ? date : DateTime.UtcNow
                });
            }
        }
        return list;
    }

    public async Task<int> SubmitScholarshipApplicationAsync(int studentId, string scholarName, string sy, string sem)
    {
        var applicationId = 0;
        _jsonStore.UpdateRoot(root =>
        {
            var applications = CanonicalJsonStore.GetOrCreateArray(root, "scholarship_applications");
            applicationId = applications.OfType<JsonObject>().Select(item => item["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            applications.Add(new JsonObject
            {
                ["id"] = applicationId,
                ["student_id"] = studentId,
                ["scholarship_name"] = scholarName,
                ["school_year"] = sy,
                ["semester"] = sem,
                ["requirements_submitted"] = false,
                ["status"] = "pending",
                ["requested_at"] = DateTime.UtcNow.ToString("O")
            });
        });
        await LogActivityAsync(studentId, $"Applied for '{scholarName}' grant");
        return applicationId;
    }

    // ─── Assignments & Coursework ────────────────────────────────────────────

    /// <summary>Published and closed assignments of the sections the student is enrolled in, with their own submission.</summary>
    public Task<List<AssignmentItem>> GetAssignmentsAsync(int studentId)
    {
        var root = _jsonStore.ReadRoot();
        var offeringIds = CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>()
            .Where(e => e["student_id"]?.GetValue<int>() == studentId && e["status"]?.GetValue<string>() != "dropped")
            .Select(e => e["class_offering_id"]?.GetValue<int>() ?? 0)
            .ToHashSet();
        var offerings = CanonicalJsonStore.GetArray(root, "class_offerings").OfType<JsonObject>().ToList();
        var subjects = CanonicalJsonStore.GetArray(root, "subjects").OfType<JsonObject>().ToList();
        var submissions = CanonicalJsonStore.GetArray(root, "assignment_submissions").OfType<JsonObject>()
            .Where(s => s["student_id"]?.GetValue<int>() == studentId).ToList();

        var items = CanonicalJsonStore.GetArray(root, "assignments").OfType<JsonObject>()
            .Where(a => a["status"]?.GetValue<string>() is "Published" or "Closed" && offeringIds.Contains(a["class_offering_id"]?.GetValue<int>() ?? 0))
            .Select(a =>
            {
                var offering = offerings.FirstOrDefault(o => o["id"]?.GetValue<int>() == a["class_offering_id"]?.GetValue<int>());
                var subject = subjects.FirstOrDefault(s => s["id"]?.GetValue<int>() == offering?["subject_id"]?.GetValue<int>());
                var id = a["id"]?.GetValue<int>() ?? 0;
                var submission = submissions.FirstOrDefault(s => s["assignment_id"]?.GetValue<int>() == id);
                return new AssignmentItem
                {
                    Id = id,
                    ClassOfferingId = a["class_offering_id"]?.GetValue<int>() ?? 0,
                    SubjectCode = subject?["subject_code"]?.GetValue<string>() ?? "",
                    SubjectName = subject?["subject_name"]?.GetValue<string>() ?? "",
                    Title = a["title"]?.GetValue<string>() ?? "",
                    Description = a["instructions"]?.GetValue<string>() ?? "",
                    Type = a["type"]?.GetValue<string>() ?? "",
                    MaxScore = a["max_score"]?.GetValue<decimal>() ?? 100m,
                    DueDate = DateTime.TryParse(a["due_date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var due) ? due : null,
                    Status = a["status"]?.GetValue<string>() ?? "Published",
                    IsClosed = a["status"]?.GetValue<string>() == "Closed",
                    IsSubmitted = submission is not null,
                    IsLate = submission?["is_late"]?.GetValue<bool>() ?? false,
                    SubmissionText = submission?["text"]?.GetValue<string>() ?? "",
                    MyScore = submission?["score"] is JsonValue sv && sv.TryGetValue<decimal>(out var score) ? score : null,
                    Feedback = submission?["feedback"]?.GetValue<string>() ?? ""
                };
            })
            .OrderBy(i => i.DueDate ?? DateTime.MaxValue)
            .ThenBy(i => i.Id)
            .ToList();
        return Task.FromResult(items);
    }

    /// <summary>
    /// Turns in (or updates) the student's answer. Refused when the assignment is closed or not theirs, when the answer is
    /// empty or too long, or when the instructor has already scored it. Returns an error message, or null on success.
    /// </summary>
    public async Task<string?> SubmitAssignmentWorkAsync(int assignmentId, int studentId, string? answer)
    {
        answer = (answer ?? "").Trim();
        if (answer.Length == 0) return "Write your answer before turning it in.";
        if (answer.Length > 10_000) return "Your answer is too long (10,000 characters at most).";

        string? error = null;
        _jsonStore.UpdateRoot(root =>
        {
            var assignment = CanonicalJsonStore.GetArray(root, "assignments").OfType<JsonObject>()
                .FirstOrDefault(a => a["id"]?.GetValue<int>() == assignmentId && a["status"]?.GetValue<string>() is "Published" or "Closed");
            var enrolled = assignment is not null && CanonicalJsonStore.GetArray(root, "enrolled_subjects").OfType<JsonObject>().Any(e =>
                e["student_id"]?.GetValue<int>() == studentId && e["status"]?.GetValue<string>() != "dropped"
                && e["class_offering_id"]?.GetValue<int>() == assignment["class_offering_id"]?.GetValue<int>());
            if (assignment is null || !enrolled) { error = "That assignment is not available to you."; return; }
            if (assignment["status"]?.GetValue<string>() == "Closed") { error = "This assignment is closed."; return; }

            var submissions = CanonicalJsonStore.GetOrCreateArray(root, "assignment_submissions");
            var existing = submissions.OfType<JsonObject>().FirstOrDefault(s =>
                s["assignment_id"]?.GetValue<int>() == assignmentId && s["student_id"]?.GetValue<int>() == studentId);
            if (existing?["score"] is JsonValue) { error = "Your instructor has already scored this. It can no longer be changed."; return; }

            var now = DateTime.UtcNow;
            var due = DateTime.TryParse(assignment["due_date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTime?)null;
            if (existing is null)
            {
                existing = new JsonObject { ["id"] = CanonicalJsonStore.NextId(submissions), ["assignment_id"] = assignmentId, ["student_id"] = studentId };
                submissions.Add(existing);
            }
            existing["text"] = answer;
            existing["submitted_at"] = now.ToString("O", CultureInfo.InvariantCulture);
            existing["is_late"] = due is { } d && now > d;
            existing["status"] = "Submitted";
        });

        if (error is null) await LogActivityAsync(studentId, $"Submitted assignment #{assignmentId}");
        return error;
    }
}
