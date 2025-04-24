using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using QASystem.ViewModels;

namespace QASystem.ViewComponents
{
    public class NotificationViewComponent : ViewComponent
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;

        public NotificationViewComponent(QasystemContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (!User.Identity.IsAuthenticated)
                return View(new NotificationViewModel { UnreadCount = 0, Notifications = Enumerable.Empty<Notification>() });

            var user = await _userManager.GetUserAsync(HttpContext.User);

            // Lấy notifications
            var notifications = await _context.Notifications
                .Where(n => n.UserId == user.Id)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .ToListAsync();

            // Tính số chưa đọc
            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == user.Id && !n.IsRead);

            var model = new NotificationViewModel
            {
                UnreadCount = unreadCount,
                Notifications = notifications
            };

            return View(model);
        }

    }
}
