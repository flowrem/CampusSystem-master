using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using FacultyPortalMain.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<FacultyJsonStore>();
builder.Services.AddSingleton<CourseworkStore>();
builder.Services.AddRazorPages();
builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "Faculty Portal",
    CookieName = ".CampusSystem.Faculty",
    Roles = ["faculty", "admin"],
    HomeFor = _ => "/Roster",
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

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

