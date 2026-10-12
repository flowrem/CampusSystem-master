using RegistrarMain.HealthAndRepair;
using Microsoft.AspNetCore.Authentication;
using RegistrarMain.Contracts;
using RegistrarMain.Services;
using CampusSystem.Data.Auth;
using CampusSystem.Data.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication("DevelopmentTest")
        .AddScheme<AuthenticationSchemeOptions, DevelopmentTestAuthenticationHandler>("DevelopmentTest", _ => { });
}
builder.Services.AddAuthorization();

// Registrar Portal Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddSingleton<CanonicalJsonStore>();
builder.Services.AddSingleton<RegistrarApiStore>();
builder.Services.AddScoped<SettingsService>();

builder.Services.AddHttpClient();
builder.Services.AddRazorPages();
builder.Services.AddCampusAuth(new CampusAuthOptions
{
    AppName = "Registrar Office",
    CookieName = ".CampusSystem.Registrar",
    Roles = ["registrar", "admin"],
    HomeFor = _ => "/Dashboard",
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
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapCampusAuth();

app.MapGet("/", context =>
{
    context.Response.Redirect("/Dashboard");
    return Task.CompletedTask;
});

app.MapStaticAssets().AllowAnonymous();
app.MapControllers();
app.MapRazorPages();

app.Run();

