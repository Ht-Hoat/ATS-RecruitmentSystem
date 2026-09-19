using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

// N1.F: bộ lọc danh sách ứng viên cho HR.
public class ApplicantFilterTests
{
    private static ApplicationService NewSvc(TestDb t) => new(t.Db, new NotificationService(t.Db), t.CvStorage);

    /// <summary>Một tin + n ứng viên, mỗi người một hồ sơ riêng (chỉ số duy nhất theo cặp job/hồ sơ).</summary>
    private static (Job Job, TestDb Db) SeedJob(TestDb t, string techStack = "C#,Docker,React")
    {
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        return (t.AddJob(m.Id, techStack: techStack), t);
    }

    private static Application AddApplicant(TestDb t, Job job, string suffix,
        string tags = "C#", int years = 0, string status = ApplicationStatus.Submitted,
        int? ai = null, int? hr = null, bool withCv = true)
    {
        var u = t.AddUser("SV " + suffix, $"sv{suffix}@itcp.vn", Roles.StudentId);
        var p = new CandidateProfile
        {
            UserId = u.Id, FullName = "SV " + suffix, Email = $"sv{suffix}@itcp.vn",
            TechSkillTags = tags, Skills = tags, YearsOfExperience = years
        };
        t.Db.CandidateProfiles.Add(p);
        t.Db.SaveChanges();

        var a = new Application
        {
            JobId = job.Id, CandidateProfileId = p.Id, Status = status,
            AiScore = ai, HrScore = hr, AppliedAt = DateTime.UtcNow,
            CvFileNameSnapshot = withCv ? "cv.pdf" : "(chưa tải CV)",
            CvDataSnapshot = withCv ? System.Text.Encoding.UTF8.GetBytes("%PDF-1.4") : null
        };
        t.Db.Applications.Add(a);
        t.Db.SaveChanges();
        return a;
    }

    // ===== Không lọc =====

    [Fact]
    public void NullFilter_BehavesLikeNoFilter()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "1");
        AddApplicant(t, job, "2");

        Assert.Equal(2, NewSvc(t).GetByJob(job.Id, "date", null).Count);
        Assert.Equal(2, NewSvc(t).GetByJob(job.Id).Count);             // chữ ký cũ vẫn chạy
        Assert.Equal(2, NewSvc(t).GetByJob(job.Id, "date", new ApplicantFilter()).Count);
    }

    [Fact]
    public void EmptyFilter_IsNotActive()
    {
        Assert.False(new ApplicantFilter().IsActive);
        Assert.True(new ApplicantFilter(Status: ApplicationStatus.Rejected).IsActive);
        Assert.True(new ApplicantFilter(HasCv: false).IsActive);
        Assert.True(new ApplicantFilter(Band: ScoreBand.High).IsActive);
        Assert.True(new ApplicantFilter(Level: CandidateLevel.Senior).IsActive);
        Assert.True(new ApplicantFilter(RequiredTech: new[] { "C#" }).IsActive);
    }

    // ===== Lọc theo trạng thái =====

    [Fact]
    public void FilterByStatus_KeepsOnlyThatStage()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "1", status: ApplicationStatus.Submitted);
        AddApplicant(t, job, "2", status: ApplicationStatus.Rejected);
        AddApplicant(t, job, "3", status: ApplicationStatus.Rejected);

        var got = NewSvc(t).GetByJob(job.Id, "date", new ApplicantFilter(Status: ApplicationStatus.Rejected));

        Assert.Equal(2, got.Count);
        Assert.All(got, x => Assert.Equal(ApplicationStatus.Rejected, x.Status));
    }

    // ===== Lọc theo CV =====

    [Fact]
    public void FilterByHasCv_SplitsBothWays()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "1", withCv: true);
        AddApplicant(t, job, "2", withCv: false);
        AddApplicant(t, job, "3", withCv: false);
        var svc = NewSvc(t);

        Assert.Single(svc.GetByJob(job.Id, "date", new ApplicantFilter(HasCv: true)));
        Assert.Equal(2, svc.GetByJob(job.Id, "date", new ApplicantFilter(HasCv: false)).Count);
        Assert.Equal(3, svc.GetByJob(job.Id, "date", new ApplicantFilter(HasCv: null)).Count);
    }

    [Fact]
    public void ListItem_ReportsHasCv()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "1", withCv: true);
        AddApplicant(t, job, "2", withCv: false);

        var got = NewSvc(t).GetByJob(job.Id).OrderBy(x => x.FullName).ToList();

        Assert.True(got[0].HasCv);
        Assert.False(got[1].HasCv);
    }

    // ===== Lọc theo khoảng % phù hợp =====

    [Fact]
    public void FilterByBand_UsesFinalScore_PreferringHrOverAi()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "high", ai: 95);
        AddApplicant(t, job, "mid", ai: 60);
        AddApplicant(t, job, "low", ai: 20);
        // AI chấm thấp nhưng Mentor đã chốt 90 — phải nằm ở khoảng cao, theo điểm chốt.
        AddApplicant(t, job, "hrhigh", ai: 10, hr: 90);
        var svc = NewSvc(t);

        var high = svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.High));
        Assert.Equal(2, high.Count);
        Assert.Contains(high, x => x.FullName.Contains("hrhigh"));

        Assert.Single(svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.Mid)));
        Assert.Single(svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.Low)));
    }

    /// <summary>
    /// Đây là lý do phải có khoảng thứ tư: đơn chưa chấm có COALESCE = NULL nên rơi khỏi
    /// cả ba khoảng số. Không có "Chưa đánh giá" thì chúng biến mất khỏi mọi lựa chọn.
    /// </summary>
    [Fact]
    public void UnscoredApplicants_FallOutOfNumericBands_ButHaveTheirOwn()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "scored", ai: 75);
        AddApplicant(t, job, "new1");
        AddApplicant(t, job, "new2");
        var svc = NewSvc(t);

        Assert.DoesNotContain(svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.High)),
            x => x.FinalScore is null);
        Assert.DoesNotContain(svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.Low)),
            x => x.FinalScore is null);

        var unscored = svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: ScoreBand.Unscored));
        Assert.Equal(2, unscored.Count);
        Assert.All(unscored, x => Assert.Null(x.FinalScore));
    }

    /// <summary>Ba khoảng số không chồng lấn và không để lọt điểm nào từ 0 tới 100.</summary>
    [Theory]
    [InlineData(0, ScoreBand.Low)]
    [InlineData(49, ScoreBand.Low)]
    [InlineData(50, ScoreBand.Mid)]
    [InlineData(80, ScoreBand.Mid)]
    [InlineData(81, ScoreBand.High)]
    [InlineData(100, ScoreBand.High)]
    public void ScoreBands_CoverEveryScoreExactlyOnce(int score, string expectedBand)
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "x", ai: score);
        var svc = NewSvc(t);

        foreach (var band in ScoreBand.All)
        {
            var hit = svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: band)).Count;
            Assert.Equal(band == expectedBand ? 1 : 0, hit);
        }
    }

    // ===== Lọc theo cấp bậc =====

    [Theory]
    [InlineData(0, CandidateLevel.Fresher)]
    [InlineData(1, CandidateLevel.Junior)]
    [InlineData(2, CandidateLevel.Middle)]
    [InlineData(4, CandidateLevel.Middle)]
    [InlineData(5, CandidateLevel.Senior)]
    [InlineData(12, CandidateLevel.Senior)]
    public void FilterByLevel_MatchesTheLabelShownOnTheRow(int years, string expected)
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "x", years: years);
        var svc = NewSvc(t);

        // Nhãn hiển thị và kết quả lọc phải khớp nhau — nếu không, một ứng viên hiện
        // "Senior" nhưng lại rơi vào kết quả lọc "Middle".
        Assert.Equal(expected, Assert.Single(svc.GetByJob(job.Id)).LevelName);

        foreach (var level in CandidateLevel.All)
        {
            var hit = svc.GetByJob(job.Id, "date", new ApplicantFilter(Level: level)).Count;
            Assert.Equal(level == expected ? 1 : 0, hit);
        }
    }

    // ===== Lọc theo tech stack =====

    /// <summary>Chọn nhiều công nghệ = ứng viên phải có ĐỦ, không phải có một trong số đó.</summary>
    [Fact]
    public void FilterByTech_RequiresAllSelected_NotAny()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "both", tags: "C#,Docker,Redis");
        AddApplicant(t, job, "onlycs", tags: "C#");
        AddApplicant(t, job, "onlydocker", tags: "Docker");
        var svc = NewSvc(t);

        var both = svc.GetByJob(job.Id, "date", new ApplicantFilter(RequiredTech: new[] { "C#", "Docker" }));

        Assert.Equal("SV both", Assert.Single(both).FullName);
    }

    /// <summary>
    /// So khớp dùng đúng chuẩn hóa của TechList (thường hóa, gộp khoảng trắng) — cùng công
    /// thức mà phần chấm điểm offline dùng, nên hai chỗ không trả lời khác nhau về cùng một
    /// câu "ứng viên này có SQL Server không".
    /// </summary>
    [Theory]
    [InlineData("sql server")]
    [InlineData("SQL  Server")]
    [InlineData("SQL SERVER")]
    public void FilterByTech_NormalizesCaseAndWhitespace(string candidateTag)
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t, techStack: "SQL Server");
        AddApplicant(t, job, "x", tags: candidateTag);

        var got = NewSvc(t).GetByJob(job.Id, "date", new ApplicantFilter(RequiredTech: new[] { "SQL Server" }));

        Assert.Single(got);
    }

    [Fact]
    public void FilterByTech_EmptyList_DoesNotFilter()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "1", tags: "Go");

        Assert.Single(NewSvc(t).GetByJob(job.Id, "date", new ApplicantFilter(RequiredTech: Array.Empty<string>())));
    }

    // ===== Kết hợp + sắp xếp =====

    [Fact]
    public void Filters_Combine_WithAndSemantics()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "match", tags: "C#,Docker", years: 5, ai: 90, status: ApplicationStatus.Reviewing);
        AddApplicant(t, job, "wrongstatus", tags: "C#,Docker", years: 5, ai: 90, status: ApplicationStatus.Rejected);
        AddApplicant(t, job, "wronglevel", tags: "C#,Docker", years: 0, ai: 90, status: ApplicationStatus.Reviewing);
        AddApplicant(t, job, "wrongscore", tags: "C#,Docker", years: 5, ai: 10, status: ApplicationStatus.Reviewing);
        AddApplicant(t, job, "wrongtech", tags: "C#", years: 5, ai: 90, status: ApplicationStatus.Reviewing);

        var got = NewSvc(t).GetByJob(job.Id, "date", new ApplicantFilter(
            Status: ApplicationStatus.Reviewing,
            HasCv: true,
            Band: ScoreBand.High,
            Level: CandidateLevel.Senior,
            RequiredTech: new[] { "C#", "Docker" }));

        Assert.Equal("SV match", Assert.Single(got).FullName);
    }

    [Fact]
    public void SortByScore_StillWorksWithFilterApplied()
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "a", ai: 55, status: ApplicationStatus.Reviewing);
        AddApplicant(t, job, "b", ai: 78, status: ApplicationStatus.Reviewing);
        AddApplicant(t, job, "c", ai: 95, status: ApplicationStatus.Submitted);

        var got = NewSvc(t).GetByJob(job.Id, "score", new ApplicantFilter(Status: ApplicationStatus.Reviewing));

        Assert.Equal(new int?[] { 78, 55 }, got.Select(x => x.FinalScore).ToArray());
    }

    // ===== Ngưỡng điểm dùng chung =====

    /// <summary>
    /// Nhãn hiển thị của ScoreBand phải sinh ra đúng con số mà bộ lọc dùng. Trước đây nhãn
    /// ghi "> 80%" còn Ui.ScoreClass lại cắt ở 70, nên một ứng viên 75 điểm hiện huy hiệu
    /// "phù hợp cao" nhưng lại rơi khỏi kết quả lọc "> 80%".
    /// </summary>
    [Fact]
    public void ScoreBandLabels_MatchTheNumericThresholds()
    {
        Assert.True(ScoreBand.LabelsMatchThresholds);
    }

    /// <summary>Huy hiệu, dòng tô sáng và bộ lọc phải phân loại cùng một điểm giống nhau.</summary>
    [Theory]
    [InlineData(100, ScoreBand.High, "score score-high", true)]
    [InlineData(81, ScoreBand.High, "score score-high", true)]
    [InlineData(80, ScoreBand.Mid, "score score-mid", false)]
    [InlineData(50, ScoreBand.Mid, "score score-mid", false)]
    [InlineData(49, ScoreBand.Low, "score score-low", false)]
    [InlineData(0, ScoreBand.Low, "score score-low", false)]
    public void Badge_RowHighlight_AndFilter_AgreeOnEveryScore(
        int score, string expectedBand, string expectedCss, bool expectedTopRow)
    {
        using var t = new TestDb();
        var (job, _) = SeedJob(t);
        AddApplicant(t, job, "x", ai: score);
        var svc = NewSvc(t);

        // 1) màu huy hiệu
        Assert.Equal(expectedCss, Ui.ScoreClass(score));
        // 2) dòng có được tô sáng không
        Assert.Equal(expectedTopRow, Ui.IsTopScore(score));
        // 3) rơi vào đúng một khoảng lọc, và là khoảng được chờ đợi
        foreach (var band in ScoreBand.All)
        {
            var hit = svc.GetByJob(job.Id, "date", new ApplicantFilter(Band: band)).Count;
            Assert.Equal(band == expectedBand ? 1 : 0, hit);
        }
    }

    [Fact]
    public void Filter_NeverLeaksApplicantsFromAnotherJob()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var jobA = t.AddJob(m.Id, "Tin A");
        var jobB = t.AddJob(m.Id, "Tin B");
        AddApplicant(t, jobA, "a", ai: 90);
        AddApplicant(t, jobB, "b", ai: 90);

        var got = NewSvc(t).GetByJob(jobA.Id, "date", new ApplicantFilter(Band: ScoreBand.High));

        Assert.Equal("SV a", Assert.Single(got).FullName);
    }
}
