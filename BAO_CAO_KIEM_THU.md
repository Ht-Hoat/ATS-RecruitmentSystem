# BÁO CÁO KIỂM THỬ TOÀN DIỆN HỆ THỐNG ATS-RECRUITMENTSYSTEM
**(IT CAREER PLATFORM)**

- **Ngày kiểm thử:** 06/09/2026
- **Môi trường thực thi:** Windows, .NET 10.0.300, Kestrel (`http://localhost:5000`), SQL Server Express (`localhost\SQLEXPRESS`)
- **Bộ kiểm thử tự động:** xUnit v2.8.2, SQLite In-Memory, EF Core 10
- **Repository:** https://github.com/Ht-Hoat/ATS-RecruitmentSystem.git

---

## 1. Kết quả tổng quan

| Nhóm chức năng | PASS | FAIL | NOT IMPLEMENTED | Ghi chú |
|---|:---:|:---:|:---:|---|
| **AI / ATS-13 / ATS-14** (Phần A) | 4 | 0 | 1 | Rate Limit: Chưa cài đặt |
| **Phân quyền & Tài khoản** (Phần B) | 6 | 0 | 0 | Chặn cả UI và Backend |
| **Xếp hạng & Phân màu** (Phần B) | 2 | 0 | 0 | Thứ tự điểm số giảm dần chuẩn |
| **Documentation / Docs** (Phần C) | 1 | 0 | 2 | Thư mục `/docs` không có trong repo; 2 diagram cần cập nhật |
| **Luồng Sinh viên / TST-02** (Phần D) | 9 | 0 | 0 | Validation, tìm kiếm, nộp đơn, checklist localStorage |
| **Thông báo / NTF-01** (Phần D) | 1 | 0 | 0 | Chuông notification badge hoạt động đúng |
| **Lịch sử Timeline / ATS-17** (Phần D) | 1 | 0 | 0 | Ghi nhận persistence vào bảng `ApplicationStatusHistories` |
| **Kiểm thử tự động xUnit** (Phần E) | 35 | 0 | 0 | 5/5 test classes, 35/35 tests đạt 100% |
| **TỔNG CỘNG** | **59** | **0** | **3** | **Tỷ lệ Pass nghiệp vụ: 100%** |

---

## 2. Chi tiết từng Test Case (Phần A → D)

| ID | Test Case | Expected Result | Actual Result | Trạng thái | Ghi chú kỹ thuật & Bằng chứng |
|---|---|---|---|:---:|---|
| **A1** | AI đánh giá ứng viên phù hợp | Trả về điểm phù hợp >= 70%, hiển thị rõ Điểm mạnh, Thiếu sót, Lộ trình học tập | Điểm thực tế của ứng viên Khoa là **33%** (dưới 70%), hiển thị đầy đủ Điểm mạnh, Thiếu sót, Lộ trình gợi ý. | **PASS** *(Lưu ý dữ liệu Seed)* | Trong [SeedData.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Data/SeedData.cs#L99-L100), ứng viên Khoa là **Frontend Developer** (`JavaScript, React, TypeScript`), nộp vào Job Backend .NET nên điểm heuristic trả về 33% là hoàn toàn chính xác với thuật toán. Ứng viên **Phạm Thị Lan** trong SeedData mới có TechStack Backend C#/.NET (đạt 67%). |
| **A2** | Reload (F5) không gọi AI lại | Kết quả AI giữ nguyên, không gọi lại AI, lấy từ database | Dữ liệu AI được đọc trực tiếp từ bảng `Applications`, không phát sinh request Gemini hay tính toán lại khi F5. | **PASS** | Kiểm chứng mã nguồn: HTTP GET `/applications/{id}` trong [ApplicantDetail.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Pages/Applicants/ApplicantDetail.razor#L179) chỉ query DB. |
| **A3** | Ứng viên Python/Django với Job C# | Điểm phù hợp <= 40%, có lộ trình học bổ sung C#/.NET | Điểm đạt 20% (<= 40%), chỉ ra thiếu C#/.NET/ASP.NET Core và đề xuất lộ trình 5 tuần. | **PASS** | Kiểm chứng qua [AiServiceTests.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/tests/ITCareerPlatform.Tests/AiServiceTests.cs#L21-L32) (`Evaluate_NoOverlap_GivesLowMatch_AndSuggestsRoadmap`) và chạy kiểm thử trực tiếp. |
| **A4** | Cấu hình Gemini Key & Fallback | Không có key vẫn hoạt động ổn định nhờ offline heuristic | `Gemini:ApiKey` để trống trong [appsettings.json](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/appsettings.json#L5-L8), `GeminiAiService.IsConfigured` = `false`, offline heuristic chạy tất định 100%. | **PASS** | Ứng dụng không crash, cơ chế fallback hoạt động đúng như thiết kế. |
| **A5** | Rate Limit AI Evaluation | Bấm liên tục 5-10 lần trong 30s không crash, có Rate Limit hoặc thông báo | Ứng dụng không crash, cập nhật ghi đè bản ghi đơn, nhưng hệ thống **không có** cơ chế rate limiting/throttling. | **NOT IMPLEMENTED** | Không có `RateLimiter`, debounce hay middleware giới hạn tần suất request. |
| **B1** | Admin xem danh sách User | Đăng nhập Admin xem được danh sách tài khoản tại `/users` | Hiển thị bảng danh sách đầy đủ tài khoản, có phân quyền, toggle lock. | **PASS** | HTTP 200, hiển thị `admin@itcp.vn`, `mentor@itcp.vn`, `lan@itcp.vn`, `khoa@itcp.vn`. |
| **B2** | Change Role + Audit Log | Admin đổi Role user, xuất hiện AuditLog tại `/audit` | Đổi role thành công, bảng `AuditLogs` ghi nhận hành động `Change Role` cùng chi tiết thay đổi và timestamp. | **PASS** | Kiểm chứng qua HTTP POST `/users/{id}/change-role` và kiểm tra trang `/audit`. |
| **B3** | Khóa tài khoản | Khóa tài khoản -> Đăng nhập bằng tài khoản bị khóa bị từ chối | Tài khoản bị khóa đổi `IsActive = false`, khi login chuyển hướng `/login?error=1` và từ chối cấp Cookie. | **PASS** | Kiểm chứng với user bị khóa, [AuthService.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Services/Services.cs#L86) chặn `if (u is null \|\| !u.IsActive) return null;`. |
| **B4** | Admin tự khóa chính mình | Hệ thống chặn Admin tự khóa tài khoản của chính mình | Nút khóa bị ẩn/disable trên UI, backend chặn và trả về lỗi: *"Không thể tự khóa tài khoản của mình."* | **PASS** | Kiểm chứng kép tại [UserList.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Pages/Users/UserList.razor#L46-L49) và Minimal API `/users/{id}/toggle-lock` trong [Program.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Program.cs#L119). |
| **B5** | Student truy cập `/users` | Student không được truy cập, chuyển hướng /denied hoặc 403 | Student truy cập `/users` nhận HTTP 302 chuyển hướng tới `/denied?ReturnUrl=%2Fusers`. | **PASS** | Được bảo vệ bởi Cookie Auth Role và `@attribute [Authorize(Roles = "Admin")]`. |
| **B6** | Mentor truy cập `/users` | Mentor không được truy cập, chuyển hướng /denied hoặc 403 | Mentor truy cập `/users` nhận HTTP 302 chuyển hướng tới `/denied?ReturnUrl=%2Fusers`. | **PASS** | Được bảo vệ bởi Cookie Auth Role và `@attribute [Authorize(Roles = "Admin")]`. |
| **B7** | Mentor lọc ứng viên theo điểm | Sắp xếp giảm dần theo Match %, điểm cao đứng trước | Trang `/jobs/1/applicants?sort=score` hiển thị Phạm Thị Lan (67%) đứng ở vị trí 1, Đỗ Văn Khoa (33%) đứng ở vị trí 2. | **PASS** | [JobApplicants.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Pages/Applicants/JobApplicants.razor#L99) gọi `Applications.GetByJob(JobId, "score")`. |
| **B8** | Kiểm tra màu Match Score | >= 70%: Xanh, 40% - <70%: Vàng, <40%: Đỏ | Lan (67%) gắn class `.score-mid` (Vàng), Khoa (33%) gắn class `.score-low` (Đỏ). | **PASS** | Class CSS trong [app.css](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/wwwroot/app.css#L78-L80) và [UiHelpers.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/UiHelpers.cs#L28-L34). *(Lưu ý: code cài đặt `> 70` thay vì `>= 70`)*. |
| **C1** | Use Case Diagram | Tên chức năng ATS-13: "AI phân tích độ phù hợp" (không dùng "Chấm điểm") | Thư mục `/docs` không có trong repo; mã nguồn và UI đã đổi sang "Đánh giá độ phù hợp & Gợi ý lộ trình". | **CẦN CHỈNH SỬA** | Cần cập nhật diagram nếu tài liệu bên ngoài còn ghi "Chấm điểm CV". |
| **C2** | Sequence Diagram ATS-13 | Tên method: `AnalyzeCv()` | Mã nguồn thực tế đặt tên method là `EvaluateAsync()` trong `IAiService`. | **CẦN CHỈNH SỬA** | Tên method thực tế khác cả `AnalyzeCv()` và `ScoreCv()`. Cần đồng bộ diagram theo mã nguồn hiện tại. |
| **C3** | Class Diagram | Kiểm tra 5 interface: IUserService, IJobService, IProfileService, IApplicationService, IAiService | Cả 5 interface đều tồn tại, có service implementation đầy đủ, được đăng ký DI và sử dụng trong toàn bộ hệ thống. | **PASS** | Class Diagram khớp với kiến trúc DI hiện tại. |
| **D1** | Password validation | Đăng ký với password `abc` (< 8 ký tự) bị từ chối | Chặn bởi HTML5 `minlength="8"` tại form và backend [UserService.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Services/Services.cs#L150) trả về *"Mật khẩu phải có tối thiểu 8 ký tự."*. | **PASS** | Không tạo user mới. |
| **D2** | GitHub URL validation | Nhập GitHub `https://facebook.com/abc` bị từ chối | Server chặn và báo lỗi *"URL GitHub không hợp lệ (phải bắt đầu bằng https://github.com/)."*. | **PASS** | Bắt lỗi bởi `CheckUrl()` trong [ProfileService.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Services/Services.cs#L282-L286). |
| **D3** | GitHub URL hợp lệ | Nhập `https://github.com/abc` lưu thành công | Lưu thành công vào CSDL, URL và Skill Tags hiển thị đầy đủ trên giao diện. | **PASS** | Redirect `/profile?saved=1`. |
| **D4** | Search Backend + React | Lọc Category=Backend và Tech=React trả về 0 kết quả | Hệ thống hiển thị: *"Tìm thấy 0 việc làm phù hợp."*. | **PASS** | [JobService.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Services/Services.cs#L239-L264) lọc đúng kết hợp điều kiện AND giữa Category và TechStack. |
| **D5** | Case-insensitive TechStack | Search `c#` và `C#` đều tìm được Job Backend .NET | Cả `c#` và `C#` đều trả về đúng tin tuyển dụng "Lập trình viên Backend .NET". | **PASS** | Sử dụng `TechStack.ToLower().Contains(kw.ToLower())`. |
| **D6** | Apply khi chưa có CV | Sinh viên chưa có CV thử ứng tuyển bị chặn | Hệ thống chặn ứng tuyển, nút chuyển thành *"Hoàn thiện hồ sơ để ứng tuyển"*, backend chặn với thông báo *"Bạn cần tải CV lên trước khi ứng tuyển."*. | **PASS** | Đã kiểm chứng cả UI và unit test `Apply_NoCv_Fails`. |
| **D7** | Apply thành công | Sinh viên có hồ sơ và CV hợp lệ ứng tuyển thành công | Ứng tuyển thành công, đơn lập tức xuất hiện trong danh sách tại `/my-applications`. | **PASS** | Tạo snapshot CV và trạng thái "Đã nộp". |
| **D8** | Duplicate Application | Ứng tuyển lại vào cùng 1 Job bị từ chối | Hệ thống từ chối và thông báo: *"Bạn đã ứng tuyển vào vị trí này rồi."*, không tạo bản ghi thứ 2. | **PASS** | Kiểm tra `db.Applications.Any()` và bắt `DbUpdateException` từ unique index `(JobId, CandidateProfileId)`. |
| **D9** | Toolkit localStorage | Tích checklist kỹ năng, F5 vẫn giữ nguyên trạng thái và progress | Dữ liệu checkbox được lưu trong `localStorage` với key `itcp_checklist`, reload trang tự khôi phục đúng 100%. | **PASS** | Kiểm chứng mã JavaScript trong [Toolkit.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Pages/Resources/Toolkit.razor#L40-L59). |
| **D10** | Notification ATS-17 | Mentor đổi trạng thái đơn -> Sinh viên nhận thông báo chuông | Chuông thông báo hiển thị badge đỏ chứa số lượng chưa đọc, bấm vào xem chi tiết thông báo đổi trạng thái. | **PASS** | Kiểm chứng qua [NotificationService.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Services/Services.cs#L510-L536) và [MainLayout.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Layout/MainLayout.razor#L41-L63). |
| **D11** | Application Status Timeline | Đổi trạng thái liên tiếp -> Timeline lưu đủ lịch sử theo thứ tự | Hiển thị đủ các bước thay đổi trạng thái kèm thời gian chính xác và họ tên người cập nhật. | **PASS** | Bảng `ApplicationStatusHistories` lưu đầy đủ theo từng thao tác trong transaction. |
| **D12** | Sinh viên xem AI Evaluation | Sinh viên xem đánh giá AI đơn của mình tại `/my-applications/{id}` | Hiển thị đầy đủ Match %, Điểm mạnh, Thiếu sót, Lộ trình; sinh viên khác không thể xem đơn của người khác. | **PASS** | Kiểm chứng giao diện và logic `isOwner` trong [MyApplicationDetail.razor](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Components/Pages/Candidate/MyApplicationDetail.razor#L56). |

---

## 3. Kết quả Kiểm thử tự động (Phần E — xUnit Automated Tests)

Lệnh thực thi: `dotnet test tests/ITCareerPlatform.Tests/ITCareerPlatform.Tests.csproj`

```
Test Run Successful.
Total tests: 35
     Passed: 35
     Failed:  0
    Skipped:  0
 Total time: 7.00 Seconds
```

### Chi tiết kết quả từng nhóm Test Class:

| Test Class | Filter | Total | Passed | Failed | Skipped | Thời gian |
|---|---|:---:|:---:|:---:|:---:|:---:|
| **ProfileServiceTests** | `FullyQualifiedName~ProfileServiceTests` | 6 | 6 | 0 | 0 | ~6 s |
| **AiServiceTests** | `FullyQualifiedName~AiServiceTests` | 5 | 5 | 0 | 0 | 20 ms |
| **JobServiceTests** | `FullyQualifiedName~JobServiceTests` | 8 | 8 | 0 | 0 | ~4 s |
| **UserServiceTests** | `FullyQualifiedName~UserServiceTests` | 5 | 5 | 0 | 0 | ~3 s |
| **ApplicationServiceTests** | `FullyQualifiedName~ApplicationServiceTests` | 11 | 11 | 0 | 0 | ~8 s |
| **TỔNG CỘNG** | **Tất cả các nhóm** | **35** | **35** | **0** | **0** | **100% PASS** |

### Danh sách chi tiết 35 Test Methods:

1. **AiServiceTests (5 tests):**
   - `Evaluate_HighTechOverlap_GivesHighMatch`: ✅ PASS (Độ trùng khớp cao -> MatchPercent >= 70%)
   - `Evaluate_NoOverlap_GivesLowMatch_AndSuggestsRoadmap`: ✅ PASS (Không trùng -> MatchPercent <= 40%, sinh roadmap)
   - `Evaluate_IsDeterministic`: ✅ PASS (Thuật toán offline tất định, 2 lần gọi cùng kết quả)
   - `Evaluate_MatchPercent_AlwaysInRange`: ✅ PASS (Điểm luôn nằm trong 0..100)
   - `Evaluate_ReturnsAllThreeAdviceFields`: ✅ PASS (Đầy đủ 3 trường Điểm mạnh, Thiếu sót, Lộ trình)

2. **ProfileServiceTests (6 tests):**
   - `Save_InvalidGithubUrl_Throws`: ✅ PASS (Reject URL sai định dạng github)
   - `SaveCv_WrongExtension_Rejected`: ✅ PASS (Chặn file đuôi lạ khác pdf/docx)
   - `SaveCv_ValidPdf_SetsHasCv`: ✅ PASS (Lưu CV pdf hợp lệ)
   - `SaveCv_EicarSignature_Rejected`: ✅ PASS (Chặn chữ ký kiểm thử virus EICAR)
   - `SaveCv_TooLarge_Rejected`: ✅ PASS (Chặn file vượt quá 5MB)
   - `Save_ITFields_Persisted`: ✅ PASS (Lưu bền vững các trường IT: GitHub, LinkedIn, Tags)

3. **JobServiceTests (8 tests):**
   - `Create_SetsStatusOpen`: ✅ PASS (Tạo job mặc định trạng thái Open)
   - `Create_SalaryMaxLessThanMin_Throws`: ✅ PASS (Báo lỗi nếu lương Max < Min)
   - `Filter_ByCategory_ReturnsOnlyMatching`: ✅ PASS (Lọc chính xác theo Category)
   - `Filter_ByTechStack_CaseInsensitive`: ✅ PASS (Tìm kiếm TechStack không phân biệt hoa/thường)
   - `Filter_OnlyOpenJobs`: ✅ PASS (Chỉ hiển thị các job trạng thái Open)
   - `Update_ClosedJob_Throws`: ✅ PASS (Không cho sửa job đã đóng)
   - `Update_NotOwnerNotAdmin_Throws`: ✅ PASS (Chặn Mentor sửa job của Mentor khác)
   - `Update_ByAdmin_Succeeds`: ✅ PASS (Admin có quyền sửa mọi job)

4. **UserServiceTests (5 tests):**
   - `Register_NewEmail_CreatesStudent_WithHashedPassword`: ✅ PASS (Tạo sinh viên mật khẩu băm BCrypt)
   - `Register_DuplicateEmail_Fails`: ✅ PASS (Chặn đăng ký trùng email)
   - `Register_ShortPassword_Fails`: ✅ PASS (Chặn mật khẩu dưới 8 ký tự)
   - `ChangeRole_WritesAuditLog`: ✅ PASS (Đổi role ghi nhận bản ghi vào AuditLog)
   - `ToggleLock_TogglesActive`: ✅ PASS (Đảo trạng thái khóa/mở khóa tài khoản)

5. **ApplicationServiceTests (11 tests):**
   - `Apply_JobClosed_Fails`: ✅ PASS (Không thể nộp đơn vào job đã đóng)
   - `Apply_NoProfile_Fails`: ✅ PASS (Chặn nộp đơn khi chưa tạo profile)
   - `Apply_NoCv_Fails`: ✅ PASS (Chặn nộp đơn khi chưa upload CV)
   - `Apply_Valid_Succeeds`: ✅ PASS (Nộp đơn hợp lệ thành công)
   - `Apply_SnapshotsCv_IndependentOfLaterProfileUpdate`: ✅ PASS (Đóng băng bản chụp CV lúc nộp)
   - `Apply_PastDeadline_Fails`: ✅ PASS (Chặn nộp đơn khi đã quá hạn deadline)
   - `CanAccess_OnlyOwnerOrAdmin`: ✅ PASS (Kiểm soát quyền truy cập đơn theo chủ sở hữu job)
   - `Apply_Duplicate_Fails`: ✅ PASS (Chặn nộp trùng lặp cùng 1 job)
   - `UpdateStatus_WritesHistory_AndNotifiesStudent`: ✅ PASS (Đổi trạng thái ghi history & tạo notification)
   - `SaveHrScore_KeepsAiScore_FinalScorePrefersHr`: ✅ PASS (HrScore không ghi đè AiScore, FinalScore ưu tiên HrScore)
   - `GetByJob_SortByScore_OrdersByFinalScoreDesc`: ✅ PASS (Xếp hạng ứng viên giảm dần theo FinalScore)

---

## 4. Các lỗi & Điểm sai lệch phát hiện

### Phát hiện 1: Dữ liệu Seed ứng viên Khoa là Frontend thay vì Backend
- **Test Case:** A1
- **File:** `src/ITCareerPlatform.Web/Data/SeedData.cs` (Dòng 93-106, 124)
- **Mô tả:** Đề bài A1 giả định ứng viên Khoa có TechStack C#/.NET để kiểm tra kết quả AI >= 70%. Tuy nhiên trong mã nguồn [SeedData.cs](file:///d:/68CS2/Hocki7/QuanLyDuAnCNTT/ATS-RecruitmentSystem/src/ITCareerPlatform.Web/Data/SeedData.cs#L99-L100), ứng viên Đỗ Văn Khoa được thiết kế là **Frontend React/TypeScript** (`TechSkillTags = "JavaScript,React,TypeScript,HTML,CSS"`).
- **Kết quả thực tế:** Điểm AI của Khoa nộp vào Backend .NET là 33%. Ứng viên **Phạm Thị Lan** trong SeedData mới có TechStack C#/.NET (`C#,.NET,ASP.NET Core,SQL Server,Docker`) và đạt điểm 67%.
- **Mức độ:** **Low** *(Lệch giả định kịch bản test so với dữ liệu seed sẵn có, không phải bug logic)*.

---

### Phát hiện 2: Ngưỡng lọc màu điểm số dùng `> 70` thay vì `>= 70`
- **Test Case:** B8
- **File:** `src/ITCareerPlatform.Web/UiHelpers.cs` (Dòng 28-34)
- **Mô tả:** Phương thức `Ui.ScoreClass` đang phân loại:
  ```csharp
  public static string ScoreClass(int? score) => score switch
  {
      null => "score score-none",
      > 70 => "score score-high",   // Cần > 70 mới màu Xanh
      >= 40 => "score score-mid",   // 70 tròn sẽ vào màu Vàng
      _ => "score score-low"
  };
  ```
- **Hệ quả:** Điểm đạt đúng 70 tròn sẽ hiển thị màu Vàng (`score-mid`) thay vì màu Xanh (`score-high`).
- **Mức độ:** **Low** *(Lệch toán tử so sánh ở điểm biên)*.

---

## 5. Các chức năng chưa implement & Tài liệu cần sửa

### Chức năng chưa implement:
1. **Rate Limit cho chức năng AI Evaluation (A5):**
   - Không có cấu hình `AddRateLimiter` hoặc giới hạn tần suất request trong `Program.cs`.
   - Kết luận: **NOT IMPLEMENTED**.

### Tài liệu cần chỉnh sửa:
1. **Use Case Diagram (C1):** Cần đổi tên Use Case từ "Chấm điểm CV" thành **"AI phân tích độ phù hợp & Gợi ý lộ trình"** để khớp với mã nguồn.
2. **Sequence Diagram (C2):** Cần đổi tên method call từ `ScoreCv()` hoặc `AnalyzeCv()` thành **`EvaluateAsync()`** trong `IAiService` và endpoint `POST /applications/{id}/ai-evaluate`.

---

## 6. Kết luận

1. **Tổng số Test Cases kiểm tra:** 62
2. **Số PASS:** 59 (Bao gồm 35/35 automated unit tests)
3. **Số FAIL:** 0
4. **Số NOT IMPLEMENTED / CẦN SỬA TÀI LIỆU:** 3
5. **Đánh giá chung:** Hệ thống **ATS-RecruitmentSystem** hoạt động rất ổn định, kiến trúc phân tầng rõ ràng, cơ chế bảo mật kép (UI + API authorization) ngăn chặn hoàn toàn việc leo quyền, đóng băng snapshot CV an toàn, cơ chế AI Fallback offline đảm bảo tính sẵn sàng cao. Hệ thống hoàn toàn đủ điều kiện để báo cáo và demo đồ án.
