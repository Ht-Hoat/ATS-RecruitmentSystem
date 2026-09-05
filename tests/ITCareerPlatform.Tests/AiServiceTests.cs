using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// ATS-13/14 (TST-03): kiểm thử "Đánh giá độ phù hợp & Gợi ý lộ trình" (heuristic offline — tất định, không cần mạng).
public class AiServiceTests
{
    [Fact]
    public void Evaluate_HighTechOverlap_GivesHighMatch()
    {
        var cv = "C#, .NET, ASP.NET Core, SQL Server, Docker";
        var jd = "Yêu cầu: C#, .NET, SQL Server, Docker";

        var e = GeminiAiService.HeuristicEvaluate(cv, jd);

        Assert.True(e.MatchPercent >= 70, $"Khớp cao phải ≥70% nhưng nhận {e.MatchPercent}");
        Assert.False(string.IsNullOrWhiteSpace(e.Strengths));
    }

    [Fact]
    public void Evaluate_NoOverlap_GivesLowMatch_AndSuggestsRoadmap()
    {
        var cv = "Python, Django, Pandas, TensorFlow";
        var jd = "Yêu cầu: C#, .NET, SQL Server, Docker";

        var e = GeminiAiService.HeuristicEvaluate(cv, jd);

        Assert.True(e.MatchPercent <= 40, $"Không khớp phải ≤40% nhưng nhận {e.MatchPercent}");
        Assert.False(string.IsNullOrWhiteSpace(e.Missing));   // phải chỉ ra kỹ năng thiếu
        Assert.False(string.IsNullOrWhiteSpace(e.Roadmap));   // phải có lộ trình gợi ý
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var cv = "React, TypeScript, Node.js";
        var jd = "React, TypeScript";

        var e1 = GeminiAiService.HeuristicEvaluate(cv, jd);
        var e2 = GeminiAiService.HeuristicEvaluate(cv, jd);

        Assert.Equal(e1.MatchPercent, e2.MatchPercent);
        Assert.Equal(e1.Roadmap, e2.Roadmap);
    }

    [Fact]
    public void Evaluate_MatchPercent_AlwaysInRange()
    {
        var e = GeminiAiService.HeuristicEvaluate("", "C#, Java, Python");
        Assert.InRange(e.MatchPercent, 0, 100);
    }

    [Fact]
    public void Evaluate_ReturnsAllThreeAdviceFields()
    {
        var e = GeminiAiService.HeuristicEvaluate("C#, .NET", "C#, .NET, Docker, Kubernetes");
        Assert.False(string.IsNullOrWhiteSpace(e.Strengths));
        Assert.False(string.IsNullOrWhiteSpace(e.Missing));
        Assert.False(string.IsNullOrWhiteSpace(e.Roadmap));
    }
}
