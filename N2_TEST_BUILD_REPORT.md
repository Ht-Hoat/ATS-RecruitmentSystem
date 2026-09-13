# N2 Verification & Test/Build Report

## 1. Environment

- Repository: ATS-RecruitmentSystem (Ht-Hoat/ATS-RecruitmentSystem)
- Branch: dev-kiet
- Solution: ITCareerPlatform.slnx
- Date/Time: 2026-09-13 14:26:30 (UTC+7)
- .NET SDK: .NET 10.0

## 2. N2.C – Location Case-insensitive

### Requirement
- Tìm kiếm/lọc Job theo Location không phân biệt hoa thường qua toàn bộ luồng:
  `UI (Positions.razor) → JobService.Filter() → EF Core → Database`
- Các giá trị tìm kiếm sau phải cho kết quả tương đương:
  - `Hà Nội`
  - `hà nội`
  - `HÀ NỘI`
  - `hÀ NộI`
- Không phá các bộ lọc Job hiện có: Category, Level, Tech Stack, Salary, Employment Type, Location, Sort, Open/Deadline.

### Files inspected
- `src/ITCareerPlatform.Web/Services/Services.cs` (`JobService.Filter`)
- `src/ITCareerPlatform.Web/Components/Pages/Candidate/Positions.razor`
- `tests/ITCareerPlatform.Tests/TestDb.cs`
- `tests/ITCareerPlatform.Tests/JobServiceTests.cs`

### Changes made
- Trong `JobService.Filter()`: sử dụng `q.Where(j => j.Location.ToLower().Contains(loc))` với `loc = location.Trim().ToLower()`.
  - Trên SQL Server (production), collation mặc định là case-insensitive (`CI`), đồng thời `LOWER(...)` đảm bảo tính nhất quán trên mọi collation.
  - Trên SQLite (test environment), SQLite mặc định chỉ lowercase mã ASCII. Trong `TestDb.cs`, hàm Unicode `lower` được đăng ký qua `_conn.CreateFunction("lower", (string? s) => s?.ToLower());` đảm bảo hạ ký tự tiếng Việt có dấu chuẩn Unicode trong SQL query của EF Core SQLite.
- Bổ sung unit tests kiểm tra đa dạng biến thể hoa/thường cả ở dữ liệu lưu trữ trong DB và từ khóa truy vấn người dùng nhập.

### Tests
- `Filter_ByLocation_CaseInsensitive`: kiểm tra 4 biến thể InlineData: `"Hà Nội"`, `"hà nội"`, `"HÀ NỘI"`, `"hÀ NộI"`. Kết quả: PASS.
- `Filter_ByLocation_VaryingDatabaseAndQueryCasing`: kiểm tra dữ liệu DB viết hoa `"HÀ NỘI"` truy vấn `"hà nội"`, dữ liệu DB viết thường `"hà nội"` truy vấn `"HÀ NỘI"`, `"hÀ NộI"` truy vấn `"Hà Nội"`. Kết quả: PASS.
- `Filter_CombinedFilters_MatchesAllCriteria`: kiểm tra kết hợp Location cùng Category, Tech Stack, Level, MinSalary, EmploymentType. Kết quả: PASS.

### Result
PASS

---

## 3. N2.D – Form Validation

### Requirement
- Kiểm tra HTML markup thực tế có native HTML5 validation attributes: `required`, `minlength`, `min`, `max`, `type="email"`, `type="number"`, `data-vi-validate`.
- Không chỉ dựa vào DataAnnotations C# mà phải có thuộc tính native trên các trường bắt buộc và form.
- Kiểm tra các form: Job Create/Edit, User Create, Register, Login, Applicant Detail (HR Score/Note), Candidate Profile.

### Forms inspected
- `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobFields.razor` (dùng chung cho `JobForm.razor` và `JobEdit.razor`)
- `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobForm.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobEdit.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Users/UserForm.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Register.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Login.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Candidate/Profile.razor`
- `src/ITCareerPlatform.Web/Components/Pages/Applicants/ApplicantDetail.razor`
- `src/ITCareerPlatform.Web/Components/App.razor` (Global validation handler)

### Attributes verified
- `required`: Có trên các input bắt buộc (title, description, requirements, category, level, employmentType, location, techStack, deadline, fullName, email, password, confirmPassword, hrScore, hrNote, cv file).
- `minlength`:
  - Job Title: `minlength="5"`
  - Job Description: `minlength="20"`
  - Job Requirements: `minlength="10"`
  - Job Location: `minlength="2"`
  - Job TechStack: `minlength="2"`
  - FullName: `minlength="2"` (UserForm, Register, Profile)
  - Password: `minlength="6"` (UserForm), `minlength="8"` (Register)
  - HR Note: `minlength="10"` (ApplicantDetail)
- `min` / `max`:
  - SalaryMin/SalaryMax: `min="0" max="999"` (JobFields)
  - HR Score: `type="number" min="0" max="100"` (ApplicantDetail)
- `type="email"`: Có trên tất cả các trường email (`Login.razor`, `Register.razor`, `UserForm.razor`, `Profile.razor`).
- `type="number"`: Có trên `salaryMin`, `salaryMax`, `hrScore`.
- `data-vi-validate`: Được gắn trực tiếp trên thẻ `<form>` của:
  - `JobForm.razor`
  - `JobEdit.razor`
  - `UserForm.razor`
  - `Register.razor`
  - `Login.razor`
  - `Profile.razor` (form thông tin cá nhân và form tải CV)
  - `ApplicantDetail.razor` (form điều chỉnh điểm HR)
  Được xử lý bởi event delegation script trong `App.razor` bắt sự kiện capture `invalid`, ánh xạ sang thông báo tiếng Việt tương ứng theo từng trạng thái `valueMissing`, `tooShort`, `typeMismatch`, `rangeUnderflow`, `rangeOverflow`, và xóa lỗi qua `input`/`change`.

### Changes made
- `JobFields.razor`: Bổ sung `required`, `minlength="5"` cho title; `required`, `minlength="20"` cho description; `required`, `minlength="10"` cho requirements; `required`, `minlength="2"` cho location và techStack; `min="0" max="999"` cho salaryMin và salaryMax.
- `Login.razor`: Thêm `data-vi-validate` vào thẻ `<form>`.
- `UserForm.razor`: Thêm `minlength="2"` vào `fullName`.
- `Profile.razor`: Thêm `data-vi-validate` vào form lưu hồ sơ và form upload CV; thêm `minlength="2"` vào `fullName`; thêm `required` vào `email`.

### Result
PASS

---

## 4. N2.H – CV Templates

### Requirement
- Profile của Sinh viên IT phải có khu vực/link/section liên quan đến CV Templates.
- Người dùng có thể truy cập chức năng CV Templates từ Profile theo đúng thiết kế.
- Route `/cv-templates` phải tồn tại, được cấp quyền và hoạt động bình thường, không bị dead link.
- Không tự tạo backend phức tạp nếu chỉ yêu cầu trang/template navigation.

### Route verification
- File `src/ITCareerPlatform.Web/Components/Pages/Resources/CvTemplates.razor` định nghĩa route:
  ```razor
  @page "/cv-templates"
  @attribute [Authorize(Roles = "SinhVienIT")]
  ```
- Chứa danh sách các mẫu CV phổ biến (Simple Clean, Modern Tech, Data Focused, DevOps Pro, Fresher Friendly, Minimal) cùng 10 mẹo viết CV IT thiết thực.

### Profile integration
- Trong `src/ITCareerPlatform.Web/Components/Pages/Candidate/Profile.razor`:
  - Section 5 "5 · Mẫu CV IT & Mẹo viết CV" được hiển thị rõ ràng.
  - Có liên kết trực tiếp: `<a class="btn-sm" href="/cv-templates">Xem tất cả mẫu CV</a>`.
  - Có các thẻ preview mẫu nhanh và mẹo tối ưu hồ sơ IT.

### Changes made
- Đã xác minh liên kết và route từ `Profile.razor` (`/profile`) tới `/cv-templates`. Luồng điều hướng hoàn chỉnh và hoạt động đúng thiết kế. Không cần thay đổi ngoài phạm vi.

### Result
PASS

---

## 5. Build

### Command
```bash
dotnet restore ITCareerPlatform.slnx
dotnet build ITCareerPlatform.slnx --no-restore
```

### Result
PASS

### Errors
- 0 Error(s)
- 1 Warning(s) (Cảnh báo bảo mật gói chuyển tiếp SQLitePCLRaw.lib.e_sqlite3 2.1.11 trong test project, không ảnh hưởng tới source code)

---

## 6. Test

### Command
```bash
dotnet test ITCareerPlatform.slnx --no-build
```

### Result
PASS

### Total
- Passed: 59
- Failed: 0
- Skipped: 0
- Duration: ~21s

---

## 7. Final Assessment

| Item | Result | Notes |
|------|--------|-------|
| N2.C | PASS | Đã kiểm tra đầy đủ các biến thể hoa/thường ("Hà Nội", "hà nội", "HÀ NỘI", "hÀ NộI") và phối hợp nhiều bộ lọc. |
| N2.D | PASS | Toàn bộ các form đã có thuộc tính HTML native (`required`, `minlength`, `min`, `max`, `type="email"`, `type="number"`) và `data-vi-validate`. |
| N2.H | PASS | Route `/cv-templates` tồn tại, tích hợp trong Section 5 của `/profile`, điều hướng thông suốt. |
| Build | PASS | Build thành công với 0 lỗi trên toàn solution. |
| Test | PASS | 59/59 unit tests passed (0 failed, 0 skipped). |

## 8. Remaining Issues
- Không có lỗi nào tồn tại.

## 9. Files Changed
1. `src/ITCareerPlatform.Web/Components/Pages/Jobs/JobFields.razor` (bổ sung validation attributes native)
2. `src/ITCareerPlatform.Web/Components/Pages/Login.razor` (thêm `data-vi-validate`)
3. `src/ITCareerPlatform.Web/Components/Pages/Users/UserForm.razor` (thêm `minlength="2"` cho fullName)
4. `src/ITCareerPlatform.Web/Components/Pages/Candidate/Profile.razor` (thêm `data-vi-validate`, `minlength="2"` fullName, `required` email)
5. `tests/ITCareerPlatform.Tests/JobServiceTests.cs` (thêm test biến thể casing DB và Query)
6. `N2_TEST_BUILD_REPORT.md` (báo cáo kiểm tra và kết quả build/test)
