using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;
using TestingMain.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<JsonCollectionBrowser>();

builder.Services.AddRazorPages();

// This console edits every department's data, so only administrators may open it.
builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "Testing Console",
    CookieName = ".CampusSystem.Testing",
    Roles = ["admin"],
    HomeFor = _ => "/",
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
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
