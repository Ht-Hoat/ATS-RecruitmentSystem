using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// N1.E: lịch phỏng vấn gắn vào trạng thái đơn (Hướng B) · N1.B: ghi chú nội bộ.
public class InterviewScheduleTests
{
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db), t.CvStorage);

    // P0-2: InterviewSchedule.At là mốc UTC (endpoint đã quy đổi từ giờ VN trước khi dựng).
    // Dùng DateTime.Now ở đây thì trên máy UTC+7 một lịch "2 giờ trước" lại rơi vào tương
    // lai của UTC, và test "từ chối lịch quá khứ" xanh hay đỏ tùy múi giờ của máy chạy.
    private static InterviewSchedule Tomorrow(string? link = "https://meet.google.com/abc-defg-hij") =>
        new(DateTime.UtcNow.AddDays(1), link, "Vòng 1 — kỹ thuật");

    /// <summary>
    /// Dựng một đơn ở trạng thái "Đang xem xét" và trả về (id đơn, mentor, sinh viên).
    ///
    /// P1-4: đây là trạng thái DUY NHẤT chuyển thẳng sang "Phỏng vấn" được. Bộ test này nói
    /// về lịch phỏng vấn chứ không về luồng trạng thái, nên nó bắt đầu ngay trước bước đó —
    /// luồng được kiểm riêng trong StatusFlowTests.
    /// </summary>
    private static (int AppId, User Mentor, User Student) Seed(TestDb t)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        return (t.AddApplication(job.Id, p.Id, ApplicationStatus.Reviewing).Id, m, sv);
    }

    // ===== Lưu lịch =====

    [Fact]
    public void MoveToInterview_WithSchedule_SavesAllThreeFields()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var schedule = Tomorrow();

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, schedule, m.Id, out var msg);

        Assert.True(ok);
        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(ApplicationStatus.Interview, a.Status);
        Assert.Equal(schedule.At, a.InterviewAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal("https://meet.google.com/abc-defg-hij", a.InterviewLink);
        Assert.Equal("Vòng 1 — kỹ thuật", a.InterviewNote);
    }

    /// <summary>Lời mời phỏng vấn không có giờ hẹn thì vô nghĩa với sinh viên.</summary>
    [Fact]
    public void MoveToInterview_WithoutSchedule_Fails_AndDoesNotChangeStatus()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("thời gian phỏng vấn", msg);
        Assert.Equal(ApplicationStatus.Reviewing, t.NewContext().Applications.Find(appId)!.Status);
    }

    [Fact]
    public void MoveToInterview_PastDateTime_Fails()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var past = new InterviewSchedule(DateTime.UtcNow.AddHours(-2), "https://zoom.us/j/123", "");

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, past, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("tương lai", msg);
        Assert.Null(t.NewContext().Applications.Find(appId)!.InterviewAt);
    }

    /// <summary>
    /// Link được render thành thẻ a href trên trang của sinh viên, nên "javascript:" lọt qua
    /// đây là XSS do chính nhà tuyển dụng nhập vào — phải chặn ở tầng service, không phải ở form.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("meet.google.com/abc")]      // thiếu scheme -> không phải URI tuyệt đối
    [InlineData("ftp://example.com/file")]
    public void MoveToInterview_UnsafeLink_Rejected(string link)
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(link), m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("http", msg);
        Assert.Equal(ApplicationStatus.Reviewing, t.NewContext().Applications.Find(appId)!.Status);
    }

    /// <summary>Phỏng vấn trực tiếp thì không có link — chỉ giờ hẹn là bắt buộc.</summary>
    [Fact]
    public void MoveToInterview_EmptyLink_IsAllowed()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(""), m.Id, out _);

        Assert.True(ok);
        Assert.Null(t.NewContext().Applications.Find(appId)!.InterviewLink);   // rỗng lưu thành null
    }

    // ===== Đổi lịch =====

    /// <summary>
    /// Đổi lịch = giữ nguyên trạng thái "Phỏng vấn" nhưng gửi giờ mới. Bản cũ của UpdateStatus
    /// chặn ngay ở "Trạng thái không thay đổi", nên sẽ không đổi lịch được lần nào.
    /// </summary>
    [Fact]
    public void Reschedule_SameStatus_UpdatesTime_WithoutExtraHistoryRow()
    {
        using var t = new TestDb();
        var (appId, m, sv) = Seed(t);
        var svc = NewSvc(t);
        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(), m.Id, out _));

        var newTime = DateTime.UtcNow.AddDays(3);
        var ok = svc.UpdateStatus(appId, ApplicationStatus.Interview,
            new InterviewSchedule(newTime, "https://zoom.us/j/999", "Đổi sang thứ Sáu"), m.Id, out var msg);

        Assert.True(ok);
        Assert.Contains("lịch phỏng vấn", msg);

        using var v = t.NewContext();
        var a = v.Applications.Find(appId)!;
        Assert.Equal(newTime, a.InterviewAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal("https://zoom.us/j/999", a.InterviewLink);

        // Trạng thái không đổi nên không sinh thêm dòng lịch sử; nhưng sinh viên vẫn phải
        // được báo, nếu không thì họ đến vào giờ cũ.
        Assert.Single(v.ApplicationStatusHistories);
        Assert.Equal(2, v.Notifications.Count(n => n.UserId == sv.Id));
    }

    [Fact]
    public void SameStatus_WithoutSchedule_StillRejected()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);

        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Reviewing, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("không thay đổi", msg);
    }

    /// <summary>Chuyển sang trạng thái khác vẫn giữ lịch cũ để tra cứu buổi đã diễn ra.</summary>
    [Fact]
    public void MovingAwayFromInterview_KeepsScheduleForReference()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var svc = NewSvc(t);
        svc.UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(), m.Id, out _);

        Assert.True(svc.UpdateStatus(appId, ApplicationStatus.Accepted, m.Id, out _));

        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(ApplicationStatus.Accepted, a.Status);
        Assert.NotNull(a.InterviewAt);
        Assert.Equal("https://meet.google.com/abc-defg-hij", a.InterviewLink);
    }

    // ===== Thông báo cho sinh viên =====

    [Fact]
    public void InterviewNotification_CarriesTimeAndLink_AndPointsAtTheApplication()
    {
        using var t = new TestDb();
        var (appId, m, sv) = Seed(t);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(), m.Id, out _);

        var n = Assert.Single(t.NewContext().Notifications.Where(x => x.UserId == sv.Id));
        Assert.Equal("Mời phỏng vấn", n.Title);
        Assert.Contains("meet.google.com", n.Message);
        // Trỏ vào ĐƠN cụ thể, không phải danh sách: thứ cần đọc nằm trong chính đơn đó.
        Assert.Equal($"/my-applications/{appId}", n.Link);
    }

    /// <summary>Tên tin 160 ký tự + link 400 ký tự vượt giới hạn cột 500 mà không ai cố ý.</summary>
    [Fact]
    public void Notification_LongContent_IsClipped_NotRejected()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id, title: new string('T', 160));
        var appId = t.AddApplication(job.Id, p.Id, ApplicationStatus.Reviewing).Id;

        var longLink = "https://meet.google.com/" + new string('x', 370);
        var ok = NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview,
            new InterviewSchedule(DateTime.UtcNow.AddDays(1), longLink, ""), m.Id, out _);

        Assert.True(ok);
        var n = Assert.Single(t.NewContext().Notifications);
        Assert.True(n.Message.Length <= 500);
        Assert.True(n.Title.Length <= 160);
    }

    /// <summary>Lịch phỏng vấn PHẢI có trong ApplicationDetail — sinh viên cần đọc.</summary>
    [Fact]
    public void ApplicationDetail_CarriesInterviewSchedule()
    {
        using var t = new TestDb();
        var (appId, m, _) = Seed(t);
        var svc = NewSvc(t);
        svc.UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(), m.Id, out _);

        var detail = svc.GetDetail(appId)!;

        Assert.True(detail.HasInterview);
        Assert.Equal("https://meet.google.com/abc-defg-hij", detail.InterviewLink);
        Assert.Equal("Vòng 1 — kỹ thuật", detail.InterviewNote);
    }

    [Fact]
    public void GetByCandidate_CarriesInterviewSoTheCardCanShowIt()
    {
        using var t = new TestDb();
        var (appId, m, sv) = Seed(t);
        var svc = NewSvc(t);
        svc.UpdateStatus(appId, ApplicationStatus.Interview, Tomorrow(), m.Id, out _);

        var item = Assert.Single(svc.GetByCandidate(sv.Id));

        Assert.True(item.HasInterview);
        Assert.NotNull(item.InterviewAt);
        Assert.Equal("https://meet.google.com/abc-defg-hij", item.InterviewLink);
    }
}
