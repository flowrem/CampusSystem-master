using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Contracts;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[Route("api/records")]
public sealed class RecordsController(RegistrarApiStore store) : RegistrarControllerBase(store)
{
    [HttpGet("mine")]
    [Authorize]
    public IActionResult Mine() =>
        GetCurrentStudentId() is { } studentId ? Ok(Store.GetRecordsRequests(studentId)) : MissingStudent();

    [HttpPost]
    [Authorize]
    public IActionResult Create(RecordsRequestDto request)
    {
        if (GetCurrentStudentId() is not { } studentId) return MissingStudent();
        if (string.IsNullOrWhiteSpace(request.DocumentType)) return BadRequest("Document type is required.");

        var response = Store.CreateRecordsRequest(studentId, request.DocumentType);
        return Created($"/api/records/{response.Id}", response);
    }
}
