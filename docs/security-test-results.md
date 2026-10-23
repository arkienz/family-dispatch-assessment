# Security test results

Run on 2026-10-23 against the app in **Production mode** (no developer error pages),
on a fresh database seeded with the demo data. The live-site check is in section 4.

Seeded ids used below: Alpha Transport loads are 1-3 (ALP-1001..1003), Beta Logistics
loads are 4-5 (BET-2001, BET-2002). User 3 is Dan Rivera (Alpha driver), user 7 is Leo
Park (Beta driver).

## 1. The cross-company test the brief asks for

**Signed in as an Alpha user, request a Beta load directly by id. It must be denied.**

Try it in a browser:

1. Open the login page and click **Sign in as** next to `owner@alpha.example`.
2. Open `/Loads/1` (ALP-1001, an Alpha load): the load page shows.
3. Open `/Loads/4` (BET-2001, a Beta load): **"Not found in your company"**, HTTP 404.
4. Open `/Loads/99999` (does not exist): the same page.

The 404 is deliberate. A 403 would confirm that load 4 exists in some other company;
the response for another company's load is the same as for an id that does not exist,
apart from the per-response antiforgery token in the sign-out form.

### Repeat it yourself

[`docs/cross-company-check.sh`](cross-company-check.sh) runs the whole test against any
running copy with one command (needs `sh` and `curl`; Git Bash works on Windows):

```bash
sh docs/cross-company-check.sh https://<live-url>
```

For a local run add `-k` to accept the development certificate. It assumes fresh demo
data (the free host reseeds on every restart) and signs in four demo users, so wait a
minute between runs (sign-in limit: 10 per minute per IP).

### Recorded run (output of the script above, unedited)

```text
$ sh docs/cross-company-check.sh https://localhost:7184 -k
### 1. Alpha owner (owner@alpha.example) reads loads by id
Alpha owner GET /Loads/1 -> 200 "ALP-1001 "
Alpha owner GET /Loads/4 -> 404 "Not found in your company"
Alpha owner GET /Loads/99999 -> 404 "Not found in your company"
/Loads/4 (a real Beta load) and /Loads/99999 (no such load) are identical apart from the antiforgery token.
Beta names, refs or trucks in Alpha's /Loads, /Dashboard, /Drivers, /Trucks: 0

### 2. Beta owner uploads a delivery document to BET-2001 (load 4)
Beta owner POST /Loads/4?handler=Upload -> 302
Beta's document: /Loads/4/Documents/1
Beta owner GET /Loads/4/Documents/1 -> 200

### 3. Alpha users try to reach or change Beta data directly
Alpha owner GET /Loads/4/Documents/1 -> 404 "Not found in your company"
Alpha owner (Beta's document via an Alpha load) GET /Loads/1/Documents/1 -> 404 "Not found in your company"
Alpha dispatcher POST /Loads/4?handler=Status target=PickedUp -> 404
Alpha dispatcher POST /Loads/4?handler=Upload -> 404
Alpha dispatcher POST /Loads/4/Assign -> 404
Alpha dispatcher POST /Loads/1/Assign DriverId=7 (Leo Park, a Beta driver) -> 200, form shows: "Choose a driver from your company."
Afterwards, as Beta owner: BET-2001 status "Assigned", documents listed: 1
As Alpha owner: ALP-1001 driver still "Dan Rivera"

### 4. Roles inside one company
driver1@alpha.example (driver2's load) GET /Loads/3 -> 404 "Not found in your company"
driver1@alpha.example GET /Dashboard -> 403 "Your role can't do this"
dispatch@alpha.example (Owner-only page) GET /Trucks/Create -> 403 "Your role can't do this"
Anonymous: GET /Loads/4 -> 302 to https://localhost:7184/Account/Login?ReturnUrl=%2FLoads%2F4
```

The write attempts in section 3 have no button in the UI (an Alpha user never sees a
Beta load). They are sent by hand with a valid antiforgery token from the Alpha user's
own page, which is what an attacker would do.

## 2. Automated tests

```text
dotnet test
Passed!  - Failed:     0, Passed:   114, Skipped:     0, Total:   114, Duration: 11 s - FamilyDispatch.Tests.dll (net10.0)
```

The HTTP tests start the real app in Production mode (`WebApplicationFactory`) with its
own temporary SQLite database and upload folder, sign in through the real login form,
and send real requests with antiforgery tokens. Where a test expects "unchanged", it
reads the database directly (bypassing the filters) to prove nothing was written.

| Test class | Tests | What they prove |
|---|---|---|
| `TenantIsolationTests` | 8 | Alpha owner, dispatcher and driver get 404 on Beta loads by id, and Beta on Alpha's; that 404 is identical to a missing id; the Loads, Dashboard, Drivers and Trucks pages hold no other-company refs, trucks, drivers or company name |
| `StatusTests` | 12 | **Alpha dispatcher moving a Beta load: 404, Beta load unchanged**; driver1 moving driver2's load: 404; skipping a step, going back, garbage values and a missing antiforgery token refused with the 400 page; a page that is out of date (someone moved the load since) gets 409 "This load changed first", not a second move; the status form sends the status it showed; a driver runs their own load to Delivered with both times stamped; a dispatcher moves a same-company load |
| `DocumentTests` | 18 | **Alpha upload to a Beta load: 404, no row and no file written**; **Alpha download of a Beta document: 404 via Beta's load and via an Alpha load**; document paired with the wrong load: 404; driver2 can't download driver1's document or upload to driver1's load; anonymous download goes to login; an .exe renamed .pdf, a .txt, an empty file, a file over 5 MB and a body over the 6 MB request cap refused with nothing stored (the last with the readable 400 page, no crash text); real PDF/PNG/JPEG accepted; download returns the same bytes as an attachment with `nosniff` and `Cache-Control: no-store`, stored under a random name; file names lose folders, control characters and invisible right-to-left overrides |
| `ManagementTests` | 19 | **Beta driver or Beta truck on an Alpha load refused (create and assign), nothing created or changed**; **a posted `CompanyId` field is ignored**; assign on a Beta load: 404; a non-driver can't be assigned; references unique per company only; empty New load and Add truck forms show validation messages (not a 500); assign refused once picked up; drivers 403 on create and assign; Dispatcher 403 on add truck (GET and POST) with nothing created; Owner adds a truck under their own company |
| `DriverIsolationTests` | 3 | Driver1's list shows only their loads; their own load opens; driver2's load is 404 |
| `RoleTests` | 15 | Anonymous users go to login; drivers 403 on Dashboard, Drivers and Trucks; office staff open the Dashboard; wrong password and unknown email show the same message; login without antiforgery token refused; auth cookie `HttpOnly; Secure; SameSite=Lax`; error pages show no exception details; CSS/JS public so the login page is styled |
| `LoadWorkflowTests` | 15 | All 9 status pairs (only Assigned -> Picked Up -> Delivered allowed); driver/truck can change only before pickup; an undefined status value refused; each move stamps its time; a refused move leaves the load unchanged |
| `RateLimitTests` | 3 | After 3 sign-in attempts in a minute (limit lowered for the test; 10 in the app) the next one gets 429 "Too many sign-in attempts" even with the right password, and no auth cookie; viewing the login page is not limited; a client rotating fake leading `X-Forwarded-For` entries still hits the limit, because only the last entry (the proxy's) counts; two different client IPs get separate allowances (proves the forwarded header is really used) |
| `AccountLockoutTests` | 3 | After 3 failed sign-ins (limit lowered for the test; 10 in the app), each from a different fake IP, the account gets 429 "Too many failed sign-ins for this account" even with the right password and no auth cookie, while other accounts still sign in; an unknown email locks the same way (reveals no accounts); a successful sign-in resets the count |
| `SecurityHeaderTests` | 6 | Every response (a page, a redirect, a static file, an error page) carries a `Content-Security-Policy` with `script-src 'self'`, `frame-ancestors 'none'` and no `unsafe-inline`, plus `nosniff`, `Referrer-Policy`, `Permissions-Policy` and `X-Robots-Tag: noindex`; the login and load pages contain no inline script, so the strict policy doesn't break them |
| `UploadLimitTests` | 1 | With the limit lowered to 2 per hour, a user's third upload gets 429 "Too many attempts" and nothing is stored; the same user can still move the load and open the page, and another user of the same company can still upload |
| `AppDbContextTests` | 11 | The tenant filter is evaluated per request, not cached across users; load filter by company and by driver, queried directly; a missing or unknown role fails closed (sees only its own loads); new rows are stamped with the user's company; saving a row for another company, moving a row to another company, or saving without a company throws; the database refuses a row pointing at a company that doesn't exist and a duplicate reference within one company (another company may reuse it); two simultaneous status moves can't both win |

Items in bold are the cross-company write attempts.

### Do the tests catch a real mistake? (mutation check)

Each guard was removed on purpose, the suite run, and the code restored (33 guards):

| Guard removed | Tests that failed |
|---|---|
| Company filter on loads (and on users, which also hides joined loads) | 6 |
| Company filter on loads only | 1 (`LoadFilter_OfficeUser_SeesOnlyOwnCompanysLoads`) |
| Driver-only condition on loads | 3 |
| Driver filter fails open again (an unknown role treated as office staff) | 1 |
| Status rule (allow skipping a step) | 3 |
| Concurrency check on status | 1 |
| Out-of-date page answered with 409 | 1 |
| Status form sends the status it showed | 1 |
| Download: load must be visible to the user | 1 |
| Download: document must belong to that load | 1 |
| Download: `Cache-Control: no-store` | 1 |
| File signature check (magic bytes) | 1 |
| Hidden characters removed from file names | 3 |
| Error page keeps the status of a bad request (oversized upload) | 1 |
| Driver id from a form checked against own company | 3 |
| Driver must have the Driver role | 1 |
| Truck id from a form checked against own company | 2 |
| Driver/truck change only while Assigned | 3 |
| Empty New load form handled | 1 |
| Empty Add truck form handled | 1 |
| Database: unique reference per company | 1 |
| Database: tenant row must point at a real company | 1 |
| Add truck limited to Owner | 1 |
| Static assets public (regression of a bug found in step 5) | 2 |
| Rate limit on the login page | 2 |
| Forwarded-for: use only the last entry (`ForwardLimit = 1`) | 1 |
| Content-Security-Policy header | 4 |
| `X-Robots-Tag: noindex` header | 3 |
| No inline script (one put back on the login page) | 1 |
| Upload limit on the load page | 1 |
| Rate limiter after authentication (before it, all users share one upload allowance) | 1 |
| Per-account failed sign-in lockout | 2 |
| Successful sign-in resets the failure count | 1 |

One finding from this check: with only the load filter removed, the HTTP tests stayed
green, because every load page also joins the load's driver and the user filter hid the
Beta driver. That is a second layer of protection, but it masked the first, so a test
that queries loads directly (no join) was added.

## 3. Bugs found while testing and reviewing

| Found by | Bug | Fix |
|---|---|---|
| Container-style run (step 5) | Sign-in was required for CSS/JS too, so the login page loaded unstyled | Static assets marked public, plus 2 regression tests |
| `DocumentTests` (step 7) | `UploadedByUserId` is not EF Core's conventional foreign-key name, so EF added a hidden `UploadedById` column that was always 0 and every upload failed | Foreign key named explicitly in `AppDbContext` |
| Review agents (all three) | An empty New load or Add truck form crashed with a 500: the reference was trimmed before validation, and an empty field arrives as null | Trim only what was posted; tests post empty forms |
| Code review, confirmed by a test | An upload over the 6 MB request cap showed "Something went wrong": the exception handler keeps the 400 status, but the error page assumed 500 when the path had no code | The error page uses the response's status; the upload form also checks the 5 MB limit in the browser |
| Code review | Every 400 said "Loads move one step at a time", even for an expired login form | One general 400 message |
| Code review | Two tabs on one load: the late click got "not allowed" instead of "This load changed first" | The form sends the status it showed; a mismatch answers 409 |
| Security review | The rate-limit test would have passed with forwarded headers turned off | Test that two client IPs get separate allowances |
| Live check on Render (section 4) | The per-IP sign-in limit keyed on a Render/Cloudflare proxy hop, not the visitor, so attempts spread over proxies weren't stopped | Failed sign-ins limited per account (`AccountLockoutTests`); SQL command logging turned down to warnings in Production (the live log showed every statement) |
| Code review | The driver filter treated a missing role as office staff (not reachable today: the server writes the role) | Filter on "is Owner or Dispatcher", so it fails closed |

## 4. Live site

Checked on 2026-10-23 at 13:05-13:11 PH time against **https://family-dispatch-demo.onrender.com** (Render free tier,
Production mode, Docker image built by Render from https://github.com/arkienz/family-dispatch-assessment).

### Cross-company test (output of the script, unedited)

```text
$ sh docs/cross-company-check.sh https://family-dispatch-demo.onrender.com
### 1. Alpha owner (owner@alpha.example) reads loads by id
Alpha owner GET /Loads/1 -> 200 "ALP-1001 "
Alpha owner GET /Loads/4 -> 404 "Not found in your company"
Alpha owner GET /Loads/99999 -> 404 "Not found in your company"
/Loads/4 (a real Beta load) and /Loads/99999 (no such load) are identical apart from the antiforgery token.
Beta names, refs or trucks in Alpha's /Loads, /Dashboard, /Drivers, /Trucks: 0

### 2. Beta owner uploads a delivery document to BET-2001 (load 4)
Beta owner POST /Loads/4?handler=Upload -> 302
Beta's document: /Loads/4/Documents/1
Beta owner GET /Loads/4/Documents/1 -> 200

### 3. Alpha users try to reach or change Beta data directly
Alpha owner GET /Loads/4/Documents/1 -> 404 "Not found in your company"
Alpha owner (Beta's document via an Alpha load) GET /Loads/1/Documents/1 -> 404 "Not found in your company"
Alpha dispatcher POST /Loads/4?handler=Status target=PickedUp -> 404
Alpha dispatcher POST /Loads/4?handler=Upload -> 404
Alpha dispatcher POST /Loads/4/Assign -> 404
Alpha dispatcher POST /Loads/1/Assign DriverId=7 (Leo Park, a Beta driver) -> 200, form shows: "Choose a driver from your company."
Afterwards, as Beta owner: BET-2001 status "Assigned", documents listed: 1
As Alpha owner: ALP-1001 driver still "Dan Rivera"

### 4. Roles inside one company
driver1@alpha.example (driver2's load) GET /Loads/3 -> 404 "Not found in your company"
driver1@alpha.example GET /Dashboard -> 403 "Your role can't do this"
dispatch@alpha.example (Owner-only page) GET /Trucks/Create -> 403 "Your role can't do this"
Anonymous: GET /Loads/4 -> 302 to https://family-dispatch-demo.onrender.com/Account/Login?ReturnUrl=%2FLoads%2F4
```

### Transport, headers and exposure

| Check | Result |
|---|---|
| `curl -I https://family-dispatch-demo.onrender.com/Account/Login` | 200 with `strict-transport-security: max-age=2592000`, the full `content-security-policy` (`script-src 'self'`, `frame-ancestors 'none'`), `x-content-type-options: nosniff`, `referrer-policy: no-referrer`, `permissions-policy`, `x-robots-tag: noindex, nofollow` |
| Plain HTTP | `http://...` answers 301 to `https://...` |
| Private files, signed out: `/.git/config`, `/.env`, `/app.db`, `/App_Data/app.db`, `/data/app.db`, `/appsettings.json`, `/appsettings.Development.json`, `/FamilyDispatch.Web.dll`, `/web.config`, `/swagger`, `/uploads/` | every one 302 to the login page, 0 bytes of content |
| The same paths, signed in as owner@alpha.example | every one 404 "Not found in your company" (the app's own page), never the file |
| Error page | the app's page ("Something went wrong"), no exception text or stack trace |

### Finding: the client IP behind Render's proxies

The sign-in limit per IP was tested live by sending sign-in attempts in quick succession:

```text
control, no X-Forwarded-For, 21 attempts (13:08:30):
200 200 200 200 200 200 200 200 200 200 429 429 200 429 429 200 200 200 200 200 200 (13:09:05)
rotating fake X-Forwarded-For, 21 attempts (13:10:10):
200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 200 (13:10:49)
```

With one client IP the control run would block every attempt after the tenth until the
window reset. The 200s between 429s show the attempts landing in different allowances:
the last `X-Forwarded-For` entry, the one the app reads, is a Render/Cloudflare proxy hop
that changes between requests, not the visitor's IP. So the per-IP limit counts per proxy
hop. The automated tests had assumed one proxy that appends the real client IP last, which
is not how this host works.

**Fix:** failed sign-ins are now limited **per account** in the login page (10 failures in
5 minutes, then that account is refused even with the right password; unknown emails count
the same way, so the lockout reveals nothing). This holds whatever the network path.
Tests: `AccountLockoutTests` (3), each attempt from a different fake IP. The per-IP limit
stays as a rough extra layer, and the README says what it really does.

**Re-checked live after the fix was deployed (commit `5b22e84`, 2026-10-23 13:26):**

```text
driver2@alpha.example, 10 wrong passwords: 10 x 200 "Email or password is incorrect."
then the right password -> HTTP 429 "Too many failed sign-ins for this account. Wait 5 minutes, then try again." (no auth cookie)
driver1@alpha.example, right password -> 302 to /, auth cookie set (other accounts unaffected)
```
