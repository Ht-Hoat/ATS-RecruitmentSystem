using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P0-1: trang chi tiết tin cho Sinh viên IT.
///
/// Trước đây Job.Description và Job.Requirements chỉ được render ở form nhập của nhà tuyển
/// dụng — không route nào cho sinh viên đọc chúng, trong khi người đăng tin bị bắt buộc nhập.
/// Bộ test này giữ hai thứ: vị ngữ "tin còn nhận hồ sơ", và việc hai trường đó không bị
/// một lần chiếu DTO nào làm rơi mất về sau.
/// </summary>
public class JobVisibilityTests
{
    private const string Jd = "Phát triển REST API bằng ASP.NET Core cho sản phẩm tuyển dụng IT.";
    private const string Req = "Thành thạo C#, EF Core; hiểu Docker; 1 năm kinh nghiệm.";

    private static Job Seed(TestDb t, string status = JobStatus.Open, int deadlineOffsetDays = 10)
    {
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var j = t.AddJob(m.Id, status: status);
        j.Description = Jd;
        j.Requirements = Req;
        j.Deadline = VietnamDateHelper.Today().AddDays(deadlineOffsetDays);
        t.Db.SaveChanges();
        return j;
    }

    [Fact]
    public void OpenJob_WithinDeadline_IsVisible()
    {
        using var t = new TestDb();
        var j = Seed(t);

        var visible = new JobService(t.Db).GetVisibleForCandidate(j.Id);

        Assert.NotNull(visible);
        Assert.Equal(j.Id, visible!.Id);
    }

    /// <summary>
    /// Tin đã đóng phải trả null chứ không phải "trả về kèm cờ": trang chi tiết phân biệt
    /// ba trạng thái (không tồn tại / đã đóng / bình thường) bằng cách hỏi cả GetById lẫn
    /// hàm này, nên hàm này chỉ cần trả lời đúng một câu hỏi "còn nhận hồ sơ không".
    /// </summary>
    [Fact]
    public void ClosedJob_IsNotVisible()
    {
        using var t = new TestDb();
        var j = Seed(t, status: JobStatus.Closed);

        Assert.Null(new JobService(t.Db).GetVisibleForCandidate(j.Id));
        Assert.NotNull(new JobService(t.Db).GetById(j.Id));   // vẫn tồn tại — chỉ là không còn mở
    }

    [Fact]
    public void OpenJob_PastDeadline_IsNotVisible()
    {
        using var t = new TestDb();
        var j = Seed(t, deadlineOffsetDays: -1);

        Assert.Null(new JobService(t.Db).GetVisibleForCandidate(j.Id));
    }

    /// <summary>Hạn nộp là HÔM NAY thì vẫn còn nhận hồ sơ — hết hạn tính từ ngày hôm sau.</summary>
    [Fact]
    public void OpenJob_DeadlineToday_IsStillVisible()
    {
        using var t = new TestDb();
        var j = Seed(t, deadlineOffsetDays: 0);

        Assert.NotNull(new JobService(t.Db).GetVisibleForCandidate(j.Id));
    }

    [Fact]
    public void UnknownId_IsNotVisible()
    {
        using var t = new TestDb();
        Seed(t);

        Assert.Null(new JobService(t.Db).GetVisibleForCandidate(9999));
    }

    /// <summary>
    /// Chống hồi quy: trang chi tiết tồn tại CHỈ để hiển thị JD và yêu cầu ứng viên. Nếu về
    /// sau ai đó tối ưu bằng cách chiếu sang một DTO nhẹ và quên hai cột này, trang sẽ trắng
    /// trơn mà không có lỗi nào — test này là thứ duy nhất phát hiện ra.
    /// </summary>
    [Fact]
    public void VisibleJob_CarriesDescriptionAndRequirements_Verbatim()
    {
        using var t = new TestDb();
        var j = Seed(t);

        var visible = new JobService(t.Db).GetVisibleForCandidate(j.Id)!;

        Assert.Equal(Jd, visible.Description);
        Assert.Equal(Req, visible.Requirements);
    }
}
