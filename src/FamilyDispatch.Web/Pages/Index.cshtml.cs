using FamilyDispatch.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FamilyDispatch.Web.Pages;

/// <summary>Landing redirect only: drivers go to their loads, office staff to the dashboard.</summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage(User.IsInRole(nameof(Role.Driver)) ? "/Loads/Index" : "/Dashboard");
}
