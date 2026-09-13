using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

public class UserServiceTests
{
    // EXT-01: đăng ký email mới -> tạo Sinh viên IT (RoleId=3), mật khẩu đã băm
    [Fact]
    public void Register_NewEmail_CreatesStudent_WithHashedPassword()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var ok = svc.Register("Nguyễn Văn A", "a@itcp.vn", "matkhau123", out var err);

        Assert.True(ok);
        Assert.Equal("", err);
        var u = Assert.Single(t.NewContext().Users.Where(x => x.Email == "a@itcp.vn"));
        Assert.Equal(Roles.StudentId, u.RoleId);
        Assert.NotEqual("matkhau123", u.PasswordHash);                 // KHÔNG lưu plaintext
        Assert.True(BCrypt.Net.BCrypt.Verify("matkhau123", u.PasswordHash));
    }

    [Fact]
    public void Register_DuplicateEmail_Fails()
    {
        using var t = new TestDb();
        t.AddUser("A", "dup@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db);

        var ok = svc.Register("B", "dup@itcp.vn", "matkhau123", out var err);

        Assert.False(ok);
        Assert.Contains("đã được đăng ký", err);
    }

    [Fact]
    public void Register_ShortPassword_Fails()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var ok = svc.Register("C", "c@itcp.vn", "123", out var err);

        Assert.False(ok);
        Assert.Contains("8 ký tự", err);
    }

    // ATS-02: đổi vai trò ghi AuditLog
    [Fact]
    public void ChangeRole_WritesAuditLog()
    {
        using var t = new TestDb();
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db);

        svc.ChangeRole(sv.Id, Roles.MentorId, admin.Id);

        using var v = t.NewContext();
        Assert.Equal(Roles.MentorId, v.Users.Find(sv.Id)!.RoleId);
        var log = Assert.Single(v.AuditLogs.Where(a => a.Action == "Change Role"));
        Assert.Equal(admin.Id, log.UserId);
    }

    [Fact]
    public void ToggleLock_TogglesActive()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId, active: true);
        var svc = new UserService(t.Db);

        svc.ToggleLock(sv.Id);

        Assert.False(t.NewContext().Users.Find(sv.Id)!.IsActive);
    }

    // ===== N1.A: đổi mật khẩu =====
    // TestDb.AddUser băm sẵn mật khẩu "12345678" cho mọi tài khoản.

    [Fact]
    public void ChangePassword_Valid_RehashesAndBumpsSecurityStamp()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var stampBefore = sv.SecurityStamp;
        var svc = new UserService(t.Db);

        var ok = svc.ChangePassword(sv.Id, "12345678", "matkhaumoi99", out var err);

        Assert.True(ok);
        Assert.Equal("", err);

        var saved = t.NewContext().Users.Find(sv.Id)!;
        Assert.True(BCrypt.Net.BCrypt.Verify("matkhaumoi99", saved.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("12345678", saved.PasswordHash));

        // Không tăng stamp thì mọi phiên mở bằng mật khẩu CŨ vẫn dùng được bình thường —
        // đổi mật khẩu khi đó chỉ là đổi thứ để gõ lần sau, không thu hồi được gì.
        Assert.Equal(stampBefore + 1, saved.SecurityStamp);
    }

    [Fact]
    public void ChangePassword_WrongCurrent_Fails_AndKeepsOldHash()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db);

        var ok = svc.ChangePassword(sv.Id, "sai-mat-khau", "matkhaumoi99", out var err);

        Assert.False(ok);
        Assert.Contains("hiện tại không đúng", err);

        var saved = t.NewContext().Users.Find(sv.Id)!;
        Assert.True(BCrypt.Net.BCrypt.Verify("12345678", saved.PasswordHash));
        Assert.Equal(sv.SecurityStamp, saved.SecurityStamp);   // phiên đang mở KHÔNG bị đụng
    }

    [Fact]
    public void ChangePassword_ShortNewPassword_Fails()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db);

        var ok = svc.ChangePassword(sv.Id, "12345678", "123", out var err);

        Assert.False(ok);
        Assert.Contains("8 ký tự", err);
        Assert.True(BCrypt.Net.BCrypt.Verify("12345678", t.NewContext().Users.Find(sv.Id)!.PasswordHash));
    }

    /// <summary>Đổi sang đúng mật khẩu cũ là thao tác rỗng, nhưng vẫn đá mọi phiên ra nếu cho qua.</summary>
    [Fact]
    public void ChangePassword_SameAsCurrent_Fails()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db);

        var ok = svc.ChangePassword(sv.Id, "12345678", "12345678", out var err);

        Assert.False(ok);
        Assert.Contains("khác mật khẩu hiện tại", err);
        Assert.Equal(sv.SecurityStamp, t.NewContext().Users.Find(sv.Id)!.SecurityStamp);
    }

    /// <summary>Id không tồn tại phải trả cùng thông báo với sai mật khẩu — không xác nhận id nào có thật.</summary>
    [Fact]
    public void ChangePassword_UnknownUser_GivesSameMessageAsWrongPassword()
    {
        using var t = new TestDb();
        var svc = new UserService(t.Db);

        var ok = svc.ChangePassword(9999, "12345678", "matkhaumoi99", out var err);

        Assert.False(ok);
        Assert.Contains("hiện tại không đúng", err);
    }

    [Fact]
    public void ChangePassword_WritesAuditLog()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new UserService(t.Db, new AuditService(t.Db));

        Assert.True(svc.ChangePassword(sv.Id, "12345678", "matkhaumoi99", out _));

        var log = Assert.Single(t.NewContext().AuditLogs.Where(a => a.Action == "Change Password"));
        Assert.Equal(sv.Id, log.UserId);
    }
}
