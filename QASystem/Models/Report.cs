// Models/Report.cs
namespace QASystem.Models
{
    public class Report
    {
        public int ReportId { get; set; }
        public int UserId { get; set; }       // Người báo cáo
        public int? QuestionId { get; set; }     // Câu hỏi bị báo cáo (nullable)
        public int? AnswerId { get; set; }       // Câu trả lời bị báo cáo (nullable)
        public string Reason { get; set; }       // Lý do báo cáo
        public DateTime ReportedAt { get; set; } // Thời gian báo cáo

        public virtual User User { get; set; }
        public virtual Question Question { get; set; }
        public virtual Answer Answer { get; set; }
    }
}