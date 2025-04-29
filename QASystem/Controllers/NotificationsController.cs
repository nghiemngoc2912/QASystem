using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QASystem.Hubs;
using QASystem.Models;
using QASystem.ViewModels;

namespace QASystem.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IHubContext<NotificationHub> _notiContext;

        public NotificationsController(QasystemContext context, UserManager<User> userManager, IHubContext<NotificationHub> notiContext)
        {
            _context = context;
            _userManager = userManager;
            _notiContext = notiContext;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var list = await _context.Notifications
                .Where(n => n.UserId == user.Id)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> BulkAction(List<int> selectedIds, string actionType)
        {
            if (selectedIds == null || !selectedIds.Any())
                return RedirectToAction("Index");

            var user = await _userManager.GetUserAsync(User);

            var notifications = await _context.Notifications
                .Where(n => selectedIds.Contains(n.NotificationId) && n.UserId == user.Id)
                .ToListAsync();

            if (actionType == "read")
            {
                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                }
            }
            else if (actionType == "unread")
            {
                foreach (var notification in notifications)
                {
                    notification.IsRead = false;
                }
            }
            await _context.SaveChangesAsync();

            await _notiContext.Clients.Group(user.Id.ToString()).SendAsync("NewNotification");

            return RedirectToAction("Index");
        }



        [HttpGet]
        public async Task<IActionResult> MarkAsRead(int id, int questionId)
        {
            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null) return NotFound();
            var user = await _userManager.GetUserAsync(User);
            if (notif.UserId != user.Id) return Forbid();
            notif.IsRead = true;
            await _context.SaveChangesAsync();

            var question = await _context.Questions
                .Include(q => q.User)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            await _notiContext.Clients.Group(question.UserId.ToString()).SendAsync("NewNotification");

            var newerCount = await _context.Notifications
                .Where(n => n.UserId == user.Id && n.QuestionId == questionId && n.CreatedAt > notif.CreatedAt)
                .CountAsync();

            var pageSize = 5;
            var pages = (newerCount / pageSize) + 1;
            return RedirectToAction(
                "Details",
                "Questions",
                new { id = questionId, page = pages }
            );

        }

        [HttpGet]
        public async Task<IActionResult> MarkAsReadIndex(int id, int questionId)
        {
            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null) return NotFound();
            var user = await _userManager.GetUserAsync(User);
            if (notif.UserId != user.Id) return Forbid();
            notif.IsRead = true;
            await _context.SaveChangesAsync();

            var question = await _context.Questions
                .Include(q => q.User)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            await _notiContext.Clients.Group(question.UserId.ToString()).SendAsync("NewNotification");

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> MarkAsNotRead(int id, int questionId)
        {
            var notif = await _context.Notifications.FindAsync(id);
            var user = await _userManager.GetUserAsync(User);

            if (notif != null && notif.UserId == user.Id)
            {
                notif.IsRead = false;
                await _context.SaveChangesAsync();

                var question = await _context.Questions
                .Include(q => q.User)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

                await _notiContext.Clients.Group(question.UserId.ToString()).SendAsync("NewNotification");
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Partial()
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                {
                    var emptyVm = new NotificationViewModel
                    {
                        UnreadCount = 0,
                        Notifications = Enumerable.Empty<Notification>()
                    };
                    return PartialView("~/Views/Shared/Components/Notification/Default.cshtml", emptyVm);
                }

                var user = await _userManager.GetUserAsync(HttpContext.User);
                if (user == null)
                    throw new Exception("UserManager.GetUserAsync returned null");

                var notifications = await _context.Notifications
                    .Where(n => n.UserId == user.Id)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                var unreadCount = await _context.Notifications
                    .CountAsync(n => n.UserId == user.Id && !n.IsRead);

                var model = new NotificationViewModel
                {
                    UnreadCount = unreadCount,
                    Notifications = notifications
                };

                return PartialView("~/Views/Shared/Components/Notification/Default.cshtml", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationsController.Partial] Exception: {ex}");
                return StatusCode(500);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(List<int> selectedIds)
        {
            if (selectedIds == null || !selectedIds.Any())
                return RedirectToAction("Index");

            var user = await _userManager.GetUserAsync(User);

            var notifications = await _context.Notifications
                .Where(n => selectedIds.Contains(n.NotificationId) && n.UserId == user.Id)
                .ToListAsync();

            _context.Notifications.RemoveRange(notifications);
            await _context.SaveChangesAsync();

            await _notiContext.Clients.Group(user.Id.ToString()).SendAsync("NewNotification");

            return RedirectToAction("Index");
        }


    }

}
