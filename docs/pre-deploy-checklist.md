# Pre-deploy checklist

## 2026-10-23, live check after the first deploy (https://family-dispatch-demo.onrender.com)

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 4 | Rate limits | fixed (verified live) | Live test showed the per-IP sign-in limit keyed on a varying Render/Cloudflare proxy hop (`docs/security-test-results.md` section 4). Added a per-account failed sign-in limit (10 per 5 min, `Pages/Account/Login.cshtml.cs`), `AccountLockoutTests` (3). Re-checked live on commit `5b22e84`: 10 wrong passwords then the right one gives 429 with no auth cookie; another account still signs in |
| 7 | Sign-in and sessions | open (medium, documented) | Unchanged: sign-out doesn't revoke a copied cookie. The per-account lockout above closes the "no lockout" part of item 7 |
| 9 | Browser and transport hardening | pass | `curl -I` on the live site: HSTS, CSP, nosniff, Referrer-Policy, Permissions-Policy, X-Robots-Tag; HTTP 301 to HTTPS (security results section 4) |
| 10 | Production config and exposure | pass | Live: Production mode (Render log), private paths 302 to login signed out and 404 signed in, never served; error page without stack trace; git history clean (run below). SQL command logging lowered to warnings in `appsettings.json` (the live log printed every statement, with parameter values hidden) |

Open: item 7 (documented), item 6 only before real users. Ready for the Demo release.

## 2026-10-23, prototype v1, re-run after fixes (before first deploy; no git repo yet)

Same app shape and level as the run below. Items 4, 9 and the item 11 note were fixed;
every other row is unchanged from the run below.

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 4 | Spend cap + rate limits | fixed | Uploads limited to 30 per hour per signed-in user (`RateLimits.Upload` policy in `Program.cs`, `[EnableRateLimiting]` on `Pages/Loads/Details.cshtml.cs`; counts Upload POSTs only); rate limiter moved after authentication so the limit is per user. Test `UploadLimitTests` (3rd upload at limit 2 gets 429, nothing stored, other actions and other users unaffected). Remaining (documented): no total disk cap |
| 9 | Browser and transport hardening | fixed (live check after deploy) | `Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`, `Referrer-Policy: no-referrer`, `Permissions-Policy: camera=(), microphone=(), geolocation=()` on every response (`Program.cs` header middleware); both inline scripts moved to `wwwroot/js/app.js`. Tests `SecurityHeaderTests` (6). Checked in the browser pane: Sign in as and the 5 MB check work, no CSP violations in the console, Bootstrap CSS/JS and its data: icon load. **After deploy:** `curl -I https://<live-url>/Account/Login` |
| 11 | Public website basics | N/A (note done) | `X-Robots-Tag: noindex, nofollow` on every response, so the demo stays out of search results (`SecurityHeaderTests`) |

Mutation check: removing the CSP header (4 tests fail), the noindex header (3), putting an
inline script back (1), removing the upload limit (1) or running the limiter before
authentication (1) is each caught. `dotnet test`: 111/111.

Still open: item 7 (sign-out doesn't revoke a copied cookie; documented), items 9 and 10
live checks after deploy, item 6 only before real users.

Item 10, git history (after the first push to https://github.com/arkienz/family-dispatch-assessment,
public, 1 commit, 117 files): no database, key, `.env`, backup, `bin`/`obj`, `.claude` or client
brief file anywhere in history; `git log -p` searched with the pre-push hook's secret patterns: no
hits; no client name or engagement terms. **pass.**

## 2026-10-23, prototype v1 (before first deploy; no git repo yet)

App shape: one ASP.NET Core (.NET 10) server-rendered web app with sign-in, two tenants,
file uploads stored on the server's disk, SQLite. No AI model, no paid or metered
services, no email/SMS. Release level: **Demo** (link for the client's reviewers,
fictional sample data only, Render free tier, data reseeds on restart).

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Access rules on the server, per row | pass | Global query filters on every tenant table, driver filter fails closed (`Data/AppDbContext.cs:27-32`); company only from the cookie (`Security/CurrentUser.cs`); one `IgnoreQueryFilters` (login, `Pages/Account/Login.cshtml.cs`); tests `TenantIsolationTests`, `DriverIsolationTests`, cross-company writes in `StatusTests`/`DocumentTests`/`ManagementTests`; recorded run in `docs/security-test-results.md` §1 |
| 2 | No secrets in anything shipped | pass | No API keys in the app at all; grep for Stripe, GitHub, AWS, Google and Anthropic key prefixes, private key blocks and hardcoded passwords: no hits (only the documented fake demo password `Demo-Pass-2026!`); no `.env`, key or backup files; `app.db` excluded by `.gitignore` (`*.db`, `App_Data/`) and `.dockerignore`; client brief gitignored |
| 3 | Storage locked down | pass | Files outside `wwwroot` under `{Storage:Root}/{companyId}/{guid}` (`Services/DocumentStorage.cs`); PDF/JPG/PNG by file signature, 5 MB, 6 MB request cap (`Pages/Loads/Details.cshtml.cs`); download only through the tenant-checked endpoint (`Program.cs`), `attachment` + `nosniff` + `no-store`; tests in `DocumentTests` |
| 4 | Spend cap + rate limits | open (small) | No metered service, Render free tier has no bill. Sign-in limited to 10/min per IP (`Program.cs` rate limiter, `RateLimitTests`). **Missing:** a per-user upload limit; with the public demo password anyone can fill the free disk (README Known issues). Fix: a per-user policy on the upload handler, ~20 min |
| 5 | AI input/output untrusted | N/A | No AI model: no AI packages or calls in `src` (grep for openai/anthropic/langchain/ollama: none) |
| 6 | Legal: terms, privacy, cookies, claims | open (not required at Demo level) | Fictional data only, no real users, so no Terms or privacy notice exist; both are needed before any real user. **Cookies:** only strictly necessary ones (auth `fd.auth`, antiforgery, TempData); no analytics, trackers or third-party scripts (Bootstrap served locally), so no banner is needed. **Claims** (README): "tenant isolation enforced on the server" (tests above), "104 tests passing" (`dotnet test` output in security results), "passwords hashed with PBKDF2" (`PasswordHasher<User>`, `Program.cs`), "10 sign-in attempts per minute" (`RateLimitTests`): all backed. "Not affiliated with any real Family Dispatch & Logistics system" stated in the README |
| 7 | Sign-in, sessions, route protection | open (medium) | Pass: fallback policy requires sign-in on every page and endpoint; role policies return 403 (`RoleTests`, `ManagementTests`); PBKDF2 hashing; same error for unknown email and wrong password, dummy hash equalises timing (`Login.cshtml.cs`); cookie `HttpOnly; Secure; SameSite=Lax` (`Program.cs:32-35`, `AuthCookie_IsHttpOnlySecureSameSite`); antiforgery on every form (tests); no password reset to secure. **Open:** sign-out deletes the browser cookie but a copied cookie stays valid up to 8 h sliding, and claims aren't re-checked (README Known issues). Fix: security stamp checked in `OnValidatePrincipal`, ~45 min |
| 8 | Untrusted input and output | pass | Server-side DataAnnotations with lengths on every form (`Loads/Create`, `Trucks/Create`, `Login`); driver/truck ids checked against own company (`Pages/Loads/LoadForm.cs`); EF Core only, no raw SQL (grep); Razor encodes output, no `Html.Raw`; uploads checked by signature, renamed, outside web root, never executed; no server-side fetches; `ReturnUrl` checked with `Url.IsLocalUrl` |
| 9 | Browser and transport hardening | open (small) | Pass: HTTPS redirect and HSTS in Production (`Program.cs:89,94`, HSTS confirmed in the container-style run), `X-Content-Type-Options: nosniff` on every response (`Program.cs:98`), no CORS (same-origin only), no third-party scripts. **Missing:** `Content-Security-Policy`, `frame-ancestors`, `Referrer-Policy`, `Permissions-Policy` (two pages have inline scripts that must move to a file first). Fix ~20 min. **After deploy:** `curl -I https://<live-url>/Account/Login` |
| 10 | Production config, exposure, dependencies | open (after deploy) | Pass: `ASPNETCORE_ENVIRONMENT=Production` and non-root user in `Dockerfile`; developer exception page only in Development; no Swagger/OpenAPI; demo accounts are intentional and documented; `dotnet list package --vulnerable --include-transitive`: no vulnerable packages; logs hold no passwords or tokens. Backups: not needed at Demo level (sample data reseeds on every start). **After deploy:** request `/.git/config`, `/.env`, `/app.db`, `/App_Data/app.db`, `/appsettings.json` from outside and expect 404; scan git history for secrets once the repo exists (`git log -p` searched for the same key prefixes as item 2) |
| 11 | Public website basics | N/A | No marketing site or landing page; the link is the app itself. Worth adding: `<meta name="robots" content="noindex">` so the demo stays out of search results (1 line) |
| 12 | Accessibility and designed look | pass | Every input has a label (`asp-for`), helper text linked with `aria-describedby`; Bootstrap focus rings kept; status badges carry text, stepper marks done steps in text and `aria-current` (`Pages/Loads/Details.cshtml`); status button 48 px tall, no sideways scroll at 375 px (walkthrough run); no motion, emoji or gradients. Plain Bootstrap on purpose (the brief grades function, not visuals). Not done: a keyboard-only pass and a dark-mode check (the app has no dark mode) |

Open items, in order of value for this release:

1. **Item 9:** security headers (CSP, frame-ancestors, Referrer-Policy, Permissions-Policy), moving the two inline scripts into a file. Small.
2. **Item 11 note:** `noindex` meta tag. Tiny.
3. **Item 4:** per-user upload rate limit. Small.
4. **Item 7:** sign-out revokes copied cookies (security stamp). Medium; documented in Known issues.
5. **Items 9 and 10, after deploy:** live `curl -I`, exposure requests and the git history scan.
6. **Item 6:** Terms and privacy notice, only before real users (not for this demo).
