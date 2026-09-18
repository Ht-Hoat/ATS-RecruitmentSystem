using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Trang /my-applications/{id} của sinh viên đọc CandidateApplicationView chứ không đọc
/// ApplicationDetail (bản của nhà tuyển dụng, có HrNote). Ranh giới được giữ bằng KIỂU: trường
/// nào không có trong record thì trang không có cách nào hiện ra.
/// </summary>
public class CandidateApplicationViewTests
{
    private static (Application App, CandidateProfile Owner) Seed(TestDb t)
    {
        var m = t.AddMentor();
        var owner = t.AddStudentWithProfile("owner");
        var job = t.AddJob(m.Id);
        var app = t.AddApplication(job.Id, owner.Id, ApplicationStatus.Rejected, aiScore: 58, hrScore: 40);
        app.HrNote = "Nhận định riêng của Mentor, không gửi ứng viên.";
        app.CandidateFeedback = "Nên luyện thêm SQL.";
        t.Db.SaveChanges();
        return (app, owner);
    }

    private static ApplicationService Svc(TestDb t) => new(t.Db, new NotificationService(t.Db), t.CvStorage);

    [Fact]
    public void Owner_SeesTheirApplication_WithFeedbackAndAiResult()
    {
        using var t = new TestDb();
        var (app, owner) = Seed(t);

        var view = Svc(t).GetForCandidate(app.Id, owner.UserId);

        Assert.NotNull(view);
        Assert.Equal("Nên luyện thêm SQL.", view!.CandidateFeedback);
        Assert.Equal(58, view.Ai.Score);
        Assert.Equal(ApplicationStatus.Rejected, view.Status);
    }

    [Fact]
    public void AnotherStudent_GetsNull_SameAsMissing()
    {
        using var t = new TestDb();
        var (app, _) = Seed(t);
        var other = t.AddStudentWithProfile("other");

        Assert.Null(Svc(t).GetForCandidate(app.Id, other.UserId));
        Assert.Null(Svc(t).GetForCandidate(99999, other.UserId));
    }

    /// <summary>
    /// Khóa ranh giới bằng chính kiểu dữ liệu: thêm HrNote, điểm chốt hay người chấm vào record
    /// này là test đỏ — buộc người thêm phải dừng lại nghĩ xem ứng viên có được thấy không.
    /// </summary>
    [Theory]
    [InlineData("HrNote")]
    [InlineData("HrScore")]
    [InlineData("HrScoreByUserId")]
    [InlineData("InternalNote")]
    [InlineData("JobCreatedById")]
    public void View_HasNoRecruiterOnlyFields(string field)
    {
        Assert.Null(typeof(CandidateApplicationView).GetProperty(field));
    }
}
