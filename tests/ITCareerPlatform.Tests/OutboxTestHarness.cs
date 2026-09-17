using ITCareerPlatform.Data;
using ITCareerPlatform.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Chạy đúng MỘT lượt quét của <see cref="OutboxSender"/> trên CSDL test.
///
/// OutboxSender tự tạo scope mỗi lượt (DbContext là scoped), nên test phải dựng một
/// IServiceScopeFactory thật trỏ về đúng CSDL SQLite in-memory của TestDb — chứ không
/// gọi thẳng vào một phương thức private.
/// </summary>
public static class OutboxTestHarness
{
    /// <summary>Trả về số email thực sự đã gửi được.</summary>
    public static Task<int> RunOnce(TestDb t, IEmailSender? sender = null)
    {
        var services = new ServiceCollection();
        // Mỗi scope lấy một AppDbContext mới trên CÙNG connection SQLite — giống hệt cách
        // ứng dụng thật chạy, nơi mỗi lượt quét mở một kết nối riêng tới cùng CSDL.
        services.AddScoped<AppDbContext>(_ => t.NewContext());
        services.AddSingleton(sender ?? new NullEmailSender(NullLogger<NullEmailSender>.Instance));

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new OutboxSender(scopeFactory, NullLogger<OutboxSender>.Instance).SendPendingAsync();
    }
}

/// <summary>
/// Bản gửi giả có cấu hình — để kiểm nhánh gửi THÀNH CÔNG mà không cần máy chủ SMTP thật.
/// <paramref name="ThrowMessage"/> khác null thì mô phỏng một lần gửi hỏng.
/// </summary>
public sealed class FakeEmailSender(bool configured = true, string? throwMessage = null) : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    public bool IsConfigured { get; } = configured;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (throwMessage is not null) throw new InvalidOperationException(throwMessage);
        Sent.Add(message);
        return Task.CompletedTask;
    }
}
