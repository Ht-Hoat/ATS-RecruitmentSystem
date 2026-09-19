using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Admin giám sát hệ thống chứ không tuyển dụng: XEM được mọi tin và ứng viên, nhưng mọi
/// thao tác tuyển dụng (đăng/sửa/đóng tin, chấm AI, mời phỏng vấn, từ chối, nhận) chỉ
/// Mentor tạo tin làm được. Luật nằm ở tầng service — các test ở đây gọi thẳng vào service,
/// không đi qua nút bấm nào.
/// </summary>
public class AdminReadOnlyTests
{
    private static ApplicationService AppSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), t.CvStorage, new AuditService(t.Db));

    private static (User Mentor, User Admin, Job Job, Application App) Seed(TestDb t)
    {
        var m = t.AddMentor();
        var admin = t.AddUser("Quản trị", "admin@itcp.vn", Roles.AdminId);
        admin.CompanyId = t.DefaultCompany.Id;   // có công ty vẫn không đăng được — vì vai trò
        t.Db.SaveChanges();
        var job = t.AddJob(m.Id);
        var app = t.AddApplication(job.Id, t.AddStudentWithProfile("a").Id);
        return (m, admin, job, app);
    }

    // ===== Tin tuyển dụng =====

    [Fact]
    public void Admin_CannotCreateJob()
    {
        using var t = new TestDb();
        var (_, admin, _, _) = Seed(t);

        Assert.Throws<UnauthorizedAccessException>(() => new JobService(t.Db).Create(new Job
        {
            Title = "Tin của Admin", Category = "Backend", Level = "Junior", CreatedById = admin.Id,
            Deadline = VietnamDateHelper.Today().AddDays(5)
        }));

        Assert.DoesNotContain(t.NewContext().Jobs, j => j.Title == "Tin của Admin");
    }

    [Fact]
    public void Admin_CannotCloseOrReopenJob()
    {
        using var t = new TestDb();
        var (m, admin, job, _) = Seed(t);
        var svc = new JobService(t.Db);

        Assert.Throws<UnauthorizedAccessException>(() => svc.Close(job.Id, admin.Id));
        Assert.Equal(JobStatus.Open, t.NewContext().Jobs.Find(job.Id)!.Status);

        svc.Close(job.Id, m.Id);                                   // chủ tin thì được
        Assert.Throws<UnauthorizedAccessException>(() => svc.Reopen(job.Id, admin.Id));
        Assert.Equal(JobStatus.Closed, t.NewContext().Jobs.Find(job.Id)!.Status);
    }

    /// <summary>Tin cũ do Admin đăng (từ trước khi có luật này) cũng không còn thao tác được.</summary>
    [Fact]
    public void Admin_CannotModify_EvenAJobTheyCreatedEarlier()
    {
        using var t = new TestDb();
        var (_, admin, _, _) = Seed(t);
        var legacy = t.AddJob(admin.Id, title: "Tin cũ của Admin");
        var svc = new JobService(t.Db);

        Assert.False(svc.CanModify(legacy.Id, admin.Id));
        Assert.True(svc.CanView(legacy.Id, admin.Id));
    }

    [Fact]
    public void CanView_And_CanModify_PerRole()
    {
        using var t = new TestDb();
        var (m, admin, job, _) = Seed(t);
        var other = t.AddMentor("Khác", "khac@itcp.vn");
        var student = t.AddUser("SV", "sv-x@itcp.vn", Roles.StudentId);
        var svc = new JobService(t.Db);

        Assert.True(svc.CanView(job.Id, m.Id));
        Assert.True(svc.CanModify(job.Id, m.Id));

        Assert.True(svc.CanView(job.Id, admin.Id));      // Admin: xem được
        Assert.False(svc.CanModify(job.Id, admin.Id));   //        nhưng không thao tác

        Assert.False(svc.CanView(job.Id, other.Id));
        Assert.False(svc.CanModify(job.Id, other.Id));
        Assert.False(svc.CanView(job.Id, student.Id));
        Assert.False(svc.CanView(9999, admin.Id));
    }

    // ===== Ứng viên =====

    [Fact]
    public void Admin_CanViewApplication_ButNotModifyIt()
    {
        using var t = new TestDb();
        var (m, admin, _, app) = Seed(t);
        var svc = AppSvc(t);

        Assert.True(svc.CanAccess(app.Id, admin.Id, isAdmin: true));
        Assert.False(svc.CanModify(app.Id, admin.Id));
        Assert.True(svc.CanModify(app.Id, m.Id));
    }

    [Fact]
    public void Admin_CannotChangeStatus_AndNothingIsWritten()
    {
        using var t = new TestDb();
        var (_, admin, _, app) = Seed(t);

        var ok = AppSvc(t).UpdateStatus(app.Id, ApplicationStatus.Reviewing, admin.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("không có quyền", msg);
        using var v = t.NewContext();
        Assert.Equal(ApplicationStatus.Submitted, v.Applications.Find(app.Id)!.Status);
        Assert.Empty(v.ApplicationStatusHistories);
        Assert.Empty(v.Notifications);
        Assert.Empty(v.EmailOutbox);
    }

    [Fact]
    public void AnotherMentor_CannotChangeStatus()
    {
        using var t = new TestDb();
        var (_, _, _, app) = Seed(t);
        var other = t.AddMentor("Khác", "khac@itcp.vn");

        Assert.False(AppSvc(t).UpdateStatus(app.Id, ApplicationStatus.Reviewing, other.Id, out _));
        Assert.Equal(ApplicationStatus.Submitted, t.NewContext().Applications.Find(app.Id)!.Status);
    }
}
