using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QASystem.Hubs;
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
        private readonly IHubContext<ReportHub> _reportContext;
        private readonly IEmailService _emailService;

        public AdminController(QasystemContext context, UserManager<User> userManager, RoleManager<IdentityRole<int>> roleManager, IEmailService emailService, IHubContext<ReportHub> reportContext)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailService = emailService;
            _reportContext = reportContext;
        }

        // Hiển thị danh sách người dùng
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ManageUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            var userRoles = new Dictionary<int, IList<string>>();
            var roles = await _roleManager.Roles.ToListAsync();

            foreach (var user in users)
            {
                var rolesForUser = await _userManager.GetRolesAsync(user);
                userRoles[user.Id] = rolesForUser;
            }

            ViewBag.UserRoles = userRoles;
            ViewBag.AllRoles = roles;
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

        // Cập nhật role của người dùng
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserRoles(string userId, string role)
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
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["Error"] = "Failed to remove current roles: " + string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return RedirectToAction("ManageUsers");
            }

            if (!string.IsNullOrEmpty(role))
            {
                var addResult = await _userManager.AddToRoleAsync(user, role);
                if (!addResult.Succeeded)
                {
                    TempData["Error"] = "Failed to add role: " + string.Join(", ", addResult.Errors.Select(e => e.Description));
                    return RedirectToAction("ManageUsers");
                }
            }

            TempData["Success"] = $"Role for {user.UserName} updated successfully.";
            return RedirectToAction("ManageUsers");
        }

        public async Task<IActionResult> ManageReports(string status, string search, string sort, int? page)
        {
            try
            {
                var reports = _context.Reports
                    .Include(r => r.User)
                    .Include(r => r.Question)
                    .Include(r => r.Answer)
                    .AsQueryable();

                // Lọc theo trạng thái
                if (!string.IsNullOrEmpty(status))
                {
                    reports = reports.Where(r => r.Status == status);
                }

                // Tìm kiếm theo lý do
                if (!string.IsNullOrEmpty(search))
                {
                    reports = reports.Where(r => r.Reason.Contains(search));
                }

                // Sắp xếp theo thời gian
                reports = sort == "asc"
                    ? reports.OrderBy(r => r.ReportedAt)
                    : reports.OrderByDescending(r => r.ReportedAt);

                // Phân trang thủ công
                const int pageSize = 6;
                int pageNumber = page ?? 1;
                if (pageNumber < 1) pageNumber = 1;

                // Tính tổng số báo cáo
                int totalReports = await reports.CountAsync();
                int totalPages = (int)Math.Ceiling((double)totalReports / pageSize);

                // Lấy báo cáo cho trang hiện tại
                var pagedReports = await reports
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Lưu thông tin phân trang và các tham số
                ViewBag.CurrentPage = pageNumber;
                ViewBag.TotalPages = totalPages;
                ViewBag.Status = status;
                ViewBag.Search = search;
                ViewBag.Sort = sort;

                return View(pagedReports);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi khi tải báo cáo: {ex.Message}";
                return View(new List<Report>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditReport(int reportId, string status)
        {
            var report = await _context.Reports
                .Include(r => r.Question)
                .ThenInclude(q => q.User)
                .Include(r => r.Answer)
                .ThenInclude(a => a.User)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null)
            {
                TempData["Error"] = "Báo cáo không tồn tại.";
                return RedirectToAction("ManageReports");
            }

            // Kiểm tra trạng thái hợp lệ
            if (status != "Accepted" && status != "Disabled")
            {
                TempData["Error"] = "Trạng thái không hợp lệ.";
                return RedirectToAction("ManageReports");
            }

            // Gán trạng thái trực tiếp
            report.Status = status;

            try
            {
                if (report.QuestionId.HasValue)
                {
                    var question = report.Question;
                    if (question == null)
                    {
                        TempData["Error"] = "Câu hỏi không tồn tại.";
                        return RedirectToAction("ManageReports");
                    }

                    question.IsDisabled = status == "Accepted";

                    var emailSubject = status == "Accepted"
                        ? "Câu hỏi của bạn đã bị vô hiệu hóa"
                        : "Câu hỏi của bạn đã được khôi phục";
                    var emailBody = status == "Accepted"
                        ? $"Kính gửi {question.User.UserName},<br/><br/>" +
                          $"Câu hỏi '<strong>{question.Title}</strong>' của bạn đã bị vô hiệu hóa do vi phạm quy định.<br/>" +
                          "Vui lòng xem lại hướng dẫn cộng đồng. Nếu có thắc mắc, liên hệ đội hỗ trợ.<br/><br/>" +
                          "Trân trọng,<br/>QASystem Team"
                        : $"Kính gửi {question.User.UserName},<br/><br/>" +
                          $"Câu hỏi '<strong>{question.Title}</strong>' của bạn đã được khôi phục sau khi xem xét báo cáo.<br/>" +
                          "Cảm ơn bạn đã tuân thủ hướng dẫn cộng đồng.<br/><br/>" +
                          "Trân trọng,<br/>QASystem Team";

                    await _emailService.SendEmailAsync(question.User.Email, emailSubject, emailBody);
                }
                else if (report.AnswerId.HasValue)
                {
                    var answer = report.Answer;
                    if (answer == null)
                    {
                        TempData["Error"] = "Câu trả lời không tồn tại.";
                        return RedirectToAction("ManageReports");
                    }

                    answer.IsDisabled = status == "Accepted";

                    var emailSubject = status == "Accepted"
                        ? "Câu trả lời của bạn đã bị vô hiệu hóa"
                        : "Câu trả lời của bạn đã được khôi phục";
                    var emailBody = status == "Accepted"
                        ? $"Kính gửi {answer.User.UserName},<br/><br/>" +
                          $"Câu trả lời của bạn đã bị vô hiệu hóa do vi phạm quy định.<br/>" +
                          "Vui lòng xem lại hướng dẫn cộng đồng. Nếu có thắc mắc, liên hệ đội hỗ trợ.<br/><br/>" +
                          "Trân trọng,<br/>QASystem Team"
                        : $"Kính gửi {answer.User.UserName},<br/><br/>" +
                          $"Câu trả lời của bạn đã được khôi phục sau khi xem xét báo cáo.<br/>" +
                          "Cảm ơn bạn đã tuân thủ hướng dẫn cộng đồng.<br/><br/>" +
                          "Trân trọng,<br/>QASystem Team";

                    await _emailService.SendEmailAsync(answer.User.Email, emailSubject, emailBody);
                }

                // Lưu thay đổi
                await _context.SaveChangesAsync();

                // Gửi thông báo SignalR
                await _reportContext.Clients.All.SendAsync("ReportAccept");

                TempData["Success"] = $"Báo cáo đã được {(status == "Accepted" ? "chấp nhận" : "từ chối")}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi khi xử lý báo cáo: {ex.Message}";
            }

            return RedirectToAction("ManageReports");
        }
    }
}