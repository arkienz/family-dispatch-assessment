using FamilyDispatch.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyDispatch.Tests;

/// <summary>Hosts the real app in Production mode on its own temp SQLite file and upload folder.</summary>
public class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-tests-" + Guid.NewGuid().ToString("N"));

    public string UploadRoot => Path.Combine(_dir, "uploads");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "app.db")}");
        builder.UseSetting("Storage:Root", UploadRoot);
        // Every test client signs in from the same (empty) address; RateLimitTests checks the real limit.
        builder.UseSetting("RateLimit:LoginPerMinute", "100000");
        builder.UseSetting("RateLimit:UploadsPerHour", "100000");
        builder.UseSetting("RateLimit:LoginFailuresPerAccount", "100000");
    }

    /// <summary>A client with its own cookie jar, on https so the Secure auth cookie is kept.</summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    /// <summary>Test-side lookup that bypasses the tenant filters, to find ids and check what was really stored.</summary>
    public T Db<T>(Func<AppDbContext, T> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return query(db);
    }

    public int LoadId(string referenceNo) => Load(referenceNo).Id;

    public int UserId(string email) => Db(db => db.Users.IgnoreQueryFilters().Single(u => u.Email == email).Id);

    public int TruckId(string unit) => Db(db => db.Trucks.IgnoreQueryFilters().Single(t => t.UnitNumber == unit).Id);

    public int CompanyId(string name) => Db(db => db.Companies.Single(c => c.Name == name).Id);

    /// <summary>Adds an Assigned load straight to the database, for tests that need one nobody else touches.</summary>
    public int AddLoad(string companyName, string referenceNo, string driverEmail) => Db(db =>
    {
        var load = new Web.Domain.Load
        {
            CompanyId = CompanyId(companyName), ReferenceNo = referenceNo, Origin = "Tulsa, OK", Destination = "Wichita, KS",
            PickupDateUtc = DateTime.UtcNow, DriverId = UserId(driverEmail),
        };
        db.Loads.Add(load);
        db.SaveChanges();
        return load.Id;
    });

    public Web.Domain.Load Load(string referenceNo) =>
        Db(db => db.Loads.IgnoreQueryFilters().AsNoTracking().Single(l => l.ReferenceNo == referenceNo));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp folder; best effort */ }
    }
}
