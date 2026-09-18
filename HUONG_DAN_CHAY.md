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

Dự án đã có sẵn migration (thư mục `Migrations/`). Mỗi lần khởi động, app tự chạy
`Migrate()` — CSDL mới được tạo từ đầu, CSDL cũ được nâng lên bản mới nhất. Không cần chạy
`dotnet ef database update` bằng tay.

**Nâng cấp CSDL tạo từ bản cũ (trước khi có migration, dùng `EnsureCreated()`):** app tự nhận
ra CSDL loại này (có bảng `Users` nhưng chưa có `__EFMigrationsHistory`) và chạy
`Data/LegacySchemaBridge.cs` trước `Migrate()`: thêm cột `Users.SecurityStamp`,
`Applications.AiSource`, thu cột `Status` về độ dài cố định để đánh index, rồi ghi
`InitialCreate` vào lịch sử migration. Mọi bước nằm trong MỘT giao dịch — hỏng thì CSDL giữ
nguyên. Log khởi động có dòng "Đã nối CSDL tạo bằng EnsureCreated()..." khi việc này xảy ra.
Nên sao lưu CSDL trước lần khởi động đầu tiên sau khi nâng cấp.

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

---

## 🔐 Dữ liệu cá nhân (P2-3)

Mục này mô tả hệ thống thu thập gì, gửi đi đâu, giữ bao lâu và người dùng rút lại bằng cách
nào — theo tinh thần Nghị định 13/2023/NĐ-CP về bảo vệ dữ liệu cá nhân.

### Thu thập những gì

| Dữ liệu | Nguồn | Dùng để làm gì |
|---|---|---|
| Họ tên, email, điện thoại, ngày sinh, địa chỉ | Sinh viên tự nhập ở `/profile` | Nhận dạng ứng viên, để nhà tuyển dụng liên hệ |
| Học vấn, kinh nghiệm, kỹ năng, số năm kinh nghiệm | Sinh viên tự nhập | Xếp hạng và lọc ứng viên |
| Tệp CV (PDF/DOCX, tối đa 5MB) | Sinh viên tải lên | Nhà tuyển dụng đọc; phân tích độ phù hợp |
| Liên kết GitHub / LinkedIn / Portfolio | Sinh viên tự nhập | Nhà tuyển dụng tham khảo |
| Nhật ký thao tác (`AuditLog`) | Hệ thống sinh | Truy vết thao tác quản trị |

### Gửi đi đâu

Chỉ **một** dịch vụ bên ngoài nhận dữ liệu: **Google Gemini**, và chỉ khi có đủ hai điều
kiện — đã cấu hình `Gemini:ApiKey`, **và** hồ sơ đó đã ghi nhận sự đồng ý.

Ba đường gọi: Mentor chấm độ phù hợp, Mentor sinh câu hỏi phỏng vấn, và sinh viên tự kiểm
tra. Cả ba đều bị chặn khi chưa có đồng ý, kèm câu giải thích rõ ràng — hệ thống **không**
âm thầm rơi về nhánh chấm ngoại tuyến, vì một con số như vậy trông y hệt kết quả thật.

Chưa cấu hình khóa Gemini thì không có dữ liệu nào rời khỏi máy chủ; điểm số do công thức
đối chiếu Tech Stack chạy tại chỗ tính ra và được gắn nhãn **📴 ngoại tuyến**.

### Đồng ý và rút lại

Sự đồng ý được ghi nhận ngay tại lúc tải CV lên (ô tích bắt buộc), lưu thành hai cột
`AiConsentAt` và `AiConsentVersion` — có mốc thời gian và phiên bản điều khoản, chứ không
phải một chữ "đã đồng ý" trơ trọi.

Sinh viên rút lại bất cứ lúc nào ở mục **Xử lý dữ liệu cá nhân bằng AI** trong `/profile`.
Rút lại nghĩa là hệ thống **ngừng gửi CV đi từ thời điểm đó**; các kết quả đã chấm trước đó
**được giữ nguyên**, vì nhà tuyển dụng đã đọc và đã dựa vào chúng để ra quyết định — xóa đi
là làm mất dấu vết của một quyết định có thật.

### Giữ bao lâu, xóa thế nào

Phiên bản này **chưa có** cơ chế tự động xóa theo hạn. Xóa tài khoản người dùng sẽ kéo theo
hồ sơ và các bản tự kiểm tra (`ON DELETE CASCADE`); tệp CV trên blob storage phải xóa riêng
bằng `ICvStorage.DeleteAsync` — lưu ý một tệp có thể đang được nhiều đơn dùng chung do khử
trùng lặp theo nội dung. Đây là hạn chế đã biết, cần làm trước khi vận hành với dữ liệu thật.

### Chống giả mạo yêu cầu (CSRF)

Mọi endpoint POST đều kiểm tra token, trừ `/account/login` và `/account/register` — hai
đường chạy trước khi có phiên, đã được giới hạn tốc độ theo IP, và một lần đối chiếu token
hỏng ở đó sẽ chặn hẳn lối vào hệ thống. Token hết hạn (tab mở quá lâu) dẫn tới trang
`/error?reason=antiforgery` giải thích phải làm gì, không phải một mã 400 trơ trọi.
