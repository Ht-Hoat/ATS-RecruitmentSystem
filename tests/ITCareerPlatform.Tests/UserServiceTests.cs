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
}
