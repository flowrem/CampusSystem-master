using System.Security.Claims;
using CampusSystem.Data.Auth;
using GuidanceDepartmentMain.Contracts;
using GuidanceDepartmentMain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GuidanceDepartmentMain.Controllers;

[ApiController]
[Route("api/guidance")]
[Authorize(AuthenticationSchemes = CampusAuth.Scheme, Roles = "guidance,admin")]
public sealed class GuidanceStudentController(IStudentDirectory students, IGuidanceRequestStore requests, GuidanceOperationsStore operations, ICounselorTriageService triage) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("students")]
    public ActionResult<IReadOnlyList<StudentSummaryDto>> GetStudents()
    {
        if (CampusAuth.HasServiceKey(HttpContext) || User.IsInRole("guidance") || User.IsInRole("admin")) return Ok(students.List());
        return User.Identity?.IsAuthenticated == true ? Forbid() : Unauthorized(new { error = "A Guidance sign-in or service key is required." });
    }

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<StudentRequestResponse>>> List([FromQuery] RequestStatus? status, [FromQuery] RequestUrgency? urgency, [FromQuery] int? counselorId, CancellationToken cancellationToken) =>
        Ok((await requests.ListAsync(null, new(status, urgency, counselorId), cancellationToken)).Select(ToResponse));

    [HttpPost("requests")]
    public async Task<ActionResult<StudentRequestResponse>> Create([FromBody] CreateRequest input, CancellationToken cancellationToken)
    {
        if (!students.Exists(input.StudentId) || string.IsNullOrWhiteSpace(input.Subject) || string.IsNullOrWhiteSpace(input.Details)) return BadRequest(new { error = "A valid student, subject, and details are required." });
        var request = new GuidanceRequestRecord { StudentId = input.StudentId, Subject = input.Subject.Trim(), Details = input.Details.Trim(), Urgency = input.Urgency, Status = RequestStatus.Requested };
        await requests.AddAsync(request, cancellationToken);
        return Created($"/api/guidance/requests/{request.Id}", ToResponse(request));
    }

    [HttpPost("requests/{id:int}/assign")]
    public async Task<ActionResult<StudentRequestResponse>> Assign(int id, [FromBody] AssignRequest input, CancellationToken cancellationToken)
    {
        var request = await requests.FindAsync(id, cancellationToken);
        if (request is null) return NotFound();
        var version = request.RowVersion;
        request.AssignedCounselorId = input.CounselorId;
        if (request.Status is RequestStatus.Requested or RequestStatus.ReferredIn) request.Status = RequestStatus.InProgress;
        try { await requests.SaveAsync(request, version, cancellationToken); }
        catch (ConcurrencyConflictException) { return Conflict(new { error = "The case changed; refresh and try again." }); }
        return Ok(ToResponse(request));
    }

    [HttpPost("requests/{id:int}/status")]
    public async Task<ActionResult<StudentRequestResponse>> ChangeStatus(int id, [FromBody] StatusRequest input, CancellationToken cancellationToken)
    {
        var request = await requests.FindAsync(id, cancellationToken);
        if (request is null) return NotFound();
        var version = request.RowVersion;
        request.Status = input.Status;
        try { await requests.SaveAsync(request, version, cancellationToken); }
        catch (ConcurrencyConflictException) { return Conflict(new { error = "The case changed; refresh and try again." }); }
        return Ok(ToResponse(request));
    }

    [HttpGet("case-notes")]
    public IActionResult CaseNotes([FromQuery] int? studentId) => Ok(operations.ListNotes(studentId));

    [HttpPost("case-notes")]
    public ActionResult<CaseNoteDto> AddCaseNote([FromBody] CreateCaseNoteRequest input)
    {
        if (!students.Exists(input.StudentId) || string.IsNullOrWhiteSpace(input.Content)) return BadRequest(new { error = "A valid student and note are required." });
        return Ok(operations.AddNote(input, User.FindFirstValue(ClaimTypes.Name) ?? "Guidance Office"));
    }

    [HttpGet("appointments")]
    public IActionResult Appointments([FromQuery] int? studentId) => Ok(operations.ListAppointments(studentId));

    [HttpPost("appointments")]
    public ActionResult<AppointmentDto> AddAppointment([FromBody] CreateAppointmentRequest input)
    {
        if (!students.Exists(input.StudentId) || input.AppointmentAt == default) return BadRequest(new { error = "A valid student and appointment time are required." });
        return Ok(operations.AddAppointment(input, User.FindFirstValue(ClaimTypes.Name) ?? "Guidance Office"));
    }

    [HttpPost("clearance")]
    public IActionResult SignClearance([FromBody] ClearanceRequest input)
    {
        if (!students.Exists(input.StudentId) || string.IsNullOrWhiteSpace(input.Status)) return BadRequest(new { error = "A valid student and clearance status are required." });
        operations.SignClearance(input, User.FindFirstValue(ClaimTypes.Name) ?? "Guidance Office");
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("/api/v1/incoming-referral")]
    public async Task<ActionResult<StudentRequestResponse>> IncomingReferral([FromBody] IncomingReferralDto referral, CancellationToken cancellationToken)
    {
        if (!CampusAuth.HasServiceKey(HttpContext) && !(User.Identity?.IsAuthenticated == true && (User.IsInRole("guidance") || User.IsInRole("admin")))) return Unauthorized(new { error = "A Guidance sign-in or service key is required." });
        var result = await triage.ProcessIncomingReferralAsync(referral, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.ErrorMessage });
    }

    private static StudentRequestResponse ToResponse(GuidanceRequestRecord item) => new(item.Id, item.StudentId, item.Subject, item.Details, item.SafetyValveText, item.Urgency, item.Status, item.AssignedCounselorId, item.RowVersion);
}

public record AssignRequest(int? CounselorId);
public record StatusRequest(RequestStatus Status);
public record CreateRequest(int StudentId, string Subject, string Details, RequestUrgency Urgency);
