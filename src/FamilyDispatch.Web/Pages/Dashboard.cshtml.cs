using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages;

[Authorize(Policy = Policies.OfficeStaff)]
public class DashboardModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<LoadRow> Loads { get; private set; } = [];
    public IReadOnlyList<DriverRow> Drivers { get; private set; } = [];
    public IReadOnlyList<Truck> Trucks { get; private set; } = [];

    public int CountOf(LoadStatus s) => Loads.Count(l => l.Status == s);

    public async Task OnGetAsync()
    {
        Loads = await LoadRow.From(db.Loads).ToListAsync();
        Drivers = await DriverRow.From(db).ToListAsync();
        Trucks = await db.Trucks.AsNoTracking().OrderBy(t => t.UnitNumber).ToListAsync();
    }
}
