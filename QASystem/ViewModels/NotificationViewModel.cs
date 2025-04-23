using QASystem.Models;

namespace QASystem.ViewModels
{
    public class NotificationViewModel
    {
        public int UnreadCount { get; set; }
        public IEnumerable<Notification> Notifications { get; set; }
    }
}
