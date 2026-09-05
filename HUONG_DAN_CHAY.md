# 🚀 Hướng dẫn cài đặt & chạy — IT Career Platform

Công nghệ: **.NET 10 (Blazor Server) + EF Core + SQL Server + Docker**

---

## 0. Yêu cầu môi trường (máy Windows)

| Thành phần | Ghi chú |
|-----------|---------|
| **.NET 10 SDK** | Tải tại https://dotnet.microsoft.com/download/dotnet/10.0 — kiểm tra: `dotnet --version` ≥ 10.0 |
| **SQL Server** | Bản Express (miễn phí) hoặc chạy bằng Docker (xem Cách B) |
| **Visual Studio 2022** (17.14+) hoặc **VS Code** | Tùy chọn — có thể build bằng dòng lệnh |
| **Docker Desktop** | Chỉ cần nếu chạy theo Cách B (khuyến nghị) |

> ⚠️ Máy phải có **Internet** ở lần build đầu để `dotnet restore` tải các gói NuGet
> (EF Core, BCrypt, xUnit...). Đây là bước bắt buộc — mã nguồn không kèm sẵn gói.

---

## Cách A — Chạy trực tiếp (SQL Server cài sẵn trên máy)

```bash
# 1. Vào thư mục web
cd ITCareerPlatform/src/ITCareerPlatform.Web

# 2. Kiểm tra chuỗi kết nối trong appsettings.json (mặc định dùng localhost)
#    "Server=localhost;Database=ITCareerPlatform;Trusted_Connection=True;TrustServerCertificate=True"

# 3. Tải gói + chạy
dotnet restore
dotnet run
```

Mở trình duyệt: **https://localhost:5001** (hoặc cổng in ra ở console).
Lần chạy đầu, ứng dụng **tự tạo CSDL + seed dữ liệu mẫu** (bảng, 3 vai trò, tài khoản demo, tin việc IT).

### Dùng SQL Server bằng Docker (nếu chưa cài SQL)
```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_password123" \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```
Rồi đổi chuỗi kết nối trong `appsettings.json` thành:
```
Server=localhost,1433;Database=ITCareerPlatform;User Id=sa;Password=Your_password123;TrustServerCertificate=True
```

---

## Cách B — Chạy toàn bộ bằng Docker Compose (KHUYẾN NGHỊ) ⭐

Chạy **App + SQL Server** chỉ bằng 1 lệnh, không cần cài .NET/SQL trên máy:

```bash
cd ITCareerPlatform
docker compose up --build
```

Mở: **http://localhost:8080**

Dừng: `Ctrl + C` rồi `docker compose down` (thêm `-v` nếu muốn xóa dữ liệu).

---

## 🔑 Tài khoản demo (mật khẩu: `123456`)

| Vai trò | Email | Dùng để thử |
|--------|-------|-------------|
| **Admin** | admin@itcp.vn | Quản lý tài khoản, phân quyền, nhật ký |
| **Mentor / HR IT** | mentor@itcp.vn | Tạo tin IT, chấm AI, duyệt điểm, dashboard |
| **Sinh viên IT** | lan@itcp.vn | Hồ sơ IT, tải CV, lọc & ứng tuyển việc |
| Sinh viên IT | khoa@itcp.vn | (đã có hồ sơ + CV mẫu) |

Có thể tự **Đăng ký** tài khoản Sinh viên IT mới tại `/register`.

---

## 🤖 Bật AI Gemini (miễn phí) — tùy chọn

Không bật vẫn chạy được: hệ thống **tự chấm điểm bằng thuật toán offline** (khớp Tech Stack).
Để dùng AI thật:

1. Lấy API key miễn phí: https://aistudio.google.com/apikey
2. Trong `src/ITCareerPlatform.Web/`, đổi tên `appsettings.Development.json.example`
   → `appsettings.Development.json`, dán key vào `Gemini:ApiKey`.
   (Tệp này đã được `.gitignore` nên **key không bị đẩy lên Git**.)
3. Với Docker: đặt biến môi trường `GEMINI_API_KEY` rồi `docker compose up`.

---

## 🗃️ Về Migration EF Core

Mặc định app tự tạo CSDL bằng `EnsureCreated()` khi chưa có migration.
Nếu muốn dùng migration chuẩn (khuyến nghị cho môi trường thật):

```bash
cd src/ITCareerPlatform.Web
dotnet tool install --global dotnet-ef      # nếu chưa có
dotnet ef migrations add InitialCreate
dotnet ef database update
```
Khi đã có migration, app sẽ tự `Migrate()` thay vì `EnsureCreated()` (xem `Program.cs`).
