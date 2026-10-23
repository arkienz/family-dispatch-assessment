using System.ComponentModel.DataAnnotations;
using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Loads;

[Authorize(Policy = Policies.OfficeStaff)]
public class CreateModel(AppDbContext db) : PageModel
{
    // No CompanyId here on purpose: a posted CompanyId field has nothing to bind to, and SaveChanges stamps it.
    public class InputModel
    {
        [Required, StringLength(20), Display(Name = "Reference")]
        public string ReferenceNo { get; set; } = "";

        [Required, StringLength(100)]
        public string Origin { get; set; } = "";

        [Required, StringLength(100)]
        public string Destination { get; set; } = "";

        [Required, Display(Name = "Pickup date and time (UTC)")]
        public DateTime? PickupDateUtc { get; set; }

        [Display(Name = "Driver")]
        public int? DriverId { get; set; }

        [Display(Name = "Truck")]
        public int? TruckId { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> Drivers { get; private set; } = [];
    public List<SelectListItem> Trucks { get; private set; } = [];

    public async Task OnGetAsync() => await LoadOptionsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        // An empty field binds as null; [Required] reports it, so just don't crash on it here.
        Input.ReferenceNo = Input.ReferenceNo?.Trim().ToUpperInvariant() ?? "";
        await LoadForm.CheckReferencesAsync(db, ModelState,
            $"{nameof(Input)}.{nameof(Input.DriverId)}", Input.DriverId,
            $"{nameof(Input)}.{nameof(Input.TruckId)}", Input.TruckId);
        if (await db.Loads.AnyAsync(l => l.ReferenceNo == Input.ReferenceNo))
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ReferenceNo)}", "This reference is already used in your company.");
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var load = new Load
        {
            ReferenceNo = Input.ReferenceNo,
            Origin = Input.Origin.Trim(),
            Destination = Input.Destination.Trim(),
            // The form's datetime-local has no zone; the label says UTC, so store it as UTC.
            PickupDateUtc = DateTime.SpecifyKind(Input.PickupDateUtc!.Value, DateTimeKind.Utc),
            DriverId = Input.DriverId!.Value,
            TruckId = Input.TruckId,
        };
        db.Loads.Add(load);
        await db.SaveChangesAsync();

        TempData["Ok"] = $"{load.ReferenceNo} created and assigned.";
        return RedirectToPage("/Loads/Details", new { id = load.Id });
    }

    private async Task LoadOptionsAsync()
    {
        Drivers = await LoadForm.DriverOptionsAsync(db);
        Trucks = await LoadForm.TruckOptionsAsync(db);
    }
}
