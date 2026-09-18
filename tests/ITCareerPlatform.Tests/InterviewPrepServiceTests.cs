using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Bộ câu hỏi luyện phỏng vấn thuộc về SINH VIÊN: mở khi được mời phỏng vấn, chỉ chủ đơn tạo
/// và đọc được, cần đồng ý xử lý dữ liệu bằng AI, và tạo lại tối đa một lần mỗi 24 giờ.
/// </summary>
public class InterviewPrepServiceTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 5, 0, 0, DateTimeKind.Utc);

    private static InterviewPrepService Svc(TestDb t, TimeProvider clock)
    {
        var apps = new ApplicationService(t.Db, new NotificationService(t.Db), t.CvStorage, null, clock);
        return new InterviewPrepService(t.Db, apps, AiServiceTestFactory.Offline(), new AiInputBuilder(t.CvStorage), clock);
    }

    private static (Application App, CandidateProfile Owner) Seed(TestDb t,
        string status = ApplicationStatus.Interview, bool consent = true)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id, withAiConsent: consent);
        var job = t.AddJob(m.Id, techStack: "C#,Docker");
        return (t.AddApplication(job.Id, p.Id, status), p);
    }

    [Fact]
    public async Task InvitedStudent_GetsQuestionsWithAnswerTips()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t);
        var svc = Svc(t, new FixedClock(Now));

        var (ok, msg) = await svc.GenerateAsync(app.Id, owner.UserId);

        Assert.True(ok, msg);
        var set = svc.Get(app.Id, owner.UserId);
        Assert.NotNull(set);
        Assert.NotEmpty(set!.Items);
        // Gợi ý viết CHO sinh viên, không còn là ghi chú chấm điểm cho người phỏng vấn.
        Assert.All(set.Items, q => Assert.DoesNotContain("nghe xem họ", q.Hint));
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.Reviewing)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task NotInvitedYet_IsRejected_AndSavesNothing(string status)
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t, status);
        var svc = Svc(t, new FixedClock(Now));

        var (ok, msg) = await svc.GenerateAsync(app.Id, owner.UserId);

        Assert.False(ok);
        Assert.Contains("mời phỏng vấn", msg);
        Assert.Null(t.NewContext().Applications.Find(app.Id)!.AiQuestions);
    }

    [Fact]
    public async Task AnotherStudent_CannotGenerateOrRead()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t);
        var other = t.AddStudentWithProfile("other");
        var svc = Svc(t, new FixedClock(Now));
        await svc.GenerateAsync(app.Id, owner.UserId);

        var (ok, msg) = await svc.GenerateAsync(app.Id, other.UserId);

        Assert.False(ok);
        Assert.Equal("Không tìm thấy đơn.", msg);
        Assert.Null(svc.Get(app.Id, other.UserId));
        Assert.NotNull(svc.Get(app.Id, owner.UserId));
    }

    [Fact]
    public async Task WithoutAiConsent_IsRejected_WithTheReason()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t, consent: false);

        var (ok, msg) = await Svc(t, new FixedClock(Now)).GenerateAsync(app.Id, owner.UserId);

        Assert.False(ok);
        Assert.Equal(AiConsentGate.BlockedForStudent, msg);
    }

    [Fact]
    public async Task Regenerate_IsLimitedToOncePer24Hours()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t);
        var clock = new FixedClock(Now);
        var svc = Svc(t, clock);
        Assert.True((await svc.GenerateAsync(app.Id, owner.UserId)).ok);

        clock.Advance(TimeSpan.FromHours(23));
        var (again, msg) = await svc.GenerateAsync(app.Id, owner.UserId);
        Assert.False(again);
        Assert.Contains("tạo lại sau", msg);

        clock.Advance(TimeSpan.FromHours(2));   // đã quá 24 giờ
        Assert.True((await svc.GenerateAsync(app.Id, owner.UserId)).ok);
    }

    [Fact]
    public void WhyNot_IsNull_WhenGenerationIsAllowed()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t);

        Assert.Null(Svc(t, new FixedClock(Now)).WhyNot(app.Id, owner.UserId));
    }
}
