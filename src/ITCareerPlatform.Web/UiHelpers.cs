using System.Security.Claims;

namespace ITCareerPlatform.Models;

/// <summary>
/// Metadata của một chuyên ngành IT: tên, lớp CSS và màu biểu đồ khai báo cùng một chỗ.
/// Trước đây 8 danh mục được liệt kê ở 3 nơi (Job.Categories, Ui.CategoryClass, bảng màu
/// của Dashboard) — thêm một danh mục mà quên nơi thứ ba thì biểu đồ lặng lẽ tô màu xám.
/// </summary>
public sealed record CategoryInfo(string Name, string CssClass, string Color)
{
    public static readonly IReadOnlyList<CategoryInfo> All = new[]
    {
        new CategoryInfo("Backend",  "cat cat-backend",  "#2563eb"),
        new CategoryInfo("Frontend", "cat cat-frontend", "#16a34a"),
        new CategoryInfo("Mobile",   "cat cat-mobile",   "#7c3aed"),
        new CategoryInfo("DevOps",   "cat cat-devops",   "#ea580c"),
        new CategoryInfo("Data/AI",  "cat cat-dataai",   "#dc2626"),
        new CategoryInfo("QA",       "cat cat-qa",       "#ca8a04"),
        new CategoryInfo("Design",   "cat cat-design",   "#db2777"),
        new CategoryInfo("Khác",     "cat cat-other",    "#64748b"),
    };

    public const string FallbackColor = "#64748b";

    private static readonly Dictionary<string, CategoryInfo> ByName =
        All.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public static CategoryInfo? Find(string? name) =>
        name is not null && ByName.TryGetValue(name, out var c) ? c : null;
}

/// <summary>
/// Hai ngưỡng chia % phù hợp thành ba khoảng. Khai báo đúng MỘT chỗ vì ba nơi cùng đọc:
/// màu badge (Ui.ScoreClass), dòng được tô sáng trong bảng ứng viên, và bộ lọc khoảng
/// điểm của N1.F. Ba nơi đó nằm trên cùng một màn hình — lệch nhau một con số là ứng viên
/// hiện huy hiệu "phù hợp cao" nhưng lại rơi khỏi kết quả lọc "&gt; 80%".
/// </summary>
public static class ScoreThreshold
{
    public const int HighAbove = 80;   // > 80  : phù hợp cao
    public const int MidFrom = 50;     // 50-80 : trung bình
}

// Ánh xạ dữ liệu -> lớp CSS màu + định dạng hiển thị (dùng chung cho mọi trang).
public static class Ui
{
    /// <summary>Dòng có được tô sáng trong bảng ứng viên không — cùng ngưỡng với badge.</summary>
    public static bool IsTopScore(int? score) => score > ScoreThreshold.HighAbove;

    public static string CategoryClass(string? category) =>
        CategoryInfo.Find(category)?.CssClass ?? "cat cat-other";

    public static string CategoryColor(string? category) =>
        CategoryInfo.Find(category)?.Color ?? CategoryInfo.FallbackColor;

    public static string LevelClass(string? level) => level switch
    {
        "Intern" => "lv lv-intern",
        "Junior" => "lv lv-junior",
        "Middle" => "lv lv-middle",
        "Senior" => "lv lv-senior",
        _ => "lv lv-junior"
    };

    // ATS-15: phân màu theo ngưỡng điểm AI/Final.
    // Dùng ĐÚNG ngưỡng của ScoreBand (bộ lọc N1.F). Trước đây màu chạy theo 70/40 còn bộ
    // lọc theo 80/50, nên trên cùng một bảng, một ứng viên 75% hiện huy hiệu "⭐ phù hợp
    // cao" nhưng chỉ tìm thấy được ở khoảng "50 - 80%".
    public static string ScoreClass(int? score) => score switch
    {
        null => "score score-none",
        > ScoreThreshold.HighAbove => "score score-high",
        >= ScoreThreshold.MidFrom => "score score-mid",
        _ => "score score-low"
    };

    public static string ScoreIcon(int? score) => score switch
    {
        null => "—",
        > 80 => "⭐",
        >= 50 => "⚡",
        _ => "⚠️"
    };

    // ATS-17 + P0-4: màu badge trạng thái đơn
    public static string StatusClass(string? status) => status switch
    {
        ApplicationStatus.Submitted => "st st-submitted",
        ApplicationStatus.Reviewing => "st st-reviewing",
        ApplicationStatus.Interview => "st st-interview",
        ApplicationStatus.Accepted => "st st-accepted",
        ApplicationStatus.Rejected => "st st-rejected",
        ApplicationStatus.Withdrawn => "st st-withdrawn",
        _ => "st"
    };

    public static string StatusColor(string? status) => status switch
    {
        ApplicationStatus.Submitted => "#3b82f6",
        ApplicationStatus.Reviewing => "#eab308",
        ApplicationStatus.Interview => "#f97316",
        ApplicationStatus.Accepted => "#22c55e",
        ApplicationStatus.Rejected => "#ef4444",
        ApplicationStatus.Withdrawn => "#64748b",
        _ => CategoryInfo.FallbackColor
    };

    public static string JobStatusLabel(string? status) =>
        status == JobStatus.Closed ? "Đã đóng" : "Đang mở";

    // ---------------------------------------------------------------
    //  P0-2: Múi giờ Việt Nam và định dạng thời gian.
    //  Dữ liệu lưu CSDL luôn ở UTC; đây là NƠI DUY NHẤT quy đổi sang giờ VN
    //  trước khi format hoặc đưa vào ô input.
    //  Windows dùng id "SE Asia Standard Time", Linux/Docker dùng "Asia/Ho_Chi_Minh".
    // ---------------------------------------------------------------
    public static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback nếu OS không chứa định nghĩa múi giờ
                return TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "ICT", "ICT");
            }
        }
    }

    /// <summary>Quy đổi một mốc thời gian UTC sang giờ Việt Nam (UTC+7).</summary>
    public static DateTime ToVietnamTime(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), VietnamTimeZone);
    }

    /// <summary>
    /// Chiều ngược lại: một giờ TƯỜNG của Việt Nam (thứ mà &lt;input type="datetime-local"&gt;
    /// gửi lên, và thứ nhà tuyển dụng nghĩ trong đầu) quy về UTC để lưu xuống CSDL.
    /// Thiếu bước này, giờ 14:30 do Mentor nhập được lưu thẳng thành 14:30 UTC — tức
    /// 21:30 giờ Việt Nam — và mọi so sánh "đã qua hay chưa" lệch đúng 7 tiếng.
    /// </summary>
    public static DateTime FromVietnamTime(DateTime vietnamWallClock)
    {
        var unspecified = DateTime.SpecifyKind(vietnamWallClock, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, VietnamTimeZone);
    }

    public const string DateTimePattern = "dd/MM/yyyy HH:mm";
    public const string DatePattern = "dd/MM/yyyy";
    public const string ShortDateTimePattern = "dd/MM HH:mm";

    public static string DateTimeText(DateTime? value) =>
        value.HasValue ? ToVietnamTime(value.Value).ToString(DateTimePattern) : "";

    public static string DateText(DateTime? value) =>
        value.HasValue ? ToVietnamTime(value.Value).ToString(DatePattern) : "";

    public static string ShortDateTimeText(DateTime? value) =>
        value.HasValue ? ToVietnamTime(value.Value).ToString(ShortDateTimePattern) : "";

    /// <summary>
    /// Giá trị cho &lt;input type="date"&gt; — luôn theo chuẩn HTML (yyyy-MM-dd) theo giờ Việt Nam.
    /// Hai chỗ gọi hàm này (hạn nộp, ngày sinh) đều là khái niệm NGÀY và luôn được lưu ở
    /// 00:00, nên phép cộng 7 giờ không bao giờ đẩy sang ngày kế. Nếu sau này có cột ngày
    /// nào lưu kèm giờ, phải tách riêng một hàm không quy đổi cho nó.
    /// </summary>
    public static string InputDate(DateTime? value)
    {
        var target = value.HasValue ? ToVietnamTime(value.Value) : ToVietnamTime(DateTime.UtcNow);
        return target.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Giá trị cho &lt;input type="datetime-local"&gt; (yyyy-MM-ddTHH:mm) quy đổi từ UTC sang giờ VN.
    /// </summary>
    public static string InputDateTimeLocal(DateTime? value) =>
        value.HasValue ? ToVietnamTime(value.Value).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>Giá trị cho &lt;input type="number"&gt;: dấu chấm thập phân, không theo culture máy chủ.</summary>
    public static string InputNumber(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Hiển thị khoảng lương, tránh "0–0 tr" khi chưa nhập.</summary>
    public static string SalaryText(decimal min, decimal max) =>
        min <= 0 && max <= 0 ? "Thỏa thuận" : $"{min:0.##}–{max:0.##} tr";
}

/// <summary>
/// Đọc danh tính người đang đăng nhập từ ClaimsPrincipal.
/// Trước đây đoạn int.TryParse(FindFirst(NameIdentifier)) được chép vào 9 component,
/// và các bản sao xử lý trường hợp lỗi khác nhau — hai bản để uid = 0 rồi vẫn đem đi
/// so quyền sở hữu.
/// </summary>
public static class CurrentUser
{
    /// <summary>Id người dùng, hoặc 0 nếu chưa đăng nhập / claim hỏng.</summary>
    public static int Id(ClaimsPrincipal? user) =>
        int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string RoleName(ClaimsPrincipal? user) =>
        user?.FindFirstValue(ClaimTypes.Role) ?? "";

    public static bool IsAdmin(ClaimsPrincipal? user) => user?.IsInRole(Roles.Admin) ?? false;

    public static bool IsStudent(ClaimsPrincipal? user) => user?.IsInRole(Roles.Student) ?? false;
}

/// <summary>
/// P0-2: một chỗ duy nhất phát biểu khái niệm "bây giờ" và "hôm nay theo giờ Việt Nam".
///
/// Hai khái niệm này KHÁC NHAU và hay bị lẫn:
///  · Hạn nộp (Job.Deadline) là một NGÀY trên tờ lịch Việt Nam — so sánh với <see cref="Today"/>.
///  · Mốc thời gian (AppliedAt, InterviewAt, CreatedAt) là một THỜI ĐIỂM lưu ở UTC — so
///    sánh với <see cref="UtcNow"/>, hoặc với <see cref="StartOfVietnamDayUtc"/> khi cần
///    "từ đầu ngày hôm đó theo giờ Việt Nam".
/// Trộn hai loại vào cùng một phép so sánh chính là lỗi lệch 7 tiếng mà P0-2 đi sửa.
/// </summary>
public static class VietnamDateHelper
{
    /// <summary>
    /// Việt Nam giữ UTC+7 cố định từ 1975 và không có giờ mùa hè, nên một con số bù giờ là
    /// đủ đúng. Hằng này chỉ dùng cho phần GỘP NHÓM chạy trong SQL (DATEADD dịch được,
    /// TimeZoneInfo thì không); mọi quy đổi ở phía C# vẫn đi qua Ui.ToVietnamTime.
    /// </summary>
    public const int OffsetHours = 7;

    /// <summary>Thời điểm hiện tại ở UTC. Mọi service lấy giờ qua đây để test cố định được đồng hồ.</summary>
    public static DateTime UtcNow(TimeProvider? clock = null) =>
        (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>Hôm nay trên tờ lịch Việt Nam (00:00, kiểu Unspecified — một NGÀY, không phải thời điểm).</summary>
    public static DateTime Today(TimeProvider? clock = null) =>
        Ui.ToVietnamTime(UtcNow(clock)).Date;

    /// <summary>
    /// Quy một giờ TƯỜNG của Việt Nam về UTC — dùng ở biên nhận dữ liệu, cho giá trị
    /// &lt;input type="datetime-local"&gt; mà người dùng vừa gõ.
    /// </summary>
    public static DateTime ToUtcFromVietnam(DateTime vietnamWallClock) =>
        Ui.FromVietnamTime(vietnamWallClock);

    /// <summary>
    /// Mốc UTC ứng với 00:00 giờ Việt Nam của một ngày. Dùng khi cần lọc "các đơn nộp từ
    /// đầu ngày X" trên một cột lưu UTC — so thẳng cột UTC với một ngày giờ VN thì lệch 7 tiếng.
    /// </summary>
    public static DateTime StartOfVietnamDayUtc(DateTime vietnamDate) =>
        Ui.FromVietnamTime(vietnamDate.Date);
}

