using FamilyDispatch.Web.Domain;

namespace FamilyDispatch.Web.Pages;

/// <summary>Display helpers shared by every page, so a status or a date looks the same everywhere.</summary>
public static class Ui
{
    public static string StatusLabel(LoadStatus s) => s switch
    {
        LoadStatus.PickedUp => "Picked Up",
        _ => s.ToString(),
    };

    // The label is always shown, so the meaning never rests on color alone.
    public static string StatusBadgeClass(LoadStatus s) => s switch
    {
        LoadStatus.Assigned => "badge bg-warning-subtle text-warning-emphasis border border-warning-subtle",
        LoadStatus.PickedUp => "badge bg-primary-subtle text-primary-emphasis border border-primary-subtle",
        _ => "badge bg-success-subtle text-success-emphasis border border-success-subtle",
    };

    public static string Utc(DateTime d) => d.ToString("yyyy-MM-dd HH:mm") + " UTC";

    public static string Utc(DateTime? d) => d is { } v ? Utc(v) : "Not yet";

    public static string FileSize(long bytes) =>
        bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} KB" : $"{bytes / (1024.0 * 1024):0.0} MB";
}

/// <summary>One row of the shared loads table (Dashboard and Loads list).</summary>
public record LoadRow(int Id, string ReferenceNo, string Origin, string Destination, DateTime PickupDateUtc,
    string DriverName, string? TruckUnit, LoadStatus Status, int DocumentCount)
{
    // Projects from the filtered DbSet, so the rows are already limited to the user's company (and own loads for a driver).
    public static IQueryable<LoadRow> From(IQueryable<Load> loads) => loads
        .OrderBy(l => l.PickupDateUtc)
        .Select(l => new LoadRow(l.Id, l.ReferenceNo, l.Origin, l.Destination, l.PickupDateUtc,
            l.Driver.DisplayName, l.Truck != null ? l.Truck.UnitNumber : null, l.Status, l.Documents.Count));
}

public record LoadsTableModel(IReadOnlyList<LoadRow> Rows, bool ShowDriver);

/// <summary>A driver with their open (not delivered) load count.</summary>
public record DriverRow(int Id, string Name, string Email, int ActiveLoads)
{
    public static IQueryable<DriverRow> From(Data.AppDbContext db) => db.Users
        .Where(u => u.Role == Role.Driver)
        .OrderBy(u => u.DisplayName)
        .Select(u => new DriverRow(u.Id, u.DisplayName, u.Email,
            db.Loads.Count(l => l.DriverId == u.Id && l.Status != LoadStatus.Delivered)));
}
