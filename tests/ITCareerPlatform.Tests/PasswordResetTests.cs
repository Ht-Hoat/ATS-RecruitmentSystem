using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P0-3: Admin đặt lại mật khẩu + chốt buộc đổi ở lần đăng nhập sau.
///
/// Trước đây UserService chỉ có ChangePassword (đòi mật khẩu hiện tại), nên một sinh viên
/// quên mật khẩu là mất luôn hồ sơ và toàn bộ lịch sử ứng tuyển — hệ thống chưa gửi được
/// email, và đường tự đăng ký chỉ tạo tài khoản mới.
/// </summary>
public class PasswordResetTests
{
    private const string OldPassword = "12345678";   // TestDb.AddUser băm đúng chuỗi này

    private static (UserService Svc, AuthService Auth) NewServices(TestDb t) =>
        (new UserService(t.Db, new AuditService(t.Db)), new AuthService(t.Db));

    [Fact]
    public void Reset_InvalidatesOldPassword_AndTempPasswordWorks()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var (svc, auth) = NewServices(t);

        Assert.True(svc.ResetPassword(sv.Id, admin.Id, out var temp, out var err), err);

        Assert.Null(auth.Validate("sv@itcp.vn", OldPassword));
        Assert.NotNull(auth.Validate("sv@itcp.vn", temp));
    }

    /// <summary>
    /// Phiên đang mở phải chết theo mật khẩu cũ. Không tăng SecurityStamp thì người đang
    /// chiếm được phiên vẫn dùng tiếp như chưa có gì xảy ra — reset thành vô tác dụng.
    /// </summary>
    [Fact]
    public void Reset_BumpsSecurityStamp_AndSetsMustChangePassword()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var stampBefore = sv.SecurityStamp;
        var (svc, _) = NewServices(t);

        Assert.True(svc.ResetPassword(sv.Id, admin.Id, out _, out _));

        var after = t.NewContext().Users.Find(sv.Id)!;
        Assert.True(after.SecurityStamp > stampBefore);
        Assert.True(after.MustChangePassword);
    }

    /// <summary>
    /// Độ dài tối thiểu 12 và hai lần gọi ra hai giá trị khác nhau. Nếu mật khẩu tạm sinh
    /// từ Random (gieo theo thời gian) thì hai lần reset liên tiếp có thể đoán được nhau.
    /// </summary>
    [Fact]
    public void TempPassword_IsLongEnough_AndNotRepeated()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var a = t.AddUser("A", "a@itcp.vn", Roles.StudentId);
        var b = t.AddUser("B", "b@itcp.vn", Roles.StudentId);
        var (svc, _) = NewServices(t);

        svc.ResetPassword(a.Id, admin.Id, out var first, out _);
        svc.ResetPassword(b.Id, admin.Id, out var second, out _);

        Assert.True(first.Length >= 12, $"Mật khẩu tạm chỉ dài {first.Length} ký tự.");
        Assert.NotEqual(first, second);
        Assert.Contains(first, char.IsDigit);
        Assert.Contains(first, char.IsLetter);
    }

    /// <summary>Cùng quy ước với toggle-lock và change-role: thao tác quản trị không tự áp lên mình.</summary>
    [Fact]
    public void Reset_OnSelf_IsRejectedWithReason()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var (svc, auth) = NewServices(t);

        var ok = svc.ResetPassword(admin.Id, admin.Id, out var temp, out var err);

        Assert.False(ok);
        Assert.Contains("Đổi mật khẩu", err);
        Assert.Equal("", temp);
        Assert.NotNull(auth.Validate("admin@itcp.vn", OldPassword));   // mật khẩu cũ còn nguyên
    }

    [Fact]
    public void Reset_UnknownUser_IsRejected()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var (svc, _) = NewServices(t);

        Assert.False(svc.ResetPassword(9999, admin.Id, out _, out var err));
        Assert.Contains("Không tìm thấy", err);
    }

    /// <summary>
    /// Đổi xong là hết nghĩa vụ. Không gỡ cờ thì middleware đá người dùng về /change-password
    /// mãi mãi — đổi mật khẩu thành công nhưng không vào được hệ thống.
    /// </summary>
    [Fact]
    public void ChangePassword_ClearsMustChangePasswordFlag()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var (svc, _) = NewServices(t);
        Assert.True(svc.ResetPassword(sv.Id, admin.Id, out var temp, out _));

        Assert.True(svc.ChangePassword(sv.Id, temp, "MatKhauMoi123", out var err), err);

        Assert.False(t.NewContext().Users.Find(sv.Id)!.MustChangePassword);
    }

    /// <summary>
    /// Nhật ký ghi lại VIỆC đã reset, không ghi mật khẩu tạm. AuditLog là thứ Admin nào cũng
    /// đọc được ở /audit-logs; mật khẩu nằm trong đó là mật khẩu dùng được của người khác.
    /// </summary>
    [Fact]
    public void Reset_IsAudited_ButNeverLogsTheTempPassword()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var (svc, _) = NewServices(t);

        Assert.True(svc.ResetPassword(sv.Id, admin.Id, out var temp, out _));

        var log = Assert.Single(t.NewContext().AuditLogs.Where(a => a.Action == "Reset Password"));
        Assert.Equal(admin.Id, log.UserId);
        Assert.Contains("SV", log.Details);
        Assert.DoesNotContain(temp, log.Details);
    }

    /// <summary>Mốc nhật ký cũng phải là UTC như mọi mốc khác (P0-2).</summary>
    [Fact]
    public void AuditLog_TimestampIsStampedInUtc()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var before = DateTime.UtcNow.AddSeconds(-5);
        var (svc, _) = NewServices(t);

        svc.ResetPassword(sv.Id, admin.Id, out _, out _);

        var log = t.NewContext().AuditLogs.Single(a => a.Action == "Reset Password");
        Assert.InRange(log.Timestamp, before, DateTime.UtcNow.AddSeconds(5));
    }
}
