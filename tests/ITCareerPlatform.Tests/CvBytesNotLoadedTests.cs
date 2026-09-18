using System.Text;
using System.Text.RegularExpressions;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Hàm di trú P2-2 cố ý GIỮ lại cột CvData/CvDataSnapshot (để còn quay lui), nên với dữ liệu
/// cũ hai cột đó đầy mãi. Mọi đường chỉ cần đọc/ghi vài cột chữ — trang hiển thị, đổi trạng
/// thái, rút đơn — không được kéo chúng về. Các test ở đây đọc CÂU SQL THẬT đã chạy.
/// </summary>
public class CvBytesNotLoadedTests
{
    // Cột byte[] xuất hiện trong danh sách SELECT hoặc trong SET của UPDATE. Còn dạng
    // "CvData" IS NOT NULL (hỏi có/không) thì được phép: nó không đưa nội dung đi đâu cả.
    private static readonly Regex ReadsOrWritesBytes = new("\"CvData(Snapshot)?\"(?!\\s+IS\\s)", RegexOptions.Compiled);

    private static void AssertNoCvBytes(List<string> sql)
    {
        var offending = sql.Where(s => ReadsOrWritesBytes.IsMatch(s)).ToList();
        Assert.True(offending.Count == 0, "Câu SQL chạm tới cột byte[] CV:\n" + string.Join("\n---\n", offending));
    }

    /// <summary>Hồ sơ + đơn kiểu cũ: nội dung CV chỉ nằm trong cột byte[], chưa có khóa.</summary>
    private static (Application App, User Mentor, CandidateProfile Profile) SeedLegacy(TestDb t)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);                         // CvData có, CvStorageKey null
        var job = t.AddJob(m.Id);
        var app = t.Db.Applications.Add(new Application
        {
            JobId = job.Id, CandidateProfileId = p.Id, Status = ApplicationStatus.Submitted,
            CvFileNameSnapshot = "cu.pdf", CvDataSnapshot = Encoding.UTF8.GetBytes("%PDF-1.4 CV CU"),
            AiScore = 71, HrScore = 66, HrNote = "Lý do chốt điểm có sẵn."
        }).Entity;
        t.Db.SaveChanges();
        return (app, m, p);
    }

    private static ApplicationService Svc(ITCareerPlatform.Data.AppDbContext db, TestDb t) =>
        new(db, new NotificationService(db), t.CvStorage);

    [Fact]
    public void UpdateStatus_NeitherReadsNorWritesCvBytes_AndKeepsThem()
    {
        using var t = new TestDb();
        var (app, m, _) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);

        Assert.True(Svc(db, t).UpdateStatus(app.Id, ApplicationStatus.Reviewing, m.Id, out var msg), msg);

        AssertNoCvBytes(sql);
        using var v = t.NewContext();
        var after = v.Applications.Find(app.Id)!;
        Assert.Equal(ApplicationStatus.Reviewing, after.Status);
        // Chỉ những cột vừa đổi được ghi — không cột nào khác bị bản gắn tạm ghi đè về mặc định.
        Assert.Equal("%PDF-1.4 CV CU", Encoding.UTF8.GetString(after.CvDataSnapshot!));
        Assert.Equal("cu.pdf", after.CvFileNameSnapshot);
        Assert.Equal(71, after.AiScore);
        Assert.Equal(66, after.HrScore);
        Assert.Equal("Lý do chốt điểm có sẵn.", after.HrNote);
        Assert.NotEqual(default, after.AppliedAt);
    }

    [Fact]
    public void UpdateStatus_ToInterview_WritesTheWholeSchedule_WithoutTouchingBytes()
    {
        using var t = new TestDb();
        var (app, m, _) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);
        var svc = Svc(db, t);
        Assert.True(svc.UpdateStatus(app.Id, ApplicationStatus.Reviewing, m.Id, out _));
        var at = DateTime.UtcNow.AddDays(2);

        Assert.True(svc.UpdateStatus(app.Id, ApplicationStatus.Interview,
            new InterviewSchedule(at, "https://meet.google.com/abc", "Vòng 1"), m.Id, out var msg), msg);

        AssertNoCvBytes(sql);
        var after = t.NewContext().Applications.Find(app.Id)!;
        Assert.Equal(ApplicationStatus.Interview, after.Status);
        Assert.Equal(at, after.InterviewAt!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal("https://meet.google.com/abc", after.InterviewLink);
        Assert.Equal("Vòng 1", after.InterviewNote);
        Assert.Equal(1, after.InterviewSequence);
        Assert.NotNull(after.CvDataSnapshot);
        // Email mời phỏng vấn vẫn được xếp hàng — bản chiếu phải mang đủ email/tên ứng viên.
        Assert.Single(t.NewContext().EmailOutbox);
    }

    [Fact]
    public void UpdateStatus_Reject_WritesFeedback_AndEmailCarriesIt()
    {
        using var t = new TestDb();
        var (app, m, _) = SeedLegacy(t);
        using var db = t.NewContext(new List<string>());

        Assert.True(Svc(db, t).UpdateStatus(app.Id, ApplicationStatus.Rejected, null,
            "Cần thêm kinh nghiệm Docker.", m.Id, out var msg), msg);

        using var v = t.NewContext();
        Assert.Equal("Cần thêm kinh nghiệm Docker.", v.Applications.Find(app.Id)!.CandidateFeedback);
        Assert.Contains("Docker", v.EmailOutbox.Single().Body);
    }

    [Fact]
    public void Withdraw_NeitherReadsNorWritesCvBytes()
    {
        using var t = new TestDb();
        var (app, _, p) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);

        Assert.True(Svc(db, t).Withdraw(app.Id, p.UserId, out var msg), msg);

        AssertNoCvBytes(sql);
        var after = t.NewContext().Applications.Find(app.Id)!;
        Assert.Equal(ApplicationStatus.Withdrawn, after.Status);
        Assert.NotNull(after.CvDataSnapshot);
    }

    [Fact]
    public void BulkUpdate_NeitherReadsNorWritesCvBytes()
    {
        using var t = new TestDb();
        var (app, m, _) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);

        var result = Svc(db, t).BulkUpdateStatus(app.JobId, new[] { app.Id }, ApplicationStatus.Reviewing, m.Id);

        Assert.Equal(1, result.Updated);
        AssertNoCvBytes(sql);
    }

    // ===== Trang hiển thị của sinh viên =====

    [Fact]
    public void GetCvStatus_LegacyProfile_ReportsCv_WithoutLoadingIt()
    {
        using var t = new TestDb();
        var (_, _, p) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);

        var status = new ProfileService(db, t.CvStorage).GetCvStatus(p.UserId);

        Assert.NotNull(status);
        Assert.True(status!.HasCv);
        Assert.True(status.HasAiConsent);
        AssertNoCvBytes(sql);
    }

    [Fact]
    public void GetCvStatus_NoProfile_IsNull()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);

        Assert.Null(new ProfileService(t.Db, t.CvStorage).GetCvStatus(sv.Id));
    }

    [Fact]
    public void GetForEdit_CarriesEveryFormField_ButNoCvBytes()
    {
        using var t = new TestDb();
        var (_, _, p) = SeedLegacy(t);
        var sql = new List<string>();
        using var db = t.NewContext(sql);

        var found = new ProfileService(db, t.CvStorage).GetForEdit(p.UserId);

        Assert.NotNull(found);
        Assert.True(found!.HasCv);                 // hồ sơ cũ: chỉ có byte[], vẫn phải báo là có CV
        Assert.Null(found.Profile.CvData);
        Assert.Equal("SV Test", found.Profile.FullName);
        Assert.Equal(p.TechSkillTags, found.Profile.TechSkillTags);
        Assert.Equal("cv.pdf", found.Profile.CvFileName);
        AssertNoCvBytes(sql);
    }

    [Fact]
    public void GetForEdit_WithoutCv_ReportsNoCv()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id, withCv: false);

        Assert.False(new ProfileService(t.Db, t.CvStorage).GetForEdit(sv.Id)!.HasCv);
    }
}
