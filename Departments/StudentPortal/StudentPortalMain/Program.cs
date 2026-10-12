using StudentPortalMain.Services;
using CampusSystem.Data.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<CanonicalJsonStore>();

// Student Portal session services
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".CampusSystem.StudentPortal.Session";
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddScoped<StudentPortalDbService>();

builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// ─── Student Auth Guard ───────────────────────────────────────────────────
// Redirect unauthenticated users to /Login for every page except Login and Logout
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    bool isPublic = path.StartsWith("/Login", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/Logout", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);

    if (!isPublic)
    {
        var isAuthenticated = context.Session.GetInt32("is_authenticated") == 1;
        if (!isAuthenticated)
        {
            context.Response.Redirect("/Login");
            return;
        }
    }

    await next();
});

app.MapGet("/", context =>
{
    context.Response.Redirect("/Dashboard");
    return Task.CompletedTask;
});

app.MapRazorPages();

app.Run();
