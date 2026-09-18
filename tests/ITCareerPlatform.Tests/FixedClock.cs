using ITCareerPlatform.Models;

namespace ITCareerPlatform.Tests;

/// <summary>
/// P0-2: đồng hồ cố định cho test.
///
/// Kế thừa TimeProvider của BCL thay vì thêm package FakeTimeProvider: chỉ cần override
/// đúng một hàm, và mọi service trong dự án đã nhận TimeProvider qua constructor.
///
/// Vì sao phải có: những luật như "lịch phỏng vấn không được ở quá khứ" hay "tin hết hạn
/// hôm qua thì không nhận đơn" chỉ kiểm được khi ta CHỌN được thời điểm. Đọc giờ máy chạy
/// test thì kết quả phụ thuộc múi giờ của máy đó — đúng cái lỗi mà P0-2 đi sửa.
/// </summary>
public sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    private DateTime _utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(_utcNow, TimeSpan.Zero);

    /// <summary>Đẩy đồng hồ tới trước — để kiểm những luật tính theo "ngày mới".</summary>
    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

    /// <summary>Giờ Việt Nam tương ứng với thời điểm đang cố định (tiện cho phần dựng dữ liệu).</summary>
    public DateTime VietnamNow => Ui.ToVietnamTime(_utcNow);

    /// <summary>
    /// Một đồng hồ đứng vào 00:30 GIỜ VIỆT NAM — tức 17:30 UTC của NGÀY HÔM TRƯỚC.
    /// Đây là khung giờ làm lộ mọi lỗi lệch ngày: dùng DateTime.Today của container (chạy
    /// UTC) sẽ ra ngày hôm qua theo lịch Việt Nam.
    /// </summary>
    public static FixedClock AtVietnamMidnightHalfHour(int year, int month, int day) =>
        new(Ui.FromVietnamTime(new DateTime(year, month, day, 0, 30, 0)));
}
