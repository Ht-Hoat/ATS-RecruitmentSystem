using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using System.Text;
using Xunit;

namespace ITCareerPlatform.Tests;

public class ProfileServiceTests
{
    // ATS-08: lưu 4 trường IT mới
    [Fact]
    public void Save_ITFields_Persisted()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        svc.Save(sv.Id, new CandidateProfile
        {
            FullName = "SV", Email = "sv@itcp.vn",
            GithubUrl = "https://github.com/abc",
            LinkedInUrl = "https://linkedin.com/in/abc",
            PortfolioUrl = "https://abc.dev", TechSkillTags = "C#,React"
        });

        var p = t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id);
        Assert.Equal("https://github.com/abc", p.GithubUrl);
        Assert.Equal("C#,React", p.TechSkillTags);
        Assert.Equal(2, p.SkillTagList.Count());
    }

    // ATS-08.4: URL GitHub sai định dạng -> ném lỗi server-side
    [Fact]
    public void Save_InvalidGithubUrl_Throws()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        // Họ tên + email hợp lệ, để lỗi ném ra chắc chắn đến từ luật URL chứ không phải
        // từ ràng buộc bắt buộc của hồ sơ.
        Assert.Throws<ArgumentException>(() =>
            svc.Save(sv.Id, new CandidateProfile
            {
                FullName = "SV", Email = "sv@itcp.vn",
                GithubUrl = "http://facebook.com/abc"
            }));
    }

    // ATS-09: upload CV hợp lệ -> HasCv = true
    [Fact]
    public void SaveCv_ValidPdf_SetsHasCv()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        var (ok, err) = svc.SaveCv(sv.Id, Encoding.UTF8.GetBytes("%PDF-1.4 noi dung cv"), "cv.pdf", "application/pdf");

        Assert.True(ok);
        Assert.Null(err);
        Assert.True(t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id).HasCv);
    }

    // SEC-01: chặn chữ ký EICAR
    [Fact]
    public void SaveCv_EicarSignature_Rejected()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);
        var eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        var (ok, err) = svc.SaveCv(sv.Id, eicar, "cv.pdf", "application/pdf");

        Assert.False(ok);
        Assert.Contains("mã độc", err);
    }

    // SEC-01: sai định dạng -> từ chối
    [Fact]
    public void SaveCv_WrongExtension_Rejected()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        var (ok, err) = svc.SaveCv(sv.Id, Encoding.UTF8.GetBytes("hello"), "cv.exe", "application/octet-stream");

        Assert.False(ok);
        Assert.Contains("PDF hoặc DOCX", err);
    }

    // SEC-01: quá 5MB -> từ chối
    [Fact]
    public void SaveCv_TooLarge_Rejected()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);
        var big = new byte[5 * 1024 * 1024 + 1];
        big[0] = (byte)'%'; big[1] = (byte)'P';

        var (ok, err) = svc.SaveCv(sv.Id, big, "cv.pdf", "application/pdf");

        Assert.False(ok);
        Assert.Contains("5MB", err);
    }

    // ===== N1.F: số năm kinh nghiệm =====

    [Fact]
    public void Save_PersistsYearsOfExperience()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        svc.Save(sv.Id, new CandidateProfile
        {
            FullName = "Nguyễn Văn A", Email = "a@itcp.vn", YearsOfExperience = 3
        });

        var p = t.NewContext().CandidateProfiles.Single(x => x.UserId == sv.Id);
        Assert.Equal(3, p.YearsOfExperience);
        Assert.Equal(CandidateLevel.Middle, p.Level);
    }

    /// <summary>[Range] trên entity là luật thật; min/max của thẻ input chỉ ràng buộc trình duyệt.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public void Save_OutOfRangeYears_Rejected(int years)
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var svc = new ProfileService(t.Db);

        var ex = Assert.Throws<ArgumentException>(() => svc.Save(sv.Id, new CandidateProfile
        {
            FullName = "Nguyễn Văn A", Email = "a@itcp.vn", YearsOfExperience = years
        }));

        Assert.Contains("kinh nghiệm", ex.Message);
    }

    [Fact]
    public void Save_DefaultYears_IsFresher()
    {
        using var t = new TestDb();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);

        var p = new ProfileService(t.Db).Save(sv.Id, new CandidateProfile
        {
            FullName = "Nguyễn Văn A", Email = "a@itcp.vn"
        });

        Assert.Equal(0, p.YearsOfExperience);
        Assert.Equal(CandidateLevel.Fresher, p.Level);
    }

}
