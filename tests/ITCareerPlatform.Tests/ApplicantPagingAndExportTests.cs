using System.Text;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P2-1: phân trang, xuất CSV, thao tác hàng loạt.
///
/// GetByJob trước đây trả toàn bộ đơn của một tin. Với một tin 200 ứng viên — quy mô bình
/// thường của một tin thật — trang đó không dùng được.
/// </summary>
public class ApplicantPagingAndExportTests
{
    private static ApplicationService NewSvc(TestDb t) =>
        new(t.Db, new NotificationService(t.Db), new AuditService(t.Db));

    /// <summary>
    /// n ứng viên cho một tin. Index duy nhất (JobId, CandidateProfileId) chỉ cho mỗi hồ sơ
    /// nộp một lần vào một tin, nên mỗi đơn cần một hồ sơ riêng.
    /// </summary>
    private static (Job Job, User Mentor) SeedApplicants(
        TestDb t, int n, string status = ApplicationStatus.Submitted)
    {
        var m = t.AddMentor();
        var job = t.AddJob(m.Id);
        for (var i = 0; i < n; i++)
        {
            var p = t.AddStudentWithProfile($"p{i}");
            t.AddApplication(job.Id, p.Id, status);
        }
        return (job, m);
    }

    // ===== Phân trang =====

    [Fact]
    public void Paging_SplitsRows_AndReportsTheTrueTotal()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 25);
        var svc = NewSvc(t);

        var first = svc.GetByJobPaged(job.Id, "date", null, page: 1, pageSize: 10);

        Assert.Equal(10, first.Items.Count);
        Assert.Equal(25, first.Total);
        Assert.Equal(3, first.TotalPages);
        Assert.False(first.HasPrev);
        Assert.True(first.HasNext);
    }

    /// <summary>Tổng số dòng qua các trang phải đúng bằng Total, không sót và không lặp.</summary>
    [Fact]
    public void AllPagesTogether_CoverEveryRowExactlyOnce()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 25);
        var svc = NewSvc(t);

        var ids = new List<int>();
        for (var p = 1; p <= 3; p++)
            ids.AddRange(svc.GetByJobPaged(job.Id, "date", null, p, 10).Items.Select(x => x.Id));

        Assert.Equal(25, ids.Count);
        Assert.Equal(25, ids.Distinct().Count());
    }

    /// <summary>
    /// Kẹp CẢ HAI đầu — bài học đã ghi trong AuditService: ?p=9999 trên 3 trang dữ liệu từng
    /// cho ra một bảng rỗng kèm dòng "Trang 9999 / 3" và không có nút nào quay lại được.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageNumber_BelowRange_ClampsToFirstPage(int requested)
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 25);

        var result = NewSvc(t).GetByJobPaged(job.Id, "date", null, requested, 10);

        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public void PageNumber_AboveRange_ClampsToLastPage()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 25);

        var result = NewSvc(t).GetByJobPaged(job.Id, "date", null, 9999, 10);

        Assert.Equal(3, result.Page);
        Assert.Equal(5, result.Items.Count);       // trang cuối còn 5 dòng
        Assert.False(result.HasNext);
        Assert.True(result.HasPrev);
    }

    [Fact]
    public void EmptyJob_StillReportsOnePage()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var job = t.AddJob(m.Id);

        var result = NewSvc(t).GetByJobPaged(job.Id, "date", null, 1, 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasNext);
    }

    /// <summary>
    /// Bộ lọc tech chạy ở phía C# SAU khi chiếu, nên phải LỌC XONG rồi mới cắt trang. Làm
    /// ngược lại thì mỗi trang hiện một số dòng khác nhau và tổng các trang không bằng Total.
    /// </summary>
    [Fact]
    public void TechFilter_IsAppliedBeforePaging()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var job = t.AddJob(m.Id, techStack: "C#,Docker");
        for (var i = 0; i < 6; i++)
            t.AddApplication(job.Id, t.AddStudentWithProfile($"with{i}", "C#,Docker").Id);
        for (var i = 0; i < 6; i++)
            t.AddApplication(job.Id, t.AddStudentWithProfile($"without{i}", "Python").Id);

        var filter = new ApplicantFilter(RequiredTech: new[] { "Docker" });
        var result = NewSvc(t).GetByJobPaged(job.Id, "date", filter, 1, 10);

        // 6 dòng khớp, tất cả nằm trên một trang — không phải "10 dòng đầu rồi lọc còn 5".
        Assert.Equal(6, result.Total);
        Assert.Equal(6, result.Items.Count);
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public void FirstRowNumber_ContinuesAcrossPages()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 25);
        var svc = NewSvc(t);

        Assert.Equal(1, svc.GetByJobPaged(job.Id, "date", null, 1, 10).FirstRowNumber);
        Assert.Equal(11, svc.GetByJobPaged(job.Id, "date", null, 2, 10).FirstRowNumber);
        Assert.Equal(21, svc.GetByJobPaged(job.Id, "date", null, 3, 10).FirstRowNumber);
    }

    // ===== Xuất CSV =====

    [Fact]
    public void Csv_StartsWithUtf8Bom_SoExcelReadsVietnamese()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 1);
        var items = NewSvc(t).GetByJob(job.Id);

        var bytes = CsvExport.Applicants(items);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    /// <summary>
    /// Chống CSV injection: Excel coi ô bắt đầu bằng = + - @ là CÔNG THỨC. Một ứng viên đặt
    /// họ tên là "=cmd|..." sẽ biến tệp Mentor vừa tải về thành một lệnh chạy trên máy họ.
    /// </summary>
    [Theory]
    [InlineData("=cmd|' /c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A9)")]
    public void Csv_EscapesFormulaLikeValues(string dangerousName)
    {
        var items = new[]
        {
            new ApplicantListItem(1, dangerousName, "a@itcp.vn", "C#", null, null,
                ApplicationStatus.Submitted, DateTime.UtcNow, true, 1)
        };

        var csv = Encoding.UTF8.GetString(CsvExport.Applicants(items));

        Assert.Contains("\"'" + dangerousName, csv);
    }

    [Fact]
    public void Csv_KeepsCommasInsideOneColumn()
    {
        var items = new[]
        {
            new ApplicantListItem(1, "Nguyễn Văn A", "a@itcp.vn", "C#, .NET, SQL Server", 80, null,
                ApplicationStatus.Submitted, DateTime.UtcNow, true, 2)
        };

        var csv = Encoding.UTF8.GetString(CsvExport.Applicants(items));

        Assert.Contains("\"C#, .NET, SQL Server\"", csv);
    }

    /// <summary>
    /// Tệp CSV rời khỏi hệ thống và thường được gửi qua email hay chat, nên không được mang
    /// theo nội dung CV, ghi chú nội bộ hay câu hỏi phỏng vấn.
    /// </summary>
    [Fact]
    public void Csv_NeverCarriesCvOrInternalNotes()
    {
        using var t = new TestDb();
        var (job, m) = SeedApplicants(t, 1);
        var svc = NewSvc(t);
        var appId = t.NewContext().Applications.Single().Id;
        svc.SaveInternalNote(appId, "Ghi chú tuyệt mật về ứng viên", m.Id);

        var csv = Encoding.UTF8.GetString(CsvExport.Applicants(svc.GetByJob(job.Id)));

        Assert.DoesNotContain("tuyệt mật", csv);
        Assert.DoesNotContain("%PDF", csv);
    }

    [Fact]
    public void Csv_HasOneHeaderRowAndOneRowPerApplicant()
    {
        using var t = new TestDb();
        var (job, _) = SeedApplicants(t, 3);

        var csv = Encoding.UTF8.GetString(CsvExport.Applicants(NewSvc(t).GetByJob(job.Id)));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);
        Assert.Contains("Họ tên", lines[0]);
    }

    // ===== Thao tác hàng loạt =====

    [Fact]
    public void Bulk_UpdatesEveryValidApplication()
    {
        using var t = new TestDb();
        var (job, m) = SeedApplicants(t, 3);
        var svc = NewSvc(t);
        var ids = t.NewContext().Applications.Select(a => a.Id).ToList();

        var result = svc.BulkUpdateStatus(ids, ApplicationStatus.Reviewing, m.Id);

        Assert.Equal(3, result.Updated);
        Assert.Equal(0, result.FailedCount);
        Assert.All(t.NewContext().Applications.ToList(),
            a => Assert.Equal(ApplicationStatus.Reviewing, a.Status));
    }

    /// <summary>
    /// Một đơn không hợp lệ KHÔNG được chặn những đơn còn lại — nếu chặn, Mentor phải mò
    /// xem đơn nào hỏng rồi bỏ nó ra và bấm lại từ đầu.
    /// </summary>
    [Fact]
    public void Bulk_SkipsInvalidOnes_ButStillProcessesTheRest()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var job = t.AddJob(m.Id);
        var ok1 = t.AddApplication(job.Id, t.AddStudentWithProfile("a").Id, ApplicationStatus.Submitted);
        var ok2 = t.AddApplication(job.Id, t.AddStudentWithProfile("b").Id, ApplicationStatus.Submitted);
        // Đã trúng tuyển: trạng thái kết thúc, không đổi tiếp được (P1-4).
        var bad = t.AddApplication(job.Id, t.AddStudentWithProfile("c").Id, ApplicationStatus.Accepted);

        var result = NewSvc(t).BulkUpdateStatus(
            new[] { ok1.Id, ok2.Id, bad.Id }, ApplicationStatus.Reviewing, m.Id);

        Assert.Equal(2, result.Updated);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains($"#{bad.Id}", result.Failures[0]);
        Assert.Equal(ApplicationStatus.Accepted, t.NewContext().Applications.Find(bad.Id)!.Status);
    }

    /// <summary>Câu tổng kết phải nêu CẢ phần thất bại, nếu không người dùng sẽ bấm lại lần nữa.</summary>
    [Fact]
    public void Bulk_Message_NamesFailuresWithReasons()
    {
        using var t = new TestDb();
        var m = t.AddMentor();
        var job = t.AddJob(m.Id);
        var bad = t.AddApplication(job.Id, t.AddStudentWithProfile("c").Id, ApplicationStatus.Rejected);

        var result = NewSvc(t).BulkUpdateStatus(new[] { bad.Id }, ApplicationStatus.Reviewing, m.Id);

        Assert.Contains("không hợp lệ", result.Message);
        Assert.Contains($"#{bad.Id}", result.Message);
    }

    /// <summary>Mỗi buổi phỏng vấn cần một giờ hẹn riêng — không có giờ chung nào đúng cho 20 đơn.</summary>
    [Fact]
    public void Bulk_ToInterview_IsBlockedWithAnExplanation()
    {
        using var t = new TestDb();
        var (job, m) = SeedApplicants(t, 2, ApplicationStatus.Reviewing);
        var ids = t.NewContext().Applications.Select(a => a.Id).ToList();

        var result = NewSvc(t).BulkUpdateStatus(ids, ApplicationStatus.Interview, m.Id);

        Assert.Equal(0, result.Updated);
        Assert.Contains("lịch hẹn riêng", result.Failures[0]);
        Assert.All(t.NewContext().Applications.ToList(),
            a => Assert.Equal(ApplicationStatus.Reviewing, a.Status));
    }

    /// <summary>
    /// Đi qua đúng UpdateStatus nghĩa là lịch sử, thông báo và hàng đợi email đều được sinh
    /// ra như khi đổi từng đơn — một đường ghi tắt "cho nhanh" sẽ bỏ qua cả ba.
    /// </summary>
    [Fact]
    public void Bulk_StillWritesHistoryAndNotifications()
    {
        using var t = new TestDb();
        var (job, m) = SeedApplicants(t, 2);
        var ids = t.NewContext().Applications.Select(a => a.Id).ToList();

        NewSvc(t).BulkUpdateStatus(ids, ApplicationStatus.Reviewing, m.Id);

        using var v = t.NewContext();
        Assert.Equal(2, v.ApplicationStatusHistories.Count());
        Assert.Equal(2, v.Notifications.Count());
    }

    [Fact]
    public void Bulk_DeduplicatesRepeatedIds()
    {
        using var t = new TestDb();
        var (job, m) = SeedApplicants(t, 1);
        var id = t.NewContext().Applications.Single().Id;

        var result = NewSvc(t).BulkUpdateStatus(new[] { id, id, id }, ApplicationStatus.Reviewing, m.Id);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.FailedCount);
    }
}
