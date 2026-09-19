using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

/// <summary>
/// Bộ câu hỏi LUYỆN PHỎNG VẤN cho sinh viên: khi được mời phỏng vấn, sinh viên tạo một bộ câu
/// hỏi nhà tuyển dụng nhiều khả năng sẽ hỏi (dựa trên CV đã nộp và JD), kèm gợi ý cách trả lời.
///
/// Trước đây bộ câu hỏi được sinh cho phía nhà tuyển dụng. Nó chuyển sang đây vì người cần
/// chuẩn bị là ứng viên; nhà tuyển dụng tự biết mình muốn hỏi gì.
/// </summary>
public interface IInterviewPrepService
{
    /// <summary>Bộ câu hỏi đã tạo cho đơn của CHÍNH sinh viên này; null nếu chưa có hoặc không phải chủ đơn.</summary>
    InterviewQuestionSet? Get(int appId, int candidateUserId);

    /// <summary>HIST: lịch sử các bộ câu hỏi đã tạo cho đơn của CHÍNH sinh viên này (mới nhất trước).</summary>
    IReadOnlyList<InterviewQuestionSet> GetHistory(int appId, int candidateUserId);

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

    public InterviewQuestionSet? Get(int appId, int candidateUserId) =>
        IsOwner(appId, candidateUserId) ? applications.GetAiQuestions(appId) : null;

    // HIST: chỉ chủ đơn mới xem được lịch sử của mình; người khác nhận danh sách rỗng.
    public IReadOnlyList<InterviewQuestionSet> GetHistory(int appId, int candidateUserId) =>
        IsOwner(appId, candidateUserId)
            ? applications.GetAiQuestionHistory(appId)
            : Array.Empty<InterviewQuestionSet>();

    public string? WhyNot(int appId, int candidateUserId)
    {
        var a = db.Applications.AsNoTracking()
            .Where(x => x.Id == appId && x.CandidateProfile!.UserId == candidateUserId)
            .Select(x => new { x.Status, Consented = x.CandidateProfile!.AiConsentAt != null, x.AiQuestionsAt })
            .FirstOrDefault();

        // Đơn của người khác và đơn không tồn tại trả về CÙNG một câu — không xác nhận id nào có thật.
        if (a is null) return "Không tìm thấy đơn.";
        if (a.Status != ApplicationStatus.Interview)
            return "Bộ câu hỏi luyện tập mở khi bạn được mời phỏng vấn.";
        // Bộ câu hỏi soạn từ nội dung CV gửi tới dịch vụ AI — cùng luật đồng ý với chấm điểm.
        if (!a.Consented) return AiConsentGate.BlockedForStudent;

        if (a.AiQuestionsAt is DateTime last)
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
        if (a?.CandidateProfile is null || a.Job is null) return (false, "Không tìm thấy đơn.");

        // Lần gọi mô hình nằm NGOÀI mọi giao dịch CSDL, giống SelfCheckService.
        var set = await ai.GenerateQuestionsAsync(await inputBuilder.ForApplicationAsync(a, a.CandidateProfile, ct), ct);
        applications.SaveAiQuestions(appId, set);

        return applications.GetAiQuestions(appId) is null
            ? (false, "Không tạo được bộ câu hỏi, vui lòng thử lại sau.")
            : (true, "Đã tạo bộ câu hỏi luyện phỏng vấn.");
    }

    private bool IsOwner(int appId, int candidateUserId) =>
        db.Applications.Any(x => x.Id == appId && x.CandidateProfile!.UserId == candidateUserId);
}
