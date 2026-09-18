using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-5: giới hạn danh sách tin theo chủ tin, và ghi lại ai đã chốt điểm.
///
/// Hai vấn đề cũ: JobList gọi GetAll() cho mọi vai trò nên một Mentor đọc được toàn bộ tin
/// của Mentor khác kèm số ứng viên từng tin; và Application không có trường nào lưu người
/// chấm, nên "% chốt" trên màn hình là con số không có chủ.
/// </summary>
public class JobOwnershipAndScorerTests
{
    private static ApplicationService NewSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), t.CvStorage, new AuditService(t.Db));

    // ===== Danh sách tin theo chủ tin =====

    [Fact]
    public void GetByOwner_ReturnsOnlyOwnJobs_WhileGetAllReturnsEverything()
    {
        using var t = new TestDb();
        var a = t.AddMentor("A", "a@itcp.vn");
        var b = t.AddMentor("B", "b@itcp.vn");
        t.AddJob(a.Id, title: "Tin của A");
        t.AddJob(a.Id, title: "Tin thứ hai của A");
        t.AddJob(b.Id, title: "Tin của B");
        var svc = new JobService(t.Db);

        var mine = svc.GetByOwner(a.Id);

        Assert.Equal(2, mine.Count);
        Assert.All(mine, j => Assert.Equal(a.Id, j.CreatedById));
        Assert.Equal(3, svc.GetAll().Count);   // Admin vẫn thấy tất cả
    }

    [Fact]
    public void GetByOwner_WithNoJobs_ReturnsEmpty_NotEverything()
    {
        using var t = new TestDb();
        var a = t.AddMentor("A", "a@itcp.vn");
        var b = t.AddMentor("B", "b@itcp.vn");
        t.AddJob(b.Id);

        Assert.Empty(new JobService(t.Db).GetByOwner(a.Id));
    }

    // ===== Ai đã chốt điểm =====

    private static (int AppId, User Mentor, User Admin) SeedScored(TestDb t)
    {
        var m = t.AddMentor();
        var admin = t.AddUser("Quản trị", "admin@itcp.vn", Roles.AdminId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        return (t.AddApplication(job.Id, p.Id).Id, m, admin);
    }

    [Fact]
    public void SaveHrScore_RecordsWhoScored()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);

        NewSvc(t).SaveHrScore(appId, 75, "Nền tảng tốt, thiếu kinh nghiệm Docker.", m.Id);

        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(m.Id, a.HrScoreByUserId);
        Assert.Equal(75, a.HrScore);
        Assert.NotNull(a.HrAdjustedAt);
    }

    // ===== Luật chốt điểm nằm ở service, không chỉ ở thẻ input =====

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(150)]
    public void SaveHrScore_OutOfRange_IsRejected_AndChangesNothing(int score)
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);

        var ex = Assert.Throws<ArgumentException>(() =>
            NewSvc(t).SaveHrScore(appId, score, "Lý do đủ dài để hợp lệ.", m.Id));

        Assert.Contains("0 đến 100", ex.Message);
        Assert.Null(t.NewContext().Applications.Find(appId)!.HrScore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ngắn")]
    [InlineData("         x         ")]   // khoảng trắng không được tính là lý do
    public void SaveHrScore_ReasonTooShort_IsRejected(string note)
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);

        Assert.Throws<ArgumentException>(() => NewSvc(t).SaveHrScore(appId, 70, note, m.Id));
        Assert.Null(t.NewContext().Applications.Find(appId)!.HrScore);
    }

    /// <summary>
    /// maxlength="500" chỉ ràng buộc trình duyệt. Trước bản sửa, 501 ký tự đi thẳng xuống
    /// CSDL — trên SQL Server đó là lỗi 500 "String or binary data would be truncated".
    /// </summary>
    [Fact]
    public void SaveHrScore_ReasonLongerThanColumn_IsRejected_BeforeReachingTheDatabase()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);

        var ex = Assert.Throws<ArgumentException>(() =>
            NewSvc(t).SaveHrScore(appId, 70, new string('a', 501), m.Id));

        Assert.Contains("500", ex.Message);
        Assert.Null(t.NewContext().Applications.Find(appId)!.HrScore);
    }

    [Fact]
    public void SaveHrScore_Audit_RecordsTheScore_ButNotTheReason()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);
        const string reason = "Nhận định riêng: phỏng vấn thử thấy yếu giao tiếp.";

        NewSvc(t).SaveHrScore(appId, 64, reason, m.Id);

        var log = t.NewContext().AuditLogs.Single(x => x.Action == "Score Applicant");
        Assert.Contains("64%", log.Details);
        Assert.DoesNotContain("giao tiếp", log.Details);
    }

    /// <summary>Chấm lại thì mốc thời gian và con số đi theo lần chấm MỚI NHẤT.</summary>
    [Fact]
    public void SaveHrScore_Rescore_KeepsTheLatest()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);
        var svc = NewSvc(t);
        svc.SaveHrScore(appId, 75, "Lần chấm đầu.", m.Id);

        svc.SaveHrScore(appId, 60, "Xem lại, hạ xuống.", m.Id);

        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(m.Id, a.HrScoreByUserId);
        Assert.Equal(60, a.HrScore);
        Assert.Equal("Xem lại, hạ xuống.", a.HrNote);
    }

    /// <summary>Admin chỉ xem: không chốt đè lên điểm của Mentor, dù gọi thẳng vào service.</summary>
    [Fact]
    public void SaveHrScore_ByAdmin_IsDenied_AndKeepsTheMentorsScore()
    {
        using var t = new TestDb();
        var (appId, m, admin) = SeedScored(t);
        var svc = NewSvc(t);
        svc.SaveHrScore(appId, 75, "Lần chấm đầu.", m.Id);

        Assert.Throws<UnauthorizedAccessException>(() => svc.SaveHrScore(appId, 60, "Xem lại, hạ xuống.", admin.Id));

        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(m.Id, a.HrScoreByUserId);
        Assert.Equal(75, a.HrScore);
    }

    /// <summary>Chưa ai chấm thì trường này là null — giao diện không hiện dòng "chốt bởi".</summary>
    [Fact]
    public void UnscoredApplication_HasNoScorer()
    {
        using var t = new TestDb();
        var (appId, _, _) = SeedScored(t);

        var detail = NewSvc(t).GetDetail(appId)!;

        Assert.Null(detail.HrScore);
        Assert.Null(detail.HrScoreByUserId);
        Assert.Null(detail.HrAdjustedAt);
    }

    [Fact]
    public void ApplicationDetail_CarriesTheScorer()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);
        var svc = NewSvc(t);
        svc.SaveHrScore(appId, 82, "Rất phù hợp.", m.Id);

        var detail = svc.GetDetail(appId)!;

        Assert.Equal(m.Id, detail.HrScoreByUserId);
        Assert.Equal(82, detail.HrScore);
    }

    /// <summary>Cột "% chốt" của bảng danh sách cũng cần người chốt để hiện tooltip.</summary>
    [Fact]
    public void ApplicantListItem_CarriesTheScorer()
    {
        using var t = new TestDb();
        var (appId, m, _) = SeedScored(t);
        var svc = NewSvc(t);
        svc.SaveHrScore(appId, 82, "Rất phù hợp.", m.Id);
        var jobId = t.NewContext().Applications.Find(appId)!.JobId;

        var item = Assert.Single(svc.GetByJob(jobId));

        Assert.Equal(m.Id, item.HrScoreByUserId);
    }

    /// <summary>
    /// SeedData và các đường job nền gọi SaveHrScore với actorUserId = 0. Lưu số 0 vào cột
    /// này sẽ làm giao diện đi tìm tên của "người dùng #0" và hiện ra một cái tên không có thật.
    /// </summary>
    [Fact]
    public void SaveHrScore_WithoutActor_LeavesScorerNull()
    {
        using var t = new TestDb();
        var (appId, _, _) = SeedScored(t);

        NewSvc(t).SaveHrScore(appId, 70, "Chấm tự động.", 0);

        Assert.Null(t.NewContext().Applications.Find(appId)!.HrScoreByUserId);
    }
}
