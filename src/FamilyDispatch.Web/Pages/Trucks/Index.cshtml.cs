using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Trucks;

[Authorize(Policy = Policies.OfficeStaff)]
public class IndexModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<Truck> Trucks { get; private set; } = [];

    public async Task OnGetAsync() =>
        Trucks = await db.Trucks.AsNoTracking().OrderBy(t => t.UnitNumber).ToListAsync();
}
