using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Contracts;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[Route("api/registrations")]
public sealed class RegistrationsController(RegistrarApiStore store) : RegistrarControllerBase(store)
{
    [HttpGet("mine")]
    [Authorize]
    public IActionResult Mine() =>
        GetCurrentStudentId() is { } studentId ? Ok(Store.GetRegistrations(studentId)) : MissingStudent();

    [HttpPost]
    [Authorize]
    public IActionResult Create(EnrollmentRequestDto request)
    {
        if (GetCurrentStudentId() is not { } studentId) return MissingStudent();
        if (string.IsNullOrWhiteSpace(request.Semester)) return BadRequest("Semester is required.");

        try
        {
            var registration = Store.Register(studentId, request);
            return Created($"/api/registrations/{registration.Id}", registration);
        }
        catch (RegistrarApiException ex)
        {
            return ApiError(ex);
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize]
    public IActionResult Delete(int id, [FromQuery] string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) return BadRequest("rowVersion is required.");
        if (GetCurrentStudentId() is not { } studentId) return MissingStudent();

        try
        {
            Store.Drop(studentId, id, rowVersion);
            return NoContent();
        }
        catch (RegistrarApiException ex)
        {
            return ApiError(ex);
        }
    }
}
