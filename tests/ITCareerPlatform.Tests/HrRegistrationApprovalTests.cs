using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// HR-REG: HR/Mentor tự đăng ký ngoài → tài khoản CHỜ ADMIN DUYỆT → Admin duyệt/từ chối.
/// Nền tảng cần HR tự lên tài khoản để tuyển dụng, nhưng phải qua kiểm duyệt để tránh
/// tài khoản giả mạo — nên khác đường Sinh viên tự đăng ký (kích hoạt ngay).
/// </summary>
public class HrRegistrationApprovalTests
{
    private const string HrPassword = "HrMatKhau123";

    private static (UserService Svc, AuthService Auth) NewServices(TestDb t) =>
        (new UserService(t.Db, new AuditService(t.Db)), new AuthService(t.Db));

    [Fact]
    public void RegisterHr_CreatesPendingInactiveMentor_WithCompany()
    {
        using var t = new TestDb();
        var (svc, _) = NewServices(t);

        Assert.True(svc.RegisterHr("Trần HR", "hr@fpt.vn", HrPassword, "FPT Software", out var err), err);

        var u = t.NewContext().Users.Single(x => x.Email == "hr@fpt.vn");
        Assert.Equal(Roles.MentorId, u.RoleId);
        Assert.False(u.IsActive);          // chưa duyệt: chưa đăng nhập được
        Assert.True(u.PendingApproval);
        Assert.NotNull(u.CompanyId);       // công ty nhập lúc đăng ký đã được tạo/nối
    }

    [Fact]
    public void PendingHr_CannotLogIn_UntilApproved()
    {
        using var t = new TestDb();
        var (svc, auth) = NewServices(t);
        svc.RegisterHr("Trần HR", "hr@fpt.vn", HrPassword, "", out _);

        // Đang chờ duyệt → AuthService chặn (IsActive=false)
        Assert.Null(auth.Validate("hr@fpt.vn", HrPassword));

        var pending = svc.GetPending();
        var hr = Assert.Single(pending);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        Assert.True(svc.ApproveUser(hr.Id, admin.Id, out var err), err);

        // Duyệt xong → đăng nhập được bằng đúng mật khẩu đã đăng ký
        Assert.NotNull(auth.Validate("hr@fpt.vn", HrPassword));
        var after = t.NewContext().Users.Find(hr.Id)!;
        Assert.True(after.IsActive);
        Assert.False(after.PendingApproval);
    }

    [Fact]
    public void RejectPendingHr_RemovesAccount()
    {
        using var t = new TestDb();
        var (svc, _) = NewServices(t);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        svc.RegisterHr("Trần HR", "hr@fpt.vn", HrPassword, "", out _);
        var hr = Assert.Single(svc.GetPending());

        Assert.True(svc.RejectUser(hr.Id, admin.Id, out var err), err);

        Assert.Empty(svc.GetPending());
        Assert.Null(t.NewContext().Users.FirstOrDefault(x => x.Email == "hr@fpt.vn"));
    }

    [Fact]
    public void RegisterHr_DuplicateEmail_IsRejected()
    {
        using var t = new TestDb();
        var (svc, _) = NewServices(t);
        t.AddUser("Có sẵn", "hr@fpt.vn", Roles.StudentId);

        Assert.False(svc.RegisterHr("Trần HR", "hr@fpt.vn", HrPassword, "", out var err));
        Assert.Contains("đã được đăng ký", err);
    }

    [Fact]
    public void Approve_NonPendingUser_IsRejected()
    {
        using var t = new TestDb();
        var (svc, _) = NewServices(t);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var normal = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);   // không chờ duyệt

        Assert.False(svc.ApproveUser(normal.Id, admin.Id, out var err));
        Assert.Contains("không ở trạng thái chờ duyệt", err);
    }

    [Fact]
    public void Approve_IsAudited()
    {
        using var t = new TestDb();
        var (svc, _) = NewServices(t);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        svc.RegisterHr("Trần HR", "hr@fpt.vn", HrPassword, "", out _);
        var hr = Assert.Single(svc.GetPending());

        svc.ApproveUser(hr.Id, admin.Id, out _);

        var log = Assert.Single(t.NewContext().AuditLogs.Where(a => a.Action == "Approve User"));
        Assert.Equal(admin.Id, log.UserId);
        Assert.Contains("hr@fpt.vn", log.Details);
    }
}
