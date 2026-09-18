using Microsoft.AspNetCore.DataProtection;

namespace ITCareerPlatform.Services;

/// <summary>
/// Chuyển mật khẩu tạm từ endpoint reset-password sang trang /users để hiện ĐÚNG MỘT LẦN
/// cho Admin — dùng khi chưa cấu hình SMTP hoặc lần gửi email vừa rồi hỏng.
///
/// Không đi qua query string: thanh địa chỉ nằm lại trong lịch sử trình duyệt của Admin và
/// trong access log của mọi proxy đứng giữa, và ở đó nó là một mật khẩu dùng được.
/// Thay vào đó là một cookie:
///   - mã hóa + ký bằng Data Protection, hết hạn sau <see cref="Lifetime"/>;
///   - HttpOnly, SameSite=Strict, chỉ gửi kèm đường dẫn /users;
///   - gắn với Id của Admin đã bấm reset — Admin khác dùng chung máy không đọc được;
///   - bị xóa ngay lần đọc đầu tiên, nên tải lại trang là mật khẩu biến mất.
/// </summary>
public static class TempPasswordHandoff
{
    public const string CookieName = "ITCP.TempPw";
    private const string Purpose = "ITCareerPlatform.TempPasswordHandoff.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public static void Store(HttpContext ctx, IDataProtectionProvider dp, int adminUserId, string tempPassword)
    {
        var protector = dp.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        var payload = protector.Protect($"{adminUserId}|{tempPassword}", Lifetime);
        ctx.Response.Cookies.Append(CookieName, payload, CookieOptions(ctx));
    }

    /// <summary>Đọc rồi xóa. Trả null khi không có, đã hết hạn, bị sửa, hoặc thuộc Admin khác.</summary>
    public static string? TakeOnce(HttpContext ctx, IDataProtectionProvider dp, int adminUserId)
    {
        if (!ctx.Request.Cookies.TryGetValue(CookieName, out var payload) || string.IsNullOrEmpty(payload))
            return null;

        // Xóa trước khi giải mã: dù giá trị hỏng hay hợp lệ thì cũng không được đọc lần hai.
        if (!ctx.Response.HasStarted)
            ctx.Response.Cookies.Delete(CookieName, CookieOptions(ctx));

        try
        {
            var plain = dp.CreateProtector(Purpose).ToTimeLimitedDataProtector().Unprotect(payload);
            var sep = plain.IndexOf('|');
            if (sep <= 0) return null;
            return plain[..sep] == adminUserId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ? plain[(sep + 1)..]
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // hết hạn hoặc bị sửa
        }
    }

    private static CookieOptions CookieOptions(HttpContext ctx) => new()
    {
        HttpOnly = true,
        Secure = ctx.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/users",
        MaxAge = Lifetime
    };
}
