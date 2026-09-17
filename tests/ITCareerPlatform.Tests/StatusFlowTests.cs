using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-4: luồng chuyển trạng thái hợp lệ + phản hồi gửi ứng viên.
///
/// Trước bản này UpdateStatus chỉ hỏi "trạng thái này có tồn tại không", nên đi được
/// Trúng tuyển → Đã nộp và Từ chối → Phỏng vấn, và MỖI lần đổi lại bắn một thông báo cho
/// sinh viên về một chuyện lẽ ra không xảy ra được.
/// </summary>
public class StatusFlowTests
{
    private static ApplicationService NewSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), new AuditService(t.Db));

    private static InterviewSchedule Soon() =>
        new(DateTime.UtcNow.AddDays(1), "https://meet.google.com/abc-defg-hij", "Vòng 1");

    private static (int AppId, User Mentor, User Student) Seed(
        TestDb t, string status = ApplicationStatus.Submitted)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        return (t.AddApplication(job.Id, p.Id, status).Id, m, sv);
    }

    // ===== Bảng luồng =====

    [Theory]
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Reviewing)]
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Reviewing, ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Reviewing, ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Rejected)]
    public void ValidTransitions_AreAllowed(string from, string to)
    {
        Assert.True(ApplicationStatusFlow.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ApplicationStatus.Accepted, ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.Rejected, ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Withdrawn, ApplicationStatus.Reviewing)]
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Accepted)]   // không bỏ qua các vòng giữa
    [InlineData(ApplicationStatus.Submitted, ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Interview, ApplicationStatus.Reviewing)]  // không lùi ngược
    public void InvalidTransitions_AreRejected(string from, string to)
    {
        Assert.False(ApplicationStatusFlow.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void TerminalStates_HaveNoNextStates(string status)
    {
        Assert.Empty(ApplicationStatusFlow.NextStates(status));
        Assert.True(ApplicationStatusFlow.IsTerminal(status));
    }

    /// <summary>
    /// "Phỏng vấn" → "Phỏng vấn" phải hợp lệ: đó là thao tác đổi lịch hoặc hẹn vòng tiếp.
    /// Bỏ nó đi thì Mentor không đổi được giờ hẹn sau khi đã gửi lời mời.
    /// </summary>
    [Fact]
    public void Interview_CanStayAtInterview_ForRescheduling()
    {
        Assert.True(ApplicationStatusFlow.CanTransition(ApplicationStatus.Interview, ApplicationStatus.Interview));
    }

    /// <summary>
    /// Dropdown và phần kiểm tra phải đọc CÙNG một bảng. Nếu lệch, người dùng thấy một lựa
    /// chọn rồi bị từ chối mà không có gì giải thích vì sao lựa chọn đó lại có mặt.
    /// </summary>
    [Fact]
    public void NextStates_NeverOffersSomethingCanTransitionRejects()
    {
        foreach (var from in ApplicationStatus.All)
            foreach (var to in ApplicationStatusFlow.NextStates(from))
                Assert.True(ApplicationStatusFlow.CanTransition(from, to), $"{from} -> {to}");
    }

    /// <summary>Mọi bước tiếp theo đề xuất cho Mentor đều phải là trạng thái Mentor chọn được.</summary>
    [Fact]
    public void NextStates_NeverOffersWithdrawn()
    {
        foreach (var from in ApplicationStatus.All)
            Assert.DoesNotContain(ApplicationStatus.Withdrawn, ApplicationStatusFlow.NextStates(from));
    }

    // ===== UpdateStatus tôn trọng bảng luồng =====

    [Fact]
    public void UpdateStatus_BackwardsFromAccepted_IsRejected_AndChangesNothing()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t, ApplicationStatus.Accepted);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Submitted, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("kết thúc", msg);
        using var v = t.NewContext();
        Assert.Equal(ApplicationStatus.Accepted, v.Applications.Find(appId)!.Status);
        Assert.Empty(v.ApplicationStatusHistories);
    }

    [Fact]
    public void UpdateStatus_FromRejectedToInterview_IsRejected()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t, ApplicationStatus.Rejected);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, Soon(), m.Id, out var msg);

        Assert.False(ok);
        Assert.Equal(ApplicationStatus.Rejected, t.NewContext().Applications.Find(appId)!.Status);
    }

    /// <summary>Câu từ chối phải nói ra các bước hợp lệ, nếu không Mentor chỉ biết là mình sai.</summary>
    [Fact]
    public void UpdateStatus_SkippingStages_ExplainsWhatIsAllowed()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Accepted, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains(ApplicationStatus.Reviewing, msg);
        Assert.Contains(ApplicationStatus.Rejected, msg);
    }

    /// <summary>
    /// Chuyển bị từ chối thì KHÔNG được sinh thông báo nào. Bản cũ bắn thông báo mỗi lần đổi,
    /// kể cả những lần đổi lẽ ra không hợp lệ.
    /// </summary>
    [Fact]
    public void RejectedTransition_CreatesNoNotification()
    {
        using var t = new TestDb();
        var (appId, m, sv) = Seed(t, ApplicationStatus.Accepted);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Reviewing, m.Id, out _);

        Assert.Empty(t.NewContext().Notifications.Where(n => n.UserId == sv.Id));
    }

    [Fact]
    public void FullHappyPath_WalksTheWholePipeline()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var svc = NewSvc(t);

        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Reviewing, m.Id, out var m1), m1);
        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Interview, Soon(), m.Id, out var m2), m2);
        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Accepted, m.Id, out var m3), m3);

        Assert.Equal(ApplicationStatus.Accepted, t.NewContext().Applications.Find(appId)!.Status);
    }

    // ===== Phản hồi gửi ứng viên =====

    [Fact]
    public void CandidateFeedback_IsSaved_AndReadableFromApplicationDetail()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var svc = NewSvc(t);
        const string feedback = "Hồ sơ tốt nhưng chưa đủ kinh nghiệm Docker cho vị trí này.";

        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Rejected, null, feedback, m.Id, out var msg), msg);

        Assert.Equal(feedback, svc.GetDetail(appId)!.CandidateFeedback);
    }

    /// <summary>
    /// Phản hồi chỉ được ghi ở đúng trạng thái "Từ chối". Form luôn gửi mọi ô lên (trang
    /// render tĩnh), nên ghi vô điều kiện sẽ gắn một lời từ chối vào đơn đang được mời phỏng vấn.
    /// </summary>
    [Fact]
    public void CandidateFeedback_IsIgnored_OnNonRejectingTransitions()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        Assert.True(NewSvc(t).UpdateStatus(appId, ApplicationStatus.Reviewing, null,
            "Câu này không được lưu", m.Id, out _));

        Assert.Null(t.NewContext().Applications.Find(appId)!.CandidateFeedback);
    }

    [Fact]
    public void CandidateFeedback_GoesIntoTheNotification()
    {
        using var t = new TestDb();
        var (appId, m, sv) = Seed(t);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Rejected, null,
            "Cần thêm kinh nghiệm với hệ thống phân tán.", m.Id, out _);

        var n = Assert.Single(t.NewContext().Notifications.Where(x => x.UserId == sv.Id));
        Assert.Contains("hệ thống phân tán", n.Message);
    }

    /// <summary>
    /// Ranh giới của P1-4: CandidateFeedback là trường ghi chú DUY NHẤT được phép có trong
    /// ApplicationDetail, vì record đó là thứ trang của sinh viên đọc.
    /// </summary>
    [Fact]
    public void ApplicationDetail_ExposesFeedback_ButStillHidesInternalNote()
    {
        var names = typeof(ApplicationDetail).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("CandidateFeedback", names);
        Assert.DoesNotContain("InternalNote", names);
    }

    [Fact]
    public void CandidateFeedback_LongerThanColumn_IsClipped_NotRejected()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        Assert.True(NewSvc(t).UpdateStatus(appId, ApplicationStatus.Rejected, null,
            new string('x', 1500), m.Id, out _));

        Assert.Equal(1000, t.NewContext().Applications.Find(appId)!.CandidateFeedback!.Length);
    }
}
