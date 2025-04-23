using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using QASystem.Services;

namespace QASystem.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    public class AdminController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly IEmailService _emailService;

        public AdminController(QasystemContext context, UserManager<User> userManager, RoleManager<IdentityRole<int>> roleManager, IEmailService emailService)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailService = emailService;
        }
        // Hiển thị danh sách người dùng
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ManageUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            var userRoles = new Dictionary<int, IList<string>>();
            var roles = await _roleManager.Roles.ToListAsync(); // Lấy danh sách role từ DB

            foreach (var user in users)
            {
                var rolesForUser = await _userManager.GetRolesAsync(user);
                userRoles[user.Id] = rolesForUser;
            }

            ViewBag.UserRoles = userRoles;
            ViewBag.AllRoles = roles; // Truyền danh sách role vào view
            return View(users);
        }

        // Khóa tài khoản
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("ManageUsers");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (user.Id == currentUser.Id)
            {
                TempData["Error"] = "You cannot lock yourself.";
                return RedirectToAction("ManageUsers");
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            if (result.Succeeded)
            {
                TempData["Success"] = $"User {user.UserName} has been locked.";
            }
            else
            {
                TempData["Error"] = "Failed to lock user: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("ManageUsers");
        }

        // Mở khóa tài khoản
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("ManageUsers");
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, null);
            if (result.Succeeded)
            {
                TempData["Success"] = $"User {user.UserName} has been unlocked.";
            }
            else
            {
                TempData["Error"] = "Failed to unlock user: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("ManageUsers");
        }

        // Cập nhật role của người dùng (chỉ chọn 1 role)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserRoles(string userId, string role) // Thay List<string> bằng string
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("ManageUsers");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (user.Id == currentUser.Id && role != "Admin")
            {
                TempData["Error"] = "You cannot remove your own Admin role.";
                return RedirectToAction("ManageUsers");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles); // Xóa tất cả role hiện tại
            if (!removeResult.Succeeded)
            {
                TempData["Error"] = "Failed to remove current roles: " + string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return RedirectToAction("ManageUsers");
            }

            if (!string.IsNullOrEmpty(role)) // Nếu có role được chọn
            {
                var addResult = await _userManager.AddToRoleAsync(user, role); // Thêm role mới
                if (!addResult.Succeeded)
                {
                    TempData["Error"] = "Failed to add role: " + string.Join(", ", addResult.Errors.Select(e => e.Description));
                    return RedirectToAction("ManageUsers");
                }
            }

            TempData["Success"] = $"Role for {user.UserName} updated successfully.";
            return RedirectToAction("ManageUsers");
        }


        public async Task<IActionResult> ManageReports()
        {
            var reports = await _context.Reports
                .Include(r => r.User)
                .Include(r => r.Question)
                .Include(r => r.Answer)
                .OrderByDescending(r => r.ReportedAt)
                .ToListAsync();

            return View(reports);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableContent(int? questionId, int? answerId)
        {
            if (!questionId.HasValue && !answerId.HasValue)
            {
                TempData["Error"] = "Must provide either questionId or answerId.";
                return RedirectToAction("ManageReports");
            }

            if (questionId.HasValue)
            {
                var question = await _context.Questions.FindAsync(questionId);
                if (question == null)
                {
                    TempData["Error"] = "Question not found.";
                    return RedirectToAction("ManageReports");
                }
                question.IsDisabled = true;
            }
            else if (answerId.HasValue)
            {
                var answer = await _context.Answers.FindAsync(answerId);
                if (answer == null)
                {
                    TempData["Error"] = "Answer not found.";
                    return RedirectToAction("ManageReports");
                }
                answer.IsDisabled = true;
            }


            if (questionId.HasValue)
            {
                var question = await _context.Questions
                    .Include(q => q.User)
                    .FirstOrDefaultAsync(q => q.QuestionId == questionId);
                if (question == null)
                {
                    TempData["Error"] = "Question not found.";
                    return RedirectToAction("ManageReports");
                }
                question.IsDisabled = true;

                // Gửi email cho chủ nhân của question
                await _emailService.SendEmailAsync(
                    question.User.Email,
                    "Your Question Has Been Disabled",
                    $"Dear {question.User.UserName},<br/><br/>" +
                    $"Your question titled '<strong>{question.Title}</strong>' has been disabled by a moderator due to a report.<br/>" +
                    "Please review our community guidelines. If you have any questions, contact our support team.<br/><br/>" +
                    "Regards,<br/>QASystem Team"
                );
            }
            else if (answerId.HasValue)
            {
                var answer = await _context.Answers
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.AnswerId == answerId);
                if (answer == null)
                {
                    TempData["Error"] = "Answer not found.";
                    return RedirectToAction("ManageReports");
                }
                answer.IsDisabled = true;

                // Gửi email cho chủ nhân của answer
                await _emailService.SendEmailAsync(
                    answer.User.Email,
                    "Your Answer Has Been Disabled",
                    $"Dear {answer.User.UserName},<br/><br/>" +
                    $"Your answer to a question has been disabled by a moderator due to a report.<br/>" +
                    "Please review our community guidelines. If you have any questions, contact our support team.<br/><br/>" +
                    "Regards,<br/>QASystem Team"
                );
            }


            await _context.SaveChangesAsync();
            TempData["Success"] = "Content has been disabled.";
            return RedirectToAction("ManageReports");
        }
    }
}
