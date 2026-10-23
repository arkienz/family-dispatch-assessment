namespace FamilyDispatch.Web.Domain;

public enum Role { Owner, Dispatcher, Driver }

public enum LoadStatus { Assigned, PickedUp, Delivered }

/// <summary>Every row a tenant owns. AppDbContext filters and stamps CompanyId on these.</summary>
public interface ITenantOwned
{
    int CompanyId { get; set; }
}

public class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public class User : ITenantOwned
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}

public class Truck : ITenantOwned
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string UnitNumber { get; set; }
    public required string Plate { get; set; }
}

public class Load : ITenantOwned
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string ReferenceNo { get; set; }
    public required string Origin { get; set; }
    public required string Destination { get; set; }
    public DateTime PickupDateUtc { get; set; }
    public int DriverId { get; set; }
    public User Driver { get; set; } = null!;
    public int? TruckId { get; set; }
    public Truck? Truck { get; set; }
    public LoadStatus Status { get; set; } = LoadStatus.Assigned;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public List<LoadDocument> Documents { get; set; } = [];
}

public class LoadDocument : ITenantOwned
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int LoadId { get; set; }
    public Load Load { get; set; } = null!;
    public required string OriginalFileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public int UploadedByUserId { get; set; }
    public User UploadedBy { get; set; } = null!;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
