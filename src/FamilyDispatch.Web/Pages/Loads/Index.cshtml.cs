using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Pages.Loads;

/// <summary>Any signed-in user. The query filter limits rows to the company, and a driver to their own loads.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public bool IsDriver => User.IsInRole(nameof(Role.Driver));
    public IReadOnlyList<LoadRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await LoadRow.From(db.Loads).ToListAsync();
}
