using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// Ranh giới phòng thủ trước Gemini: một dịch vụ ngoài có thể trả về bất cứ thứ gì, kể cả
// đúng JSON nhưng sai hình dạng. Hai hàm này quyết định lúc nào rơi về đường offline, nên
// chúng là chỗ đáng test nhất trong AiService — trước đây chưa có test nào chạm tới.
public class AiParsingTests
{
    // ===== ExtractJsonObject =====

    [Fact]
    public void ExtractJsonObject_TakesFirstBalancedObject()
    {
        var raw = "Kết quả đây: {\"a\":1,\"b\":{\"c\":2}} — chúc bạn một ngày tốt lành!";

        Assert.Equal("{\"a\":1,\"b\":{\"c\":2}}", GeminiAiService.ExtractJsonObject(raw));
    }

    /// <summary>
    /// Dấu '}' nằm trong chuỗi không được tính là đóng ngoặc. Bản dùng regex tham lam trước
    /// đây vứt cả câu trả lời hợp lệ chỉ vì mô hình chào thêm một câu có dấu ngoặc nhọn.
    /// </summary>
    [Fact]
    public void ExtractJsonObject_IgnoresBracesInsideStrings()
    {
        var raw = "{\"note\":\"dùng cú pháp ${biến} nhé\",\"ok\":true}";

        Assert.Equal(raw, GeminiAiService.ExtractJsonObject(raw));
    }

    [Fact]
    public void ExtractJsonObject_HandlesEscapedQuotes()
    {
        var raw = "{\"q\":\"anh ấy nói \\\"xin chào}\\\" rồi đi\"}";

        Assert.Equal(raw, GeminiAiService.ExtractJsonObject(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("không có ngoặc nào")]
    [InlineData("{\"thiếu đóng ngoặc\":1")]
    public void ExtractJsonObject_NoUsableObject_ReturnsNull(string? raw)
    {
        Assert.Null(GeminiAiService.ExtractJsonObject(raw));
    }

    // ===== ParseEvaluation =====

    /// <summary>
    /// Mô hình trả điểm ở cả ba dạng: 85, 85.0 và "85". Bản cũ chỉ nhận số nguyên JSON,
    /// nên một câu trả lời đúng nhưng ghi 85.0 bị vứt và thay bằng điểm offline.
    /// </summary>
    [Theory]
    [InlineData("85", 85)]
    [InlineData("85.0", 85)]
    [InlineData("84.6", 85)]
    [InlineData("\"85\"", 85)]
    [InlineData("\"84.6\"", 85)]
    public void ParseEvaluation_AcceptsEveryShapeOfPercent(string percentJson, int expected)
    {
        var raw = $"{{\"matchPercent\":{percentJson},\"strengths\":\"s\",\"missing\":\"m\",\"roadmap\":\"r\"}}";

        var e = GeminiAiService.ParseEvaluation(raw);

        Assert.NotNull(e);
        Assert.Equal(expected, e!.MatchPercent);
        Assert.Equal(EvaluationSource.Gemini, e.Source);
    }

    [Theory]
    [InlineData("150", 100)]
    [InlineData("-20", 0)]
    public void ParseEvaluation_ClampsOutOfRangePercent(string percentJson, int expected)
    {
        var raw = $"{{\"matchPercent\":{percentJson},\"strengths\":\"\",\"missing\":\"\",\"roadmap\":\"\"}}";

        Assert.Equal(expected, GeminiAiService.ParseEvaluation(raw)!.MatchPercent);
    }

    [Theory]
    [InlineData("{\"strengths\":\"s\"}")]                       // thiếu matchPercent
    [InlineData("{\"matchPercent\":\"tám mươi\"}")]             // không đọc được thành số
    [InlineData("{\"matchPercent\":null}")]
    [InlineData("không phải json")]
    public void ParseEvaluation_UnusableResponse_ReturnsNull(string raw)
    {
        Assert.Null(GeminiAiService.ParseEvaluation(raw));
    }

    /// <summary>Thiếu trường văn bản thì thành chuỗi rỗng, không làm hỏng cả kết quả.</summary>
    [Fact]
    public void ParseEvaluation_MissingTextFields_BecomeEmptyStrings()
    {
        var e = GeminiAiService.ParseEvaluation("{\"matchPercent\":70}");

        Assert.NotNull(e);
        Assert.Equal("", e!.Strengths);
        Assert.Equal("", e.Missing);
        Assert.Equal("", e.Roadmap);
    }
}
