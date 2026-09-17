# 🚀 Hướng dẫn cài đặt & chạy — IT Career Platform

Công nghệ: **.NET 10 (Blazor Server) + EF Core + SQL Server + Docker**

---

## 0. Yêu cầu môi trường (máy Windows)

| Thành phần                                                  | Ghi chú                                                                                              |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| **.NET 10 SDK**                                         | Tải tại https://dotnet.microsoft.com/download/dotnet/10.0 — kiểm tra:`dotnet --version` ≥ 10.0 |
| **SQL Server**                                          | Bản Express (miễn phí) hoặc chạy bằng Docker (xem Cách B)                                      |
| **Visual Studio 2022** (17.14+) hoặc **VS Code** | Tùy chọn — có thể build bằng dòng lệnh                                                        |
| **Docker Desktop**                                      | Chỉ cần nếu chạy theo Cách B (khuyến nghị)                                                     |

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

> ⚠️ Dữ liệu mẫu **chỉ được nạp khi `ASPNETCORE_ENVIRONMENT=Development`**, và gợi ý tài khoản
> trên trang đăng nhập cũng chỉ hiện ở môi trường đó. Chạy ở môi trường khác, cơ sở dữ liệu
> sẽ trống và bạn phải tự tạo tài khoản Admin đầu tiên — đây là chủ ý, để một bản triển khai
> thật không tự sinh sẵn tài khoản quản trị dùng mật khẩu `123456`.

| Vai trò                 | Email          | Dùng để thử                                 |
| ------------------------ | -------------- | ----------------------------------------------- |
| **Admin**          | admin@itcp.vn  | Quản lý tài khoản, phân quyền, nhật ký  |
| **Mentor / HR IT** | mentor@itcp.vn | Tạo tin IT, chấm AI, duyệt điểm, dashboard |
| **Sinh viên IT**  | lan@itcp.vn    | Hồ sơ IT, tải CV, lọc & ứng tuyển việc   |
| Sinh viên IT            | khoa@itcp.vn   | (đã có hồ sơ + CV mẫu)                    |

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

---

## 🕖 Múi giờ và dữ liệu cũ (P0-2)

Từ bản này, **mọi mốc thời gian được lưu ở UTC** và chỉ quy đổi sang giờ Việt Nam khi
hiển thị (một chỗ duy nhất: `Ui.ToVietnamTime` trong `UiHelpers.cs`). Hạn nộp (`Deadline`)
là một **ngày trên tờ lịch Việt Nam**, không phải ngày của máy chủ.

Vì sao đổi: container chạy UTC trong khi người dùng ở UTC+7, nên bản cũ hiển thị mọi mốc
sớm hơn thực tế 7 giờ, chấp nhận lịch phỏng vấn đã trôi qua tới 7 tiếng, và trong khung
00:00–07:00 giờ Việt Nam thì tin đã hết hạn vẫn hiện ra và vẫn nhận được đơn.

**Dữ liệu cũ:** các bản ghi tạo trước bản này được lưu bằng giờ của container, sau thay đổi
này sẽ được đọc như thể chúng là UTC — tức lệch đi đúng bằng độ lệch múi giờ của máy chủ cũ.
Không có migration dịch chuyển dữ liệu (một lần dịch sai là hỏng vĩnh viễn, và không có
cách nào biết chắc mốc cũ được ghi ở múi giờ nào).

- **Môi trường Development:** xóa và tạo lại CSDL là xong — `SeedData` sẽ gieo lại đúng quy ước.
  ```bash
  cd src/ITCareerPlatform.Web
  dotnet ef database drop -f
  dotnet run
  ```
- **Môi trường thật:** nếu đã có dữ liệu cần giữ, hãy tự chạy một câu `UPDATE` một lần cho
  từng cột thời gian (trừ đi độ lệch của máy chủ cũ) **trước khi** triển khai bản mới.

`Dockerfile` có `ENV TZ=Asia/Ho_Chi_Minh`, nhưng đó chỉ để **dòng log** của container đọc
được theo giờ Việt Nam. Tính đúng đắn của nghiệp vụ không phụ thuộc biến này.

---

## 🔑 Quên mật khẩu — Admin đặt lại (P0-3)

Hệ thống chưa gửi được email, nên đường khôi phục hiện tại đi qua Admin:

1. Admin vào `/users`, bấm **Đặt lại MK** ở dòng tài khoản cần khôi phục.
2. Trang hiện **một lần** mật khẩu tạm do hệ thống sinh (14 ký tự, nguồn ngẫu nhiên mật mã
   học). Admin đưa trực tiếp cho người dùng — mật khẩu này **không** được ghi vào
   `AuditLog` hay log máy chủ.
3. Người dùng đăng nhập bằng mật khẩu tạm và bị giữ ở `/change-password` cho tới khi đổi
   xong; mọi trang khác đều chuyển hướng về đó.

Đặt lại mật khẩu làm **mọi phiên đang mở** của tài khoản đó hết hiệu lực ngay ở request kế tiếp.
Admin không tự đặt lại mật khẩu của chính mình được — hãy dùng chức năng **Đổi mật khẩu**.

---

## 📧 Email và tệp lịch .ics (P1-3)

Hệ thống gửi email cho **ba sự kiện**: mời phỏng vấn (kèm tệp `.ics` để ứng viên thêm vào
lịch), trúng tuyển, và từ chối. Email từ chối mang theo phần **Phản hồi gửi ứng viên** nếu
nhà tuyển dụng có nhập — nhưng **không bao giờ** mang điểm số, lý do chốt điểm hay ghi chú
nội bộ của Mentor.

**Không cấu hình gì thì hệ thống vẫn chạy bình thường**: email được xếp vào bảng
`EmailOutbox` và ghi một dòng cảnh báo trong log (chỉ người nhận + tiêu đề, không có nội
dung). Bản ghi **nằm lại trong hàng đợi**, nên cấu hình SMTP sau đó vẫn gửi được.

### Cấu hình SMTP

Trong `appsettings.Development.json` (đã được `.gitignore`), hoặc bằng biến môi trường:

```json
{
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "User": "tai-khoan@gmail.com",
    "Password": "mat-khau-ung-dung-16-ky-tu",
    "From": "tai-khoan@gmail.com"
  }
}
```

Với Docker, dùng dấu gạch dưới kép: `Smtp__Host`, `Smtp__Port`, `Smtp__User`,
`Smtp__Password`, `Smtp__From`, `Smtp__EnableSsl`.

> Gmail yêu cầu **App Password** (bật 2FA rồi tạo ở myaccount.google.com/apppasswords),
> không dùng được mật khẩu đăng nhập thường.

### Cách gửi hoạt động

Email **không** được gửi ngay trong request đổi trạng thái: SMTP chậm và hay lỗi, gửi đồng
bộ thì một lần timeout làm nhà tuyển dụng thấy "đổi trạng thái thất bại" dù trạng thái đã
đổi. Thay vào đó, bản ghi email được thêm **trong cùng transaction** với việc đổi trạng
thái, rồi một tiến trình nền quét hàng đợi **30 giây một lần**, gửi tối đa 20 bản ghi mỗi
lượt và bỏ hẳn bản ghi đã thử quá 5 lần (cột `LastError` ghi lý do lần cuối).

### Mật khẩu tạm (P0-3) đi theo đường nào

Có SMTP thì mật khẩu tạm gửi **thẳng vào hộp thư** người dùng và không xuất hiện trên màn
hình. Chưa cấu hình SMTP — hoặc lần gửi vừa rồi hỏng — thì mới lùi về cách cũ là hiện một
lần cho Admin đọc lại cho người dùng. Email này gửi trực tiếp chứ không qua `EmailOutbox`,
vì xếp hàng nghĩa là mật khẩu nằm ở dạng rõ trong một cột CSDL cho tới khi gửi xong.
