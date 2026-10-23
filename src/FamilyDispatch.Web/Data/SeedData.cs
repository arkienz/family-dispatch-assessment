using FamilyDispatch.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Web.Data;

/// <summary>Fictional demo data. Runs at startup when the database has no companies.</summary>
public static class SeedData
{
    // Fake, demo-only password shared by every sample account. Documented in the README.
    public const string DemoPassword = "Demo-Pass-2026!";

    // Also listed on the login page so the reviewer can switch accounts in one click.
    public static readonly (string Company, string Email, string Name, Role Role)[] DemoUsers =
    [
        ("Alpha Transport", "owner@alpha.example", "Olivia Grant", Role.Owner),
        ("Alpha Transport", "dispatch@alpha.example", "Marcus Lee", Role.Dispatcher),
        ("Alpha Transport", "driver1@alpha.example", "Dan Rivera", Role.Driver),
        ("Alpha Transport", "driver2@alpha.example", "Sam Cole", Role.Driver),
        ("Beta Logistics", "owner@beta.example", "Priya Shah", Role.Owner),
        ("Beta Logistics", "dispatch@beta.example", "Ken Ortiz", Role.Dispatcher),
        ("Beta Logistics", "driver1@beta.example", "Leo Park", Role.Driver),
    ];

    public static void EnsureSeeded(AppDbContext db, IPasswordHasher<User> hasher)
    {
        db.Database.EnsureCreated();
        // Runs without a signed-in user, so the filters hide everything: check the tenant table, which has none.
        if (db.Companies.Any()) return;

        var alpha = new Company { Name = "Alpha Transport" };
        var beta = new Company { Name = "Beta Logistics" };
        db.Companies.AddRange(alpha, beta);
        db.SaveChanges();

        var companies = new Dictionary<string, Company> { [alpha.Name] = alpha, [beta.Name] = beta };
        var users = DemoUsers.Select(d =>
        {
            var u = new User { CompanyId = companies[d.Company].Id, Email = d.Email, DisplayName = d.Name, Role = d.Role };
            u.PasswordHash = hasher.HashPassword(u, DemoPassword);
            return u;
        }).ToDictionary(u => u.Email);
        db.Users.AddRange(users.Values);
        var alphaDriver1 = users["driver1@alpha.example"];
        var alphaDriver2 = users["driver2@alpha.example"];
        var betaDriver1 = users["driver1@beta.example"];

        var a101 = new Truck { CompanyId = alpha.Id, UnitNumber = "A-101", Plate = "TX 4KD-118" };
        var a102 = new Truck { CompanyId = alpha.Id, UnitNumber = "A-102", Plate = "TX 9PM-552" };
        var b201 = new Truck { CompanyId = beta.Id, UnitNumber = "B-201", Plate = "TN 3RC-740" };
        db.Trucks.AddRange(a101, a102, b201);
        db.SaveChanges();

        var now = DateTime.UtcNow;
        var day = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        Load NewLoad(Company c, string reference, string from, string to, User driver, Truck truck, int hoursFromToday, LoadStatus status) => new()
        {
            CompanyId = c.Id, ReferenceNo = reference, Origin = from, Destination = to,
            PickupDateUtc = day.AddHours(hoursFromToday), DriverId = driver.Id, TruckId = truck.Id,
            Status = status, CreatedAtUtc = now,
            PickedUpAtUtc = status == LoadStatus.PickedUp ? now : null,
        };

        // Saved in this order so the ids are predictable for the demo: Alpha 1-3, Beta 4-5.
        foreach (var load in new[]
        {
            NewLoad(alpha, "ALP-1001", "Dallas, TX", "Oklahoma City, OK", alphaDriver1, a101, 14, LoadStatus.Assigned),
            NewLoad(alpha, "ALP-1002", "Fort Worth, TX", "Shreveport, LA", alphaDriver1, a101, 8, LoadStatus.PickedUp),
            NewLoad(alpha, "ALP-1003", "Austin, TX", "Houston, TX", alphaDriver2, a102, 30, LoadStatus.Assigned),
            NewLoad(beta, "BET-2001", "Memphis, TN", "Little Rock, AR", betaDriver1, b201, 16, LoadStatus.Assigned),
            NewLoad(beta, "BET-2002", "Nashville, TN", "Atlanta, GA", betaDriver1, b201, 9, LoadStatus.PickedUp),
        })
        {
            db.Loads.Add(load);
            db.SaveChanges();
        }
    }
}
