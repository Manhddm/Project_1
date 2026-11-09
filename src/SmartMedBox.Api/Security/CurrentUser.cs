using System.Security.Claims;

namespace SmartMedBox.Api.Security;

public static class CurrentUser
{
    /// <summary>Lấy userId từ cookie phiên đã xác thực — nguồn duy nhất cho kiểm tra quyền.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
