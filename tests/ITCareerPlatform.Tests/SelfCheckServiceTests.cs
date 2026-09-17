using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-2: sinh viên tự chạy đánh giá độ phù hợp trước khi nộp.
///
/// Ranh giới quan trọng nhất được khóa ở đây: kết quả tự kiểm tra KHÔNG chạm vào
/// Application. Điểm sinh viên tự chạy không được lẫn vào con số nhà tuyển dụng đọc để
/// sàng lọc, và ngược lại.
/// </summary>
public class SelfCheckServiceTests
{
    /// <summary>
    /// Không cấu hình khóa Gemini nên GeminiAiService rơi về nhánh đối chiếu Tech Stack
    /// ngoại tuyến — đúng bằng cách nó chạy trên máy chưa cấu hình gì.
    /// </summary>
    private static SelfCheckService NewSvc(TestDb t, TimeProvider? clock = null)
    {
        var ai = AiServiceTestFactory.Offline();
        return new SelfCheckService(t.Db, new JobService(t.Db, null, clock), ai, new AiInputBuilder(), clock);
    }

    private static (User Student, Job Job) Seed(TestDb t, bool withCv = true, bool withProfile = true)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        if (withProfile) t.AddProfile(sv.Id, withCv: withCv);
        var job = t.AddJob(m.Id);
        return (sv, job);
    }

    // ===== Đường thành công =====

    [Fact]
    public async Task Run_SavesOneRow_AndReturnsIt()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);

        var (ok, msg) = await NewSvc(t).RunAsync(sv.Id, job.Id);

        Assert.True(ok, msg);
        var row = Assert.Single(t.NewContext().SelfChecks);
        Assert.Equal(sv.Id, row.UserId);
        Assert.Equal(job.Id, row.JobId);
        Assert.InRange(row.Score, 0, 100);
    }

    /// <summary>
    /// Ranh giới của P1-2: bảng Applications KHÔNG được đụng tới. Nếu self-check ghi vào
    /// AiScore thì nhà tuyển dụng sẽ đọc một con số do chính ứng viên tạo ra.
    /// </summary>
    [Fact]
    public async Task Run_DoesNotTouchTheApplication()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var app = t.AddApplication(job.Id, p.Id, aiScore: 42);

        await NewSvc(t).RunAsync(sv.Id, job.Id);

        var after = t.NewContext().Applications.Find(app.Id)!;
        Assert.Equal(42, after.AiScore);
        Assert.Null(after.AiStrengths);
        Assert.Null(after.AiScoredAt);
    }

    /// <summary>Chạy lại ghi THÊM bản ghi — sinh viên cần thấy mình tiến bộ giữa hai lần.</summary>
    [Fact]
    public async Task RunTwice_AppendsInsteadOfOverwriting()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        var svc = NewSvc(t);

        await svc.RunAsync(sv.Id, job.Id);
        await svc.RunAsync(sv.Id, job.Id);

        Assert.Equal(2, t.NewContext().SelfChecks.Count());
    }

    [Fact]
    public async Task GetLatest_ReturnsTheMostRecentRun()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        var svc = NewSvc(t);
        await svc.RunAsync(sv.Id, job.Id);
        await svc.RunAsync(sv.Id, job.Id);

        var latest = svc.GetLatest(sv.Id, job.Id)!;

        Assert.Equal(t.NewContext().SelfChecks.Max(x => x.Id), latest.Id);
    }

    [Fact]
    public void GetLatest_WhenNeverRun_IsNull()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);

        Assert.Null(NewSvc(t).GetLatest(sv.Id, job.Id));
    }

    /// <summary>
    /// Chưa cấu hình khóa Gemini thì vẫn phải có kết quả, và nhãn nguồn phải nói rõ là
    /// ngoại tuyến — im lặng rơi về công thức đối chiếu nghĩa là không ai biết mình đang
    /// đọc con số loại nào.
    /// </summary>
    [Fact]
    public async Task Run_WithoutGeminiKey_StillProducesResult_MarkedOffline()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);

        Assert.True((await NewSvc(t).RunAsync(sv.Id, job.Id)).ok);

        Assert.Equal(EvaluationSource.Offline, t.NewContext().SelfChecks.Single().Source);
    }

    // ===== Điều kiện bị từ chối =====

    [Fact]
    public async Task Run_WithoutProfile_IsRejected()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t, withProfile: false);

        var (ok, msg) = await NewSvc(t).RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Contains("hồ sơ IT", msg);
        Assert.Empty(t.NewContext().SelfChecks);
    }

    /// <summary>Cùng điều kiện và cùng câu chữ với luồng ứng tuyển — hai chỗ không được nói khác nhau.</summary>
    [Fact]
    public async Task Run_WithoutCv_IsRejected()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t, withCv: false);

        var (ok, msg) = await NewSvc(t).RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Contains("CV", msg);
        Assert.Empty(t.NewContext().SelfChecks);
    }

    [Fact]
    public async Task Run_OnClosedJob_IsRejected()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        job.Status = JobStatus.Closed;
        t.Db.SaveChanges();

        var (ok, msg) = await NewSvc(t).RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Contains("đã đóng", msg);
        Assert.Empty(t.NewContext().SelfChecks);
    }

    [Fact]
    public async Task Run_OnExpiredJob_IsRejected()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        job.Deadline = VietnamDateHelper.Today().AddDays(-1);
        t.Db.SaveChanges();

        var (ok, _) = await NewSvc(t).RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Empty(t.NewContext().SelfChecks);
    }

    // ===== Hạn mức ngày =====

    [Fact]
    public async Task SixthRunOfTheDay_IsRejected_WithReason()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        var clock = new FixedClock(new DateTime(2026, 3, 15, 5, 0, 0, DateTimeKind.Utc));  // 12:00 giờ VN
        var svc = NewSvc(t, clock);

        for (var i = 0; i < SelfCheck.DailyLimit; i++)
            Assert.True((await svc.RunAsync(sv.Id, job.Id)).ok, $"lượt {i + 1}");

        var (ok, msg) = await svc.RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Contains("hết", msg);
        Assert.Equal(SelfCheck.DailyLimit, t.NewContext().SelfChecks.Count());
    }

    /// <summary>
    /// Hạn mức reset theo NGÀY VIỆT NAM, không theo ngày UTC của container. Đồng hồ nhảy từ
    /// 23:00 sang 01:00 giờ VN — cùng một ngày UTC, nhưng đã sang ngày mới ở Việt Nam.
    /// </summary>
    [Fact]
    public async Task NextVietnamDay_RestoresTheQuota()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        // 16:00 UTC = 23:00 giờ VN ngày 15/3.
        var clock = new FixedClock(new DateTime(2026, 3, 15, 16, 0, 0, DateTimeKind.Utc));
        var svc = NewSvc(t, clock);
        for (var i = 0; i < SelfCheck.DailyLimit; i++) await svc.RunAsync(sv.Id, job.Id);
        Assert.False((await svc.RunAsync(sv.Id, job.Id)).ok);

        clock.Advance(TimeSpan.FromHours(2));   // 18:00 UTC = 01:00 giờ VN ngày 16/3

        Assert.Equal(SelfCheck.DailyLimit, svc.RemainingToday(sv.Id));
        Assert.True((await svc.RunAsync(sv.Id, job.Id)).ok);
    }

    /// <summary>Hạn mức tính theo NGƯỜI — lượt của sinh viên này không ăn vào lượt của người kia.</summary>
    [Fact]
    public async Task Quota_IsPerStudent()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        var other = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(other.Id);
        var svc = NewSvc(t);
        for (var i = 0; i < SelfCheck.DailyLimit; i++) await svc.RunAsync(sv.Id, job.Id);

        Assert.Equal(0, svc.RemainingToday(sv.Id));
        Assert.Equal(SelfCheck.DailyLimit, svc.RemainingToday(other.Id));
        Assert.True((await svc.RunAsync(other.Id, job.Id)).ok);
    }

    [Fact]
    public async Task RemainingToday_CountsDown()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t);
        var svc = NewSvc(t);
        Assert.Equal(SelfCheck.DailyLimit, svc.RemainingToday(sv.Id));

        await svc.RunAsync(sv.Id, job.Id);

        Assert.Equal(SelfCheck.DailyLimit - 1, svc.RemainingToday(sv.Id));
    }

    // ===== Dựng dữ liệu đưa vào AI =====

    /// <summary>
    /// Ba đường (chấm điểm, sinh câu hỏi, tự kiểm tra) phải đọc CÙNG một cách dựng dữ liệu.
    /// Nếu tách hai bản, điểm self-check và điểm nhà tuyển dụng sẽ lệch nhau mà không ai
    /// giải thích được vì sao.
    /// </summary>
    [Fact]
    public void AiInputBuilder_SelfCheckAndApplication_ShareTheSameShape()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var app = t.Db.Applications.Add(new Application
        {
            JobId = job.Id, CandidateProfileId = p.Id, CvFileNameSnapshot = "cv.pdf",
            CvDataSnapshot = p.CvData, CvContentTypeSnapshot = p.CvContentType,
            Status = ApplicationStatus.Submitted
        }).Entity;
        t.Db.SaveChanges();
        app.Job = job;
        var builder = new AiInputBuilder();

        var forApp = builder.ForApplication(app, p);
        var forSelf = builder.ForSelfCheck(p, job);

        // CV giống hệt nhau (bản chụp chính là bản hiện tại), nên bốn ô phải trùng khớp.
        Assert.Equal(forApp.CandidateText, forSelf.CandidateText);
        Assert.Equal(forApp.JobText, forSelf.JobText);
        Assert.Equal(forApp.CandidateTech, forSelf.CandidateTech);
        Assert.Equal(forApp.RequiredTech, forSelf.RequiredTech);
    }
}
