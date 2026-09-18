using ITCareerPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

/// <summary>
/// P2-2: chuyển nội dung CV từ cột varbinary ra blob storage.
///
/// CHẠY LẶP LẠI ĐƯỢC (idempotent): mỗi lượt chỉ xử lý những bản ghi CÒN byte[] và CHƯA có
/// khóa. Chạy lần thứ hai không tạo thêm bản nào — điều kiện lọc không còn khớp dòng nào.
/// Đó là yêu cầu bắt buộc: một lần di trú trên CSDL thật có thể bị ngắt giữa chừng, và lúc
/// đó thứ duy nhất làm được là chạy lại từ đầu.
///
/// Không xóa cột byte[] và cũng không đặt nó về null ở đây: giữ lại bản gốc cho tới khi
/// người vận hành đã kiểm tra xong là cách duy nhất để quay lui được. Việc dọn cột là một
/// bước RIÊNG, có ý thức, làm sau.
/// </summary>
public class CvMigrationRunner(AppDbContext db, ICvStorage storage, ILogger<CvMigrationRunner> logger)
{
    /// <summary>Số bản ghi xử lý mỗi lượt — giữ bộ nhớ có trần khi mỗi bản ghi tới 5MB.</summary>
    private const int BatchSize = 50;

    public record Result(int Profiles, int Applications)
    {
        public int Total => Profiles + Applications;
        public bool DidNothing => Total == 0;
    }

    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var profiles = await MigrateProfilesAsync(ct);
        var applications = await MigrateApplicationsAsync(ct);

        var result = new Result(profiles, applications);
        if (!result.DidNothing)
            logger.LogInformation(
                "P2-2: đã chuyển {Profiles} CV hồ sơ và {Applications} bản chụp CV sang blob storage.",
                profiles, applications);
        return result;
    }

    private async Task<int> MigrateProfilesAsync(CancellationToken ct)
    {
        var done = 0;
        while (true)
        {
            // Nạp theo lô và ĐỌC LẠI mỗi vòng thay vì giữ một con trỏ: sau khi lưu, chính
            // những dòng vừa xử lý rơi khỏi điều kiện lọc, nên vòng sau luôn lấy lô kế tiếp.
            var batch = await db.CandidateProfiles
                .Where(p => p.CvStorageKey == null && p.CvData != null)
                .OrderBy(p => p.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return done;

            foreach (var p in batch)
                p.CvStorageKey = await storage.SaveAsync(p.CvData!, p.CvFileName ?? "cv.pdf", ct);

            await db.SaveChangesAsync(ct);
            done += batch.Count;
        }
    }

    private async Task<int> MigrateApplicationsAsync(CancellationToken ct)
    {
        var done = 0;
        while (true)
        {
            var batch = await db.Applications
                .Where(a => a.CvStorageKeySnapshot == null && a.CvDataSnapshot != null)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) return done;

            foreach (var a in batch)
                a.CvStorageKeySnapshot = await storage.SaveAsync(a.CvDataSnapshot!, a.CvFileNameSnapshot, ct);

            await db.SaveChangesAsync(ct);
            done += batch.Count;
        }
    }
}
