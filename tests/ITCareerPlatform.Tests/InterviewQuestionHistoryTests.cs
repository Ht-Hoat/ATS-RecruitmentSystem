using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// HIST: mỗi lần sinh/sinh-lại bộ câu hỏi luyện phỏng vấn được lưu thêm một bản chụp vào
/// lịch sử, để sinh viên xem lại các lần trước thay vì mất khi bấm "tạo lại".
/// Câu trả lời gợi ý ngắn (Hint) đã có sẵn trong từng câu hỏi.
/// </summary>
public class InterviewQuestionHistoryTests
{
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db), t.CvStorage);

    private static InterviewQuestionSet Set(params string[] questions) =>
        new(questions.Select(q => new InterviewQuestion(q, QuestionCategory.Other, "Gợi ý trả lời: nói ý chính ngắn gọn.")).ToList(),
            EvaluationSource.Offline);

    private Application NewApplication(TestDb t)
    {
        var mentor = t.AddMentor();
        var job = t.AddJob(mentor.Id);
        var profile = t.AddProfile(t.AddUser("SV", "sv@itcp.vn", Roles.StudentId).Id);
        return t.AddApplication(job.Id, profile.Id, status: ApplicationStatus.Interview);
    }

    [Fact]
    public void SaveTwice_KeepsBothInHistory_NewestFirst()
    {
        using var t = new TestDb();
        var app = NewApplication(t);
        var svc = NewSvc(t);

        svc.SaveAiQuestions(app.Id, Set("Câu hỏi bộ 1 — A", "Câu hỏi bộ 1 — B"));
        svc.SaveAiQuestions(app.Id, Set("Câu hỏi bộ 2 — X", "Câu hỏi bộ 2 — Y"));

        var history = svc.GetAiQuestionHistory(app.Id);
        Assert.Equal(2, history.Count);
        Assert.Contains("bộ 2", history[0].Items[0].Question);   // mới nhất đứng đầu
        Assert.Contains("bộ 1", history[1].Items[0].Question);
    }

    [Fact]
    public void CurrentSet_IsAlwaysTheLatest()
    {
        using var t = new TestDb();
        var app = NewApplication(t);
        var svc = NewSvc(t);

        svc.SaveAiQuestions(app.Id, Set("cũ"));
        svc.SaveAiQuestions(app.Id, Set("mới"));

        var current = svc.GetAiQuestions(app.Id);
        Assert.NotNull(current);
        Assert.Contains("mới", current!.Items[0].Question);
    }

    [Fact]
    public void EachQuestion_CarriesAShortSuggestedAnswer()
    {
        using var t = new TestDb();
        var app = NewApplication(t);
        var svc = NewSvc(t);

        svc.SaveAiQuestions(app.Id, Set("Giải thích ACID trong SQL?"));

        var q = svc.GetAiQuestions(app.Id)!.Items[0];
        Assert.False(string.IsNullOrWhiteSpace(q.Hint));   // câu trả lời gợi ý ngắn
    }

    [Fact]
    public void History_IsPerApplication()
    {
        using var t = new TestDb();
        var a1 = NewApplication(t);
        var svc = NewSvc(t);
        svc.SaveAiQuestions(a1.Id, Set("chỉ của đơn 1"));

        Assert.Single(svc.GetAiQuestionHistory(a1.Id));
        Assert.Empty(svc.GetAiQuestionHistory(a1.Id + 999));
    }

    [Fact]
    public void PrepService_History_OnlyForOwner()
    {
        using var t = new TestDb();
        var mentor = t.AddMentor();
        var job = t.AddJob(mentor.Id);
        var owner = t.AddUser("Owner", "owner@itcp.vn", Roles.StudentId);
        var profile = t.AddProfile(owner.Id);
        var app = t.AddApplication(job.Id, profile.Id, status: ApplicationStatus.Interview);
        var apps = NewSvc(t);
        apps.SaveAiQuestions(app.Id, Set("của owner"));

        var prep = new InterviewPrepService(t.Db, apps, new AiServiceFake(), new AiInputBuilder(t.CvStorage));

        Assert.Single(prep.GetHistory(app.Id, owner.Id));           // chủ đơn xem được
        Assert.Empty(prep.GetHistory(app.Id, owner.Id + 12345));    // người khác thì không
    }

    // Fake AI đủ để dựng InterviewPrepService cho test lịch sử (không gọi mạng).
    private sealed class AiServiceFake : IAiService
    {
        public Task<AiEvaluation> EvaluateAsync(AiEvaluationInput input, CancellationToken ct = default) =>
            Task.FromResult(GeminiAiService.HeuristicEvaluate(input));
        public Task<InterviewQuestionSet> GenerateQuestionsAsync(AiEvaluationInput input, CancellationToken ct = default) =>
            Task.FromResult(GeminiAiService.HeuristicQuestions(input));
    }
}
