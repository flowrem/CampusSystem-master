using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[ApiController]
public abstract class RegistrarControllerBase(RegistrarApiStore store) : ControllerBase
{
    protected RegistrarApiStore Store { get; } = store;

    /// <summary>The signed-in student's <c>users.id</c>, or null when the claim is missing or names no student.</summary>
    protected int? GetCurrentStudentId() =>
        int.TryParse(User.FindFirstValue("StudentId"), out var id) && Store.StudentExists(id) ? id : null;

    protected IActionResult MissingStudent() => Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Student identity is not available",
        detail: "The authenticated student claim does not resolve to a registered student.");

    protected IActionResult ApiError(RegistrarApiException ex) => Problem(statusCode: ex.StatusCode, detail: ex.Message);
}
