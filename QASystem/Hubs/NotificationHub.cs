using Microsoft.AspNetCore.SignalR;

namespace QASystem.Hubs
{
    public class NotificationHub : Hub
    {
        // Khi client kết nối, nhóm hóa theo userId để dễ gửi riêng
        public override Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                Groups.AddToGroupAsync(Context.ConnectionId, $"Notifications_{userId}");
            }
            return base.OnConnectedAsync();
        }
    }
}
