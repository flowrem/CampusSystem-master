using System.Security.Claims;
using CampusSystem.Data.Auth;

namespace FinanceMain.Security;

public static class FinanceRoles
{
    public const string Finance = "finance";
    public const string Admin = "admin";
    public const string Student = "student";
    public const string Faculty = "faculty";
}

/// <summary>
/// The signed-in user. A student always sees their own records. Finance staff can view a student's pages by adding
/// <c>?studentId=</c> to the address.
/// </summary>
public sealed class CurrentUserContext(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsStudent => string.Equals(User.GetRole(), FinanceRoles.Student, StringComparison.OrdinalIgnoreCase);

    /// <summary>users.id of the student whose pages are shown, or 0 when a staff member has not chosen one.</summary>
    public int GetCurrentStudentId()
    {
        if (IsStudent) return User.GetUserId() ?? 0;
        return int.TryParse(accessor.HttpContext?.Request.Query["studentId"], out var id) ? id : 0;
    }

    public string StudentNumber => User.FindFirstValue("IdNumber") ?? "";

    /// <summary>Students may open only their own documents. Staff may open any.</summary>
    public bool CanView(string studentNumber) =>
        !IsStudent || string.Equals(StudentNumber, studentNumber, StringComparison.OrdinalIgnoreCase);
}
