using ITCareerPlatform.Models;
using ITCareerPlatform.Services;
using Xunit;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P0-2: mốc thời gian lưu UTC, hiển thị theo giờ Việt Nam.
///
/// Mọi test ở đây chạy trên <see cref="FixedClock"/> nên kết quả KHÔNG phụ thuộc múi giờ
/// của máy chạy test — đó vừa là yêu cầu, vừa là cách chứng minh lỗi cũ đã được sửa: bản
/// cũ so giờ tường của nhà tuyển dụng với DateTime.Now của container chạy UTC.
/// </summary>
public class VietnamTimeTests
{
    private static ApplicationService NewSvc(TestDb t, TimeProvider clock) =>
        new(t.Db, new NotificationService(t.Db), null, clock);

    private static (int AppId, User Mentor) SeedApplication(TestDb t)
    {
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        var p = t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        return (t.AddApplication(job.Id, p.Id).Id, m);
    }

    // ===== Hiển thị =====

    /// <summary>UTC 02:00 là 09:00 sáng ở Việt Nam — con số người dùng phải đọc được.</summary>
    [Fact]
    public void DateTimeText_ConvertsUtcToVietnamTime()
    {
        var utc = new DateTime(2026, 3, 15, 2, 0, 0, DateTimeKind.Utc);

        Assert.Equal("15/03/2026 09:00", Ui.DateTimeText(utc));
    }

    /// <summary>
    /// 23:30 UTC đã là ngày HÔM SAU ở Việt Nam. Bản cũ in thẳng giá trị UTC nên mốc của một
    /// đơn nộp lúc 6h30 sáng hiện thành 23h30 tối hôm trước.
    /// </summary>
    [Fact]
    public void DateText_LateUtcEvening_IsNextDayInVietnam()
    {
        var utc = new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("16/03/2026", Ui.DateText(utc));
    }

    /// <summary>Ô datetime-local nhận giờ VN, nên một mốc UTC phải được quy đổi trước khi đổ vào.</summary>
    [Fact]
    public void InputDateTimeLocal_ShowsVietnamWallClock()
    {
        var utc = new DateTime(2026, 3, 15, 2, 0, 0, DateTimeKind.Utc);

        Assert.Equal("2026-03-15T09:00", Ui.InputDateTimeLocal(utc));
    }

    /// <summary>Hai chiều quy đổi phải khớp nhau, nếu không mỗi lần sửa lịch lại dịch đi 7 tiếng.</summary>
    [Fact]
    public void VietnamRoundTrip_IsLossless()
    {
        var vn = new DateTime(2026, 3, 15, 14, 30, 0);

        var utc = Ui.FromVietnamTime(vn);

        Assert.Equal(new DateTime(2026, 3, 15, 7, 30, 0), utc);
        Assert.Equal(vn, Ui.ToVietnamTime(utc));
    }

    // ===== Lịch phỏng vấn =====

    /// <summary>
    /// ĐÂY LÀ LỖI CŨ: giá trị datetime-local là giờ tường VN, đã được endpoint quy về UTC,
    /// rồi bị so với DateTime.Now của container chạy UTC. Một buổi hẹn đã trôi qua 6 tiếng
    /// vẫn được nhận. Với đồng hồ cố định, test này đúng ở mọi múi giờ.
    /// </summary>
    [Fact]
    public void Schedule_InThePast_IsRejected_RegardlessOfMachineTimeZone()
    {
        using var t = new TestDb();
        var (appId, m) = SeedApplication(t);
        var clock = new FixedClock(new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc));
        var past = new InterviewSchedule(new DateTime(2026, 3, 15, 4, 0, 0), "https://zoom.us/j/1", "");

        var ok = NewSvc(t, clock).UpdateStatus(appId, ApplicationStatus.Interview, past, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("tương lai", msg);
        Assert.Null(t.NewContext().Applications.Find(appId)!.InterviewAt);
    }

    /// <summary>Đặt lịch cách 5 phút là vô nghĩa với ứng viên — họ còn chưa mở được thông báo.</summary>
    [Fact]
    public void Schedule_FiveMinutesAway_IsRejected()
    {
        using var t = new TestDb();
        var (appId, m) = SeedApplication(t);
        var now = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(now);
        var soon = new InterviewSchedule(now.AddMinutes(5), "", "");

        var ok = NewSvc(t, clock).UpdateStatus(appId, ApplicationStatus.Interview, soon, m.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("15 phút", msg);
        Assert.Equal(ApplicationStatus.Submitted, t.NewContext().Applications.Find(appId)!.Status);
    }

    [Fact]
    public void Schedule_TwoHoursAway_IsAccepted()
    {
        using var t = new TestDb();
        var (appId, m) = SeedApplication(t);
        var now = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FixedClock(now);

        var ok = NewSvc(t, clock).UpdateStatus(appId, ApplicationStatus.Interview,
            new InterviewSchedule(now.AddHours(2), "", ""), m.Id, out var msg);

        Assert.True(ok, msg);
        Assert.Equal(now.AddHours(2), t.NewContext().Applications.Find(appId)!.InterviewAt);
    }

    // ===== Hạn nộp trong khung 00:00-07:00 giờ VN =====
    //
    // Khung này là chỗ lỗi cũ lộ ra: lúc 00:30 ngày 16/3 ở Việt Nam thì UTC vẫn đang là
    // 17:30 ngày 15/3, nên DateTime.Today của container cho ra NGÀY HÔM TRƯỚC.

    [Fact]
    public void Filter_AtHalfPastMidnightVietnam_HidesJobThatExpiredYesterday()
    {
        using var t = new TestDb();
        var clock = FixedClock.AtVietnamMidnightHalfHour(2026, 3, 16);
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var expired = t.AddJob(m.Id, title: "Hết hạn hôm qua");
        expired.Deadline = new DateTime(2026, 3, 15);
        var live = t.AddJob(m.Id, title: "Còn hạn");
        live.Deadline = new DateTime(2026, 3, 16);
        t.Db.SaveChanges();

        var result = new JobService(t.Db, null, clock).Filter(null, null, null, "new");

        Assert.Single(result);
        Assert.Equal("Còn hạn", result[0].Title);
    }

    [Fact]
    public void Apply_AtHalfPastMidnightVietnam_RejectsJobThatExpiredYesterday()
    {
        using var t = new TestDb();
        var clock = FixedClock.AtVietnamMidnightHalfHour(2026, 3, 16);
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        job.Deadline = new DateTime(2026, 3, 15);
        t.Db.SaveChanges();

        var ok = NewSvc(t, clock).Apply(job.Id, sv.Id, out var msg);

        Assert.False(ok);
        Assert.Contains("quá hạn", msg);
        Assert.Empty(t.NewContext().Applications);
    }

    /// <summary>
    /// Mặt còn lại của cùng một luật: tin hết hạn ĐÚNG hôm nay vẫn nhận đơn, kể cả khi theo
    /// giờ UTC thì "hôm nay" đã là ngày hôm trước. Không có test này thì một bản sửa quá tay
    /// (lùi mọi thứ về UTC) vẫn xanh trong khi nó chặn nhầm cả ngày cuối.
    /// </summary>
    [Fact]
    public void Apply_AtHalfPastMidnightVietnam_AcceptsJobExpiringToday()
    {
        using var t = new TestDb();
        var clock = FixedClock.AtVietnamMidnightHalfHour(2026, 3, 16);
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        job.Deadline = new DateTime(2026, 3, 16);
        t.Db.SaveChanges();

        var ok = NewSvc(t, clock).Apply(job.Id, sv.Id, out var msg);

        Assert.True(ok, msg);
    }

    /// <summary>
    /// Mốc đóng dấu tập trung phải là UTC. Nếu AppDbContext gán giờ máy chủ, đơn vừa nộp
    /// trên máy UTC+7 sẽ mang mốc ở tương lai 7 tiếng so với mọi mốc khác trong hệ thống.
    /// </summary>
    [Fact]
    public void AppliedAt_IsStampedInUtc()
    {
        using var t = new TestDb();
        var m = t.AddUser("M", "m@itcp.vn", Roles.MentorId);
        var sv = t.AddUser("SV", "sv@itcp.vn", Roles.StudentId);
        t.AddProfile(sv.Id);
        var job = t.AddJob(m.Id);
        var before = DateTime.UtcNow.AddSeconds(-5);

        Assert.True(NewSvc(t, TimeProvider.System).Apply(job.Id, sv.Id, out _));

        var applied = t.NewContext().Applications.Single().AppliedAt;
        Assert.InRange(applied, before, DateTime.UtcNow.AddSeconds(5));
    }
}
