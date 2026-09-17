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
    Task<AiEvaluationInput> ForApplicationAsync(Application a, CandidateProfile p, CancellationToken ct = default);

    /// <summary>
    /// Dữ liệu cho một lần TỰ KIỂM TRA. Khác biệt duy nhất so với bản trên: chưa có đơn nên
    /// chưa có bản chụp, dùng hồ sơ và CV hiện tại của sinh viên.
    /// </summary>
    Task<AiEvaluationInput> ForSelfCheckAsync(CandidateProfile p, Job job, CancellationToken ct = default);
}

public class AiInputBuilder(ICvStorage cvStorage) : IAiInputBuilder
{
    public async Task<AiEvaluationInput> ForApplicationAsync(Application a, CandidateProfile p, CancellationToken ct = default)
    {
        // P2-2: nội dung CV có thể nằm ở blob storage, ở cột byte[] cũ, hoặc chỉ có ở hồ sơ
        // (đơn tạo trước khi có bản chụp). Thử đúng thứ tự đó — bỏ bước storage thì sau khi
        // di trú xong, mọi lần chấm điểm đều chạy trên một CV rỗng mà không có lỗi nào báo.
        var bytes = await ReadAsync(a.CvStorageKeySnapshot, a.CvDataSnapshot, ct)
                    ?? await ReadAsync(p.CvStorageKey, p.CvData, ct);

        var fileName = a.CvStorageKeySnapshot is not null || a.CvDataSnapshot is not null
            ? a.CvFileNameSnapshot
            : p.CvFileName;

        return Build(p, a.Job!, CvTextExtractor.Extract(bytes, fileName));
    }

    public async Task<AiEvaluationInput> ForSelfCheckAsync(CandidateProfile p, Job job, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(p.CvStorageKey, p.CvData, ct);
        return Build(p, job, CvTextExtractor.Extract(bytes, p.CvFileName));
    }

    /// <summary>Ưu tiên khóa lưu trữ, lùi về cột byte[] khi chưa di trú hoặc khóa đã mất tệp.</summary>
    private async Task<byte[]?> ReadAsync(string? key, byte[]? legacy, CancellationToken ct)
    {
        if (key is not null)
        {
            var data = await cvStorage.ReadAsync(key, ct);
            if (data is not null) return data;
        }
        return legacy;
    }

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
