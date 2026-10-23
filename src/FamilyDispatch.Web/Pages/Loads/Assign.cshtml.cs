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

/// <summary>Change driver/truck. Office staff only, and only while the load is still Assigned.</summary>
[Authorize(Policy = Policies.OfficeStaff)]
public class AssignModel(AppDbContext db) : PageModel
{
    public Load Load { get; private set; } = null!;

    [BindProperty, Display(Name = "Driver")]
    public int? DriverId { get; set; }

    [BindProperty, Display(Name = "Truck")]
    public int? TruckId { get; set; }

    public List<SelectListItem> Drivers { get; private set; } = [];
    public List<SelectListItem> Trucks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var load = await db.Loads.FirstOrDefaultAsync(l => l.Id == id);
        if (load is null) return NotFound();
        Load = load;
        DriverId = load.DriverId;
        TruckId = load.TruckId;
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var load = await db.Loads.FirstOrDefaultAsync(l => l.Id == id);
        if (load is null) return NotFound();
        if (!LoadWorkflow.CanReassign(load.Status))
        {
            TempData["Error"] = $"{load.ReferenceNo} is {Ui.StatusLabel(load.Status)}. Driver and truck can only change while Assigned.";
            return RedirectToPage("/Loads/Details", new { id });
        }

        await LoadForm.CheckReferencesAsync(db, ModelState, nameof(DriverId), DriverId, nameof(TruckId), TruckId);
        if (!ModelState.IsValid)
        {
            Load = load;
            await LoadOptionsAsync();
            return Page();
        }

        load.DriverId = DriverId!.Value;
        load.TruckId = TruckId;
        try
        {
            // Status is a concurrency token: if the load was picked up meanwhile, this save fails instead of reassigning it.
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }

        TempData["Ok"] = $"{load.ReferenceNo} driver and truck saved.";
        return RedirectToPage("/Loads/Details", new { id });
    }

    private async Task LoadOptionsAsync()
    {
        Drivers = await LoadForm.DriverOptionsAsync(db);
        Trucks = await LoadForm.TruckOptionsAsync(db);
    }
}
