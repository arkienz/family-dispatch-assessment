using System.ComponentModel.DataAnnotations;
using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Trucks;

[Authorize(Policy = Policies.OwnerOnly)]
public class CreateModel(AppDbContext db) : PageModel
{
    [BindProperty, Required, StringLength(20), Display(Name = "Unit number")]
    public string UnitNumber { get; set; } = "";

    [BindProperty, Required, StringLength(20)]
    public string Plate { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        // An empty field binds as null; [Required] reports it, so just don't crash on it here.
        UnitNumber = UnitNumber?.Trim().ToUpperInvariant() ?? "";
        if (await db.Trucks.AnyAsync(t => t.UnitNumber == UnitNumber))
            ModelState.AddModelError(nameof(UnitNumber), "This unit number is already used in your company.");
        if (!ModelState.IsValid) return Page();

        db.Trucks.Add(new Truck { UnitNumber = UnitNumber, Plate = Plate.Trim().ToUpperInvariant() });
        await db.SaveChangesAsync();

        TempData["Ok"] = $"Truck {UnitNumber} added.";
        return RedirectToPage("/Trucks/Index");
    }
}
