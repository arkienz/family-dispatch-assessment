# UI plan: Family Dispatch prototype

Build guide for the pages listed in the README (section "Roles"). Plain and fast: the client grades security and structure, not looks. Static reference: `docs/ui-mockup.html` (Loads list + Load details, open it in a browser, drag the window to ~400px).

**Who and what "good" means.** The reviewer: switches between 7 accounts on a PC, often while screen-recording, and must see at a glance whose company and role they are in and that a cross-company request is refused. Drivers: on a phone at ~400px, one obvious next action per load.

**Not applied on purpose** (house-app standards, out of scope for a 24h assessment): sidebar, toast deck, fuzzy search (no search boxes; 5 loads), dark mode, sign-in scene, notification bell. Plain Bootstrap alerts instead of toasts.

## 1. Styling: Bootstrap 5 from the Razor Pages template
- Keep the template's `wwwroot/lib/bootstrap` (check it is 5.3+, needed for `text-bg-*` and `*-subtle`). Zero setup, the reviewer recognises it, forms/tables/navbar/alerts come ready. A classless CSS would need restyling for tables, badges and the navbar.
- Delete the template's Privacy page, footer and its `margin-bottom: 60px`.
- **Delete `html { font-size: 14px }`** from the template's `site.css` (keep 16px on all widths): 14px is small on a driver's phone and iOS zooms into inputs under 16px.
- One accent: Bootstrap `primary` (#0d6efd, white text 4.5:1). Used for the one main button per screen only.
- Add to `site.css`, nothing else:
```css
.btn { white-space: nowrap; }                     /* labels never wrap */
.table td, .table th { font-variant-numeric: tabular-nums; }
.context-bar { font-size: .9rem; }
.stepper { list-style: none; padding: 0; display: flex; gap: .5rem; flex-wrap: wrap; }
.stepper li { padding: .25rem .6rem; border: 1px solid var(--bs-border-color); border-radius: .375rem; }
.stepper li.done { background: var(--bs-success-bg-subtle); color: var(--bs-success-text-emphasis); }
.stepper li[aria-current="step"] { border-color: var(--bs-primary); font-weight: 600; }
```
- **Status badge, one place** (`Pages/Shared/_StatusBadge.cshtml` taking `LoadStatus`, or a static helper returning label + class). The text is always shown, so meaning never rests on color:

| Status | Label | Classes | Contrast |
|---|---|---|---|
| Assigned | Assigned | `badge bg-warning-subtle text-warning-emphasis border border-warning-subtle` | #664d03 on #fff3cd, ~9:1 |
| PickedUp | Picked Up | `badge bg-primary-subtle text-primary-emphasis border border-primary-subtle` | #052c65 on #cfe2ff, ~12:1 |
| Delivered | Delivered | `badge bg-success-subtle text-success-emphasis border border-success-subtle` | #0a3622 on #d1e7dd, ~12:1 |

- Dates: one format, `yyyy-MM-dd HH:mm 'UTC'`, via one helper.

## 2. Header (`_Layout.cshtml`)
- Navbar (template one, keep its collapse toggler): brand text "Family Dispatch (demo)". Links by role:
  - Owner / Dispatcher: Dashboard, Loads, Drivers, Trucks.
  - Driver: My loads.
  - Right side: **Sign out** as a `<form method="post" asp-page="/Account/Logout">` with a `btn btn-outline-secondary btn-sm` (POST + antiforgery, never a GET link).
- **Context bar** under the navbar, outside the collapse so it shows on phones too, on every signed-in page:
  `<div class="context-bar bg-body-tertiary border-bottom py-2"><div class="container">Signed in as <strong>Olivia Grant</strong> · Owner · <strong>Alpha Transport</strong></div></div>`
  Read name, role and company from the claims (add a `DisplayName` and `CompanyName` claim at sign-in so the layout needs no DB query). This is what makes every screenshot self-explaining.
- Page title `<h1 class="h3">` per page; `<title>` = "Page · Family Dispatch".
- Alerts after POST-redirect-GET: `TempData["Ok"]` -> `alert alert-success`, errors -> `alert alert-danger` with `role="alert"`. Rendered once in the layout above `@RenderBody()`.

## 3. Pages

**Login** (`/Account/Login`), two columns on PC (`col-md-5` form, `col-md-7` helper), stacked on phone.
- Form: Email (`type=email`, `autocomplete=username`, placeholder `owner@alpha.example`), Password (`autocomplete=current-password`), "Sign in" `btn-primary`. Error: one `alert-danger` "Email or password is incorrect." (same text for both cases).
- **Demo logins panel** (`card`): heading "Demo accounts (fake, for review only)", line "Password for all: `Demo-Pass-2026!`", then a table: Company | Name | Role | Email | button **Sign in as** (`btn btn-sm btn-outline-primary`, `type="button"`, `data-email=...`). Group rows by company. Six-line script fills the email and the password and calls `form.requestSubmit()`, so one click switches user. Without JS the table is still readable.
- Under it, the security check note (section 4).

**`/`**: redirect only. Office -> `/Dashboard`, Driver -> `/Loads`.

**Dashboard** (Owner, Dispatcher)
- Row of 5 small `card`s (`col-6 col-md`): Assigned, Picked Up, Delivered, Drivers, Trucks. Number large (`fs-3`), label under it. Each links to its list.
- "Loads" section: the shared `_LoadsTable` partial + "New load" `btn-primary` (the one primary action).
- Two columns on PC (`col-md-6`): Drivers (name, active loads), Trucks (unit, plate). Stack on phone.

**Loads list** (`/Loads`). Office: title "Loads" + "New load" button. Driver: title "My loads", no button.
- `_LoadsTable` partial, shared with Dashboard: `<div class="table-responsive"><table class="table table-striped table-hover align-middle">`.
- Columns: Load (Ref as the link to details, `#id` muted under it), Route ("Dallas, TX → Oklahoma City, OK"), Pickup (UTC), Driver (hidden for drivers), Truck, Status (badge), Docs (count).
- Phone: `d-none d-md-table-cell` on Pickup, Truck and Docs, so Load / Route / Status fit at 400px without scrolling. `table-responsive` catches anything left, inside the container: never page-level sideways scroll.
- Ref cell `text-nowrap` (else "ALP-1001" breaks at the hyphen at 400px); the link gets `d-inline-block py-2` for a ~40px tap height.

**Load details** (`/Loads/{id}`)
- Header row: `h1` "ALP-1001" + status badge, muted "Load #1". Right: "Change driver/truck" `btn-outline-secondary` (office only, only while Assigned), "Back to loads" link.
- Details `<dl class="row">`: Route, Pickup (UTC), Driver, Truck, Created, Picked up, Delivered ("Not yet" when null).
- **Status card**:
  - Stepper `<ol class="stepper" aria-label="Load status">`: Assigned → Picked Up → Delivered. Past steps `class="done"` with visually-hidden " (done)", current `aria-current="step"`.
  - Only the next valid action: a POST form (`asp-page-handler="Status"`, hidden `target` = next status) with one `btn btn-primary btn-lg` "Mark as Picked Up" or "Mark as Delivered" (`w-100` on phone: `col-12 col-sm-auto`). Under the Delivered button: "Delivered is final. It can't be moved back." When Delivered: no button, text "Delivered 2026-10-24 16:05 UTC. No further steps."
  - The server (LoadWorkflow) is the control; the hidden button is convenience. A skip sent by hand gets 400 (section Errors).
- **Documents card**:
  - Upload form (`enctype="multipart/form-data"`, `asp-page-handler="Upload"`): label "Delivery document", `<input type="file" accept=".pdf,.jpg,.jpeg,.png" class="form-control">`, helper `form-text` "PDF, JPG or PNG, up to 5 MB. Signed delivery receipt (POD) or a photo of it." Button "Upload" `btn-outline-primary` (secondary to the status action). Errors from validation in `alert-danger`: "Only PDF, JPG or PNG files are accepted." / "File is larger than 5 MB." / "Choose a file first."
  - List: striped table File (download link) | Size | Uploaded by | Uploaded (UTC). Download goes to `/Loads/{loadId}/Documents/{docId}`.
  - Empty: "No documents yet. Upload the signed delivery receipt here."

**Create load** (`/Loads/Create`, office)
- Fields, each with `<label>` and `asp-validation-for`: Reference (placeholder `ALP-1004`), Origin (`Dallas, TX`), Destination (`Oklahoma City, OK`), Pickup date and time UTC (`datetime-local`, label says UTC), Driver (select, required, first option "Choose a driver"), Truck (select, optional, first option "No truck yet").
- Helper under the selects: "Only your company's drivers and trucks are listed. The server checks again on save."
- Buttons: "Create load" `btn-primary`, "Cancel" link. Full width on PC is fine at `col-lg-8`; it is a short form.
- No drivers: replace the form with "Add a driver before creating loads. (No user management in this prototype; see README Known issues.)"

**Assign** (`/Loads/{id}/Assign`, office): same Driver/Truck selects preselected, load ref and route shown read-only above. If the load is not Assigned: no form, `alert-warning` "ALP-1002 is Picked Up. Driver and truck can only change while Assigned." Button "Save".

**Drivers** (`/Drivers`): striped table Name | Email | Active loads. Empty: "No drivers in Alpha Transport yet."

**Trucks** (`/Trucks`): striped table Unit | Plate. "Add truck" `btn-primary` shown to Owner only. For a Dispatcher, a muted line: "Only the Owner can add trucks." (says why, instead of a silently missing button). Empty: "No trucks yet." + the Owner's Add button.
**Trucks/Create** (Owner): Unit number (`A-103`), Plate (`TX 7KD-219`). "Add truck" / Cancel.

**Errors** (`/Error/{code}`, one page switching on the code, no stack trace in any of them, "Back to loads" link, context bar still shown when signed in):

| Code | Title | Text |
|---|---|---|
| 404 | Not found in your company | "This load or page doesn't exist in Alpha Transport. Records from other companies are hidden, so this page looks the same whether the record exists elsewhere or not." (company name from the claim) |
| 403 | Your role can't do this | "You're signed in as Dispatcher. This page or action is for another role (for example, only the Owner can add trucks)." |
| 400 | That change isn't allowed | "Loads move one step at a time: Assigned, then Picked Up, then Delivered." |
| 409 | This load changed first | "Someone updated this load a moment ago. Reload to see its current status." |
| 500 | Something went wrong | "Nothing was saved. Try again; if it keeps happening, note the time and tell the developer." |

Note: status-code re-execute only runs on an empty body, so return `NotFound()` / `Forbid()` / `BadRequest()` without content. The 400/409 from the Status handler can instead redirect back to Details with an `alert-danger`; either is fine, pick one.

**Empty states, all in one line + the next step:** Loads (office) "No loads yet." + New load; Loads (driver) "No loads assigned to you right now."; Documents, Drivers, Trucks as above; Dashboard counts show 0, never blank.

## 4. Showing the security test (screen recording)
- Login page, under the demo panel, a small `alert alert-info`: "**Security check:** sign in as owner@alpha.example, then open `/Loads/4` (BET-2001, a Beta Logistics load). You get 404 Not found in your company. Try `/Trucks/Create` as dispatch@alpha.example for 403." Confirm the real ids after seeding (Alpha 1-3, Beta 4-5 if seeded in that order).
- Showing `#id` on lists and details lets the reviewer see the id they are changing in the URL.
- The same steps go in the README and `docs/security-test-results.md`, with the test names that prove cross-company writes and downloads are refused (no UI path exists for those; that is the point).
- Rule for the builder: every hidden button has a server check behind it (policy, query filter, LoadWorkflow, FK check). Never add an `if (role)` in a `.cshtml` as the only guard.

## 5. Phone (~400px), driver pages first
- Template `viewport` meta stays. Container `container` (not fixed widths).
- Loads table: columns trimmed (section 3), no page sideways scroll. Details: `dl` rows stack (`col-sm-3` / `col-sm-9`), stepper wraps, status button full width and `btn-lg` (48px tap), upload input full width.
- Buttons one line (`white-space: nowrap`); button groups use `d-flex flex-wrap gap-2` so a second button drops to the next line instead of squeezing.
- Context bar stays visible (outside the collapsed menu).
- Check: Loads and Details as driver1@alpha.example at 400px, Dashboard at 768px, everything at PC width.

## 6. Accessibility basics
- Every input has a `<label for>` (Razor `asp-for` does this); helper text linked with `aria-describedby`; validation messages next to the field and a summary at the top.
- Focus: keep Bootstrap's focus rings; don't remove outlines in `site.css`.
- Contrast: Bootstrap body text and the subtle badges above pass AA; don't use `text-muted` on tinted backgrounds for anything important.
- Placeholders are examples, not label repeats: `owner@alpha.example`, `ALP-1004`, `Dallas, TX`, `Oklahoma City, OK`, `A-103`, `TX 7KD-219`.
- Alerts use `role="alert"` (errors) / `role="status"` (success), so a screen reader hears the result after a POST.
- `lang="en"`, one `h1` per page, tables with `<th scope="col">`.

## Seed display data used in the mockup (suggestions for `SeedData`)
Alpha Transport: Olivia Grant (Owner), Marcus Lee (Dispatcher), Dan Rivera (driver1), Sam Cole (driver2); trucks A-101 `TX 4KD-118`, A-102 `TX 9PM-552`.
Beta Logistics: Priya Shah (Owner), Ken Ortiz (Dispatcher), Leo Park (driver1); truck B-201 `TN 3RC-740`.
Loads: ALP-1001 Dallas, TX → Oklahoma City, OK (Dan, A-101, Assigned); ALP-1002 Fort Worth, TX → Shreveport, LA (Dan, A-101, Picked Up); ALP-1003 Austin, TX → Houston, TX (Sam, A-102, Assigned); BET-2001 Memphis, TN → Little Rock, AR (Leo, B-201, Assigned); BET-2002 Nashville, TN → Atlanta, GA (Leo, B-201, Picked Up).
