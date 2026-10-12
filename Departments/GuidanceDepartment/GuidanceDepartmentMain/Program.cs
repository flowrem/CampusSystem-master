using GuidanceDepartmentMain.Services;
using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<JsonStudentDirectory>();
builder.Services.AddSingleton<IStudentDirectory>(services => services.GetRequiredService<JsonStudentDirectory>());
builder.Services.AddSingleton<IGuidanceRequestStore, JsonGuidanceRequestStore>();
builder.Services.AddSingleton<IRefreshTokenStore, JsonRefreshTokenStore>();
builder.Services.AddSingleton<IAuditLogService, JsonAuditLogService>();
builder.Services.AddSingleton<GuidanceOperationsStore>();
builder.Services.AddScoped<ICounselorTriageService, CounselorTriageService>();
builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "NU Lipa Guidance Department",
    CookieName = ".CampusSystem.Guidance.Auth",
    Roles = ["guidance", "admin"],
    HomeFor = _ => "/dashboard.html"
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var isCounselorPage = path == "/" || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
    if (isCounselorPage && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/Login?returnUrl=" + Uri.EscapeDataString(path == "/" ? "/dashboard.html" : path));
        return;
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapCampusAuth();

app.Run();
