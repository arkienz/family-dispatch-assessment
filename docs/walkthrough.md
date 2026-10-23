# Walkthrough

A guided tour of what was built, in the order of the brief's requirements. It doubles as
the script for the screen recording (about 6 minutes). Every account uses the password
`Demo-Pass-2026!`; the login page has a **Sign in as** button for each one.

The bar under the menu always shows who is signed in, their role and their company, so
every screen says whose data it is.

You need any small PDF to upload (a fake one is fine).

## 1. Sign-in (requirement 1)

1. Open the site. Signed-out visitors land on **Sign in**.
2. Type `owner@alpha.example` with a wrong password: "Email or password is incorrect."
   An email that doesn't exist gets the same message, so sign-in can't be used to find
   out which accounts exist.
3. Click **Sign in as** next to Olivia Grant (Owner, Alpha Transport).

## 2. Company dashboard (requirements 2 and 3)

As **owner@alpha.example**, the Dashboard shows:

- counts: Assigned, Picked Up, Delivered, Drivers, Trucks;
- Alpha's loads ALP-1001, ALP-1002 and ALP-1003, with driver, truck, status and document count;
- Alpha's drivers (Dan Rivera, Sam Cole) with their open loads, and trucks A-101, A-102.

Nothing from Beta Logistics appears anywhere. Open **Drivers** and **Trucks** in the menu
for the full lists.

## 3. Cross-company security test (requirements 7 and 8)

Still as **owner@alpha.example**:

1. Open `/Loads/1` (ALP-1001, Alpha's own load): the load page.
2. Change the address to `/Loads/4`, which is **BET-2001, a Beta Logistics load**:
   **"Not found in your company"**, HTTP 404.
3. Open `/Loads/99999`, which doesn't exist: the exact same page. Alpha can't even tell
   that load 4 exists.

This is enforced in the database query, not by hiding links: every query is filtered to
the company in the signed-in user's cookie. Requests that try to *change* Beta data
(move its status, upload to it, reassign it, download its document) have no button at
all; they were sent by hand and are all refused too, with the Beta data unchanged. See
[security-test-results.md](security-test-results.md) for the recorded run and the 114
automated tests.

## 4. Load status workflow (requirement 5)

Sign out, then **Sign in as** Dan Rivera, **driver1@alpha.example**.

1. The menu shows only **My loads**: ALP-1001 and ALP-1002. ALP-1003 belongs to Sam
   Cole and is not listed (requirement 4).
2. Open ALP-1001 (Assigned). The status card shows Assigned → Picked Up → Delivered and
   one button: **Mark as Picked Up**. Click it: "ALP-1001 marked as Picked Up." and the
   pickup time is recorded.
3. Click **Mark as Delivered** (the note says Delivered is final). The button is gone:
   "Delivered ... No further steps."

The server owns the rule (one step forward, never skip, never back); a skip or a step
back sent by hand is refused with "That request wasn't accepted". If someone else moved
the load while your page was open, you get "This load changed first" instead.

## 5. Driver view (requirement 4)

Still as **driver1@alpha.example**:

1. Open `/Loads/3` (ALP-1003, Sam Cole's load): **404**. A driver can't open another
   driver's load in the same company.
2. Open `/Dashboard`: **403 "Your role can't do this"**.

## 6. Delivery document (requirement 6)

Still as **driver1@alpha.example**, open ALP-1002:

1. Under **Documents**, choose a small PDF and click **Upload**: "Document attached to
   ALP-1002." The file is listed with its size, uploader and time.
2. Click the file name: it downloads.
3. Try a file that isn't a PDF/JPG/PNG (or a `.txt` renamed to `.pdf`): "Only PDF, JPG or
   PNG files are accepted." The file signature (its first bytes) is checked, not just the
   extension. A file over 5 MB is stopped in the browser with "File is larger than 5 MB."

Files are stored outside the website folder under a random name, and only come back
through a download that applies the same company and driver checks as the load.

## 7. Roles inside one company (requirement 2)

**Sign in as** Marcus Lee, **dispatch@alpha.example**:

1. **Loads → New load**: create `ALP-1004`, Austin, TX → Dallas, TX, driver Sam Cole.
   It opens with "ALP-1004 created and assigned." The driver list only offers Alpha's
   drivers, and the server checks again on save.
2. On ALP-1004, **Change driver/truck** to Dan Rivera (allowed while the load is Assigned).
3. Open **Trucks**: "Only the Owner can add trucks." Open `/Trucks/Create` directly:
   **403**. Signed in as the Owner, the same page works.

## 8. The other company

**Sign in as** Priya Shah, **owner@beta.example**: the Dashboard shows only BET-2001,
BET-2002, Leo Park and truck B-201. Opening `/Loads/1` (an Alpha load) gives the same
404 the other way round.

## What to say about the design (for the recording)

- One ASP.NET Core app with server-rendered pages and SQLite: simple to run and review.
- The company comes only from the sign-in cookie. EF Core query filters add it to every
  query, saves stamp it on new rows, and a row can never move to another company.
- Ids from forms (driver, truck) are checked against the user's own company.
- Other companies' records answer 404, the same as an id that doesn't exist.
- Roles are server policies; the driver restriction is in the query. Buttons are hidden
  only for convenience.
- 114 automated tests send real HTTP requests to the real app; the main guards were
  removed on purpose to show the tests catch each one.
