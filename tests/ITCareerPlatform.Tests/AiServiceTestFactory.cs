using ITCareerPlatform.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Dựng một <see cref="GeminiAiService"/> THẬT nhưng KHÔNG có khóa Gemini.
///
/// Cố ý dùng bản thật thay vì một stub: đây đúng là cách dịch vụ chạy trên một máy chưa
/// cấu hình gì, nên test cũng kiểm luôn rằng nhánh dự phòng ngoại tuyến hoạt động và tự
/// khai báo mình là Offline. Một stub trả về số cố định sẽ xanh kể cả khi nhánh đó hỏng.
///
/// Không có khóa nên không lần gọi mạng nào xảy ra — test vẫn tất định và chạy offline.
/// </summary>
public static class AiServiceTestFactory
{
    public static IAiService Offline()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        var httpFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var emptyConfig = new ConfigurationBuilder().Build();   // Gemini:ApiKey không tồn tại

        return new GeminiAiService(httpFactory, emptyConfig, NullLogger<GeminiAiService>.Instance);
    }
}
