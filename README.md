# Family Dispatch: multi-tenant trucking prototype

A small multi-tenant trucking SaaS prototype built as a paid technical
assessment. Two fictional companies (Alpha Transport, Beta Logistics) each
have an Owner, a Dispatcher, Drivers, Trucks and Loads. The main design
constraint is **tenant isolation enforced on the server**: a user of one
company can never read or change another company's data, whatever request
they send. All data is fictional sample data. Not affiliated with any real
Family Dispatch & Logistics system.

**Live demo:** [family-dispatch-demo.onrender.com](https://family-dispatch-demo.onrender.com) · **Source:** [github.com/arkienz/family-dispatch-assessment](https://github.com/arkienz/family-dispatch-assessment) ·
**Security test results:** [docs/security-test-results.md](docs/security-test-results.md) ·
**Walkthrough:** [docs/walkthrough.md](docs/walkthrough.md)

The free host sleeps when idle: the first request can take 30-60 seconds.

## What it does

- **Sign-in** for the sample users: cookie authentication, passwords hashed with PBKDF2
  (ASP.NET Core `PasswordHasher`), the same error for an unknown email and a wrong password,
  and at most 10 failed sign-ins per account in 5 minutes, after which that account is
  refused (even with the right password) until the 5 minutes are up. Uploads are limited
  to 30 per hour per user, so the public demo accounts can't fill the server's disk.
- **Roles**: Company Owner, Dispatcher, Driver. Each sees only what their role needs
  (table under [Roles](#roles)).
- **Company dashboard** (Owner, Dispatcher): load counts by status, the company's
  loads, drivers with their open loads, trucks.
- **Driver view**: "My loads" shows only the loads assigned to the signed-in driver.
- **Load status workflow**: Assigned -> Picked Up -> Delivered, one step at a time,
  never skipped or reversed, with the time of each step recorded. A page that is out of
  date (someone else moved the load) gets "This load changed first" instead of a double move.
- **Delivery documents**: upload a PDF, JPG or PNG (up to 5 MB) to a load and download
  it again; files are checked by their file signature (first bytes), not just the
  extension, and stored outside the web root.
- **Load management**: office staff create loads and change the driver/truck while a
  load is still Assigned; only the Owner adds trucks.
- **Multi-tenant security**: every query is scoped to the signed-in user's company on
  the server; another company's records answer 404, exactly like an id that doesn't exist.

## Demo logins

Fake, demo-only credentials. Password for every account: `Demo-Pass-2026!`
The login page lists them with a one-click **Sign in as** button.

| Company | Name | Role | Email |
|---|---|---|---|
| Alpha Transport | Olivia Grant | Owner | owner@alpha.example |
| Alpha Transport | Marcus Lee | Dispatcher | dispatch@alpha.example |
| Alpha Transport | Dan Rivera | Driver | driver1@alpha.example |
| Alpha Transport | Sam Cole | Driver | driver2@alpha.example |
| Beta Logistics | Priya Shah | Owner | owner@beta.example |
| Beta Logistics | Ken Ortiz | Dispatcher | dispatch@beta.example |
| Beta Logistics | Leo Park | Driver | driver1@beta.example |

**The cross-company check in one minute:** sign in as `owner@alpha.example`, open
`/Loads/1` (ALP-1001, Alpha's own load: shown), then `/Loads/4` (BET-2001, a Beta load):
**404 "Not found in your company"**. As `dispatch@alpha.example`, open `/Trucks/Create`:
**403** (only the Owner adds trucks). Seeded ids: Alpha loads 1-3, Beta loads 4-5.

## Tech stack

| Part | Choice |
|---|---|
| Runtime | .NET 10 (SDK 10.0.401) |
| Web | ASP.NET Core Razor Pages (server-rendered), cookie authentication, Bootstrap 5.3 from the template |
| Data | EF Core 10.0.13 + SQLite |
| Tests | xUnit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.13 (`WebApplicationFactory`) |
| Hosting | Docker image on Render (free tier) |

No other packages. Password hashing uses `PasswordHasher<T>` from the ASP.NET Core
shared framework rather than full ASP.NET Core Identity (no extra tables or scaffolded UI
for seven demo users).

## Project layout

- `src/FamilyDispatch.Web/`: the web app
  - `Program.cs`: services, auth and policies, middleware order, document download endpoint
  - `Domain/`: entities, `ITenantOwned`, `LoadWorkflow` (the status rules)
  - `Data/AppDbContext.cs`: tenant query filters, `CompanyId` stamping; `Data/SeedData.cs`: demo data
  - `Security/CurrentUser.cs`: the signed-in user from the cookie; policy names
  - `Services/DocumentStorage.cs`: file validation and storage
  - `Pages/`: Login/Logout, Dashboard, Loads (list, details, create, assign), Drivers, Trucks, Error
- `tests/FamilyDispatch.Tests/`: unit and HTTP integration tests
- `docs/`: security test results, walkthrough, UI plan and mockup
- `Dockerfile`, `render.yaml`: container and Render deploy

## Build and run

Needs the .NET 10 SDK. The SQLite database is created and seeded with the demo data on
first start (`src/FamilyDispatch.Web/App_Data/app.db`); delete it to reset.

```bash
dotnet build
dotnet test
dotnet run --project src/FamilyDispatch.Web --launch-profile https
```

Then open https://localhost:7184.

**Container / hosting.** `Dockerfile` builds a Production image that listens on port 8080
as a non-root user and keeps its database and uploads in `/app/data` (set by
`ConnectionStrings__Default` and `Storage__Root`). `render.yaml` deploys it to Render's
free tier, which ends TLS at its proxy; the app reads `X-Forwarded-Proto` so HTTPS
redirection, HSTS and the Secure cookie work behind it. No secrets are needed: the only
credentials are the fake demo logins above.

## Architecture

One ASP.NET Core project with server-rendered Razor Pages: no separate front end, no
API layer to keep in sync, built-in antiforgery on every form, and every page testable
with plain HTTP requests. SQLite needs no database setup from a reviewer.

**Why this stack, not something else.** A single-page app plus a JSON API would double
the surface to secure (two projects, token handling, CORS) for no gain in a prototype
whose grade is security and isolation. Isolation is done with EF Core global query
filters in the application rather than database row-level security (PostgreSQL RLS),
because it works the same on SQLite, keeps the rule in C# next to the code that uses it,
and is easy to unit-test; on a production PostgreSQL I would add RLS underneath as a
second net. Full ASP.NET Core Identity was skipped for seven seeded users: its password
hasher is used on its own.

A request goes through, in this order:

1. **Forwarded headers** (Render's TLS proxy sets the real scheme and client IP).
2. **Error handling**: in Production an exception goes to the `/Error` page; HSTS is sent.
   Status code pages re-render any empty 400/403/404/409/429 through the same page. No
   page ever shows an exception or stack trace.
3. **HTTPS redirect**, then the **security headers** on every response: a strict
   `Content-Security-Policy` (scripts, styles and images only from this site; no inline
   script, all page behaviour is in `wwwroot/js/app.js`; no framing), `nosniff`,
   `Referrer-Policy: no-referrer`, a `Permissions-Policy` and `X-Robots-Tag: noindex`.
4. **Routing**, then **authentication**: the auth cookie (`HttpOnly`, `Secure`,
   `SameSite=Lax`, 8 h sliding) carries the user id, role and company id, written once
   at sign-in.
5. **Rate limiter** (after authentication, because the upload limit is per user): uploads
   per signed-in user, and a rough per-IP limit on sign-in POSTs. The dependable sign-in
   guard is the per-account failed sign-in limit in the login page itself (see Known
   issues for why the IP isn't enough on Render).
6. **Authorization**: a fallback policy requires a signed-in user on every page and
   endpoint; pages for office staff or the Owner carry a named policy. A signed-in user
   with the wrong role gets a real 403.
7. **Page handler**: reads and writes through `AppDbContext`, whose global query
   filters scope every query to the user's company (and a driver to their own loads).
   Status rules live in `LoadWorkflow`; files go through `DocumentStorage`.

Data model: `Company`; and, each with a `CompanyId`, `User` (Owner, Dispatcher or Driver),
`Truck`, `Load` (reference, route, pickup time, driver, optional truck, status and the
time of each step) and `LoadDocument` (load, original and stored file name, type, size,
uploader). All times are stored and shown in UTC.

## Tenant isolation

Enforced on the server in layers, so one missed check is not enough to leak data.

1. **The company comes only from the auth cookie.** `CurrentUser` reads the company id
   claim written at sign-in. No route, query string, form field or header can choose
   the company, and there are no `/companies/{id}/...` routes.
2. **Global query filters** in `AppDbContext` add `CompanyId = <user's company>` to every
   query on users, trucks, loads and documents. The load filter also adds
   `DriverId = <user>` for anyone who is not Owner or Dispatcher, so a missing or unknown
   role fails closed (sees only its own loads), not open. Pages don't add their own company checks, so none can be
   forgotten. The filter reads the current user on each request (a captured value would
   be cached in the EF model and leak across users; a test guards this), and context
   pooling is off for the same reason. With no signed-in user the filters match nothing.
3. **Writes are stamped and locked.** On save, every new row gets the user's company id;
   saving a row that names another company throws, and a row's company can never be
   changed after insert. The load form has no company field, so a posted `CompanyId` is
   ignored (tested). The database backs this up: every tenant row has a foreign key to a
   real company, and load references and truck units are unique per company.
4. **Ids from forms are checked.** The filter doesn't stop a foreign key that points into
   another company, so the driver and truck ids on Create and Assign are looked up
   through the filtered tables first: a Beta driver or truck is "not found" and refused.
5. **Files.** Stored outside `wwwroot` under `{Storage:Root}/{companyId}/{random name}`,
   never served as static files. The download endpoint returns a file only when the
   document is in the user's company, belongs to the requested load, and that load is
   visible to the user (so a driver gets only their own loads' documents).
6. **404, not 403, for other companies' records.** A Beta load requested by an Alpha
   user gets the same response as an id that doesn't exist, so ids can't be used to
   discover what exists in another company. 403 is used only for a role check inside
   your own company.
7. **One bypass, on purpose.** `IgnoreQueryFilters()` appears once in the app: the
   sign-in lookup by email, where there is no company yet. No raw SQL is used anywhere.

## Roles

Checked on the server by authorization policies (`OfficeStaff` = Owner or Dispatcher,
`OwnerOnly`) and, for drivers, by the load query filter. Buttons a role can't use are
hidden only for convenience; every one of them has a server check and a test behind it.

| Action | Owner | Dispatcher | Driver |
|---|---|---|---|
| Dashboard, Drivers, Trucks lists | yes | yes | 403 |
| See loads | all of the company's | all of the company's | only their own (others 404) |
| Move a load to the next status | yes | yes | own loads |
| Upload / download delivery documents | yes | yes | own loads |
| Create a load | yes | yes | 403 |
| Change driver/truck (only while Assigned) | yes | yes | 403 |
| Add a truck | yes | 403 | 403 |

Owner and Dispatcher are the same on loads; the Owner-only truck page gives a real,
testable 403 for a role difference within one company.

**How the brief was read:**

- *"An owner or authorized dispatcher"*: a user with the Dispatcher role in that company.
  There is no per-dispatcher grant in this prototype; adding one would be a flag on the
  user checked by the same `OfficeStaff` policy.
- *"An authorized user moves a load"*: anyone who can see the load. That is office staff
  for any company load, and a driver for their own loads only (drivers do the pickup and
  delivery in practice). Only the order of steps is restricted, not who takes which step.
- *"The application must deny the request"*: another company's record answers **404**
  rather than 403 on purpose, so the response doesn't confirm the record exists.

## Security test results

Full results, the recorded cross-company run and the test list:
[docs/security-test-results.md](docs/security-test-results.md).

- **Cross-company test:** signed in as an Alpha user, `GET /Loads/4` (a Beta load)
  returns 404, identical to a missing id. Changing its status, uploading to it,
  reassigning it, downloading its document and putting a Beta driver on an Alpha load
  were all refused, and the Beta data was unchanged.
- **Automated tests: 114, all passing** (`dotnet test`). They start the real app in
  Production mode and send real HTTP requests with antiforgery tokens, plus unit tests
  for the status rules and the data layer. The main guards (33 of them) were also removed
  one at a time on purpose to confirm a test fails without each.

## Known issues

- **Database schema** is created with `EnsureCreated`, not migrations (fine for a demo
  that reseeds; a real deployment needs migrations).
- **Sign-in limits are in memory:** the per-account failed sign-in count resets on a
  restart. Anyone who knows an email can lock that account for 5 minutes by failing 10
  times (the usual trade-off of a lockout; a CAPTCHA after a few failures would soften it).
- **Free hosting:** the disk resets on restart or redeploy (demo data comes back from the
  seed, uploaded documents are lost), the service sleeps after about 15 minutes idle, and
  a restart signs everyone out (data-protection keys are not persisted).
- **Sessions aren't checked against the database:** role and company changes apply at
  the next sign-in, and signing out deletes the browser's cookie but does not cancel a
  copy of it (a stolen cookie works until it expires; it slides for 8 h while used). The
  fix is a security stamp on the user, checked on each request and changed at sign-out.
- **Upload limit is per user and per hour (30), with no total cap:** the seven public demo
  accounts together could still add a few GB a day to the free host's disk, which a
  restart clears.
- **Global ids:** load and document ids are shared by all companies and are shown in the
  UI, so a user can tell roughly how many loads all companies created between two of
  their own. No records are revealed; per-company numbers would fix it.
- **Two simultaneous creates with the same reference:** the database refuses the second
  one, which then shows the generic error page instead of the form message.
- **Documents** can't be deleted or replaced; Delivered does not require a document.
- **No user management:** users come from the seed; there is no screen to add drivers.
- **Security headers:** all set (see Architecture), checked by tests; on the live site
  they are checked with `curl -I` after each deploy.
- **Client IP on Render:** the live check showed that the last `X-Forwarded-For` entry,
  which the app reads, is a Render/Cloudflare proxy hop that changes between requests, not
  the visitor's IP. A client still can't choose it (only the last entry is used), but the
  per-IP sign-in limit therefore counts per proxy hop, not per person. That is why sign-in
  is also limited per account. Reading the right header for Render's setup would make the
  per-IP limit real; it was not done because the header layout couldn't be confirmed from
  outside.
- **SQLite** suits one instance; more than one would need a server database.
- Times are shown in UTC only.

## AI tools

I used Claude Code (Anthropic's coding agent) for most of the typing on this project.
Here is how I split the work with it.

- **I set the direction.** Before any code, I agreed the stack and the isolation approach
  (one ASP.NET Core app, SQLite, EF Core query filters, 404 for other companies' records),
  then had it write a build plan with a fixed order of steps. I let it build one step at
  a time and approved each step before the next one started.
- **It wrote the code and the tests.** Every step ended with `dotnet test`. I also had it
  remove each main safeguard on purpose and rerun the tests, to prove a test fails when a
  guard is missing. That is how the first round of tests turned out to miss the load
  filter (the driver join was hiding it), which is now covered by its own test.
- **I had the result checked by fresh eyes.** Three separate review agents read the
  finished code without the builder's notes: one for security, one for code quality, and
  one that compared the work to this brief the way you would. They found no way across
  companies, but they did find real bugs, for example an empty "New load" form crashed the
  page. I chose which findings to fix; the list is in
  [docs/security-test-results.md](docs/security-test-results.md).
- **The live site taught me something the tests didn't.** After deploying, the
  sign-in limit per IP did not hold on Render, because the address the app sees is one
  of Render's proxies, not the visitor. I added a lockout per account instead and
  checked it on the live site.
- **The parts that are mine alone:** keeping the client's brief out of the public repo,
  the git history, the GitHub repo and the Render deploy.

I have gone through the code and can explain any part of it, including the trade-offs
listed under Known issues.

## Docs

- [docs/security-test-results.md](docs/security-test-results.md): cross-company test, test results
- [docs/cross-company-check.sh](docs/cross-company-check.sh): repeats the cross-company test against any running copy
- [docs/walkthrough.md](docs/walkthrough.md): a guided tour of the requirements
- [docs/pre-deploy-checklist.md](docs/pre-deploy-checklist.md): the pre-deploy security check and the live re-checks
- [docs/ui-plan.md](docs/ui-plan.md), [docs/ui-mockup.html](docs/ui-mockup.html): UI plan and static mockup
- The assessment brief is the client's own document and is not included in this repo.

## Status

All required features are built and tested, plus a per-account sign-in limit, an upload
limit and security headers (114 tests passing). Deployed on Render at https://family-dispatch-demo.onrender.com; the
cross-company test, security headers and file exposure were checked on the live site
([docs/security-test-results.md](docs/security-test-results.md) section 4). The pre-deploy
security check is recorded in [docs/pre-deploy-checklist.md](docs/pre-deploy-checklist.md).
Still open before submission: a recorded walkthrough.

## License

Proprietary. Assessment submission; all rights reserved.
