using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FamilyDispatch.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting(RateLimits.Login)] // the policy only counts POSTs (sign-in attempts)
public class LoginModel(AppDbContext db, IPasswordHasher<User> hasher, IMemoryCache cache, IConfiguration config) : PageModel
{
    public const string FailedMessage = "Email or password is incorrect.";
    public const string LockedMessage = "Too many failed sign-ins for this account. Wait 5 minutes, then try again.";

    // Failed sign-ins per account, the guard that holds whatever the network path: behind Render's proxies the
    // client IP the per-IP limit sees is a proxy hop, not the person. Unknown emails count the same way, so the
    // lockout doesn't reveal which accounts exist.
    // shortcut: kept in memory (resets on restart, per instance); a shared store if the app ever runs on several instances.
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    private sealed class FailureCount { public int Value; }

    // Verified when the email is unknown, so both failures take about the same time.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(null!, "not-a-real-password");

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? RedirectToPage("/Index") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var email = Email.Trim().ToLowerInvariant();
        var failuresKey = "login-failures:" + email;
        if (cache.TryGetValue(failuresKey, out FailureCount? failures)
            && failures!.Value >= config.GetValue("RateLimit:LoginFailuresPerAccount", 10))
        {
            // Checked before the password, so a correct guess during the lockout doesn't get through either.
            Error = LockedMessage;
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Page();
        }

        IActionResult Fail()
        {
            // The count's expiry is set once, at the first failure, so the window doesn't slide with each attempt.
            var count = cache.GetOrCreate(failuresKey, e =>
            {
                e.AbsoluteExpirationRelativeToNow = FailureWindow;
                return new FailureCount();
            })!;
            Interlocked.Increment(ref count.Value);
            Error = FailedMessage;
            return Page();
        }

        // The only IgnoreQueryFilters in the app: at sign-in there is no company yet to filter by.
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, DummyHash, Password);
            return Fail();
        }
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, Password) == PasswordVerificationResult.Failed)
            return Fail();

        cache.Remove(failuresKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AppClaims.CompanyId, user.CompanyId.ToString()),
            new(AppClaims.CompanyName, user.Company.Name),
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Index");
    }
}
