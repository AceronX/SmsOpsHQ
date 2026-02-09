using System.Security.Claims;

namespace SmsOpsHQ.Api.Extensions;

// Extracts user identity fields from JWT claims set during authentication.
// JWT claims structure: sub=UserId, unique_name=Username, role=Role, store_id=StoreId.
public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        string? sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        return int.TryParse(sub, out int userId) ? userId : 0;
    }

    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
               ?? principal.FindFirstValue("unique_name")
               ?? string.Empty;
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue("role") ?? string.Empty;
    }

    public static int? GetStoreId(this ClaimsPrincipal principal)
    {
        string? storeIdClaim = principal.FindFirstValue("store_id");
        if (string.IsNullOrEmpty(storeIdClaim))
            return null;
        return int.TryParse(storeIdClaim, out int storeId) ? storeId : null;
    }

    // Returns true for HQAdmin or HQViewer roles that can access any store.
    public static bool IsHqUser(this ClaimsPrincipal principal)
    {
        string role = principal.GetRole();
        return role == "HQAdmin" || role == "HQViewer";
    }

    // Returns true if the user is authorized to access the given store.
    public static bool CanAccessStore(this ClaimsPrincipal principal, int storeId)
    {
        if (principal.IsHqUser())
            return true;
        return principal.GetStoreId() == storeId;
    }
}
