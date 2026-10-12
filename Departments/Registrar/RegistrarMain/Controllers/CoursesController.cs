using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RegistrarMain.Contracts;
using RegistrarMain.Services;

namespace RegistrarMain.Controllers;

[Route("api/courses")]
public sealed class CoursesController(RegistrarApiStore store) : RegistrarControllerBase(store)
{
    [HttpGet]
    [Authorize]
    public ActionResult<IReadOnlyList<CourseDto>> List([FromQuery] string? search) =>
        Ok(Store.GetCourses(search));

    [HttpGet("{id:int}")]
    [Authorize]
    public ActionResult<CourseDto> Get(int id) =>
        Store.GetCourse(id) is { } course ? Ok(course) : NotFound();

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public IActionResult Create(CreateCourseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.Credits <= 0)
            return BadRequest("Code, title, and positive credits are required.");

        try
        {
            var course = Store.CreateCourse(request);
            return CreatedAtAction(nameof(Get), new { id = course.Id }, course);
        }
        catch (RegistrarApiException ex)
        {
            return ApiError(ex);
        }
    }
}
