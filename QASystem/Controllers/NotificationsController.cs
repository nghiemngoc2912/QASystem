using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;

namespace QASystem.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;

        public NotificationsController(QasystemContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
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

        [HttpGet]
        public async Task<IActionResult> MarkAsRead(int id, int questionId)
        {
            var notif = await _context.Notifications.FindAsync(id);
            var user = await _userManager.GetUserAsync(User);

            if (notif != null && notif.UserId == user.Id)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(
                actionName: "Details",
                controllerName: "Questions",
                routeValues: new { id = questionId, page = 1 }
            );
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
            }

            return RedirectToAction("Index");
        }

    }

}
