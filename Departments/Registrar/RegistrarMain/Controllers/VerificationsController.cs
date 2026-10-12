using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Contracts;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[Route("api/verifications")]
public sealed class VerificationsController(RegistrarApiStore store) : RegistrarControllerBase(store)
{
    [HttpGet("mine")]
    [Authorize]
    public IActionResult Mine() =>
        GetCurrentStudentId() is { } studentId ? Ok(Store.GetVerifications(studentId)) : MissingStudent();

    [HttpPost]
    [Authorize]
    public IActionResult Create([FromBody] VerificationRequestDto? request = null)
    {
        if (GetCurrentStudentId() is not { } studentId) return MissingStudent();

        var response = Store.CreateVerification(studentId, request?.Purpose);
        return Created($"/api/verifications/{response.Id}", response);
    }
}
