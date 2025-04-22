namespace QASystem.Models
{
    public class Notification
    {
        public int NotificationId { get; set; }
        public int? QuestionId { get; set; }
        public int? AnswerId { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? UserId { get; set; }
        public Question Question { get; set; }
        public Answer Answer { get; set; }
        public User User { get; set; }
    }
}