using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P1-5: giới hạn danh sách tin theo chủ tin.
///
/// Vấn đề cũ: JobList gọi GetAll() cho mọi vai trò nên một Mentor đọc được toàn bộ tin của
/// Mentor khác kèm số ứng viên từng tin. (Phần "ai đã chốt điểm" đã bỏ cùng chức năng chốt
/// điểm tay — quyết định của Mentor giờ là mời phỏng vấn hoặc từ chối.)
/// </summary>
public class JobOwnershipAndScorerTests
{
    private static ApplicationService NewSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), t.CvStorage, new AuditService(t.Db));

    // ===== Danh sách tin theo chủ tin =====

    [Fact]
    public void GetByOwner_ReturnsOnlyOwnJobs_WhileGetAllReturnsEverything()
    {
        using var t = new TestDb();
        var a = t.AddMentor("A", "a@itcp.vn");
        var b = t.AddMentor("B", "b@itcp.vn");
        t.AddJob(a.Id, title: "Tin của A");
        t.AddJob(a.Id, title: "Tin thứ hai của A");
        t.AddJob(b.Id, title: "Tin của B");
        var svc = new JobService(t.Db);

        var mine = svc.GetByOwner(a.Id);

        Assert.Equal(2, mine.Count);
        Assert.All(mine, j => Assert.Equal(a.Id, j.CreatedById));
        Assert.Equal(3, svc.GetAll().Count);   // Admin vẫn thấy tất cả
    }

    [Fact]
    public void GetByOwner_WithNoJobs_ReturnsEmpty_NotEverything()
    {
        using var t = new TestDb();
        var a = t.AddMentor("A", "a@itcp.vn");
        var b = t.AddMentor("B", "b@itcp.vn");
        t.AddJob(b.Id);

        Assert.Empty(new JobService(t.Db).GetByOwner(a.Id));
    }
}
