using FamilyDispatch.Web.Data;
using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FamilyDispatch.Tests;

/// <summary>The data-layer guards, without HTTP: filters per context instance, CompanyId stamping on insert.</summary>
public sealed class AppDbContextTests : IDisposable
{
    private record FakeUser(int? CompanyId, int? UserId = 1, Role? Role = Web.Domain.Role.Owner) : ICurrentUser;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;
    private const int Alpha = 1, Beta = 2;

    public AppDbContextTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = Context(companyId: null);
        db.Database.EnsureCreated();
        db.Companies.AddRange(new Company { Name = "Alpha" }, new Company { Name = "Beta" });
        db.Trucks.AddRange(
            new Truck { CompanyId = Alpha, UnitNumber = "A-1", Plate = "A" },
            new Truck { CompanyId = Beta, UnitNumber = "B-1", Plate = "B" });
        db.Users.AddRange(
            new User { Id = AlphaDriver1, CompanyId = Alpha, Email = "d1@a", DisplayName = "D1", Role = Role.Driver },
            new User { Id = AlphaDriver2, CompanyId = Alpha, Email = "d2@a", DisplayName = "D2", Role = Role.Driver },
            new User { Id = BetaDriver, CompanyId = Beta, Email = "d@b", DisplayName = "DB", Role = Role.Driver });
        db.Loads.AddRange(
            NewLoad(Alpha, "ALP-1", AlphaDriver1),
            NewLoad(Alpha, "ALP-2", AlphaDriver2),
            NewLoad(Beta, "BET-1", BetaDriver));
        db.SaveChanges();
    }

    private const int AlphaDriver1 = 11, AlphaDriver2 = 12, BetaDriver = 21;

    private static Load NewLoad(int companyId, string reference, int driverId) => new()
    {
        CompanyId = companyId, ReferenceNo = reference, Origin = "X", Destination = "Y", DriverId = driverId,
    };

    private AppDbContext Context(int? companyId, int? userId = 1, Role? role = Role.Owner) =>
        new(_options, new FakeUser(companyId, userId, role));

    // Queried without Include/projection on purpose: the User filter on a joined driver would also hide
    // other companies' loads and mask a broken Load filter. This proves the Load filter on its own.
    private static List<string> Refs(AppDbContext db) => db.Loads.Select(l => l.ReferenceNo).OrderBy(r => r).ToList();

    [Fact]
    public void LoadFilter_OfficeUser_SeesOnlyOwnCompanysLoads()
    {
        Assert.Equal(["ALP-1", "ALP-2"], Refs(Context(Alpha)));
        Assert.Equal(["BET-1"], Refs(Context(Beta)));
        Assert.Empty(Refs(Context(null)));
    }

    [Fact]
    public void LoadFilter_MissingOrUnknownRole_FailsClosed_SeesOnlyOwnLoads()
    {
        Assert.Equal(["ALP-1"], Refs(Context(Alpha, AlphaDriver1, role: null)));
        Assert.Equal(["ALP-1"], Refs(Context(Alpha, AlphaDriver1, role: (Role)42)));
    }

    [Fact]
    public void Database_ReferenceUniquePerCompany_NotAcrossCompanies()
    {
        using (var beta = Context(Beta))
        {
            beta.Loads.Add(NewLoad(0, "ALP-1", BetaDriver)); // Beta may reuse Alpha's reference
            beta.SaveChanges();
        }

        using var alpha = Context(Alpha);
        alpha.Loads.Add(NewLoad(0, "ALP-1", AlphaDriver2));
        Assert.Throws<DbUpdateException>(() => alpha.SaveChanges());
    }

    [Fact]
    public void Database_TenantRowMustPointAtARealCompany()
    {
        using var db = Context(null);
        db.Trucks.Add(new Truck { CompanyId = 999, UnitNumber = "X-1", Plate = "X" });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void LoadFilter_Driver_SeesOnlyOwnAssignedLoads()
    {
        Assert.Equal(["ALP-1"], Refs(Context(Alpha, AlphaDriver1, Role.Driver)));
        Assert.Equal(["ALP-2"], Refs(Context(Alpha, AlphaDriver2, Role.Driver)));
    }

    [Fact]
    public void StatusRace_SecondWriterGetsConcurrencyError_NotADoubleMove()
    {
        // Two users read the same Assigned load, then both try to move it.
        using var first = Context(Alpha);
        using var second = Context(Alpha);
        var a = first.Loads.Single(l => l.ReferenceNo == "ALP-1");
        var b = second.Loads.Single(l => l.ReferenceNo == "ALP-1");

        LoadWorkflow.Move(a, LoadStatus.PickedUp, DateTime.UtcNow);
        first.SaveChanges();
        LoadWorkflow.Move(b, LoadStatus.PickedUp, DateTime.UtcNow);

        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Filter_IsEvaluatedPerContext_NotBakedIntoTheCachedModel()
    {
        // Both contexts share one cached EF model; a captured value would leak Alpha's filter into Beta's query.
        using var alpha = Context(Alpha);
        using var beta = Context(Beta);
        using var anonymous = Context(null);

        Assert.Equal(["A-1"], alpha.Trucks.Select(t => t.UnitNumber).ToList());
        Assert.Equal(["B-1"], beta.Trucks.Select(t => t.UnitNumber).ToList());
        Assert.Empty(anonymous.Trucks.ToList());
    }

    [Fact]
    public void Insert_WithoutCompanyId_IsStampedWithUsersCompany()
    {
        using var db = Context(Alpha);
        var truck = new Truck { UnitNumber = "A-2", Plate = "A2" };

        db.Trucks.Add(truck);
        db.SaveChanges();

        Assert.Equal(Alpha, truck.CompanyId);
    }

    [Fact]
    public void Insert_ForAnotherCompany_Throws()
    {
        using var db = Context(Alpha);
        db.Trucks.Add(new Truck { CompanyId = Beta, UnitNumber = "B-9", Plate = "B9" });

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Update_ChangingCompanyId_Throws()
    {
        using var db = Context(Alpha);
        var truck = db.Trucks.Single();

        truck.CompanyId = Beta;

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(Alpha, Context(Alpha).Trucks.Single().CompanyId);
    }

    [Fact]
    public void Insert_WithNoUserAndNoCompanyId_Throws()
    {
        using var db = Context(null);
        db.Trucks.Add(new Truck { UnitNumber = "X", Plate = "X" });

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
