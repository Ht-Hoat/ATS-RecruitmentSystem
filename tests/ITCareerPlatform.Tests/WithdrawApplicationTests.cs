using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P0-4: sinh viên rút đơn ứng tuyển.
///
/// Không có chức năng này thì sinh viên nhận việc chỗ khác vẫn để lại đơn trong pipeline
/// của nhà tuyển dụng: vẫn bị chấm điểm, vẫn được mời phỏng vấn, và nhà tuyển dụng không
/// có cách nào biết ứng viên đã đi.
/// </summary>
public class WithdrawApplicationTests
{
    private static ApplicationService NewSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), new AuditService(t.Db));

    /// <summary>Một mentor, một sinh viên có hồ sơ, một tin, một đơn ở trạng thái cho trước.</summary>
    private static (int AppId, User Mentor, User Student, Job Job) Seed(
        TestDb t, string status = ApplicationStatus.Submitted)
    {
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        return (t.AddApplication(job.Id, p.Id, status).Id, m, sv, job);
    }

    // ===== Đường thành công =====

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.Reviewing)]
    [InlineData(ApplicationStatus.Interview)]
    public void Withdraw_FromOpenStatus_Succeeds(string from)
    {
        using var t = new TestDb();
        var (appId, _, sv, _) = Seed(t, from);

        var ok = NewSvc(t).Withdraw(appId, sv.Id, out var msg);

        Assert.True(ok, msg);
        Assert.Equal(ApplicationStatus.Withdrawn, t.NewContext().Applications.Find(appId)!.Status);
    }

    /// <summary>
    /// Dòng lịch sử phải mang id của SINH VIÊN. Ghi nhầm người thao tác thì dòng thời gian
    /// trông y hệt một lần nhà tuyển dụng loại hồ sơ — và đó là thứ duy nhất còn lại để tra
    /// khi hai bên nói khác nhau về chuyện đã xảy ra.
    /// </summary>
    [Fact]
    public void Withdraw_WritesHistoryRow_AttributedToTheStudent()
    {
        using var t = new TestDb();
        var (appId, _, sv, _) = Seed(t);

        Assert.True(NewSvc(t).Withdraw(appId, sv.Id, out _));

        var h = Assert.Single(t.NewContext().ApplicationStatusHistories);
        Assert.Equal(ApplicationStatus.Submitted, h.FromStatus);
        Assert.Equal(ApplicationStatus.Withdrawn, h.ToStatus);
        Assert.Equal(sv.Id, h.ChangedByUserId);
    }

    /// <summary>
    /// Thông báo đi NGƯỢC chiều so với đổi trạng thái: tới chủ tin, không tới sinh viên.
    /// Chuông thông báo vì thế phải bật cho mọi vai trò, nếu không thông báo này không tới ai.
    /// </summary>
    [Fact]
    public void Withdraw_NotifiesTheJobOwner_Once()
    {
        using var t = new TestDb();
        var (appId, m, sv, job) = Seed(t);

        Assert.True(NewSvc(t).Withdraw(appId, sv.Id, out _));

        using var v = t.NewContext();
        var n = Assert.Single(v.Notifications.Where(x => x.UserId == m.Id));
        Assert.Contains(job.Title, n.Message);
        Assert.Equal($"/applications/{appId}", n.Link);
        Assert.Empty(v.Notifications.Where(x => x.UserId == sv.Id));
    }

    [Fact]
    public void Withdraw_IsAudited()
    {
        using var t = new TestDb();
        var (appId, _, sv, _) = Seed(t);

        Assert.True(NewSvc(t).Withdraw(appId, sv.Id, out _));

        var log = Assert.Single(t.NewContext().AuditLogs.Where(a => a.Action == "Withdraw Application"));
        Assert.Equal(sv.Id, log.UserId);
    }

    // ===== Đường bị từ chối =====

    /// <summary>
    /// Không phải chủ đơn thì nhận đúng câu "Không tìm thấy đơn." — giống hệt trường hợp id
    /// không tồn tại. Tách hai thông báo ra chính là thứ xác nhận đơn nào có thật khi có ai
    /// đó dò id.
    /// </summary>
    [Fact]
    public void Withdraw_ByAnotherStudent_Fails_AndChangesNothing()
    {
        using var t = new TestDb();
        var (appId, _, _, _) = Seed(t);
        var intruder = t.AddUser("Kẻ lạ", "x@itcp.vn", Roles.StudentId);

        var ok = NewSvc(t).Withdraw(appId, intruder.Id, out var msg);

        Assert.False(ok);
        Assert.Equal("Không tìm thấy đơn.", msg);
        using var v = t.NewContext();
        Assert.Equal(ApplicationStatus.Submitted, v.Applications.Find(appId)!.Status);
        Assert.Empty(v.ApplicationStatusHistories);
        Assert.Empty(v.Notifications);
    }

    [Fact]
    public void Withdraw_ByTheMentorWhoOwnsTheJob_Fails()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);

        var ok = NewSvc(t).Withdraw(appId, m.Id, out var msg);

        Assert.False(ok);
        Assert.Equal("Không tìm thấy đơn.", msg);
    }

    [Fact]
    public void Withdraw_UnknownApplication_Fails()
    {
        using var t = new TestDb();
        var (_, _, sv, _) = Seed(t);

        Assert.False(NewSvc(t).Withdraw(9999, sv.Id, out var msg));
        Assert.Equal("Không tìm thấy đơn.", msg);
    }

    [Theory]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Withdraw_FromTerminalStatus_Fails_WithReason(string status)
    {
        using var t = new TestDb();
        var (appId, _, sv, _) = Seed(t, status);

        var ok = NewSvc(t).Withdraw(appId, sv.Id, out var msg);

        Assert.False(ok);
        Assert.NotEmpty(msg);
        using var v = t.NewContext();
        Assert.Equal(status, v.Applications.Find(appId)!.Status);
        Assert.Empty(v.ApplicationStatusHistories);
    }

    /// <summary>
    /// Nhà tuyển dụng không được rút đơn thay ứng viên: "Đã rút" nằm ngoài MentorSelectable,
    /// và UpdateStatus kiểm tra bằng đúng danh sách đó chứ không phải All.
    /// </summary>
    [Fact]
    public void Mentor_CannotSetWithdrawnViaUpdateStatus()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Withdrawn, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("không hợp lệ", msg);
        Assert.Equal(ApplicationStatus.Submitted, t.NewContext().Applications.Find(appId)!.Status);
    }

    [Fact]
    public void MentorSelectable_ExcludesWithdrawn_ButAllIncludesIt()
    {
        Assert.Contains(ApplicationStatus.Withdrawn, ApplicationStatus.All);
        Assert.DoesNotContain(ApplicationStatus.Withdrawn, ApplicationStatus.MentorSelectable);
    }

    // ===== Đơn đã rút rơi khỏi danh sách việc cần làm =====

    [Fact]
    public void WithdrawnApplication_IsNotCountedAsUnreviewed()
    {
        using var t = new TestDb();
        var (appId, m, sv, _) = Seed(t);
        var svc = NewSvc(t);
        Assert.Equal(1, svc.CountUnreviewed(m.Id, isAdmin: false));

        Assert.True(svc.Withdraw(appId, sv.Id, out _));

        Assert.Equal(0, svc.CountUnreviewed(m.Id, isAdmin: false));
        Assert.Empty(svc.TopUnreviewed(m.Id, isAdmin: false, take: 10));
        Assert.Equal(0, svc.GetMentorStats(m.Id).PendingReview);
    }

    // ===== Hiển thị trên /positions và trang chi tiết =====

    /// <summary>
    /// Sau khi rút, tin đó phải hiện "Đã rút đơn" chứ không phải "Đã ứng tuyển". Bản cũ chỉ
    /// trả về tập JobId nên hai tình huống không phân biệt được, và sinh viên thấy mình vẫn
    /// "đã ứng tuyển" vào một vị trí mình vừa rút.
    /// </summary>
    [Fact]
    public void AppliedJobStatus_ReflectsWithdrawal()
    {
        using var t = new TestDb();
        var (appId, _, sv, job) = Seed(t);
        var svc = NewSvc(t);

        Assert.Equal(ApplicationStatus.Submitted, svc.AppliedJobStatus(sv.Id)[job.Id]);

        Assert.True(svc.Withdraw(appId, sv.Id, out _));

        Assert.Equal(ApplicationStatus.Withdrawn, svc.AppliedJobStatus(sv.Id)[job.Id]);
    }

    /// <summary>
    /// Phiên bản này CHƯA cho nộp lại vào cùng một tin — index duy nhất (JobId,
    /// CandidateProfileId) đang chặn. Đây là quyết định có ý thức, không phải bỏ sót; test
    /// khóa lại hành vi để lần mở khóa sau này là một thay đổi có chủ đích.
    /// </summary>
    [Fact]
    public void ReapplyingAfterWithdrawal_IsStillBlocked()
    {
        using var t = new TestDb();
        var (appId, _, sv, job) = Seed(t);
        var svc = NewSvc(t);
        Assert.True(svc.Withdraw(appId, sv.Id, out _));

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("đã ứng tuyển", msg);
    }
}
