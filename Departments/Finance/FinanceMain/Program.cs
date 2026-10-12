using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using FinanceMain.Contracts;
using FinanceMain.Security;
using FinanceMain.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<FinanceJsonStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<IAssessmentService, JsonAssessmentService>();
builder.Services.AddScoped<IPaymentService, JsonPaymentService>();
builder.Services.AddScoped<IClearanceService, JsonClearanceService>();
builder.Services.AddScoped<IRequestService, JsonRequestService>();
builder.Services.AddScoped<IScholarshipService, JsonScholarshipService>();
builder.Services.AddScoped<IClassRosterProvider, JsonClassRosterProvider>();

builder.Services.AddRazorPages();

builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "Finance Office",
    CookieName = ".CampusSystem.Finance",
    Roles = [FinanceRoles.Finance, FinanceRoles.Admin, FinanceRoles.Student, FinanceRoles.Faculty],
    HomeFor = role => role switch
    {
        FinanceRoles.Student => "/StudentAccount",
        FinanceRoles.Faculty => "/FacultyClearance",
        _ => "/Dashboard",
    },
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapCampusAuth();

// Root → Finance admin executive dashboard.
app.MapGet("/", (HttpContext context) => Results.Redirect(context.User.GetRole() switch
{
    FinanceRoles.Student => "/StudentAccount",
    FinanceRoles.Faculty => "/FacultyClearance",
    _ => "/Dashboard",
}));

// Cross-Department REST API Endpoints for Registrar, StudentPortal, and FacultyPortal
app.MapPost("/api/finance/assess", async (EnrolleeAssessmentDto input, IAssessmentService assessmentService) =>
{
    if (input.StudentId is not > 0 && string.IsNullOrWhiteSpace(input.StudentNumber))
        return Results.BadRequest(new { succeeded = false, message = "Provide studentId or studentNumber." });
    if (string.IsNullOrWhiteSpace(input.AcademicYear) || string.IsNullOrWhiteSpace(input.Semester))
        return Results.BadRequest(new { succeeded = false, message = "academicYear and semester are required." });

    try
    {
        var req = new EnrolleeAssessmentRequest(
            input.StudentId ?? 0,
            input.StudentNumber ?? "",
            input.StudentName ?? "",
            input.Program ?? "",
            input.AcademicYear,
            input.Semester,
            input.TotalUnits);

        var res = await assessmentService.AssessEnrolleeAsync(req);
        return Results.Ok(new
        {
            succeeded = true,
            isExisting = res.IsExisting,
            message = res.IsExisting
                ? "Student was already assessed for this academic term. Duplicate charge prevented."
                : "Official Assessment Order generated and linked to enrollment.",
            assessment = res.Assessment
        });
    }
    catch (FinanceValidationException ex)
    {
        return Results.BadRequest(new { succeeded = false, message = ex.Message });
    }
})
.RequireRolesOrServiceKey(FinanceRoles.Finance, FinanceRoles.Admin, "registrar");

app.MapGet("/api/finance/assessment", async (string? studentNumber, string? term, IAssessmentService assessmentService) =>
{
    var list = await assessmentService.ListAsync(new AssessmentListFilter(
        term ?? "2026-2027", "1st Semester", null, studentNumber, 1, 5));
    var match = list.Items.FirstOrDefault();
    return match != null ? Results.Ok(new { succeeded = true, assessment = match }) : Results.NotFound(new { succeeded = false, message = "Assessment not found." });
})
.RequireRolesOrServiceKey(FinanceRoles.Finance, FinanceRoles.Admin, "registrar");

app.MapGet("/api/finance/clearance", async (string? studentNumber, IClearanceService clearanceService) =>
{
    var list = await clearanceService.ListAsync(new ClearanceListFilter(
        "2026-2027", "1st Semester", null, studentNumber, 1, 5));
    var match = list.Items.FirstOrDefault();
    return match != null ? Results.Ok(new { succeeded = true, clearance = match }) : Results.NotFound(new { succeeded = false, message = "Clearance not found." });
})
.RequireRolesOrServiceKey(FinanceRoles.Finance, FinanceRoles.Admin, "registrar");

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public record EnrolleeAssessmentDto(int? StudentId, string? StudentNumber, string? StudentName, string? Program, string? AcademicYear, string? Semester, int TotalUnits);

