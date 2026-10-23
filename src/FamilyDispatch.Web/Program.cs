using System.Security.Claims;
using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using FamilyDispatch.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<DocumentStorage>();
builder.Services.AddMemoryCache();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/app.db";
var dbFile = new SqliteConnectionStringBuilder(connectionString).DataSource;
if (Path.GetDirectoryName(Path.GetFullPath(dbFile)) is { } dbDir) Directory.CreateDirectory(dbDir);
// Not AddDbContextPool: the query filters read the current user from each context instance.
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.Cookie.Name = "fd.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        // A signed-in user without the right role gets a real 403 (rendered by /Error/403), not a redirect.
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Every page needs a signed-in user unless it opts out with [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.OfficeStaff, p => p.RequireRole(nameof(Role.Owner), nameof(Role.Dispatcher)))
    .AddPolicy(Policies.OwnerOnly, p => p.RequireRole(nameof(Role.Owner)));

// TLS ends at the host's proxy (Render). Trust its X-Forwarded-Proto so the app knows the request was https:
// otherwise HTTPS redirection loops and the Secure auth cookie is never sent.
// Any proxy is trusted, but ForwardLimit stays 1: only the LAST X-Forwarded-For entry is used, so a client-sent
// X-Forwarded-For can't choose it. On Render that last entry is a proxy hop that varies between requests (checked
// live), not the client's IP, which is why sign-in is also limited per account (Login.cshtml.cs).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Password guessing: sign-in POSTs are limited per client IP. GETs of the login page are not.
// On Render this "IP" is a proxy hop (Cloudflare/Render), not the person, so it is only a rough extra layer;
// the dependable guard is the per-account failed sign-in limit in Pages/Account/Login.cshtml.cs.
// Uploads are limited per signed-in user, so the public demo accounts can't fill the server's disk.
// Configurable so the test suite, which signs in and uploads from one address, can raise them.
var loginPerMinute = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
var uploadsPerHour = builder.Configuration.GetValue("RateLimit:UploadsPerHour", 30);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimits.Login, ctx => HttpMethods.IsPost(ctx.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1) })
        : RateLimitPartition.GetNoLimiter("get"));
    // On the load page only the Upload handler counts; status moves and page views don't.
    o.AddPolicy(RateLimits.Upload, ctx => HttpMethods.IsPost(ctx.Request.Method)
                                          && string.Equals(ctx.Request.Query["handler"], "Upload", StringComparison.OrdinalIgnoreCase)
        ? RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadsPerHour, Window = TimeSpan.FromHours(1) })
        : RateLimitPartition.GetNoLimiter("other"));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    SeedData.EnsureSeeded(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
// 403/404/400/409/429 with an empty body get a readable page; no stack traces anywhere.
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseHttpsRedirection();
// Security headers on every response. All scripts and styles are files on this site (wwwroot/js/app.js,
// Bootstrap), so nothing inline needs allowing; data: images are Bootstrap's menu icon.
app.Use((ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                                    "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    headers.XContentTypeOptions = "nosniff"; // matters most for downloaded documents
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["X-Robots-Tag"] = "noindex, nofollow"; // a demo, not a site to be found by search engines
    return next();
});
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication: the upload limit is per signed-in user
app.UseAuthorization();

// Bootstrap and site CSS/JS are public; without this the fallback policy hides them from the login page.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

// Document download. Signed-in only (fallback policy). The document must belong to a load the user can see:
// the Load filter covers company and driver, the LoadDocument filter covers company, and the pair must match.
app.MapGet("/Loads/{loadId:int}/Documents/{docId:int}", async (int loadId, int docId, AppDbContext db, DocumentStorage storage,
    HttpContext http, ILogger<DocumentStorage> log) =>
{
    var doc = await db.LoadDocuments.AsNoTracking()
        .FirstOrDefaultAsync(d => d.Id == docId && d.LoadId == loadId && db.Loads.Any(l => l.Id == loadId));
    if (doc is null) return Results.NotFound();
    var stream = storage.Open(doc.CompanyId, doc.StoredFileName);
    if (stream is null)
    {
        // Expected on the free host after a restart wipes the disk; anything else is worth a look.
        log.LogWarning("Document {DocId} has a database row but no file on disk", doc.Id);
        return Results.NotFound();
    }
    // Delivery documents shouldn't stay in the browser cache on a shared PC.
    http.Response.Headers.CacheControl = "no-store";
    return Results.File(stream, doc.ContentType, fileDownloadName: doc.OriginalFileName);
});

app.Run();

// Exposed so WebApplicationFactory<Program> can host the app in tests.
public partial class Program { }
