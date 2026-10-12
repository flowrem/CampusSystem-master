using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[Route("api/transcript")]
public sealed class TranscriptController(RegistrarApiStore store) : RegistrarControllerBase(store)
{
    [HttpGet("mine")]
    [Authorize]
    public IActionResult Mine() =>
        GetCurrentStudentId() is { } studentId ? Ok(Store.GetTranscript(studentId)) : MissingStudent();
}
