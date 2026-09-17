using ITCareerPlatform.Data;
using ITCareerPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace ITCareerPlatform.Services;

/// <summary>
/// P1-3: tiến trình nền quét bảng EmailOutbox và gửi.
///
/// Tách khỏi request vì SMTP chậm và hay lỗi: gửi đồng bộ trong lần đổi trạng thái thì một
/// lần timeout làm nhà tuyển dụng thấy "đổi trạng thái thất bại" dù trạng thái đã đổi.
/// </summary>
public class OutboxSender(IServiceScopeFactory scopeFactory, ILogger<OutboxSender> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Trần mỗi lượt quét. Không có trần thì một lần triển khai sau thời gian dài ngừng gửi
    /// sẽ nạp cả nghìn bản ghi vào bộ nhớ và bắn liên tục vào máy chủ SMTP — nhiều nhà cung
    /// cấp coi đó là gửi hàng loạt và chặn cả tài khoản.
    /// </summary>
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer thay cho Task.Delay trong vòng lặp: nó không trôi dần theo thời gian
        // xử lý của từng lượt, và dừng sạch khi ứng dụng tắt.
        using var timer = new PeriodicTimer(Interval);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await SendPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Một lượt hỏng KHÔNG được làm chết tiến trình nền: nếu nó chết, mọi email về
                // sau nằm lại trong bảng vĩnh viễn và không có gì trên màn hình báo điều đó.
                logger.LogError(ex, "Lỗi ngoài dự kiến khi quét hàng đợi email.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }   // ứng dụng đang tắt
    }

    /// <summary>Gửi tối đa <see cref="BatchSize"/> email đang chờ. Public để test gọi thẳng.</summary>
    public async Task<int> SendPendingAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var pending = await db.EmailOutbox
            .Where(x => x.SentAt == null && x.Attempts < EmailOutbox.MaxAttempts)
            .OrderBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in pending)
        {
            // Tăng Attempts TRƯỚC khi gửi. Nếu tăng sau, một lần ném ngoại lệ không bắt được
            // (hoặc tiến trình bị tắt giữa chừng) sẽ để bản ghi ở nguyên số cũ và nó được thử
            // lại mãi mãi — đúng thứ mà trần MaxAttempts sinh ra để chặn.
            row.Attempts++;
            try
            {
                await sender.SendAsync(
                    new EmailMessage(row.ToEmail, row.Subject, row.Body, row.AttachmentName, row.AttachmentContent),
                    ct);

                // NullEmailSender không ném ngoại lệ nhưng cũng KHÔNG gửi gì. Đánh dấu đã gửi
                // trong trường hợp đó là nói dối, nên chỉ đóng bản ghi khi có SMTP thật.
                if (sender.IsConfigured)
                {
                    row.SentAt = DateTime.UtcNow;
                    row.LastError = null;
                    sent++;
                }
                else
                {
                    // Trả lại lượt thử: chưa cấu hình SMTP không phải là một lần thử thất bại,
                    // nếu tính vào thì sau 5 lượt quét (2 phút rưỡi) email bị bỏ hẳn và cấu
                    // hình SMTP sau đó cũng không cứu được.
                    row.Attempts--;
                    row.LastError = "Chưa cấu hình SMTP — email vẫn nằm trong hàng đợi.";
                }
            }
            catch (Exception ex)
            {
                row.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                logger.LogWarning(ex,
                    "Gửi email #{Id} tới {To} thất bại (lần {Attempt}/{Max}).",
                    row.Id, row.ToEmail, row.Attempts, EmailOutbox.MaxAttempts);
            }
        }

        if (pending.Count > 0) await db.SaveChangesAsync(ct);
        return sent;
    }
}
