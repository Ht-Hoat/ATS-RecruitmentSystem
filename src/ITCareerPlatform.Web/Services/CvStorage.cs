using System.Security.Cryptography;

namespace ITCareerPlatform.Services;

/// <summary>
/// P2-2: nơi lưu nội dung CV, thay cho cột varbinary trong CSDL.
///
/// Vấn đề cũ: Application.CvDataSnapshot lưu MỘT BẢN CV (tới 5MB) cho MỖI đơn, cộng bản gốc
/// trong CandidateProfile.CvData. Một sinh viên nộp 20 tin là 21 bản CV nằm trong CSDL — mỗi
/// lần sao lưu, mỗi lần khôi phục, mỗi lần nhân bản môi trường đều kéo theo toàn bộ chỗ đó.
///
/// Ý tưởng "đóng băng CV tại thời điểm nộp" là ĐÚNG và được giữ nguyên; chỉ cách lưu đổi.
/// </summary>
public interface ICvStorage
{
    /// <summary>Lưu nội dung và trả về khóa để đọc lại. Hai lần lưu cùng nội dung cho cùng một khóa.</summary>
    Task<string> SaveAsync(byte[] data, string fileName, CancellationToken ct = default);

    /// <summary>Đọc lại theo khóa; null nghĩa là khóa không tồn tại (đã bị xóa tay, hoặc chưa di trú).</summary>
    Task<byte[]?> ReadAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

public static class CvStorageExtensions
{
    /// <summary>
    /// P2-2: ưu tiên khóa lưu trữ, lùi về cột byte[] cũ khi chưa di trú hoặc khóa đã mất tệp.
    /// Mảng rỗng coi như không có. MỘT chỗ phát biểu thứ tự này cho mọi đường đọc CV — tải CV,
    /// chấm AI, tự kiểm tra — để chúng không thể trả lời khác nhau cho cùng một đơn.
    /// </summary>
    public static async Task<byte[]?> ReadOrLegacyAsync(this ICvStorage storage, string? key, byte[]? legacy,
        CancellationToken ct = default)
    {
        if (key is not null && await storage.ReadAsync(key, ct) is { } data) return data;
        return legacy is { Length: > 0 } ? legacy : null;
    }
}

/// <summary>
/// Bản triển khai đầu tiên: lưu tệp trên đĩa theo thư mục cấu hình (CvStorage:RootPath).
///
/// Tên tệp là SHA-256 CỦA NỘI DUNG, không phải tên tệp người dùng gửi lên. Hai lý do, cả
/// hai đều quan trọng:
///  1. Hai đơn dùng chung một CV chỉ tốn một bản trên đĩa — đây là chỗ tiết kiệm lớn nhất,
///     vì bản chụp lúc nộp thường giống hệt CV gốc.
///  2. Đường dẫn hoàn toàn do hệ thống tính. Ghép tên tệp người dùng vào đường dẫn là mở
///     sẵn một lỗ đọc/ghi tệp tùy ý: một tệp tên "../../appsettings.json" sẽ ghi đè ra
///     ngoài thư mục lưu trữ.
/// </summary>
public class DiskCvStorage : ICvStorage
{
    private readonly string _root;

    /// <summary>Thư mục gốc đang dùng — để log khởi động và test đếm được tệp thật trên đĩa.</summary>
    public string RootPath => _root;

    public DiskCvStorage(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["CvStorage:RootPath"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "cv")
            : configured;
        Directory.CreateDirectory(_root);
    }

    /// <summary>Dùng cho test: trỏ thẳng vào một thư mục tạm.</summary>
    public DiskCvStorage(string rootPath)
    {
        _root = rootPath;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(byte[] data, string fileName, CancellationToken ct = default)
    {
        var key = ComputeKey(data, fileName);
        var path = PathFor(key);

        // Đã có tệp cùng khóa nghĩa là nội dung y hệt (SHA-256 trùng), nên không ghi lại.
        // Đây chính là phần khử trùng lặp: 21 bản CV giống nhau còn đúng một tệp trên đĩa.
        if (File.Exists(path)) return key;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Ghi ra tệp tạm rồi đổi tên: nếu tiến trình chết giữa chừng, thứ còn lại là một tệp
        // tạm bỏ đi chứ không phải một tệp mang đúng tên khóa nhưng nội dung cụt.
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temp, data, ct);
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException)
        {
            // Một request khác vừa ghi xong đúng tệp này. Nội dung giống hệt nhau nên không
            // có gì phải sửa — chỉ dọn tệp tạm.
            File.Delete(temp);
        }
        return key;
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default)
    {
        if (!IsValidKey(key)) return null;
        var path = PathFor(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!IsValidKey(key)) return Task.CompletedTask;
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Khóa = SHA-256 của nội dung + phần mở rộng đã được chuẩn hóa về một trong hai giá trị
    /// cho phép. Phần mở rộng đi kèm chỉ để tệp trên đĩa mở được bằng tay khi cần gỡ lỗi;
    /// tên hiển thị cho người dùng vẫn lấy từ cột CvFileName trong CSDL.
    /// </summary>
    public static string ComputeKey(byte[] data, string? fileName)
    {
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var ext = (fileName ?? "").EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? "docx" : "pdf";
        return $"{hash}.{ext}";
    }

    /// <summary>
    /// Khóa chỉ được gồm 64 ký tự hex + "." + pdf/docx. Kiểm lại ở đường ĐỌC chứ không chỉ
    /// tin vào đường ghi: giá trị trong CSDL có thể đã bị sửa tay, và một khóa như
    /// "../../appsettings.json" mà lọt qua đây sẽ đọc được tệp bất kỳ trên máy chủ.
    /// </summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var dot = key.LastIndexOf('.');
        if (dot != 64) return false;

        var ext = key[(dot + 1)..];
        if (ext is not ("pdf" or "docx")) return false;

        return key[..64].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    /// <summary>
    /// Chia hai cấp thư mục theo 4 ký tự đầu của hash. Đổ hàng chục nghìn tệp vào một thư
    /// mục phẳng làm mọi thao tác liệt kê trên NTFS chậm dần một cách khó giải thích.
    /// </summary>
    private string PathFor(string key) =>
        Path.Combine(_root, key[..2], key[2..4], key);
}
