using System.Text;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P2-3: lưu lại sự đồng ý xử lý dữ liệu cá nhân bằng AI.
///
/// Trước bản này, việc gửi CV cho Gemini chỉ được nói bằng một dòng chữ trên giao diện:
/// không có bản ghi nào, không có cách nào từ chối, không có cách nào rút lại. Với Nghị định
/// 13/2023/NĐ-CP về bảo vệ dữ liệu cá nhân, một dòng chữ trên màn hình không đủ.
/// </summary>
public class AiConsentTests
{
    private static byte[] Pdf() => Encoding.UTF8.GetBytes("%PDF-1.4 noi dung cv");

    private static SelfCheckService NewSelfCheck(TestDb t) =>
        new(t.Db, new JobService(t.Db), AiServiceTestFactory.Offline(), new AiInputBuilder(t.CvStorage));

    // ===== Ghi nhận sự đồng ý =====

    [Fact]
    public async Task SaveCv_WithConsent_RecordsTimeAndVersion()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db, t.CvStorage);

        var (ok, err) = await svc.SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: true);

        Assert.True(ok, err);
        var p = t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id);
        Assert.True(p.HasAiConsent);
        Assert.NotNull(p.AiConsentAt);
        Assert.Equal(CandidateProfile.CurrentAiConsentVersion, p.AiConsentVersion);
    }

    /// <summary>
    /// Ô tích trên form có thuộc tính required, nhưng đó chỉ là trình duyệt. Một request tự
    /// tạo không kèm aiConsent phải bị chặn ở tầng dịch vụ, và không để lại gì trên đĩa/CSDL.
    /// </summary>
    [Fact]
    public async Task SaveCv_WithoutConsent_IsRejected_AndSavesNothing()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);

        var (ok, err) = await new ProfileService(t.Db, t.CvStorage)
            .SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: false);

        Assert.False(ok);
        Assert.Contains("đồng ý", err);
        Assert.Empty(t.NewContext().CandidateProfiles.Where(x => x.UserId == sv.Id));
        Assert.False(Directory.Exists(t.CvRoot) && Directory.EnumerateFiles(t.CvRoot, "*", SearchOption.AllDirectories).Any());
    }

    /// <summary>Đã rút đồng ý thì tải CV mới cũng phải tích lại — form khi đó hiện lại ô tích.</summary>
    [Fact]
    public async Task SaveCv_AfterWithdrawingConsent_WithoutTicking_IsRejected()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db, t.CvStorage);
        await svc.SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: true);
        svc.WithdrawAiConsent(sv.Id);

        var (ok, _) = await svc.SaveCvAsync(sv.Id, Encoding.UTF8.GetBytes("%PDF-1.4 cv moi"), "cv2.pdf",
            "application/pdf", aiConsentGiven: false);

        Assert.False(ok);
        Assert.Equal("cv.pdf", t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id).CvFileName);
    }

    /// <summary>
    /// Tải CV mới mà quên tích ô KHÔNG được hiểu là rút lại sự đồng ý — rút lại là một hành
    /// động riêng, có ý thức, với một nút riêng.
    /// </summary>
    [Fact]
    public async Task UploadingANewCv_DoesNotSilentlyRevokeConsent()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db, t.CvStorage);
        await svc.SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: true);

        await svc.SaveCvAsync(sv.Id, Encoding.UTF8.GetBytes("%PDF-1.4 cv moi"), "cv2.pdf",
            "application/pdf", aiConsentGiven: false);

        Assert.True(t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id).HasAiConsent);
    }

    // ===== Rút lại =====

    [Fact]
    public async Task WithdrawConsent_ClearsTimeAndVersion()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db, t.CvStorage);
        await svc.SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: true);

        svc.WithdrawAiConsent(sv.Id);

        var p = t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id);
        Assert.False(p.HasAiConsent);
        Assert.Null(p.AiConsentAt);
        Assert.Null(p.AiConsentVersion);
    }

    /// <summary>
    /// Rút lại nghĩa là "đừng gửi CV của tôi đi NỮA", không phải "hãy quên những gì đã xảy
    /// ra". Kết quả nhà tuyển dụng đã đọc và đã dựa vào để ra quyết định phải giữ nguyên.
    /// </summary>
    [Fact]
    public async Task WithdrawConsent_KeepsResultsAlreadyScored()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db, t.CvStorage);
        await svc.SaveCvAsync(sv.Id, Pdf(), "cv.pdf", "application/pdf", aiConsentGiven: true);
        var p = t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id);
        var job = t.AddJob(m.Id);
        var app = t.AddApplication(job.Id, p.Id);
        var apps = new ApplicationService(t.Db, new NotificationService(t.Db), t.CvStorage);
        apps.SaveAiEvaluation(app.Id, new AiEvaluation(78, "Mạnh C#", "Thiếu Docker", "Học Docker", EvaluationSource.Offline));

        svc.WithdrawAiConsent(sv.Id);

        var after = t.NewContext().Applications.Find(app.Id)!;
        Assert.Equal(78, after.AiScore);
        Assert.Equal("Mạnh C#", after.AiStrengths);
    }

    [Fact]
    public void WithdrawConsent_OnMissingProfile_DoesNothing()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);

        new ProfileService(t.Db, t.CvStorage).WithdrawAiConsent(sv.Id);   // không ném ngoại lệ

        Assert.Empty(t.NewContext().CandidateProfiles);
    }

    // ===== Chốt chặn đường gọi AI =====

    [Fact]
    public void Gate_BlocksProfileWithoutConsent()
    {
        Assert.False(AiConsentGate.Allows(new CandidateProfile()));
        Assert.False(AiConsentGate.Allows(null));
        Assert.True(AiConsentGate.Allows(new CandidateProfile { AiConsentAt = DateTime.UtcNow }));
    }

    /// <summary>
    /// Sinh viên tự kiểm tra cũng là một lần gửi CV ra ngoài, nên vẫn cần sự đồng ý — dù
    /// người bấm nút chính là chủ nhân dữ liệu.
    /// </summary>
    [Fact]
    public async Task SelfCheck_WithoutConsent_IsRejected_AndSavesNothing()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id, withAiConsent: false);
        var job = t.AddJob(m.Id);

        var (ok, msg) = await NewSelfCheck(t).RunAsync(sv.Id, job.Id);

        Assert.False(ok);
        Assert.Contains("đồng ý", msg);
        Assert.Empty(t.NewContext().SelfChecks);
    }

    [Fact]
    public async Task SelfCheck_WithConsent_Runs()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        p.AiConsentAt = DateTime.UtcNow;
        p.AiConsentVersion = CandidateProfile.CurrentAiConsentVersion;
        t.Db.SaveChanges();
        var job = t.AddJob(m.Id);

        var (ok, msg) = await NewSelfCheck(t).RunAsync(sv.Id, job.Id);

        Assert.True(ok, msg);
        Assert.Single(t.NewContext().SelfChecks);
    }

    /// <summary>
    /// Sau khi rút lại, lượt chạy MỚI bị chặn nhưng kết quả CŨ vẫn đọc được — đúng ranh giới
    /// giữa "ngừng xử lý tiếp" và "xóa dấu vết".
    /// </summary>
    [Fact]
    public async Task AfterWithdrawal_NewRunsBlocked_ButOldResultsStillReadable()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        p.AiConsentAt = DateTime.UtcNow;
        t.Db.SaveChanges();
        var job = t.AddJob(m.Id);
        var selfCheck = NewSelfCheck(t);
        Assert.True((await selfCheck.RunAsync(sv.Id, job.Id)).ok);

        new ProfileService(t.Db, t.CvStorage).WithdrawAiConsent(sv.Id);

        var (ok, msg) = await selfCheck.RunAsync(sv.Id, job.Id);
        Assert.False(ok);
        Assert.Contains("đồng ý", msg);
        Assert.NotNull(selfCheck.GetLatest(sv.Id, job.Id));     // kết quả cũ vẫn còn
    }

    /// <summary>
    /// Câu từ chối phải nói rõ ràng, không im lặng rơi về nhánh chấm ngoại tuyến: âm thầm
    /// nghĩa là Mentor nhận một con số trông y hệt kết quả thật.
    /// </summary>
    [Fact]
    public void BlockedMessages_ExplainWhatToDo()
    {
        Assert.Contains("chưa đồng ý", AiConsentGate.BlockedForMentor);
        Assert.Contains("Hồ sơ", AiConsentGate.BlockedForStudent);
    }
}
