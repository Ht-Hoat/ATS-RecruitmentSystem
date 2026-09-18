using ITCareerPlatform.Models;

namespace ITCareerPlatform.Services;

/// <summary>
/// P1-3: soạn email cho ba sự kiện quan trọng — mời phỏng vấn, trúng tuyển, từ chối.
///
/// Tách khỏi ApplicationService vì đây thuần là việc dựng văn bản: không đọc CSDL, không có
/// tác dụng phụ, nên kiểm được bằng test mà không cần dựng cả một giao dịch.
///
/// RANH GIỚI QUAN TRỌNG: nội dung email TUYỆT ĐỐI không mang điểm số (AiScore/HrScore), lý
/// do chốt điểm (HrNote) hay ghi chú nội bộ của Mentor (InternalNote). Email đi ra khỏi hệ
/// thống và có thể được chuyển tiếp cho bất kỳ ai. Chỉ CandidateFeedback — thứ nhà tuyển
/// dụng cố ý viết CHO ứng viên đọc (P1-4) — mới được đi kèm.
/// </summary>
public static class StatusEmailComposer
{
    private const string Signature = "Trân trọng,\nIT Career Platform";

    /// <summary>Trả null nghĩa là sự kiện này không cần gửi email.</summary>
    public static EmailOutbox? Compose(Application a, string newStatus, bool statusChanged)
    {
        var to = a.CandidateProfile?.Email;
        if (string.IsNullOrWhiteSpace(to)) return null;

        var name = a.CandidateProfile?.FullName ?? "bạn";
        var title = a.Job?.Title ?? "vị trí đã ứng tuyển";
        // Tên công ty có từ P1-1; nếu vì lý do nào đó chưa nạp được thì vẫn gửi với tên vị trí.
        var company = a.Job?.Company?.Name;
        var at = string.IsNullOrWhiteSpace(company) ? title : $"{title} — {company}";

        return newStatus switch
        {
            // Đổi lịch cũng gửi (statusChanged = false): ứng viên phải biết giờ mới, nếu không
            // họ đến vào giờ cũ. Không có lịch thì không có gì để mời.
            ApplicationStatus.Interview when a.InterviewAt.HasValue => new EmailOutbox
            {
                ToEmail = to,
                Subject = statusChanged ? $"Lời mời phỏng vấn: {at}" : $"Cập nhật lịch phỏng vấn: {at}",
                Body = InterviewBody(a, name, at, statusChanged),
                AttachmentName = "phong-van.ics",
                AttachmentContent = IcsBuilder.BuildInvite(
                    a.Id, a.InterviewSequence, a.InterviewAt.Value, title, a.InterviewLink, a.InterviewNote)
            },

            ApplicationStatus.Accepted when statusChanged => new EmailOutbox
            {
                ToEmail = to,
                Subject = $"Chúc mừng! Bạn đã trúng tuyển: {at}",
                Body = string.Join("\n", new[]
                {
                    $"Xin chào {name},",
                    "",
                    $"Đơn ứng tuyển của bạn vào vị trí {at} đã được chấp nhận.",
                    "Nhà tuyển dụng sẽ liên hệ với bạn để trao đổi các bước tiếp theo.",
                    "",
                    Signature
                })
            },

            ApplicationStatus.Rejected when statusChanged => new EmailOutbox
            {
                ToEmail = to,
                Subject = $"Kết quả ứng tuyển: {at}",
                Body = RejectionBody(a, name, at)
            },

            _ => null
        };
    }

    private static string InterviewBody(Application a, string name, string at, bool statusChanged)
    {
        var opening = statusChanged
            ? $"Bạn được mời phỏng vấn vị trí {at}."
            : $"Lịch phỏng vấn vị trí {at} đã được cập nhật.";

        // Phỏng vấn trực tiếp thì nói rõ là trực tiếp, thay vì để trống và bắt ứng viên đoán.
        var link = string.IsNullOrWhiteSpace(a.InterviewLink)
            ? "Hình thức: phỏng vấn trực tiếp — vui lòng liên hệ nhà tuyển dụng để biết địa điểm."
            : $"Link tham gia: {a.InterviewLink}";

        var lines = new List<string>
        {
            $"Xin chào {name},",
            "",
            opening,
            "",
            // Ui.DateTimeText quy mốc UTC sang giờ Việt Nam — nói rõ múi giờ để ứng viên ở
            // nước ngoài không phải đoán.
            $"Thời gian: {Ui.DateTimeText(a.InterviewAt)} (giờ Việt Nam)",
            link
        };
        if (!string.IsNullOrWhiteSpace(a.InterviewNote)) lines.Add($"Ghi chú: {a.InterviewNote}");

        lines.Add("");
        lines.Add("Tệp lịch đính kèm giúp bạn thêm buổi hẹn vào ứng dụng lịch của mình.");
        lines.Add("");
        lines.Add(Signature);
        return string.Join("\n", lines);
    }

    private static string RejectionBody(Application a, string name, string at)
    {
        var lines = new List<string>
        {
            $"Xin chào {name},",
            "",
            $"Cảm ơn bạn đã quan tâm tới vị trí {at}. Rất tiếc, hồ sơ của bạn chưa phù hợp với",
            "vị trí này ở thời điểm hiện tại."
        };

        if (!string.IsNullOrWhiteSpace(a.CandidateFeedback))
        {
            lines.Add("");
            lines.Add("Phản hồi từ nhà tuyển dụng:");
            lines.Add(a.CandidateFeedback!);
        }

        lines.Add("");
        lines.Add("Chúc bạn sớm tìm được vị trí phù hợp.");
        lines.Add("");
        lines.Add(Signature);
        return string.Join("\n", lines);
    }
}
