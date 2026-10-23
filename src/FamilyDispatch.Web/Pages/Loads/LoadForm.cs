using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Loads;

/// <summary>Driver and truck choices shared by Create and Assign.</summary>
public static class LoadForm
{
    public static async Task<List<SelectListItem>> DriverOptionsAsync(AppDbContext db) =>
        await db.Users.Where(u => u.Role == Role.Driver).OrderBy(u => u.DisplayName)
            .Select(u => new SelectListItem(u.DisplayName, u.Id.ToString())).ToListAsync();

    public static async Task<List<SelectListItem>> TruckOptionsAsync(AppDbContext db) =>
        await db.Trucks.OrderBy(t => t.UnitNumber)
            .Select(t => new SelectListItem(t.UnitNumber + " (" + t.Plate + ")", t.Id.ToString())).ToListAsync();

    /// <summary>
    /// Ids from a form are untrusted. The query filter does not stop a foreign key to another company's row,
    /// so each id is looked up through the filtered sets: another company's driver or truck is simply not found.
    /// </summary>
    public static async Task CheckReferencesAsync(AppDbContext db, ModelStateDictionary modelState,
        string driverField, int? driverId, string truckField, int? truckId)
    {
        if (driverId is null || !await db.Users.AnyAsync(u => u.Id == driverId && u.Role == Role.Driver))
            modelState.AddModelError(driverField, "Choose a driver from your company.");
        if (truckId is not null && !await db.Trucks.AnyAsync(t => t.Id == truckId))
            modelState.AddModelError(truckField, "Choose a truck from your company, or no truck.");
    }
}
