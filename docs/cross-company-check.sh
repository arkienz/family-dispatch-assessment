#!/bin/sh
# Repeats the cross-company security test against a running copy of the app.
#
#   sh docs/cross-company-check.sh https://your-app.onrender.com
#   sh docs/cross-company-check.sh https://localhost:7184 -k     (-k: accept the local dev certificate)
#
# Needs sh and curl (Git Bash on Windows is fine). Assumes freshly seeded demo data: Alpha loads are ids 1-3,
# Beta loads 4-5, user 7 is Leo Park (a Beta driver). It signs in four demo users (the sign-in limit is
# 10 attempts a minute per IP, so wait a minute between runs) and uploads one fake PDF to a Beta load
# as the Beta owner.

B=${1:?usage: sh docs/cross-company-check.sh BASE_URL [extra curl options]}
B=${B%/}
shift
C="curl -s $*"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
# Upload by relative name: Git Bash's curl can't open a /tmp path given inside a -F value.
cd "$TMP" || exit 1
printf '%%PDF-1.4\nfake signed delivery receipt\n%%%%EOF\n' > pod.pdf

tok() { $C -b "$1" -c "$1" "$B$2" | grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*' | head -1 | sed 's/.*value="//'; }
login() {
    J="$TMP/$1.jar"
    T=$(tok "$J" /Account/Login)
    $C -b "$J" -c "$J" -o /dev/null --data-urlencode "Email=$1" --data-urlencode "Password=Demo-Pass-2026!" \
        --data-urlencode "__RequestVerificationToken=$T" "$B/Account/Login"
    echo "$J"
}
title() { grep -o '<h1 class="h3[^"]*">[^<]*' | sed 's/<[^>]*>//; s/&#x27;/'"'"'/g' | head -1; }
get() { R=$($C -b "$1" -w '\n%{http_code}' "$B$2"); echo "$3 GET $2 -> $(echo "$R" | tail -1) \"$(echo "$R" | title)\""; }
body() { $C -b "$1" "$B$2" | grep -v RequestVerificationToken; }

ALPHA=$(login owner@alpha.example); DISP=$(login dispatch@alpha.example)
BETA=$(login owner@beta.example); D1=$(login driver1@alpha.example)

echo "### 1. Alpha owner (owner@alpha.example) reads loads by id"
get "$ALPHA" /Loads/1 "Alpha owner"
get "$ALPHA" /Loads/4 "Alpha owner"
get "$ALPHA" /Loads/99999 "Alpha owner"
[ "$(body "$ALPHA" /Loads/4)" = "$(body "$ALPHA" /Loads/99999)" ] &&
    echo "/Loads/4 (a real Beta load) and /Loads/99999 (no such load) are identical apart from the antiforgery token."
echo "Beta names, refs or trucks in Alpha's /Loads, /Dashboard, /Drivers, /Trucks: $(for p in /Loads /Dashboard /Drivers /Trucks; do $C -b "$ALPHA" "$B$p"; done | grep -oE 'BET-[0-9]+|B-201|Leo Park|Beta Logistics' | wc -l | tr -d ' ')"

echo; echo "### 2. Beta owner uploads a delivery document to BET-2001 (load 4)"
T=$(tok "$BETA" /Loads)
$C -b "$BETA" -c "$BETA" -o /dev/null -w "Beta owner POST /Loads/4?handler=Upload -> %{http_code}\n" \
    -F "__RequestVerificationToken=$T" -F "file=@pod.pdf;type=application/pdf" "$B/Loads/4?handler=Upload"
DOC=$($C -b "$BETA" "$B/Loads/4" | grep -o '/Loads/4/Documents/[0-9]*' | head -1)
echo "Beta's document: $DOC"
$C -b "$BETA" -o /dev/null -w "Beta owner GET $DOC -> %{http_code}\n" "$B$DOC"

echo; echo "### 3. Alpha users try to reach or change Beta data directly"
get "$ALPHA" "$DOC" "Alpha owner"
get "$ALPHA" "/Loads/1/Documents/${DOC##*/}" "Alpha owner (Beta's document via an Alpha load)"
T=$(tok "$DISP" /Loads)
$C -b "$DISP" -c "$DISP" -o /dev/null -w "Alpha dispatcher POST /Loads/4?handler=Status target=PickedUp -> %{http_code}\n" \
    --data-urlencode "target=PickedUp" --data-urlencode "__RequestVerificationToken=$T" "$B/Loads/4?handler=Status"
T=$(tok "$DISP" /Loads)
$C -b "$DISP" -c "$DISP" -o /dev/null -w "Alpha dispatcher POST /Loads/4?handler=Upload -> %{http_code}\n" \
    -F "__RequestVerificationToken=$T" -F "file=@pod.pdf;type=application/pdf" "$B/Loads/4?handler=Upload"
T=$(tok "$DISP" /Loads)
$C -b "$DISP" -c "$DISP" -o /dev/null -w "Alpha dispatcher POST /Loads/4/Assign -> %{http_code}\n" \
    --data-urlencode "DriverId=3" --data-urlencode "__RequestVerificationToken=$T" "$B/Loads/4/Assign"
T=$(tok "$DISP" /Loads)
R=$($C -b "$DISP" -c "$DISP" -w '\n%{http_code}' --data-urlencode "DriverId=7" --data-urlencode "__RequestVerificationToken=$T" "$B/Loads/1/Assign")
echo "Alpha dispatcher POST /Loads/1/Assign DriverId=7 (Leo Park, a Beta driver) -> $(echo "$R" | tail -1), form shows: \"$(echo "$R" | grep -o 'Choose a driver from your company.' | head -1)\""
echo "Afterwards, as Beta owner: BET-2001 status \"$($C -b "$BETA" "$B/Loads/4" | grep -o 'badge [^"]*">[^<]*' | head -1 | sed 's/.*>//')\", documents listed: $($C -b "$BETA" "$B/Loads/4" | grep -c '/Loads/4/Documents/')"
echo "As Alpha owner: ALP-1001 driver still \"$($C -b "$ALPHA" "$B/Loads/1" | tr -d '\n' | grep -o '<dt class="col-sm-3">Driver</dt><dd class="col-sm-9">[^<]*' | sed 's/.*>//')\""

echo; echo "### 4. Roles inside one company"
get "$D1" /Loads/3 "driver1@alpha.example (driver2's load)"
get "$D1" /Dashboard "driver1@alpha.example"
get "$DISP" /Trucks/Create "dispatch@alpha.example (Owner-only page)"
echo "Anonymous: $($C -o /dev/null -w 'GET /Loads/4 -> %{http_code} to %{redirect_url}' "$B/Loads/4")"
