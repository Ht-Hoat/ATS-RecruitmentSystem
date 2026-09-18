using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ITCareerPlatform.Migrations
{
    /// <summary>
    /// Migration DỮ LIỆU, không đổi schema.
    ///
    /// Bộ câu hỏi phỏng vấn trước đây do MENTOR sinh trên trang chi tiết ứng viên, với phần
    /// "hint" là ghi chú chấm điểm cho người phỏng vấn ("nghe xem họ nói được chi tiết..."). Nay
    /// bộ câu hỏi thuộc về SINH VIÊN (luyện phỏng vấn, hint là gợi ý trả lời) và dùng lại đúng
    /// các cột AiQuestions*. Không xóa dữ liệu cũ thì trên một CSDL đã chạy bản trước:
    ///   - sinh viên đọc được ghi chú chấm điểm vốn viết cho nhà tuyển dụng;
    ///   - AiQuestionsAt cũ khóa nút "Tạo bộ câu hỏi" 24 giờ dù sinh viên chưa từng bấm.
    /// Xóa MỘT lần khi nâng cấp; từ đây mọi bộ câu hỏi đều do chính sinh viên tạo.
    /// </summary>
    public partial class ClearMentorGeneratedInterviewQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [Applications] SET [AiQuestions] = NULL, [AiQuestionsSource] = NULL, [AiQuestionsAt] = NULL " +
                "WHERE [AiQuestions] IS NOT NULL OR [AiQuestionsAt] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục được: dữ liệu đã xóa, và bộ câu hỏi cũ vốn không dành cho sinh viên.
        }
    }
}
