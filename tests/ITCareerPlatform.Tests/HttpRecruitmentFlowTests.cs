using System.Net;
using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Luồng tuyển dụng đầu-cuối trên app thật: Mentor quyết định bằng các thẻ (mời phỏng vấn /
/// từ chối), sinh viên được mời thì luyện phỏng vấn với bộ câu hỏi của riêng mình.
///
/// Lớp riêng (một AppFactory riêng) vì giới hạn 10 lần đăng nhập/phút tính theo từng app.
/// </summary>
public class HttpRecruitmentFlowTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task MentorInvites_ThenStudentPreparesWithTheirOwnQuestions()
    {
        var appId = app.Query(db => db.Applications
            .Include(a => a.Job).Include(a => a.CandidateProfile)
            .Single(a => a.Job!.Title == "Lập trình viên Backend .NET" && a.CandidateProfile!.Email == "lan@itcp.vn").Id);

        // --- Mentor: trang chi tiết là các thẻ quyết định, không còn chốt điểm / ghi chú / câu hỏi ---
        var mentor = await app.LoginAs("mentor@itcp.vn");
        var hrHtml = await mentor.GetStringAsync($"/applications/{appId}");
        var hrText = WebUtility.HtmlDecode(hrHtml);
        Assert.Contains("Quyết định tuyển dụng", hrText);
        Assert.Contains("Gửi lời mời phỏng vấn", hrText);
        Assert.Contains("Gửi từ chối", hrText);
        Assert.DoesNotContain("/hr-score", hrHtml);
        Assert.DoesNotContain("/internal-note", hrHtml);
        Assert.DoesNotContain("/ai-questions", hrHtml);
        Assert.DoesNotContain("Ghi chú nội bộ", hrText);

        // Mời thẳng từ "Đã nộp". Ô datetime-local gửi giờ Việt Nam, không có múi giờ.
        var vnTomorrow = Ui.ToVietnamTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-ddTHH:mm");
        var invite = await mentor.PostAsync($"/applications/{appId}/status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.TokenIn(hrHtml),
            ["status"] = ApplicationStatus.Interview,
            ["interviewAt"] = vnTomorrow,
            ["interviewLink"] = "https://meet.google.com/abc-defg-hij",
            ["interviewNote"] = "Vòng 1"
        }));
        Assert.Contains("statusmsg=", invite.Headers.Location?.OriginalString ?? "");
        Assert.Equal(ApplicationStatus.Interview, app.Query(db => db.Applications.Find(appId)!.Status));

        // --- Sinh viên: thấy khối chuẩn bị phỏng vấn và tạo được bộ câu hỏi của mình ---
        var student = await app.LoginAs("lan@itcp.vn");
        var svHtml = await student.GetStringAsync($"/my-applications/{appId}");
        Assert.Contains("Chuẩn bị phỏng vấn", WebUtility.HtmlDecode(svHtml));
        Assert.Contains($"/my-applications/{appId}/interview-prep", svHtml);

        var gen = await student.PostAsync($"/my-applications/{appId}/interview-prep", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AppFactory.TokenIn(svHtml)
        }));
        Assert.Contains("msg=", gen.Headers.Location?.OriginalString ?? "");

        var after = WebUtility.HtmlDecode(await student.GetStringAsync($"/my-applications/{appId}"));
        Assert.Contains("Gợi ý trả lời", after);

        // Mentor không đọc bộ câu hỏi luyện tập của sinh viên trên trang của mình.
        Assert.DoesNotContain("Gợi ý trả lời", WebUtility.HtmlDecode(await mentor.GetStringAsync($"/applications/{appId}")));
    }
}
