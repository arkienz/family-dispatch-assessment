using System.Security.Claims;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FamilyDispatch.Web.Pages;

/// <summary>One page for every error status. Never shows exception details.</summary>
[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Code { get; private set; }
    public string Title { get; private set; } = "";
    public string Text { get; private set; } = "";

    public void OnGet(int? code)
    {
        // No code in the path means the exception handler sent us here. It keeps the status of a "bad request"
        // exception (e.g. an upload body over the size cap gives 400), so use that rather than assuming 500.
        Code = code ?? (Response.StatusCode >= 400 ? Response.StatusCode : 500);
        var company = User.FindFirstValue(AppClaims.CompanyName) ?? "your company";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "a user";
        (Title, Text) = Code switch
        {
            404 => ("Not found in your company",
                $"This load or page doesn't exist in {company}. Records from other companies are hidden, so this page looks the same whether the record exists elsewhere or not."),
            403 => ("Your role can't do this",
                $"You're signed in as {role}. This page or action is for another role (for example, only the Owner can add trucks)."),
            400 => ("That request wasn't accepted",
                "The page may be out of date, the form may have expired, or the change isn't allowed " +
                "(loads move one step at a time: Assigned, then Picked Up, then Delivered). Go back, reload the page and try again."),
            413 => ("File too large",
                "Delivery documents can be up to 5 MB."),
            409 => ("This load changed first",
                "Someone updated this load a moment ago. Reload to see its current status."),
            429 => ("Too many attempts",
                "Too many sign-in attempts or uploads in a short time. Wait a few minutes, then try again."),
            _ => ("Something went wrong",
                "Nothing was saved. Try again; if it keeps happening, note the time and tell the developer."),
        };
    }

    // Re-executed POSTs (a 400 or 404 from a form) land here too.
    public void OnPost(int? code) => OnGet(code);
}
