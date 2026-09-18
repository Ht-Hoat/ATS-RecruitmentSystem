using System.Net;
using System.Net.Mail;
using ITCareerPlatform.Models;

namespace ITCareerPlatform.Services;

/// <summary>
/// P1-3: gửi email.
///
/// Theo đúng triết lý đang có ở GeminiAiService — chưa cấu hình thì hệ thống vẫn chạy, và
/// nói rõ mình đang ở nhánh dự phòng. Với một hệ thống tuyển dụng, email là kênh thông tin
/// quan trọng nhất: giờ hẹn và link họp trước đây chỉ nằm trong chuông thông báo, nên sinh
/// viên không đăng nhập là mất buổi phỏng vấn.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Có cấu hình SMTP thật hay không. Giao diện dùng cờ này để biết khi nào còn phải hiện
    /// thông tin ra màn hình thay cho email — chứ không đoán bằng cách bắt ngoại lệ.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Một email chờ gửi. Tệp đính kèm dùng cho .ics của lời mời phỏng vấn.</summary>
public record EmailMessage(
    string To, string Subject, string Body,
    string? AttachmentName = null, byte[]? AttachmentContent = null);

/// <summary>
/// Bản gửi thật qua SMTP. Dùng System.Net.Mail.SmtpClient của BCL — không thêm package mới.
/// </summary>
public class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private string Host => config["Smtp:Host"] ?? "";
    private string From => config["Smtp:From"] ?? config["Smtp:User"] ?? "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Chưa cấu hình Smtp:Host / Smtp:From.");

        var port = int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
        var enableSsl = !bool.TryParse(config["Smtp:EnableSsl"], out var ssl) || ssl;
        var user = config["Smtp:User"];
        var password = config["Smtp:Password"];

        using var client = new SmtpClient(Host, port) { EnableSsl = enableSsl };
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, password);

        using var mail = new MailMessage(From, message.To, message.Subject, message.Body)
        {
            // Nội dung là văn bản thuần: tên vị trí và tên công ty do người dùng nhập, nên gửi
            // dạng HTML sẽ biến một dấu nhỏ hơn trong tiêu đề tin thành thẻ mở không đóng.
            IsBodyHtml = false
        };

        // Đính kèm dựng từ mảng byte đã có sẵn trong outbox; stream phải sống tới khi gửi xong.
        using var attachmentStream = message.AttachmentContent is null
            ? null
            : new MemoryStream(message.AttachmentContent);
        if (attachmentStream is not null && message.AttachmentName is not null)
            mail.Attachments.Add(new Attachment(attachmentStream, message.AttachmentName, "text/calendar"));

        await client.SendMailAsync(mail, ct);
        logger.LogInformation("Đã gửi email tới {To}: {Subject}", message.To, message.Subject);
    }
}

/// <summary>
/// Nhánh dự phòng khi chưa cấu hình SMTP: ghi log rồi thôi.
///
/// Ghi người nhận và TIÊU ĐỀ, không ghi nội dung — thân email mang tên vị trí, tên công ty
/// và phản hồi của nhà tuyển dụng gửi riêng cho ứng viên; log thì gom về một nơi nhiều
/// người đọc được. Ứng dụng không được chết vì thiếu cấu hình email.
/// </summary>
public class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Chưa cấu hình SMTP nên KHÔNG gửi được email tới {To} (tiêu đề: {Subject}). " +
            "Đặt Smtp:Host và Smtp:From để bật gửi thật — xem HUONG_DAN_CHAY.md.",
            message.To, message.Subject);
        return Task.CompletedTask;
    }
}

/// <summary>
/// P1-3: sinh tệp lịch .ics cho lời mời phỏng vấn.
///
/// Thời gian ghi theo UTC (hậu tố Z) — đó là lý do việc này phải làm SAU P0-2: nếu mốc lưu
/// trong CSDL không thật sự là UTC thì mọi ứng dụng lịch sẽ đặt buổi hẹn lệch 7 tiếng.
/// </summary>
public static class IcsBuilder
{
    /// <summary>
    /// UID ỔN ĐỊNH theo đơn: đổi lịch thì gửi bản CẬP NHẬT của cùng một sự kiện, chứ không
    /// tạo sự kiện thứ hai. Kèm SEQUENCE tăng dần để ứng dụng lịch biết bản nào mới hơn —
    /// thiếu nó, một số ứng dụng bỏ qua bản cập nhật và giữ nguyên giờ cũ.
    /// </summary>
    public static string Uid(int applicationId) => $"application-{applicationId}@itcareerplatform";

    public static byte[] BuildInvite(
        int applicationId, int sequence, DateTime startUtc, string jobTitle,
        string? meetingLink, string? note, TimeSpan? duration = null)
    {
        var endUtc = startUtc.Add(duration ?? TimeSpan.FromHours(1));

        // CRLF là bắt buộc theo RFC 5545; dùng "\n" thì một số ứng dụng lịch từ chối cả tệp.
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//ITCareerPlatform//Interview//VI",
            "CALSCALE:GREGORIAN",
            "METHOD:REQUEST",
            "BEGIN:VEVENT",
            "UID:" + Uid(applicationId),
            "SEQUENCE:" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "DTSTAMP:" + Stamp(DateTime.UtcNow),
            "DTSTART:" + Stamp(startUtc),
            "DTEND:" + Stamp(endUtc),
            "SUMMARY:" + Escape("Phỏng vấn: " + jobTitle),
            "STATUS:CONFIRMED"
        };

        // LOCATION là link họp nếu có; phỏng vấn trực tiếp thì để trống thay vì ghi "không có".
        if (!string.IsNullOrWhiteSpace(meetingLink))
            lines.Add("LOCATION:" + Escape(meetingLink));
        if (!string.IsNullOrWhiteSpace(note))
            lines.Add("DESCRIPTION:" + Escape(note));

        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        return System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    private static string Stamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Thoát ký tự theo RFC 5545. Tên vị trí do nhà tuyển dụng nhập, nên một dấu phẩy trong
    /// tiêu đề tin sẽ bị hiểu là dấu tách giá trị và làm hỏng cả sự kiện.
    /// </summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
