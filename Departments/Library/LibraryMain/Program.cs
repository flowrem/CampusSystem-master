using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using LibraryMain.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<LibraryDesk>();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "Library",
    CookieName = ".CampusSystem.Library",
    Roles = ["librarian", "admin", "student"],
    HomeFor = _ => "/",
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

