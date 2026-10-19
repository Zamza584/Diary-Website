using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoteKeeping;
using NoteKeeping.Components;
using NoteKeeping.Data;
using NoteKeeping.Models;
using NoteKeeping.Services;

static CookieSecurePolicy GetSecureCookiePolicy(IWebHostEnvironment environment)
{
    return environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var configuredDatabasePath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(configuredDatabasePath))
{
    configuredDatabasePath = builder.Environment.IsDevelopment()
        ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "NoteKeeping.db")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteKeeping", "NoteKeeping.db");
}

var databaseDirectory = Path.GetDirectoryName(configuredDatabasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={configuredDatabasePath}";

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<NoteKeepingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "NoteKeeping.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = GetSecureCookiePolicy(builder.Environment);
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddScoped<NotebookStorageService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "NoteKeeping.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = GetSecureCookiePolicy(builder.Environment);
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<ThemeService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString("0");
        }

        context.HttpContext.Response.Redirect("/login?error=rate-limited");
    };

    options.AddPolicy("login", httpContext =>
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var forwardedIp = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedIp))
        {
            remoteIp = forwardedIp.Split(',')[0].Trim();
        }

        return RateLimitPartition.GetFixedWindowLimiter(remoteIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NoteKeepingDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (!string.IsNullOrEmpty(path) &&
        (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseAntiforgery();

app.MapPost("/login", async (HttpContext httpContext, NoteKeepingDbContext dbContext, [FromForm] string email, [FromForm] string password) =>
{
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        return Results.Redirect("/login?error=missing");
    }

    var normalizedEmail = email.Trim();
    var user = await dbContext.UserAccounts
        .SingleOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail.ToLower());

    if (user is null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
    {
        return Results.Redirect("/login?error=invalid");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Name),
        new(ClaimTypes.Email, user.Email)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    });

    return Results.Redirect("/");
}).RequireRateLimiting("login");

app.MapPost("/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    httpContext.Session.Clear();
    return Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
