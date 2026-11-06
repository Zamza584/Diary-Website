using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Extensions.AspNetCore.DataProtection.Blobs;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoteKeeping;
using NoteKeeping.Components;
using NoteKeeping.Data;
using NoteKeeping.Models;
using NoteKeeping.Services;

const string ExternalCookieScheme = "External";
const string GoogleScheme = "Google";

static CookieSecurePolicy GetSecureCookiePolicy(IWebHostEnvironment environment)
{
    return environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
}

static ClaimsPrincipal CreateUserPrincipal(UserAccount user)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, user.Name),
        new(ClaimTypes.Email, user.Email)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    return new ClaimsPrincipal(identity);
}

static DirectoryInfo GetDataProtectionKeyDirectory(
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    var keyDirectoryPath = configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keyDirectoryPath))
    {
        keyDirectoryPath = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "DataProtection-Keys");
    }
    else if (!Path.IsPathRooted(keyDirectoryPath))
    {
        keyDirectoryPath = Path.Combine(environment.ContentRootPath, keyDirectoryPath);
    }

    return new DirectoryInfo(keyDirectoryPath);
}

string? sqliteImportPath = null;
var applicationArgs = args;
string? dataProtectionCommand = null;
string? dataProtectionBlobUriArgument = null;
if (args.Length > 0 && args[0] == "--import-sqlite")
{
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        throw new ArgumentException("Usage: --import-sqlite <path-to-sqlite-database>");
    }

    sqliteImportPath = args[1];
    applicationArgs = [];
}
else if (args.Length > 0 &&
         args[0] is "--migrate-data-protection-key-ring" or "--validate-data-protection-key-ring")
{
    if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        throw new ArgumentException(
            $"Usage: {args[0]} <https-blob-uri>");
    }

    dataProtectionCommand = args[0];
    dataProtectionBlobUriArgument = args[1];
    applicationArgs = [];
}

var builder = WebApplication.CreateBuilder(applicationArgs);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

if (dataProtectionCommand is not null)
{
    if (!Uri.TryCreate(dataProtectionBlobUriArgument, UriKind.Absolute, out var migrationBlobUri))
    {
        throw new ArgumentException("The Azure Data Protection key repository URI is not valid.");
    }

    if (dataProtectionCommand == "--migrate-data-protection-key-ring")
    {
        await DataProtectionKeyRingMigration.MigrateAsync(
            GetDataProtectionKeyDirectory(builder.Configuration, builder.Environment),
            migrationBlobUri);
    }
    else
    {
        await DataProtectionKeyRingMigration.ValidateAsync(
            GetDataProtectionKeyDirectory(builder.Configuration, builder.Environment),
            migrationBlobUri,
            builder.Configuration.GetConnectionString("DefaultConnection"));
    }

    return;
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure the PostgreSQL connection string using ConnectionStrings:DefaultConnection or the ConnectionStrings__DefaultConnection environment variable.");
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dataProtectionBlobUri = builder.Configuration["DataProtection:Azure:BlobUri"];
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("NoteKeeping");

if (!string.IsNullOrWhiteSpace(dataProtectionBlobUri))
{
    if (!Uri.TryCreate(dataProtectionBlobUri, UriKind.Absolute, out var blobUri) ||
        blobUri.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(blobUri.Query) ||
        !string.IsNullOrEmpty(blobUri.UserInfo) ||
        !string.IsNullOrEmpty(blobUri.Fragment))
    {
        throw new InvalidOperationException(
            "DataProtection:Azure:BlobUri must be an HTTPS blob URI without credentials or a query string.");
    }

    TokenCredential credential;
    if (builder.Environment.IsProduction())
    {
        var managedIdentityClientId = builder.Configuration["DataProtection:Azure:ManagedIdentityClientId"];
        credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
            ? new ManagedIdentityCredential()
            : new ManagedIdentityCredential(managedIdentityClientId);
    }
    else
    {
        credential = new DefaultAzureCredential();
    }

    dataProtection.PersistKeysToAzureBlobStorage(blobUri, credential);
}
else if (builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Configure DataProtection:Azure:BlobUri to a persistent shared Azure Blob before running in production.");
}
else
{
    dataProtection.PersistKeysToFileSystem(
        GetDataProtectionKeyDirectory(builder.Configuration, builder.Environment));
}

builder.Services.AddDbContextFactory<NoteKeepingDbContext>(options =>
    options.UseNpgsql(connectionString));

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
builder.Services.AddSingleton<NoteTextEncryption>();
builder.Services.AddSingleton<NotebookTitleEncryption>();
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var googleAuthenticationConfigured =
    !string.IsNullOrWhiteSpace(googleClientId) &&
    !string.IsNullOrWhiteSpace(googleClientSecret);

var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
authenticationBuilder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
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

if (googleAuthenticationConfigured)
{
    authenticationBuilder.AddCookie(ExternalCookieScheme, options =>
    {
        options.Cookie.Name = "NoteKeeping.External";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = GetSecureCookiePolicy(builder.Environment);
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    });

    authenticationBuilder.AddGoogle(GoogleScheme, options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.SignInScheme = ExternalCookieScheme;
    });
}

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ThemeService>();

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

    if (sqliteImportPath is not null)
    {
        await SqliteToPostgresImporter.ImportAsync(dbContext, sqliteImportPath);
    }

    await NoteTextEncryptionMigration.EncryptLegacyNotesAsync(
        dbContext,
        scope.ServiceProvider.GetRequiredService<NoteTextEncryption>());

    await NotebookTitleEncryptionMigration.EncryptLegacyTitlesAsync(
        dbContext,
        scope.ServiceProvider.GetRequiredService<NotebookTitleEncryption>());

    if (sqliteImportPath is not null)
    {
        return;
    }
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

app.MapGet("/auth/google", (IConfiguration configuration) =>
{
    var clientId = configuration["Authentication:Google:ClientId"];
    var clientSecret = configuration["Authentication:Google:ClientSecret"];
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        return Results.Redirect("/login?error=google-not-configured");
    }

    var properties = new AuthenticationProperties
    {
        RedirectUri = "/auth/google-callback"
    };

    return Results.Challenge(properties, [GoogleScheme]);
});

app.MapGet("/auth/google-callback", async (HttpContext httpContext, NoteKeepingDbContext dbContext) =>
{
    var externalResult = await httpContext.AuthenticateAsync(ExternalCookieScheme);
    await httpContext.SignOutAsync(ExternalCookieScheme);

    if (!externalResult.Succeeded || externalResult.Principal is null)
    {
        return Results.Redirect("/login?error=google-failed");
    }

    var email = externalResult.Principal.FindFirstValue(ClaimTypes.Email)
        ?? externalResult.Principal.FindFirstValue("email");
    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect("/login?error=google-email");
    }

    var normalizedEmail = email.Trim().ToLowerInvariant();
    var user = await dbContext.UserAccounts
        .SingleOrDefaultAsync(account => account.Email.ToLower() == normalizedEmail);

    if (user is null)
    {
        var name = externalResult.Principal.FindFirstValue(ClaimTypes.Name);
        user = new UserAccount
        {
            Name = string.IsNullOrWhiteSpace(name) ? normalizedEmail.Split('@')[0] : name,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.UserAccounts.Add(user);
        await dbContext.SaveChangesAsync();
    }

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        CreateUserPrincipal(user),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

    return Results.Redirect("/");
});

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

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreateUserPrincipal(user), new AuthenticationProperties
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
