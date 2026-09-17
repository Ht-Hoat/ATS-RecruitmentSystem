using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

/// <summary>
/// P1-2: sinh viên tự chạy đánh giá độ phù hợp với một tin, TRƯỚC khi ứng tuyển.
///
/// Vấn đề cũ: Application.AiScore chỉ có giá trị khi Mentor bấm nút, nên giá trị hướng
/// nghiệp — lý do sinh viên dùng sản phẩm — phụ thuộc hành động của chính người đang sàng
/// lọc họ. Và sinh viên chỉ biết mình thiếu gì SAU khi đã nộp, tức là quá muộn để sửa.
/// </summary>
public interface ISelfCheckService
{
    /// <summary>Chạy một lượt tự kiểm tra. Trả về (ok, câu để hiện cho sinh viên đọc).</summary>
    Task<(bool ok, string message)> RunAsync(int userId, int jobId, CancellationToken ct = default);

    /// <summary>Kết quả gần nhất của (sinh viên, tin) — null nếu chưa chạy lần nào.</summary>
    SelfCheck? GetLatest(int userId, int jobId);

    /// <summary>Số lượt còn lại trong ngày hôm nay (theo lịch Việt Nam).</summary>
    int RemainingToday(int userId);
}

public class SelfCheckService(
    AppDbContext db,
    IJobService jobs,
    IAiService ai,
    IAiInputBuilder inputBuilder,
    TimeProvider? clock = null) : ISelfCheckService
{
    public SelfCheck? GetLatest(int userId, int jobId) =>
        db.SelfChecks.AsNoTracking()
          .Where(x => x.UserId == userId && x.JobId == jobId)
          .OrderByDescending(x => x.Id)
          .FirstOrDefault();

    public int RemainingToday(int userId) =>
        Math.Max(0, SelfCheck.DailyLimit - CountToday(userId));

    /// <summary>
    /// Đếm bằng COUNT trong SQL, mốc là 00:00 GIỜ VIỆT NAM quy về UTC (P0-2). Lấy
    /// DateTime.Today của container chạy UTC sẽ làm hạn mức reset lúc 7 giờ sáng giờ VN.
    /// </summary>
    private int CountToday(int userId)
    {
        var startOfDayUtc = VietnamDateHelper.StartOfVietnamDayUtc(VietnamDateHelper.Today(clock));
        return db.SelfChecks.Count(x => x.UserId == userId && x.CreatedAt >= startOfDayUtc);
    }

    public async Task<(bool ok, string message)> RunAsync(int userId, int jobId, CancellationToken ct = default)
    {
        // Cùng bộ điều kiện với luồng ứng tuyển, và cùng câu chữ: nếu hai chỗ nói khác nhau
        // thì sinh viên tưởng mình đã đủ điều kiện nộp trong khi thực ra chưa.
        var profile = db.CandidateProfiles.FirstOrDefault(p => p.UserId == userId);
        if (profile is null) return (false, "Bạn cần tạo hồ sơ IT trước khi tự kiểm tra độ phù hợp.");
        if (!profile.HasCv) return (false, "Bạn cần tải CV lên trước khi tự kiểm tra độ phù hợp.");

        // Dùng lại đúng vị ngữ "tin còn nhận hồ sơ" của P0-1. Chạy đánh giá với một tin đã
        // đóng là tiêu một lượt quota cho một vị trí không nộp được nữa.
        var job = jobs.GetVisibleForCandidate(jobId);
        if (job is null) return (false, "Tin tuyển dụng này đã đóng hoặc đã hết hạn nộp hồ sơ.");

        var used = CountToday(userId);
        if (used >= SelfCheck.DailyLimit)
            return (false,
                $"Bạn đã dùng hết {SelfCheck.DailyLimit} lượt tự kiểm tra của hôm nay. " +
                $"Hạn mức được cấp lại vào 00:00 ngày mai (giờ Việt Nam) — {HoursUntilMidnight()} nữa.");

        // Không bọc trong transaction: chỉ có đúng một lần ghi, và lần gọi mô hình nằm NGOÀI
        // mọi giao dịch CSDL — giữ transaction mở suốt một lần gọi mạng 30 giây là cách chắc
        // chắn nhất để khóa bảng dưới tải thật.
        var eval = await ai.EvaluateAsync(await inputBuilder.ForSelfCheckAsync(profile, job, ct), ct);

        db.SelfChecks.Add(new SelfCheck
        {
            UserId = userId,
            JobId = jobId,
            Score = Math.Clamp(eval.MatchPercent, 0, 100),
            // Cắt đúng giới hạn cột: ba trường này đến từ một mô hình ngoài và không có gì
            // buộc nó trả về dưới 1000 ký tự. Trên SQL Server, vượt cột là một lần ghi HỎNG.
            Strengths = Clip(eval.Strengths, 1000),
            Missing = Clip(eval.Missing, 1000),
            Roadmap = Clip(eval.Roadmap, 1000),
            Source = Clip(eval.Source, 20),
            // CreatedAt đặt TỪ ĐỒNG HỒ CỦA SERVICE, không để AppDbContext đóng dấu.
            //
            // Hai giá trị này phải đến từ cùng một nguồn: hạn mức trong ngày được tính bằng
            // COUNT trên chính cột này, nên nếu mốc ghi đi theo giờ máy chủ còn mốc đếm đi
            // theo TimeProvider thì luật "5 lượt mỗi ngày" không kiểm được và không test được.
            // Đóng dấu tập trung chỉ điền khi giá trị còn default, nên gán ở đây là hợp lệ.
            CreatedAt = VietnamDateHelper.UtcNow(clock)
        });
        db.SaveChanges();

        var left = SelfCheck.DailyLimit - used - 1;
        return (true, $"Đã chạy đánh giá độ phù hợp. Bạn còn {left} lượt trong hôm nay.");
    }

    /// <summary>Nói rõ còn bao lâu nữa mới chạy lại được, thay vì chỉ báo "hết lượt".</summary>
    private string HoursUntilMidnight()
    {
        var vnNow = Ui.ToVietnamTime(VietnamDateHelper.UtcNow(clock));
        var left = vnNow.Date.AddDays(1) - vnNow;
        return left.TotalHours >= 1
            ? $"khoảng {(int)left.TotalHours} giờ {left.Minutes} phút"
            : $"khoảng {left.Minutes} phút";
    }

    private static string? Clip(string? value, int max)
    {
        var s = value?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > max ? s[..max] : s;
    }
}
