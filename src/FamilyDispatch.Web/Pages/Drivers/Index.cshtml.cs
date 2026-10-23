using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Drivers;

[Authorize(Policy = Policies.OfficeStaff)]
public class IndexModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<DriverRow> Drivers { get; private set; } = [];

    public async Task OnGetAsync() => Drivers = await DriverRow.From(db).ToListAsync();
}
