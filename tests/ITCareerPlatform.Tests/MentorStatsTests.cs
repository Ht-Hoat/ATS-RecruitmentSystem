using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// N1.G: thống kê cho dashboard Mentor.
//
// Trọng tâm của bộ test này là PHẠM VI, không phải phép cộng: dashboard cũ đếm trên
// toàn bảng, nên một Mentor đọc được lưu lượng tuyển dụng của mọi Mentor khác. Con số
// đúng mà sai phạm vi vẫn là một lỗ rò, và nó không tự lộ ra khi chạy tay với một tài khoản.
public class MentorStatsTests
{
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db), t.CvStorage);

    /// <summary>Hai Mentor, mỗi người một tin và số đơn khác nhau — không ai thấy số của người kia.</summary>
    [Fact]
    public void GetMentorStats_ScopesToOwnJobs()
    {
        using var t = new TestDb();
        var mentorA = t.AddUser("A", "a@itcp.vn", Roles.MentorId);
        var mentorB = t.AddUser("B", "b@itcp.vn", Roles.MentorId);

        var jobA = t.AddJob(mentorA.Id, "Backend A");
        var jobB = t.AddJob(mentorB.Id, "Frontend B", category: "Frontend");

        // 2 đơn vào tin của A, 1 đơn vào tin của B
        t.AddApplication(jobA.Id, t.AddStudentWithProfile("1").Id);
        t.AddApplication(jobA.Id, t.AddStudentWithProfile("2").Id);
        t.AddApplication(jobB.Id, t.AddStudentWithProfile("3").Id);

        var svc = NewSvc(t);

        var a = svc.GetMentorStats(mentorA.Id);
        Assert.Equal(1, a.TotalJobs);
        Assert.Equal(2, a.TotalApplications);

        var b = svc.GetMentorStats(mentorB.Id);
        Assert.Equal(1, b.TotalJobs);
        Assert.Equal(1, b.TotalApplications);

        // Admin (null) nhìn thấy toàn hệ thống
        var all = svc.GetMentorStats(null);
        Assert.Equal(2, all.TotalJobs);
        Assert.Equal(3, all.TotalApplications);
    }

    [Fact]
    public void CountGroupedByCategory_ScopesToOwnJobs()
    {
        using var t = new TestDb();
        var mentorA = t.AddUser("A", "a@itcp.vn", Roles.MentorId);
        var mentorB = t.AddUser("B", "b@itcp.vn", Roles.MentorId);
        var jobA = t.AddJob(mentorA.Id, "Backend A", category: "Backend");
        var jobB = t.AddJob(mentorB.Id, "Mobile B", category: "Mobile");

        t.AddApplication(jobA.Id, t.AddStudentWithProfile("1").Id);
        t.AddApplication(jobB.Id, t.AddStudentWithProfile("2").Id);

        var svc = NewSvc(t);

        var a = svc.CountGroupedByCategory(mentorA.Id);
        Assert.Equal("Backend", Assert.Single(a).Category);

        // Chuyên ngành của Mentor B không được xuất hiện trong biểu đồ của Mentor A.
        Assert.DoesNotContain(a, x => x.Category == "Mobile");

        Assert.Equal(2, svc.CountGroupedByCategory(null).Count);
    }

    /// <summary>Trung bình tính trên đơn ĐÃ chấm, và ưu tiên điểm Mentor chốt hơn điểm AI.</summary>
    [Fact]
    public void GetMentorStats_AvgFinalScore_IgnoresUnscored_AndPrefersHrScore()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var job = t.AddJob(m.Id);

        t.AddApplication(job.Id, t.AddStudentWithProfile("1").Id, aiScore: 80);
        t.AddApplication(job.Id, t.AddStudentWithProfile("2").Id, aiScore: 10, hrScore: 60);
        t.AddApplication(job.Id, t.AddStudentWithProfile("3").Id);   // chưa chấm — không vào mẫu số

        var s = NewSvc(t).GetMentorStats(m.Id);

        Assert.Equal(3, s.TotalApplications);
        Assert.Equal(2, s.ScoredApplications);
        Assert.True(s.HasScores);
        Assert.Equal(70, s.AvgFinalScore);   // (80 + 60) / 2 — KHÔNG phải (80+10)/2 hay /3
    }

    [Fact]
    public void GetMentorStats_NoApplications_IsAllZero_NotDivideByZero()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id);

        var s = NewSvc(t).GetMentorStats(m.Id);

        Assert.Equal(0, s.TotalApplications);
        Assert.Equal(0, s.ConversionRate);
        Assert.Equal(0, s.AvgFinalScore);
        Assert.False(s.HasApplications);
        Assert.False(s.HasScores);      // phân biệt "trung bình 0" với "chưa chấm đơn nào"
    }

    [Fact]
    public void GetMentorStats_ConversionRate_CountsAcceptedOnly()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var job = t.AddJob(m.Id);

        t.AddApplication(job.Id, t.AddStudentWithProfile("1").Id, status: ApplicationStatus.Accepted);
        t.AddApplication(job.Id, t.AddStudentWithProfile("2").Id, status: ApplicationStatus.Rejected);
        t.AddApplication(job.Id, t.AddStudentWithProfile("3").Id, status: ApplicationStatus.Interview);
        t.AddApplication(job.Id, t.AddStudentWithProfile("4").Id, status: ApplicationStatus.Submitted);

        var s = NewSvc(t).GetMentorStats(m.Id);

        Assert.Equal(1, s.Accepted);
        Assert.Equal(1, s.Rejected);
        Assert.Equal(1, s.Interviewing);
        Assert.Equal(1, s.PendingReview);
        Assert.Equal(25, s.ConversionRate);
    }

    /// <summary>Ngày không có đơn nào vẫn phải là một điểm bằng 0, không được biến mất.</summary>
    [Fact]
    public void ApplicationsPerDay_FillsGapDays_WithZero()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var job = t.AddJob(m.Id);

        // P0-2: AppliedAt lưu UTC, còn biểu đồ gộp theo ngày VIỆT NAM. Gieo đúng mốc UTC
        // của 00:00 giờ VN để khẳng định phép gộp không đẩy đơn sang cột hôm trước.
        t.AddApplication(job.Id, t.AddStudentWithProfile("1").Id, appliedAt: TestDb.VietnamDayStartUtc());
        t.AddApplication(job.Id, t.AddStudentWithProfile("2").Id, appliedAt: TestDb.VietnamDayStartUtc(-4));

        var days = NewSvc(t).ApplicationsPerDay(m.Id, days: 7);

        Assert.Equal(7, days.Count);                                  // đủ 7 điểm, kể cả ngày trống
        Assert.Equal(VietnamDateHelper.Today(), days[^1].Day);        // điểm cuối là hôm nay theo giờ VN
        Assert.Equal(1, days[^1].Count);
        Assert.Equal(1, days.Single(d => d.Day == VietnamDateHelper.Today().AddDays(-4)).Count);
        Assert.Equal(0, days.Single(d => d.Day == VietnamDateHelper.Today().AddDays(-1)).Count);
        Assert.Equal(2, days.Sum(d => d.Count));
    }

    [Fact]
    public void ApplicationsPerDay_ExcludesOtherMentors()
    {
        using var t = new TestDb();
        var mentorA = t.AddUser("A", "a@itcp.vn", Roles.MentorId);
        var mentorB = t.AddUser("B", "b@itcp.vn", Roles.MentorId);
        var jobA = t.AddJob(mentorA.Id);
        var jobB = t.AddJob(mentorB.Id, "Tin của B");

        t.AddApplication(jobA.Id, t.AddStudentWithProfile("1").Id, appliedAt: TestDb.VietnamDayStartUtc());
        t.AddApplication(jobB.Id, t.AddStudentWithProfile("2").Id, appliedAt: TestDb.VietnamDayStartUtc());

        Assert.Equal(1, NewSvc(t).ApplicationsPerDay(mentorA.Id, days: 3).Sum(d => d.Count));
        Assert.Equal(2, NewSvc(t).ApplicationsPerDay(null, days: 3).Sum(d => d.Count));
    }

    [Fact]
    public void TopJobsByApplicants_OrdersByCountDesc_AndScopes()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var other = t.AddUser("O", "o@itcp.vn", Roles.MentorId);

        var quiet = t.AddJob(m.Id, "Tin ít hồ sơ");
        var busy = t.AddJob(m.Id, "Tin nhiều hồ sơ");
        var foreign = t.AddJob(other.Id, "Tin của Mentor khác");

        t.AddApplication(quiet.Id, t.AddStudentWithProfile("1").Id);
        t.AddApplication(busy.Id, t.AddStudentWithProfile("2").Id);
        t.AddApplication(busy.Id, t.AddStudentWithProfile("3").Id);
        t.AddApplication(foreign.Id, t.AddStudentWithProfile("4").Id);

        var top = NewSvc(t).TopJobsByApplicants(m.Id);

        Assert.Equal(2, top.Count);                       // tin của Mentor khác không lọt vào
        Assert.Equal("Tin nhiều hồ sơ", top[0].JobTitle);
        Assert.Equal(2, top[0].Count);
        Assert.Equal(1, top[1].Count);
        Assert.DoesNotContain(top, x => x.JobId == foreign.Id);
    }

    /// <summary>Tin chưa có hồ sơ nào vẫn phải xuất hiện — Mentor cần biết tin nào đang ế.</summary>
    [Fact]
    public void TopJobsByApplicants_IncludesJobsWithZeroApplicants()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        t.AddJob(m.Id, "Tin chưa ai nộp");

        var top = NewSvc(t).TopJobsByApplicants(m.Id);

        Assert.Equal(0, Assert.Single(top).Count);
    }

    /// <summary>Hàm không tham số cũ vẫn phải trả số liệu toàn hệ thống — /dashboard đang dùng.</summary>
    [Fact]
    public void LegacyNoArgOverloads_StillReturnGlobalNumbers()
    {
        using var t = new TestDb();
        var mentorA = t.AddUser("A", "a@itcp.vn", Roles.MentorId);
        var mentorB = t.AddUser("B", "b@itcp.vn", Roles.MentorId);
        t.AddApplication(t.AddJob(mentorA.Id).Id, t.AddStudentWithProfile("1").Id);
        t.AddApplication(t.AddJob(mentorB.Id, category: "QA").Id, t.AddStudentWithProfile("2").Id);

        var svc = NewSvc(t);

        Assert.Equal(2, svc.CountGroupedByStatus().Values.Sum());
        Assert.Equal(2, svc.CountGroupedByCategory().Count);
    }
}
