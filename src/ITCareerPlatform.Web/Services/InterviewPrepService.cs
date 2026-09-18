using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

/// <summary>Bộ câu hỏi đã tạo (nếu có) và lý do chưa tạo được lúc này (null nếu tạo được).</summary>
public record InterviewPrepState(InterviewQuestionSet? Questions, string? BlockedReason);

/// <summary>
/// Bộ câu hỏi LUYỆN PHỎNG VẤN cho sinh viên: khi được mời phỏng vấn, sinh viên tạo một bộ câu
/// hỏi nhà tuyển dụng nhiều khả năng sẽ hỏi (dựa trên CV đã nộp và JD), kèm gợi ý cách trả lời.
///
/// Trước đây bộ câu hỏi được sinh cho phía nhà tuyển dụng. Nó chuyển sang đây vì người cần
/// chuẩn bị là ứng viên; nhà tuyển dụng tự biết mình muốn hỏi gì.
/// </summary>
public interface IInterviewPrepService
{
    /// <summary>
    /// Mọi thứ trang đơn của sinh viên cần, trong MỘT truy vấn. Null nếu đơn không tồn tại hoặc
    /// không thuộc sinh viên này.
    /// </summary>
    InterviewPrepState? GetState(int appId, int candidateUserId);

    /// <summary>Bộ câu hỏi đã tạo cho đơn của CHÍNH sinh viên này; null nếu chưa có hoặc không phải chủ đơn.</summary>
    InterviewQuestionSet? Get(int appId, int candidateUserId);

    /// <summary>Lý do chưa tạo được lúc này (để giao diện giải thích thay vì hiện nút), hoặc null nếu tạo được.</summary>
    string? WhyNot(int appId, int candidateUserId);

    Task<(bool ok, string message)> GenerateAsync(int appId, int candidateUserId, CancellationToken ct = default);
}

public class InterviewPrepService(
    AppDbContext db,
    IApplicationService applications,
    IAiService ai,
    IAiInputBuilder inputBuilder,
    TimeProvider? clock = null) : IInterviewPrepService
{
    /// <summary>
    /// Tạo lại tối đa một lần mỗi chừng này giờ. Mỗi lần tạo là một lần gọi Gemini (quota miễn
    /// phí có trần); không có giới hạn thì một người bấm liên tục là cả hệ thống mất tính năng.
    /// </summary>
    public const int RegenerateCooldownHours = 24;

    // Đơn của người khác và đơn không tồn tại trả về CÙNG một câu — không xác nhận id nào có thật.
    private const string NotFound = "Không tìm thấy đơn.";

    public InterviewPrepState? GetState(int appId, int candidateUserId)
    {
        var a = db.Applications.AsNoTracking()
            .Where(x => x.Id == appId && x.CandidateProfile!.UserId == candidateUserId)
            .Select(x => new
            {
                x.Status, Consented = x.CandidateProfile!.AiConsentAt != null,
                x.AiQuestionsAt, x.AiQuestions, x.AiQuestionsSource
            })
            .FirstOrDefault();
        if (a is null) return null;

        return new InterviewPrepState(
            ApplicationService.ParseQuestions(a.AiQuestions, a.AiQuestionsSource),
            BlockedReason(a.Status, a.Consented, a.AiQuestionsAt));
    }

    public InterviewQuestionSet? Get(int appId, int candidateUserId) => GetState(appId, candidateUserId)?.Questions;

    public string? WhyNot(int appId, int candidateUserId) =>
        GetState(appId, candidateUserId) is { } state ? state.BlockedReason : NotFound;

    private string? BlockedReason(string status, bool consented, DateTime? lastGeneratedAt)
    {
        if (status != ApplicationStatus.Interview)
            return "Bộ câu hỏi luyện tập mở khi bạn được mời phỏng vấn.";
        // Bộ câu hỏi soạn từ nội dung CV gửi tới dịch vụ AI — cùng luật đồng ý với chấm điểm.
        if (!consented) return AiConsentGate.BlockedForStudent;

        if (lastGeneratedAt is DateTime last)
        {
            var nextAllowed = last.AddHours(RegenerateCooldownHours);
            if (VietnamDateHelper.UtcNow(clock) < nextAllowed)
                return $"Bạn đã tạo bộ câu hỏi lúc {Ui.DateTimeText(last)}. " +
                       $"Có thể tạo lại sau {Ui.DateTimeText(nextAllowed)}.";
        }
        return null;
    }

    public async Task<(bool ok, string message)> GenerateAsync(int appId, int candidateUserId, CancellationToken ct = default)
    {
        var reason = WhyNot(appId, candidateUserId);
        if (reason is not null) return (false, reason);

        // Bản đầy đủ (kèm CV đã nộp) — AiInputBuilder cần nội dung CV để soạn câu hỏi bám hồ sơ.
        var a = applications.GetById(appId);
        if (a?.CandidateProfile is null || a.Job is null) return (false, NotFound);

        // Lần gọi mô hình nằm NGOÀI mọi giao dịch CSDL, giống SelfCheckService.
        var set = await ai.GenerateQuestionsAsync(await inputBuilder.ForApplicationAsync(a, a.CandidateProfile, ct), ct);
        return applications.SaveAiQuestions(appId, set)
            ? (true, "Đã tạo bộ câu hỏi luyện phỏng vấn.")
            : (false, "Không tạo được bộ câu hỏi, vui lòng thử lại sau.");
    }
}
