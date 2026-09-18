using System.Data;
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
          .Where(x => x.UserId == userId && x.JobId == jobId && x.Source != SelfCheck.PendingSource)
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

        // P2-3: một lượt tự kiểm tra cũng là một lần gửi CV ra ngoài, nên vẫn cần sự đồng ý —
        // dù người bấm nút chính là chủ nhân dữ liệu.
        if (!AiConsentGate.Allows(profile)) return (false, AiConsentGate.BlockedForStudent);

        // Dùng lại đúng vị ngữ "tin còn nhận hồ sơ" của P0-1. Chạy đánh giá với một tin đã
        // đóng là tiêu một lượt quota cho một vị trí không nộp được nữa.
        var job = jobs.GetVisibleForCandidate(jobId);
        if (job is null) return (false, "Tin tuyển dụng này đã đóng hoặc đã hết hạn nộp hồ sơ.");

        // Giữ chỗ TRƯỚC khi gọi AI. Bản cũ đếm rồi mới gọi AI rồi mới ghi, nên năm request
        // gửi song song lúc còn 1 lượt đều thấy "còn lượt" và cả năm cùng gọi Gemini.
        var (reserved, used) = TryReserve(userId, jobId);
        if (reserved is null)
            return used >= SelfCheck.DailyLimit
                ? (false,
                   $"Bạn đã dùng hết {SelfCheck.DailyLimit} lượt tự kiểm tra của hôm nay. " +
                   $"Hạn mức được cấp lại vào 00:00 ngày mai (giờ Việt Nam) — {HoursUntilMidnight()} nữa.")
                : (false, "Đang có một lượt tự kiểm tra khác của bạn chạy cùng lúc. Vui lòng thử lại sau ít giây.");

        // Lần gọi mô hình nằm NGOÀI mọi giao dịch CSDL — giữ transaction mở suốt một lần gọi
        // mạng 30 giây là cách chắc chắn nhất để khóa bảng dưới tải thật.
        AiEvaluation eval;
        try
        {
            eval = await ai.EvaluateAsync(await inputBuilder.ForSelfCheckAsync(profile, job, ct), ct);
        }
        catch
        {
            // Gọi hỏng (hoặc người dùng bỏ trang) thì trả lại lượt: sinh viên không nhận được
            // kết quả nào thì không được tính là đã dùng.
            db.SelfChecks.Remove(reserved);
            db.SaveChanges();
            throw;
        }

        reserved.Score = Math.Clamp(eval.MatchPercent, 0, 100);
        // Cắt đúng giới hạn cột: ba trường này đến từ một mô hình ngoài và không có gì
        // buộc nó trả về dưới 1000 ký tự. Trên SQL Server, vượt cột là một lần ghi HỎNG.
        reserved.Strengths = TextLimits.Clip(eval.Strengths, 1000);
        reserved.Missing = TextLimits.Clip(eval.Missing, 1000);
        reserved.Roadmap = TextLimits.Clip(eval.Roadmap, 1000);
        reserved.Source = TextLimits.Clip(eval.Source, 20);
        db.SaveChanges();

        var left = SelfCheck.DailyLimit - used - 1;
        return (true, $"Đã chạy đánh giá độ phù hợp. Bạn còn {left} lượt trong hôm nay.");
    }

    /// <summary>
    /// Đếm và giữ chỗ trong CÙNG một giao dịch Serializable, nên hai request không thể cùng
    /// thấy "còn lượt". Trên SQL Server, hai giao dịch chen nhau sẽ có một bên bị chọn làm nạn
    /// nhân deadlock — bên đó nhận (null, used &lt; limit) và được mời thử lại, KHÔNG vượt hạn mức.
    /// Trả về (dòng đã giữ chỗ hoặc null, số lượt đã dùng trước lượt này).
    /// </summary>
    private (SelfCheck? Reserved, int Used) TryReserve(int userId, int jobId)
    {
        var used = 0;
        try
        {
            using var tx = db.Database.BeginTransaction(IsolationLevel.Serializable);
            used = CountToday(userId);
            if (used >= SelfCheck.DailyLimit) return (null, used);

            var row = new SelfCheck
            {
                UserId = userId,
                JobId = jobId,
                Source = SelfCheck.PendingSource,
                // CreatedAt đặt TỪ ĐỒNG HỒ CỦA SERVICE, không để AppDbContext đóng dấu.
                //
                // Hai giá trị này phải đến từ cùng một nguồn: hạn mức trong ngày được tính bằng
                // COUNT trên chính cột này, nên nếu mốc ghi đi theo giờ máy chủ còn mốc đếm đi
                // theo TimeProvider thì luật "5 lượt mỗi ngày" không kiểm được và không test được.
                // Đóng dấu tập trung chỉ điền khi giá trị còn default, nên gán ở đây là hợp lệ.
                CreatedAt = VietnamDateHelper.UtcNow(clock)
            };
            db.SelfChecks.Add(row);
            db.SaveChanges();
            tx.Commit();
            return (row, used);
        }
        catch (DbUpdateException)
        {
            // Thua trong cuộc chen với một request khác. Tháo dòng chưa lưu được ra khỏi
            // context để lần SaveChanges sau (gỡ lượt, ghi kết quả) không ghi lại nó.
            foreach (var e in db.ChangeTracker.Entries<SelfCheck>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            return (null, used);
        }
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
}
