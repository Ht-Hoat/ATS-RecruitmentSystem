using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// Hạn mức tự kiểm tra phải đứng vững khi nhiều request tới cùng lúc. Bản cũ đếm → gọi AI
/// (tới 30 giây) → rồi mới ghi, nên mọi request chen vào trong khoảng gọi AI đều thấy
/// "còn lượt". Nay lượt được giữ chỗ trước khi gọi AI.
/// </summary>
public class SelfCheckQuotaRaceTests
{
    /// <summary>AI giả: chặn lại cho tới khi test mở cổng, hoặc ném lỗi khi được bảo.</summary>
    private sealed class GatedAi(bool fail = false) : IAiService
    {
        public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiEvaluation> EvaluateAsync(AiEvaluationInput input, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Gate.Task;
            if (fail) throw new HttpRequestException("mạng hỏng");
            return new AiEvaluation(77, "Tốt", "Thiếu Docker", "Học Docker", EvaluationSource.Offline);
        }

        public Task<InterviewQuestionSet> GenerateQuestionsAsync(AiEvaluationInput input, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static SelfCheckService Svc(TestDb t, IAiService ai)
    {
        var db = t.NewContext();   // mỗi request một context — như một scope DI thật
        return new SelfCheckService(db, new JobService(db), ai, new AiInputBuilder(t.CvStorage));
    }

    private static (User Student, Job Job) Seed(TestDb t, int alreadyUsedToday)
    {
        var m = t.AddMentor();
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        for (var i = 0; i < alreadyUsedToday; i++)
            t.Db.SelfChecks.Add(new SelfCheck
            {
                UserId = sv.Id, JobId = job.Id, Score = 50, Source = EvaluationSource.Offline,
                CreatedAt = DateTime.UtcNow
            });
        t.Db.SaveChanges();
        return (sv, job);
    }

    [Fact]
    public async Task RequestArrivingWhileLastSlotIsInFlight_IsRejected()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t, alreadyUsedToday: SelfCheck.DailyLimit - 1);
        var slowAi = new GatedAi();

        // Request A lấy lượt cuối rồi đứng chờ AI.
        var first = Svc(t, slowAi).RunAsync(sv.Id, job.Id);
        await slowAi.Entered.Task;

        // Request B tới đúng lúc đó — trước bản sửa, B cũng thấy "còn 1 lượt".
        var (okB, msgB) = await Svc(t, new GatedAi()).RunAsync(sv.Id, job.Id);

        slowAi.Gate.SetResult();
        var (okA, msgA) = await first;

        Assert.True(okA, msgA);
        Assert.False(okB);
        Assert.Contains("hết", msgB);
        Assert.Equal(SelfCheck.DailyLimit, t.NewContext().SelfChecks.Count());
    }

    [Fact]
    public async Task InFlightRun_IsCounted_ButNeverShownAsAResult()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t, alreadyUsedToday: 0);
        var slowAi = new GatedAi();

        var run = Svc(t, slowAi).RunAsync(sv.Id, job.Id);
        await slowAi.Entered.Task;

        var observer = Svc(t, new GatedAi());
        Assert.Equal(SelfCheck.DailyLimit - 1, observer.RemainingToday(sv.Id));
        Assert.Null(observer.GetLatest(sv.Id, job.Id));   // chưa có kết quả thì không hiện gì

        slowAi.Gate.SetResult();
        Assert.True((await run).ok);

        var latest = Svc(t, new GatedAi()).GetLatest(sv.Id, job.Id);
        Assert.NotNull(latest);
        Assert.Equal(77, latest!.Score);
        Assert.NotEqual(SelfCheck.PendingSource, latest.Source);
    }

    /// <summary>AI hỏng thì sinh viên không nhận được gì — lượt đó phải được trả lại.</summary>
    [Fact]
    public async Task FailedAiCall_GivesTheSlotBack()
    {
        using var t = new TestDb();
        var (sv, job) = Seed(t, alreadyUsedToday: 0);
        var failing = new GatedAi(fail: true);
        failing.Gate.SetResult();

        await Assert.ThrowsAsync<HttpRequestException>(() => Svc(t, failing).RunAsync(sv.Id, job.Id));

        Assert.Empty(t.NewContext().SelfChecks);
        Assert.Equal(SelfCheck.DailyLimit, Svc(t, failing).RemainingToday(sv.Id));
    }
}
