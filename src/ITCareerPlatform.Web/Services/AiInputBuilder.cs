using ITCareerPlatform.Models;

namespace ITCareerPlatform.Services;

/// <summary>
/// P1-2: một chỗ duy nhất dựng dữ liệu đưa vào AI.
///
/// Ba đường đang dùng nó: chấm điểm của Mentor (ATS-13/14), sinh câu hỏi phỏng vấn (N1.C),
/// và sinh viên tự kiểm tra độ phù hợp (P1-2). Trước đây việc này là một static local
/// function nằm trong Program.cs, nên đường thứ ba không với tới được — và nếu chép ra một
/// bản thứ hai thì điểm sinh viên tự chạy sẽ lệch khỏi điểm nhà tuyển dụng đọc, mà không
/// ai giải thích được vì sao hai con số nói về cùng một hồ sơ lại khác nhau.
/// </summary>
public interface IAiInputBuilder
{
    /// <summary>
    /// Dữ liệu cho một ĐƠN đã nộp. Luôn đọc CV ĐÃ ĐÓNG BĂNG lúc nộp, không phải CV hiện tại
    /// của hồ sơ: chấm trên bản chụp còn soạn câu hỏi từ bản mới thì hai kết quả nói về hai
    /// ứng viên khác nhau.
    /// </summary>
    AiEvaluationInput ForApplication(Application a, CandidateProfile p);

    /// <summary>
    /// Dữ liệu cho một lần TỰ KIỂM TRA. Khác biệt duy nhất so với bản trên: chưa có đơn nên
    /// chưa có bản chụp, dùng hồ sơ và CV hiện tại của sinh viên.
    /// </summary>
    AiEvaluationInput ForSelfCheck(CandidateProfile p, Job job);
}

public class AiInputBuilder : IAiInputBuilder
{
    public AiEvaluationInput ForApplication(Application a, CandidateProfile p)
    {
        var cvText = CvTextExtractor.Extract(
            a.CvDataSnapshot ?? p.CvData,
            a.HasCvSnapshot ? a.CvFileNameSnapshot : p.CvFileName);

        return Build(p, a.Job!, cvText);
    }

    public AiEvaluationInput ForSelfCheck(CandidateProfile p, Job job) =>
        Build(p, job, CvTextExtractor.Extract(p.CvData, p.CvFileName));

    /// <summary>
    /// Khuôn chung. Danh sách công nghệ đi vào ô RIÊNG có cấu trúc (CandidateTech /
    /// RequiredTech) — nhánh chấm ngoại tuyến đối chiếu đúng hai ô đó, nên trộn chúng vào
    /// phần văn bản tự do sẽ làm điểm offline tụt về 0 mà không có lỗi nào được báo.
    /// </summary>
    private static AiEvaluationInput Build(CandidateProfile p, Job job, string cvText) => new(
        CandidateText: string.Join("\n", new[]
        {
            "Kỹ năng: " + p.Skills,
            "Kinh nghiệm: " + p.Experience,
            "Học vấn: " + p.Education,
            "Nội dung CV: " + cvText
        }),
        JobText: string.Join("\n", new[]
        {
            "Vị trí: " + job.Title + " (" + job.Level + ")",
            "Danh mục: " + job.Category,
            "Yêu cầu: " + job.Requirements,
            "Mô tả: " + job.Description
        }),
        CandidateTech: p.TechSkillTags,
        RequiredTech: job.TechStack);
}
