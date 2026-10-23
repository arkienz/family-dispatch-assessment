using FamilyDispatch.Web.Domain;
using FamilyDispatch.Web.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyDispatch.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Truck> Trucks => Set<Truck>();
    public DbSet<Load> Loads => Set<Load>();
    public DbSet<LoadDocument> LoadDocuments => Set<LoadDocument>();

    // The query filters read these instance properties, so EF evaluates them per query on this
    // context instance. A captured local would be baked into the cached model and leak across
    // requests. Do not switch to AddDbContextPool for the same reason.
    public int? CurrentCompanyId => currentUser.CompanyId;
    public int? CurrentUserId => currentUser.UserId;
    // Fails closed: only a known office role sees every load; any other or missing role sees only its own.
    public bool IsOfficeStaff => currentUser.Role is Role.Owner or Role.Dispatcher;

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Anonymous: CurrentCompanyId is null, so every filter matches nothing.
        b.Entity<User>().HasQueryFilter(u => u.CompanyId == CurrentCompanyId);
        b.Entity<Truck>().HasQueryFilter(t => t.CompanyId == CurrentCompanyId);
        b.Entity<LoadDocument>().HasQueryFilter(d => d.CompanyId == CurrentCompanyId);
        // Driver isolation lives here too: a driver only ever sees loads assigned to them.
        b.Entity<Load>().HasQueryFilter(l => l.CompanyId == CurrentCompanyId
                                             && (IsOfficeStaff || l.DriverId == CurrentUserId));

        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        // The database also ties every tenant row to a real company (User already has its Company navigation),
        // and keeps references and unit numbers unique per company even if two saves race.
        b.Entity<Truck>().HasOne<Company>().WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Load>().HasOne<Company>().WithMany().HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LoadDocument>().HasOne<Company>().WithMany().HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Load>().HasIndex(l => new { l.CompanyId, l.ReferenceNo }).IsUnique();
        b.Entity<Truck>().HasIndex(t => new { t.CompanyId, t.UnitNumber }).IsUnique();
        // The UPDATE checks the status it read, so two people moving the same load at once can't both win.
        b.Entity<Load>().Property(l => l.Status).IsConcurrencyToken();
        b.Entity<Load>().HasOne(l => l.Driver).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<LoadDocument>().HasOne(d => d.UploadedBy).WithMany()
            .HasForeignKey(d => d.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        // A row can never be moved to another company after insert.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
            type.FindProperty(nameof(ITenantOwned.CompanyId))!.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCompanyId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampCompanyId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>New tenant rows always get the signed-in user's company, never one from the request.</summary>
    private void StampCompanyId()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>().Where(e => e.State == EntityState.Added))
        {
            var entity = entry.Entity;
            if (CurrentCompanyId is int companyId)
            {
                if (entity.CompanyId == 0)
                    entity.CompanyId = companyId;
                else if (entity.CompanyId != companyId)
                    throw new InvalidOperationException(
                        $"{entry.Metadata.ClrType.Name} for company {entity.CompanyId} cannot be saved by a user of company {companyId}.");
            }
            else if (entity.CompanyId == 0)
            {
                // Only the startup seed runs without a user, and it sets CompanyId itself.
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} has no CompanyId and no signed-in user.");
            }
        }
    }
}
