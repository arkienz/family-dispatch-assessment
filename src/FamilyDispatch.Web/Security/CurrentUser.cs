using System.Security.Claims;
using FamilyDispatch.Web.Domain;

namespace FamilyDispatch.Web.Security;

public static class AppClaims
{
    public const string CompanyId = "CompanyId";
    public const string CompanyName = "CompanyName";
}

public static class RateLimits
{
    public const string Login = nameof(Login);
    public const string Upload = nameof(Upload);
}

public static class Policies
{
    public const string OfficeStaff = nameof(OfficeStaff);
    public const string OwnerOnly = nameof(OwnerOnly);
}

/// <summary>The signed-in user, read only from the auth cookie. All null when anonymous.</summary>
public interface ICurrentUser
{
    int? UserId { get; }
    int? CompanyId { get; }
    Role? Role { get; }
}

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } p ? p : null;

    public int? UserId => ParseInt(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    public int? CompanyId => ParseInt(Principal?.FindFirstValue(AppClaims.CompanyId));
    public Role? Role => Enum.TryParse<Role>(Principal?.FindFirstValue(ClaimTypes.Role), out var r) ? r : null;

    private static int? ParseInt(string? s) => int.TryParse(s, out var v) ? v : null;
}
