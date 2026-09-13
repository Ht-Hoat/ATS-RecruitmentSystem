using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

public class ApplicationServiceTests
{
    // Trong DI thật, ApplicationService và NotificationService dùng CHUNG 1 DbContext (scoped)
    // → phải truyền cùng t.Db để nằm chung transaction của UpdateStatus.
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db));

    // ATS-10.2 — Kịch bản 1: tin đã đóng
    [Fact]
    public void Apply_JobClosed_Fails()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id, status: "Closed");
        var svc = NewSvc(t);

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("đã đóng", msg);
    }

    // Kịch bản 2: chưa có hồ sơ
    [Fact]
    public void Apply_NoProfile_Fails()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("hồ sơ", msg);
    }

    // Kịch bản 3: chưa có CV
    [Fact]
    public void Apply_NoCv_Fails()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id, withCv: false);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("CV", msg);
    }

    // Kịch bản 4: hợp lệ
    [Fact]
    public void Apply_Valid_Succeeds()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.True(ok);
        Assert.Contains("thành công", msg);
        Assert.Equal(1, t.NewContext().Applications.Count());
    }

    // LỖ HỔNG ĐÃ VÁ: CV được đóng băng vào đơn tại thời điểm nộp,
    // SV đổi CV sau đó KHÔNG làm thay đổi CV đã nộp của đơn cũ.
    [Fact]
    public void Apply_SnapshotsCv_IndependentOfLaterProfileUpdate()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);                       // CvFileName = "cv.pdf", CvData chứa "CV test"
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);

        Assert.True(svc.Apply(job.Id, sv.Id, out _));
        var appId = t.NewContext().Applications.First().Id;

        // SV cập nhật CV MỚI (đè CvData trong CandidateProfiles)
        new ProfileService(t.Db).SaveCv(sv.Id,
            System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 CV MOI HOAN TOAN"), "cv_moi.pdf", "application/pdf");

        using var v = t.NewContext();
        var app = v.Applications.Find(appId)!;
        Assert.True(app.HasCvSnapshot);
        Assert.Equal("cv.pdf", app.CvFileNameSnapshot);                                  // tên CV lúc nộp
        var snap = System.Text.Encoding.UTF8.GetString(app.CvDataSnapshot!);
        Assert.Contains("CV test", snap);                                                // nội dung CV lúc nộp
        Assert.DoesNotContain("MOI HOAN TOAN", snap);                                     // KHÔNG bị thay bằng CV mới
        Assert.Equal("cv_moi.pdf", v.CandidateProfiles.Single(x => x.UserId == sv.Id).CvFileName); // hồ sơ đã đổi
    }

    // #9: không cho ứng tuyển khi tin đã quá hạn nộp (dù Status vẫn Open)
    [Fact]
    public void Apply_PastDeadline_Fails()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        job.Deadline = DateTime.Today.AddDays(-1);   // quá hạn hôm qua
        t.Db.SaveChanges();
        var svc = NewSvc(t);

        var ok = svc.Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("quá hạn", msg);
    }

    // #5: Mentor chỉ truy cập đơn thuộc tin mình tạo; Admin truy cập tất cả
    [Fact]
    public void CanAccess_OnlyOwnerOrAdmin()
    {
        using var t = new TestDb();
        var owner = t.AddUser("Owner", "o@itcp.vn", Roles.MentorId);
        var other = t.AddUser("Other", "x@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(owner.Id);
        var svc = NewSvc(t);
        svc.Apply(job.Id, sv.Id, out _);
        var appId = t.NewContext().Applications.First().Id;

        Assert.True(svc.CanAccess(appId, owner.Id, isAdmin: false));    // chủ tin → OK
        Assert.False(svc.CanAccess(appId, other.Id, isAdmin: false));   // mentor khác → chặn
        Assert.True(svc.CanAccess(appId, other.Id, isAdmin: true));     // admin → OK
    }

    // Kịch bản 5: ứng tuyển trùng
    [Fact]
    public void Apply_Duplicate_Fails()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);

        Assert.True(svc.Apply(job.Id, sv.Id, out _));
        var ok2 = svc.Apply(job.Id, sv.Id, out var msg2);

        Assert.False(ok2);
        Assert.Contains("đã ứng tuyển", msg2);
    }

    // ATS-17: đổi trạng thái ghi lịch sử + tạo thông báo
    [Fact]
    public void UpdateStatus_WritesHistory_AndNotifiesStudent()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);
        svc.Apply(job.Id, sv.Id, out _);
        var appId = t.NewContext().Applications.First().Id;

        // Dùng "Đang xem xét" chứ không phải "Phỏng vấn": từ N1.E, chuyển sang Phỏng vấn
        // bắt buộc kèm lịch hẹn, và trường hợp đó có bộ test riêng ở InterviewScheduleTests.
        var ok = svc.UpdateStatus(appId, ApplicationStatus.Reviewing, m.Id, out var msg);

        Assert.True(ok);
        using var v = t.NewContext();
        Assert.Equal(ApplicationStatus.Reviewing, v.Applications.Find(appId)!.Status);
        var h = Assert.Single(v.ApplicationStatusHistories);
        Assert.Equal(ApplicationStatus.Submitted, h.FromStatus);
        Assert.Equal(ApplicationStatus.Reviewing, h.ToStatus);
        Assert.Equal(1, v.Notifications.Count(n => n.UserId == sv.Id));   // NTF-01
    }

    // ATS-16.2: HrScore không ghi đè AiScore; FinalScore ưu tiên HrScore
    [Fact]
    public void SaveHrScore_KeepsAiScore_FinalScorePrefersHr()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);
        svc.Apply(job.Id, sv.Id, out _);
        var appId = t.NewContext().Applications.First().Id;

        svc.SaveAiEvaluation(appId, new AiEvaluation(60, "điểm mạnh", "thiếu sót", "lộ trình", "raw"));
        svc.SaveHrScore(appId, 85, "Ứng viên tốt hơn CV thể hiện");

        var a = t.NewContext().Applications.Find(appId)!;
        Assert.Equal(60, a.AiScore);       // AI giữ nguyên
        Assert.Equal(85, a.HrScore);
        Assert.Equal(85, a.FinalScore);    // ưu tiên HrScore
    }

    // ATS-15: xếp hạng theo điểm cuối giảm dần
    [Fact]
    public void GetByJob_SortByScore_OrdersByFinalScoreDesc()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);
        var job = t.AddJob(m.Id);
        var svc = NewSvc(t);
        svc.Apply(job.Id, sv1.Id, out _);
        svc.Apply(job.Id, sv2.Id, out _);
        var apps = t.NewContext().Applications.OrderBy(a => a.Id).ToList();
        svc.SaveAiEvaluation(apps[0].Id, new AiEvaluation(40, "s", "m", "r", "raw"));
        svc.SaveAiEvaluation(apps[1].Id, new AiEvaluation(90, "s", "m", "r", "raw"));

        var ranked = svc.GetByJob(job.Id, "score");

        Assert.Equal(90, ranked[0].FinalScore);
        Assert.Equal(40, ranked[1].FinalScore);
    }

    // =====================================================================
    // N1.G TESTS: CountRecentApplicants, CountUnreviewed, TopUnreviewed
    // =====================================================================

    [Fact]
    public void CountRecentApplicants_Admin_CountsAllWithinHours()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j1 = t.AddJob(m1.Id, "Job 1");
        var j2 = t.AddJob(m2.Id, "Job 2");

        var svc = NewSvc(t);
        svc.Apply(j1.Id, sv1.Id, out _);
        svc.Apply(j2.Id, sv2.Id, out _);

        // Admin thấy toàn bộ đơn trong 24 giờ
        var count = svc.CountRecentApplicants(admin.Id, isAdmin: true, withinHours: 24);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CountRecentApplicants_Mentor_CountsOnlyOwnJobs()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j1 = t.AddJob(m1.Id, "Job 1");
        var j2 = t.AddJob(m2.Id, "Job 2");

        var svc = NewSvc(t);
        svc.Apply(j1.Id, sv1.Id, out _);
        svc.Apply(j2.Id, sv2.Id, out _);

        // M1 chỉ thấy đơn của tin mình tạo (Job 1)
        var countM1 = svc.CountRecentApplicants(m1.Id, isAdmin: false, withinHours: 24);
        Assert.Equal(1, countM1);

        // M2 chỉ thấy đơn của tin mình tạo (Job 2)
        var countM2 = svc.CountRecentApplicants(m2.Id, isAdmin: false, withinHours: 24);
        Assert.Equal(1, countM2);
    }

    [Fact]
    public void CountRecentApplicants_ExcludesOutsideHours()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j = t.AddJob(m.Id, "Job");
        var svc = NewSvc(t);
        svc.Apply(j.Id, sv1.Id, out _);
        svc.Apply(j.Id, sv2.Id, out _);

        // Cập nhật 1 đơn thành quá 24 giờ trước
        var firstApp = t.Db.Applications.First(a => a.CandidateProfile!.UserId == sv1.Id);
        firstApp.AppliedAt = DateTime.Now.AddHours(-25);
        t.Db.SaveChanges();

        var count = svc.CountRecentApplicants(m.Id, isAdmin: false, withinHours: 24);
        Assert.Equal(1, count);
    }

    [Fact]
    public void CountRecentApplicants_InvalidHours_ThrowsArgumentException()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var svc = NewSvc(t);

        Assert.Throws<ArgumentException>(() => svc.CountRecentApplicants(m.Id, isAdmin: false, withinHours: 0));
        Assert.Throws<ArgumentException>(() => svc.CountRecentApplicants(m.Id, isAdmin: false, withinHours: -5));
    }

    [Fact]
    public void CountUnreviewed_Admin_CountsAllSubmitted()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var admin = t.AddUser("Admin", "admin@itcp.vn", Roles.AdminId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j1 = t.AddJob(m1.Id, "Job 1");
        var j2 = t.AddJob(m2.Id, "Job 2");

        var svc = NewSvc(t);
        svc.Apply(j1.Id, sv1.Id, out _);
        svc.Apply(j2.Id, sv2.Id, out _);

        // Đơn mới có trạng thái mặc định Submitted -> Admin đếm được cả 2
        var count = svc.CountUnreviewed(admin.Id, isAdmin: true);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CountUnreviewed_Mentor_CountsOnlyOwnSubmitted()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j1 = t.AddJob(m1.Id, "Job 1");
        var j2 = t.AddJob(m2.Id, "Job 2");

        var svc = NewSvc(t);
        svc.Apply(j1.Id, sv1.Id, out _);
        svc.Apply(j2.Id, sv2.Id, out _);

        var countM1 = svc.CountUnreviewed(m1.Id, isAdmin: false);
        Assert.Equal(1, countM1);

        var countM2 = svc.CountUnreviewed(m2.Id, isAdmin: false);
        Assert.Equal(1, countM2);
    }

    [Fact]
    public void CountUnreviewed_ExcludesOtherStatuses()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j = t.AddJob(m.Id, "Job");
        var svc = NewSvc(t);
        svc.Apply(j.Id, sv1.Id, out _);
        svc.Apply(j.Id, sv2.Id, out _);

        // Chuyển sv1 sang trạng thái Reviewing
        var app1 = t.Db.Applications.First(a => a.CandidateProfile!.UserId == sv1.Id);
        svc.UpdateStatus(app1.Id, ApplicationStatus.Reviewing, m.Id, out _);

        var count = svc.CountUnreviewed(m.Id, isAdmin: false);
        Assert.Equal(1, count); // Chỉ còn sv2 ở trạng thái Submitted
    }

    [Fact]
    public void TopUnreviewed_RespectsTakeLimit()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var j = t.AddJob(m.Id, "Job");
        var svc = NewSvc(t);

        for (int i = 1; i <= 7; i++)
        {
            var sv = t.AddUser($"SV{i}", $"sv{i}@itcp.vn", Roles.StudentId);
            t.AddProfile(sv.Id);
            svc.Apply(j.Id, sv.Id, out _);
        }

        var top3 = svc.TopUnreviewed(m.Id, isAdmin: false, take: 3);
        Assert.Equal(3, top3.Count);
    }

    [Fact]
    public void TopUnreviewed_InvalidTake_ReturnsEmpty()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var j = t.AddJob(m.Id, "Job");
        var svc = NewSvc(t);
        svc.Apply(j.Id, sv.Id, out _);

        var zero = svc.TopUnreviewed(m.Id, isAdmin: false, take: 0);
        var neg = svc.TopUnreviewed(m.Id, isAdmin: false, take: -1);

        Assert.Empty(zero);
        Assert.Empty(neg);
    }

    [Fact]
    public void TopUnreviewed_OrdersByAppliedAtDesc()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV Cũ", "old@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV Mới", "new@itcp.vn", Roles.StudentId);
        var p1 = t.AddProfile(sv1.Id); p1.FullName = "SV Cũ";
        var p2 = t.AddProfile(sv2.Id); p2.FullName = "SV Mới";
        t.Db.SaveChanges();

        var j = t.AddJob(m.Id, "Job");
        var svc = NewSvc(t);
        svc.Apply(j.Id, sv1.Id, out _);
        svc.Apply(j.Id, sv2.Id, out _);

        // Giả lập thời gian nộp khác nhau
        var app1 = t.Db.Applications.First(a => a.CandidateProfile!.UserId == sv1.Id);
        var app2 = t.Db.Applications.First(a => a.CandidateProfile!.UserId == sv2.Id);
        app1.AppliedAt = DateTime.Now.AddDays(-2);
        app2.AppliedAt = DateTime.Now.AddMinutes(-5);
        t.Db.SaveChanges();

        var top = svc.TopUnreviewed(m.Id, isAdmin: false, take: 2);
        Assert.Equal(2, top.Count);
        Assert.Equal("SV Mới", top[0].FullName);
        Assert.Equal("SV Cũ", top[1].FullName);
    }

    [Fact]
    public void TopUnreviewed_IncludesJobContext()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("Nguyen Van A", "a@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        p.FullName = "Nguyen Van A";
        p.Email = "a@itcp.vn";
        t.Db.SaveChanges();

        var j = t.AddJob(m.Id, "Backend .NET Senior");
        var svc = NewSvc(t);
        svc.Apply(j.Id, sv.Id, out _);

        var top = svc.TopUnreviewed(m.Id, isAdmin: false, take: 1);
        Assert.Single(top);
        Assert.Equal(j.Id, top[0].JobId);
        Assert.Equal("Backend .NET Senior", top[0].JobTitle);
        Assert.Equal("Nguyen Van A", top[0].FullName);
        Assert.Equal("a@itcp.vn", top[0].Email);
        Assert.Equal(ApplicationStatus.Submitted, top[0].Status);
    }

    [Fact]
    public void TopUnreviewed_Mentor_RestrictedToOwnJobs()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var sv1 = t.AddUser("SV1", "sv1@itcp.vn", Roles.StudentId);
        var sv2 = t.AddUser("SV2", "sv2@itcp.vn", Roles.StudentId);
        t.AddProfile(sv1.Id); t.AddProfile(sv2.Id);

        var j1 = t.AddJob(m1.Id, "Job của M1");
        var j2 = t.AddJob(m2.Id, "Job của M2");

        var svc = NewSvc(t);
        svc.Apply(j1.Id, sv1.Id, out _);
        svc.Apply(j2.Id, sv2.Id, out _);

        var topM1 = svc.TopUnreviewed(m1.Id, isAdmin: false, take: 10);
        Assert.Single(topM1);
        Assert.Equal("Job của M1", topM1[0].JobTitle);

        var topM2 = svc.TopUnreviewed(m2.Id, isAdmin: false, take: 10);
        Assert.Single(topM2);
        Assert.Equal("Job của M2", topM2[0].JobTitle);
    }

    [Fact]
    public void TopUnreviewed_Mentor_CannotSeeOtherMentorApplications()
    {
        using var t = new TestDb();
        var m1 = t.AddUser("M1", "m1@itcp.vn", Roles.MentorId);
        var m2 = t.AddUser("M2", "m2@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);

        var j2 = t.AddJob(m2.Id, "Chỉ của M2");
        var svc = NewSvc(t);
        svc.Apply(j2.Id, sv.Id, out _);

        // M1 không có tin nào -> không thể thấy đơn của M2
        var topM1 = svc.TopUnreviewed(m1.Id, isAdmin: false, take: 5);
        Assert.Empty(topM1);
    }
}
