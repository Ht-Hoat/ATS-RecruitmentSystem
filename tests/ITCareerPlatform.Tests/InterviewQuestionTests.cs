using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// N1.C: sinh và lưu bộ câu hỏi phỏng vấn.
public class InterviewQuestionTests
{
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db));

    private static AiEvaluationInput Input(string candidateTech, string requiredTech) =>
        new("Kỹ năng: " + candidateTech, "Yêu cầu: " + requiredTech, candidateTech, requiredTech);

    // ===== Bộ offline =====
    // Đây là đường chạy mặc định khi chưa cấu hình khóa Gemini, tức là đường mà phần lớn
    // buổi chấm đồ án sẽ đi qua — nên nó được test kỹ như đường gọi mô hình thật.

    [Fact]
    public void Heuristic_AsksAboutTechTheCandidateClaims()
    {
        var set = GeminiAiService.HeuristicQuestions(Input("C#,Docker", "C#,Docker,Kubernetes"));

        Assert.True(set.HasQuestions);
        Assert.True(set.IsOffline);
        Assert.Equal(EvaluationSource.Offline, set.Source);
        Assert.Contains(set.Items, q => q.Question.Contains("C#"));
        Assert.Contains(set.Items, q => q.Question.Contains("Docker"));
    }

    /// <summary>Công nghệ JD cần mà hồ sơ chưa có phải thành câu hỏi, không phải lý do loại.</summary>
    [Fact]
    public void Heuristic_AsksAboutMissingTech_Too()
    {
        var set = GeminiAiService.HeuristicQuestions(Input("C#", "C#,Kubernetes"));

        Assert.Contains(set.Items, q => q.Question.Contains("Kubernetes"));
        Assert.Contains(set.Items, q => q.Hint.Contains("tốc độ học"));
    }

    [Fact]
    public void Heuristic_NoTechStackInJob_SaysSoInsteadOfInventingQuestions()
    {
        var set = GeminiAiService.HeuristicQuestions(new AiEvaluationInput("", "", "", ""));

        Assert.True(set.HasQuestions);
        Assert.Contains(set.Items, q => q.Category == QuestionCategory.Other);
    }

    /// <summary>Luôn có câu hỏi dự án và thái độ, kể cả khi không khớp công nghệ nào.</summary>
    [Fact]
    public void Heuristic_AlwaysIncludesProjectAndBehavioralQuestions()
    {
        var set = GeminiAiService.HeuristicQuestions(Input("Photoshop", "Rust,WebAssembly"));

        Assert.Contains(set.Items, q => q.Category == QuestionCategory.Project);
        Assert.Contains(set.Items, q => q.Category == QuestionCategory.Behavioral);
    }

    [Fact]
    public void Heuristic_IsDeterministic_AndCapped()
    {
        var input = Input("C#,Docker,React,SQL Server,Redis", "C#,Docker,React,SQL Server,Redis,Kafka,Go");

        var a = GeminiAiService.HeuristicQuestions(input);
        var b = GeminiAiService.HeuristicQuestions(input);

        Assert.Equal(a.Items.Count, b.Items.Count);
        Assert.True(a.Items.Count <= 7);
        Assert.Equal(a.Items.Select(x => x.Question), b.Items.Select(x => x.Question));
    }

    // ===== Đọc JSON của mô hình =====

    [Fact]
    public void ParseQuestions_ReadsWellFormedResponse()
    {
        var raw = """
        {"questions":[
          {"question":"Bạn dùng Docker thế nào?","category":"technical","hint":"Nghe chi tiết"},
          {"question":"Kể về một dự án","category":"project","hint":""}
        ]}
        """;

        var set = GeminiAiService.ParseQuestions(raw);

        Assert.NotNull(set);
        Assert.Equal(2, set!.Items.Count);
        Assert.Equal(EvaluationSource.Gemini, set.Source);
        // Nhãn được ép về bộ cố định, không giữ nguyên chữ mô hình trả về.
        Assert.Equal(QuestionCategory.Technical, set.Items[0].Category);
        Assert.Equal(QuestionCategory.Project, set.Items[1].Category);
    }

    /// <summary>Mô hình hay chào thêm một câu trước JSON — không được vì thế mà vứt cả câu trả lời.</summary>
    [Fact]
    public void ParseQuestions_IgnoresChatterAroundTheJson()
    {
        var raw = "Đây là bộ câu hỏi của bạn:\n```json\n{\"questions\":[{\"question\":\"Q1\",\"category\":\"x\",\"hint\":\"h\"}]}\n```\nChúc bạn phỏng vấn tốt!";

        var set = GeminiAiService.ParseQuestions(raw);

        Assert.NotNull(set);
        Assert.Equal("Q1", Assert.Single(set!.Items).Question);
    }

    /// <summary>Một phần tử hỏng không được kéo cả bộ xuống — mất một câu vẫn hơn mất tất cả.</summary>
    [Fact]
    public void ParseQuestions_SkipsBrokenEntries_KeepsTheRest()
    {
        var raw = """
        {"questions":[
          {"question":"","category":"technical","hint":"rỗng nên bỏ"},
          "một chuỗi thay vì object",
          {"question":"Câu hợp lệ","category":"technical","hint":"ok"}
        ]}
        """;

        var set = GeminiAiService.ParseQuestions(raw);

        Assert.Equal("Câu hợp lệ", Assert.Single(set!.Items).Question);
    }

    [Theory]
    [InlineData("")]
    [InlineData("không có json ở đây")]
    [InlineData("{\"questions\":[]}")]
    [InlineData("{\"matchPercent\":80}")]     // đúng JSON nhưng sai hình dạng
    public void ParseQuestions_UnusableResponse_ReturnsNull(string raw)
    {
        Assert.Null(GeminiAiService.ParseQuestions(raw));
    }

    [Fact]
    public void ParseQuestions_CapsAtSevenQuestions()
    {
        var entries = string.Join(",", Enumerable.Range(1, 20)
            .Select(i => $"{{\"question\":\"Câu {i}\",\"category\":\"technical\",\"hint\":\"h\"}}"));

        var set = GeminiAiService.ParseQuestions("{\"questions\":[" + entries + "]}");

        Assert.Equal(7, set!.Items.Count);
    }

    // ===== Lưu và đọc lại =====

    private static (int AppId, User Mentor) Seed(TestDb t)
    {
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        return (t.AddApplication(t.AddJob(m.Id).Id, p.Id).Id, m);
    }

    [Fact]
    public void SaveAndGetAiQuestions_RoundTrips()
    {
        using var t = new TestDb();
        var (appId, _) = Seed(t);
        var svc = NewSvc(t);
        var set = GeminiAiService.HeuristicQuestions(Input("C#,Docker", "C#,Docker"));

        svc.SaveAiQuestions(appId, set);
        var read = svc.GetAiQuestions(appId);

        Assert.NotNull(read);
        Assert.Equal(set.Items.Count, read!.Items.Count);
        Assert.Equal(set.Items[0].Question, read.Items[0].Question);
        Assert.Equal(set.Items[0].Category, read.Items[0].Category);
        Assert.Equal(set.Items[0].Hint, read.Items[0].Hint);
        Assert.Equal(EvaluationSource.Offline, read.Source);
    }

    [Fact]
    public void GetAiQuestions_WhenNeverGenerated_IsNull()
    {
        using var t = new TestDb();
        var (appId, _) = Seed(t);

        Assert.Null(NewSvc(t).GetAiQuestions(appId));
    }

    /// <summary>Cột hỏng (dữ liệu cũ, sửa tay) phải thành "chưa có", không phải lỗi 500 cả trang.</summary>
    [Fact]
    public void GetAiQuestions_CorruptJson_ReturnsNullInsteadOfThrowing()
    {
        using var t = new TestDb();
        var (appId, _) = Seed(t);

        var a = t.Db.Applications.Find(appId)!;
        a.AiQuestions = "{ đây không phải JSON";
        t.Db.SaveChanges();

        Assert.Null(NewSvc(t).GetAiQuestions(appId));
    }

    /// <summary>Bộ quá dài bị bỏ bớt câu cuối, chứ không ghi một chuỗi JSON bị cắt ngang.</summary>
    [Fact]
    public void SaveAiQuestions_OverlongSet_DropsTrailingQuestions_ButStaysReadable()
    {
        using var t = new TestDb();
        var (appId, _) = Seed(t);
        var svc = NewSvc(t);

        var huge = Enumerable.Range(1, 7)
            .Select(i => new InterviewQuestion(new string('Q', 400), QuestionCategory.Technical, new string('H', 400)))
            .ToList();

        svc.SaveAiQuestions(appId, new InterviewQuestionSet(huge, EvaluationSource.Gemini));

        var read = svc.GetAiQuestions(appId);
        Assert.NotNull(read);                       // vẫn đọc lại được
        Assert.True(read!.Items.Count < huge.Count); // đã bỏ bớt
        Assert.True(t.NewContext().Applications.Find(appId)!.AiQuestions!.Length <= 4000);
    }

    /// <summary>
    /// Bộ câu hỏi KHÔNG được nằm trong ApplicationDetail — record đó là thứ trang
    /// /my-applications/{id} của sinh viên đọc.
    /// </summary>
    [Fact]
    public void ApplicationDetail_DoesNotExposeQuestions()
    {
        var names = typeof(ApplicationDetail).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("AiQuestions", names);
        Assert.DoesNotContain("AiQuestionsSource", names);
    }
}
