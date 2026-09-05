namespace ITCareerPlatform.Models;

// Ánh xạ dữ liệu -> lớp CSS màu (dùng chung cho các trang hiển thị badge/chip).
public static class Ui
{
    public static string CategoryClass(string? category) => category switch
    {
        "Backend" => "cat cat-backend",
        "Frontend" => "cat cat-frontend",
        "Mobile" => "cat cat-mobile",
        "DevOps" => "cat cat-devops",
        "Data/AI" => "cat cat-dataai",
        "QA" => "cat cat-qa",
        "Design" => "cat cat-design",
        _ => "cat cat-other"
    };

    public static string LevelClass(string? level) => level switch
    {
        "Intern" => "lv lv-intern",
        "Junior" => "lv lv-junior",
        "Middle" => "lv lv-middle",
        "Senior" => "lv lv-senior",
        _ => "lv lv-junior"
    };

    // ATS-15: phân màu theo ngưỡng điểm AI/Final
    public static string ScoreClass(int? score) => score switch
    {
        null => "score score-none",
        > 70 => "score score-high",
        >= 40 => "score score-mid",
        _ => "score score-low"
    };

    public static string ScoreIcon(int? score) => score switch
    {
        null => "—",
        > 70 => "⭐",
        >= 40 => "⚡",
        _ => "⚠️"
    };

    // ATS-17: màu badge trạng thái đơn
    public static string StatusClass(string? status) => status switch
    {
        ApplicationStatus.Submitted => "st st-submitted",
        ApplicationStatus.Reviewing => "st st-reviewing",
        ApplicationStatus.Interview => "st st-interview",
        ApplicationStatus.Accepted => "st st-accepted",
        ApplicationStatus.Rejected => "st st-rejected",
        _ => "st"
    };
}
