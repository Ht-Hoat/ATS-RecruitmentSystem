using System.Text;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-3: email cho ba sự kiện quan trọng, kèm tệp lịch .ics cho lời mời phỏng vấn.
///
/// Không có đường email nào trong repo trước bản này: giờ hẹn và link họp chỉ nằm trong
/// chuông thông báo, nên sinh viên không đăng nhập là mất buổi phỏng vấn.
/// </summary>
public class EmailOutboxTests
{
    private static ApplicationService NewSvc(TestDb t, TimeProvider? clock = null) =>
        new(t.Db, new NotificationService(t.Db), new AuditService(t.Db), clock);

    private static (int AppId, User Mentor, CandidateProfile Profile, Job Job) Seed(
        TestDb t, string status = ApplicationStatus.Reviewing)
    {
        var company = new Company { Name = "FPT Software" };
        t.Db.Companies.Add(company);
        t.Db.SaveChanges();

        var m = t.AddMentor(companyId: company.Id);
        var sv = t.AddUser("Nguyễn Văn A", "vana@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        p.Email = "vana@itcp.vn";
        p.FullName = "Nguyễn Văn A";
        t.Db.SaveChanges();

        var job = t.AddJob(m.Id, title: "Backend .NET", companyId: company.Id);
        return (t.AddApplication(job.Id, p.Id, status).Id, m, p, job);
    }

    private static InterviewSchedule At(DateTime utc, string? link = "https://meet.google.com/abc-defg-hij") =>
        new(utc, link, "Vòng 1 — kỹ thuật");

    // ===== Xếp hàng đúng lúc, đúng người =====

    [Fact]
    public void MoveToInterview_QueuesExactlyOneEmail_WithIcsAttachment()
    {
        using var t = new TestDb();
        var (appId, m, p, _) = Seed(t);

        Assert.True(NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview,
            At(DateTime.UtcNow.AddDays(1)), m.Id, out var msg), msg);

        var mail = Assert.Single(t.NewContext().EmailOutbox);
        Assert.Equal(p.Email, mail.ToEmail);
        Assert.Contains("phỏng vấn", mail.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("phong-van.ics", mail.AttachmentName);
        Assert.NotNull(mail.AttachmentContent);
        Assert.Null(mail.SentAt);
    }

    /// <summary>Tên công ty (P1-1) phải có trong email — ứng viên cần biết mình trao đổi với ai.</summary>
    [Fact]
    public void InterviewEmail_NamesThePositionAndCompany()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, At(DateTime.UtcNow.AddDays(1)), m.Id, out _);

        var mail = t.NewContext().EmailOutbox.Single();
        Assert.Contains("Backend .NET", mail.Subject);
        Assert.Contains("FPT Software", mail.Subject);
    }

    [Theory]
    [InlineData(ApplicationStatus.Accepted)]
    [InlineData(ApplicationStatus.Rejected)]
    public void TerminalDecisions_QueueOneEmailEach(string status)
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t, ApplicationStatus.Interview);

        Assert.True(NewSvc(t).UpdateStatus(appId, status, m.Id, out var msg), msg);

        var mail = Assert.Single(t.NewContext().EmailOutbox);
        Assert.Contains("Backend .NET", mail.Subject);
    }

    /// <summary>Chuyển sang "Đang xem xét" là việc nội bộ — không đáng một email.</summary>
    [Fact]
    public void MovingToReviewing_QueuesNoEmail()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t, ApplicationStatus.Submitted);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Reviewing, m.Id, out _);

        Assert.Empty(t.NewContext().EmailOutbox);
    }

    /// <summary>
    /// Chuyển KHÔNG hợp lệ (P1-4) phải rơi trước mọi thao tác ghi, nên cũng không được để
    /// lại email nào. Đây là cách kiểm "giao dịch hỏng thì không sót email" mà không cần
    /// dựng lỗi nhân tạo trong transaction.
    /// </summary>
    [Fact]
    public void RejectedTransition_LeavesNoOutboxRow()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t, ApplicationStatus.Accepted);

        Assert.False(NewSvc(t).UpdateStatus(appId, ApplicationStatus.Rejected, m.Id, out _));

        Assert.Empty(t.NewContext().EmailOutbox);
    }

    /// <summary>
    /// Lịch phỏng vấn không hợp lệ cũng vậy: từ chối ở bước kiểm tra nghĩa là không có gì
    /// được ghi, kể cả email.
    /// </summary>
    [Fact]
    public void InvalidSchedule_LeavesNoOutboxRow()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);
        var clock = new FixedClock(new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));

        Assert.False(NewSvc(t, clock).UpdateStatus(appId, ApplicationStatus.Interview,
            At(new DateTime(2026, 3, 15, 4, 0, 0)), m.Id, out _));

        using var v = t.NewContext();
        Assert.Empty(v.EmailOutbox);
        Assert.Equal(ApplicationStatus.Reviewing, v.Applications.Find(appId)!.Status);
    }

    /// <summary>
    /// RANH GIỚI: email đi ra khỏi hệ thống và có thể được chuyển tiếp cho bất kỳ ai, nên
    /// tuyệt đối không mang điểm số hay ghi chú nội bộ của Mentor.
    /// </summary>
    [Fact]
    public void RejectionEmail_CarriesFeedback_ButNeverScoresOrInternalNotes()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t, ApplicationStatus.Interview);
        var svc = NewSvc(t);
        svc.SaveHrScore(appId, 37, "Yếu phần thuật toán, không nên tuyển.", m.Id);
        svc.SaveInternalNote(appId, "Ứng viên nói chuyện lan man, không tập trung.", m.Id);

        svc.UpdateStatus(appId, ApplicationStatus.Rejected, null,
            "Bạn nên bổ sung kinh nghiệm thực tế với hệ thống phân tán.", m.Id, out _);

        var mail = t.NewContext().EmailOutbox.Single();
        Assert.Contains("hệ thống phân tán", mail.Body);
        Assert.DoesNotContain("37", mail.Body);
        Assert.DoesNotContain("thuật toán", mail.Body);
        Assert.DoesNotContain("lan man", mail.Body);
    }

    // ===== Nội dung .ics =====

    [Fact]
    public void Ics_WritesStartTimeInUtc_MatchingTheVietnamTimeEntered()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);
        // Mentor gõ 14:30 ngày 20/3 theo giờ Việt Nam; endpoint quy về UTC trước khi lưu.
        var vn = new DateTime(2026, 3, 20, 14, 30, 0);
        var utc = Ui.FromVietnamTime(vn);
        // Đồng hồ cố định đứng TRƯỚC buổi hẹn, nếu không ValidateSchedule từ chối vì lịch
        // nằm trong quá khứ và không có email nào được xếp hàng.
        var clock = new FixedClock(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(NewSvc(t, clock).UpdateStatus(appId, ApplicationStatus.Interview, At(utc), m.Id, out var msg), msg);

        var ics = Encoding.UTF8.GetString(t.NewContext().EmailOutbox.Single().AttachmentContent!);
        Assert.Contains("DTSTART:20260320T073000Z", ics);   // 14:30 VN = 07:30 UTC
        Assert.Contains("DTEND:20260320T083000Z", ics);     // mặc định 1 tiếng
    }

    [Fact]
    public void Ics_CarriesTitleAndMeetingLink()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);

        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, At(DateTime.UtcNow.AddDays(1)), m.Id, out _);

        var ics = Encoding.UTF8.GetString(t.NewContext().EmailOutbox.Single().AttachmentContent!);
        Assert.Contains("SUMMARY:Phỏng vấn: Backend .NET", ics);
        Assert.Contains("LOCATION:https://meet.google.com/abc-defg-hij", ics);
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("END:VEVENT", ics);
    }

    /// <summary>
    /// Đổi lịch phải là BẢN CẬP NHẬT của cùng một sự kiện, không phải sự kiện thứ hai. UID
    /// giữ nguyên, SEQUENCE tăng — thiếu SEQUENCE thì ứng dụng lịch bỏ qua bản mới và ứng
    /// viên đến vào giờ đã hủy.
    /// </summary>
    [Fact]
    public void Reschedule_KeepsTheSameUid_AndIncrementsSequence()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);
        var svc = NewSvc(t);
        svc.UpdateStatus(appId, ApplicationStatus.Interview, At(DateTime.UtcNow.AddDays(1)), m.Id, out _);

        svc.UpdateStatus(appId, ApplicationStatus.Interview, At(DateTime.UtcNow.AddDays(3)), m.Id, out var msg);

        var mails = t.NewContext().EmailOutbox.OrderBy(x => x.Id).ToList();
        Assert.Equal(2, mails.Count);
        var first = Encoding.UTF8.GetString(mails[0].AttachmentContent!);
        var second = Encoding.UTF8.GetString(mails[1].AttachmentContent!);

        Assert.Contains("UID:" + IcsBuilder.Uid(appId), first);
        Assert.Contains("UID:" + IcsBuilder.Uid(appId), second);
        Assert.Contains("SEQUENCE:1", first);
        Assert.Contains("SEQUENCE:2", second);
    }

    /// <summary>Dấu phẩy trong tiêu đề tin là dấu tách giá trị của RFC 5545 — phải được thoát.</summary>
    [Fact]
    public void Ics_EscapesCommasInTheTitle()
    {
        var ics = Encoding.UTF8.GetString(IcsBuilder.BuildInvite(
            1, 1, new DateTime(2026, 3, 20, 7, 30, 0, DateTimeKind.Utc),
            "Backend .NET, Java; Python", null, null));

        Assert.Contains(@"SUMMARY:Phỏng vấn: Backend .NET\, Java\; Python", ics);
    }

    [Fact]
    public void Ics_OmitsLocation_WhenInterviewIsInPerson()
    {
        var ics = Encoding.UTF8.GetString(IcsBuilder.BuildInvite(
            1, 1, new DateTime(2026, 3, 20, 7, 30, 0, DateTimeKind.Utc), "Backend .NET", null, null));

        Assert.DoesNotContain("LOCATION:", ics);
    }

    // ===== Tiến trình nền =====

    /// <summary>
    /// Chưa cấu hình SMTP thì KHÔNG được ném ngoại lệ, và bản ghi phải NẰM LẠI trong hàng
    /// đợi: đánh dấu đã gửi trong khi không gửi gì là nói dối, và cấu hình SMTP sau đó sẽ
    /// không cứu được những email đã bị đóng nhầm.
    /// </summary>
    [Fact]
    public async Task NullEmailSender_DoesNotThrow_AndLeavesTheRowPending()
    {
        using var t = new TestDb();
        var (appId, m, _, _) = Seed(t);
        NewSvc(t).UpdateStatus(appId, ApplicationStatus.Interview, At(DateTime.UtcNow.AddDays(1)), m.Id, out _);

        var sent = await OutboxTestHarness.RunOnce(t);

        Assert.Equal(0, sent);
        var row = t.NewContext().EmailOutbox.Single();
        Assert.Null(row.SentAt);
        Assert.Equal(0, row.Attempts);           // chưa cấu hình không tính là một lần thử
        Assert.Contains("SMTP", row.LastError);
    }

    /// <summary>
    /// Trần số lần thử là thứ giữ cho một địa chỉ email sai chính tả không được thử lại
    /// 30 giây một lần cho tới khi có người để ý — tức là mãi mãi.
    /// </summary>
    [Fact]
    public async Task RowsPastMaxAttempts_AreSkipped()
    {
        using var t = new TestDb();
        t.Db.EmailOutbox.Add(new EmailOutbox
        {
            ToEmail = "sai@khong-ton-tai.invalid",
            Subject = "Thử",
            Body = "Nội dung",
            Attempts = EmailOutbox.MaxAttempts,
            LastError = "Đã thử hết số lần cho phép."
        });
        t.Db.SaveChanges();

        await OutboxTestHarness.RunOnce(t);

        var row = t.NewContext().EmailOutbox.Single();
        Assert.Equal(EmailOutbox.MaxAttempts, row.Attempts);   // không tăng thêm
        Assert.Null(row.SentAt);
    }
}

/// <summary>Nhánh gửi THÀNH CÔNG và nhánh gửi HỎNG của tiến trình nền.</summary>
public class OutboxSenderTests
{
    private static void Queue(TestDb t, string to = "a@itcp.vn")
    {
        t.Db.EmailOutbox.Add(new EmailOutbox { ToEmail = to, Subject = "Thử", Body = "Nội dung" });
        t.Db.SaveChanges();
    }

    [Fact]
    public async Task ConfiguredSender_MarksTheRowAsSent()
    {
        using var t = new TestDb();
        Queue(t);
        var sender = new FakeEmailSender();

        var sent = await OutboxTestHarness.RunOnce(t, sender);

        Assert.Equal(1, sent);
        var row = t.NewContext().EmailOutbox.Single();
        Assert.NotNull(row.SentAt);
        Assert.Null(row.LastError);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("a@itcp.vn", Assert.Single(sender.Sent).To);
    }

    [Fact]
    public async Task SentRows_AreNotSentAgain()
    {
        using var t = new TestDb();
        Queue(t);
        var sender = new FakeEmailSender();
        await OutboxTestHarness.RunOnce(t, sender);

        await OutboxTestHarness.RunOnce(t, sender);

        Assert.Single(sender.Sent);
    }

    /// <summary>
    /// Gửi hỏng: ghi lý do và tăng số lần thử, KHÔNG đánh dấu đã gửi. Attempts phải tăng
    /// ngay cả khi lần gửi ném ngoại lệ — nếu không, một địa chỉ hỏng được thử lại vô hạn.
    /// </summary>
    [Fact]
    public async Task FailedSend_RecordsErrorAndCountsTheAttempt()
    {
        using var t = new TestDb();
        Queue(t);

        var sent = await OutboxTestHarness.RunOnce(t, new FakeEmailSender(throwMessage: "Máy chủ SMTP từ chối."));

        Assert.Equal(0, sent);
        var row = t.NewContext().EmailOutbox.Single();
        Assert.Null(row.SentAt);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("SMTP từ chối", row.LastError);
    }

    [Fact]
    public async Task RepeatedFailures_StopAfterMaxAttempts()
    {
        using var t = new TestDb();
        Queue(t);
        var sender = new FakeEmailSender(throwMessage: "Máy chủ SMTP từ chối.");

        for (var i = 0; i < EmailOutbox.MaxAttempts + 3; i++)
            await OutboxTestHarness.RunOnce(t, sender);

        Assert.Equal(EmailOutbox.MaxAttempts, t.NewContext().EmailOutbox.Single().Attempts);
    }
}
