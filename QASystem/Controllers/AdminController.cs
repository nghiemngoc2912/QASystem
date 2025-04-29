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
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly IHubContext<ReportHub> _reportContext;
        private readonly IEmailService _emailService;

        public AdminController(
            QasystemContext context,
            UserManager<User> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            IEmailService emailService,
            IHubContext<ReportHub> reportContext)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailService = emailService;
            _reportContext = reportContext;
        }

        // Hiển thị danh sách người dùng
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ManageUsers(int page = 1, string sort = "asc")
        {
            // Validate sort parameter
            sort = sort.ToLower() == "desc" ? "desc" : "asc";

            // Get users with sorting
            var usersQuery = _userManager.Users.AsQueryable();
            usersQuery = sort == "asc"
                ? usersQuery.OrderBy(u => u.UserName)
                : usersQuery.OrderByDescending(u => u.UserName);

            // Pagination
            const int pageSize = 4;
            int totalUsers = await usersQuery.CountAsync();
            int totalPages = (int)Math.Ceiling(totalUsers / (double)pageSize);
            page = Math.Max(1, Math.Min(page, totalPages));

            var users = await usersQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get roles for each user
            var userRoles = new Dictionary<int, IList<string>>();
            foreach (var user in users)
            {
                var rolesForUser = await _userManager.GetRolesAsync(user);
                userRoles[user.Id] = rolesForUser;
            }

            // Pass data to view
            ViewBag.UserRoles = userRoles;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Sort = sort;
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

            // Validate role
            if (role != "Admin" && role != "User")
            {
                TempData["Error"] = "Invalid role. Only Admin or User roles are allowed.";
                return RedirectToAction("ManageUsers");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (user.Id == currentUser.Id && role != "Admin")
            {
                TempData["Error"] = "You cannot remove your own Admin role.";
                return RedirectToAction("ManageUsers");
            }

            // Remove current roles
            var currentRoles = await _userManager.GetRolesAsync(user);
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["Error"] = "Failed to remove current roles: " + string.Join(", ", removeResult.Errors.Select(e => e.Description));
                return RedirectToAction("ManageUsers");
            }

            // Add new role
            var addResult = await _userManager.AddToRoleAsync(user, role);
            if (!addResult.Succeeded)
            {
                TempData["Error"] = "Failed to add role: " + string.Join(", ", addResult.Errors.Select(e => e.Description));
                return RedirectToAction("ManageUsers");
            }

            TempData["Success"] = $"Role for {user.UserName} updated to {role}.";
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
            if (status != "Accepted" && status != "Disabled" && status != "Pending")
            {
                TempData["Error"] = "Trạng thái không hợp lệ.";
                return RedirectToAction("ManageReports");
            }

            // Lưu trạng thái cũ để kiểm tra thay đổi
            var oldStatus = report.Status;
            report.Status = status;

            try
            {
                // Chỉ cập nhật trạng thái nội dung nếu đang chuyển từ Pending sang Accepted/Disabled
                // hoặc từ Accepted/Disabled về Pending
                if ((oldStatus == "Pending" && (status == "Accepted" || status == "Disabled")) ||
                    ((oldStatus == "Accepted" || oldStatus == "Disabled") && status == "Pending"))
                {
                    if (report.QuestionId.HasValue)
                    {
                        var question = report.Question;
                        if (question == null)
                        {
                            TempData["Error"] = "Câu hỏi không tồn tại.";
                            return RedirectToAction("ManageReports");
                        }

                        // Nếu chuyển về Pending, bật lại nội dung
                        // Nếu chuyển sang Accepted, tắt nội dung
                        question.IsDisabled = status == "Accepted";

                        // Chỉ gửi email nếu thực sự thay đổi trạng thái
                        if (oldStatus != status)
                        {
                            var emailSubject = status == "Accepted"
                                ? "Câu hỏi của bạn đã bị vô hiệu hóa"
                                : status == "Pending"
                                    ? "Câu hỏi của bạn đã được khôi phục (báo cáo đang xem xét lại)"
                                    : "Câu hỏi của bạn đã được khôi phục";
                            var emailBody = status == "Accepted"
                                ? $"Kính gửi {question.User.UserName},<br/><br/>" +
                                  $"Câu hỏi '<strong>{question.Title}</strong>' của bạn đã bị vô hiệu hóa do vi phạm quy định.<br/>" +
                                  "Vui lòng xem lại hướng dẫn cộng đồng. Nếu có thắc mắc, liên hệ đội hỗ trợ.<br/><br/>" +
                                  "Trân trọng,<br/>QASystem Team"
                                : status == "Pending"
                                    ? $"Kính gửi {question.User.UserName},<br/><br/>" +
                                      $"Câu hỏi '<strong>{question.Title}</strong>' của bạn đang được xem xét lại sau báo cáo.<br/>" +
                                      "Chúng tôi sẽ thông báo kết quả sau khi hoàn tất đánh giá.<br/><br/>" +
                                      "Trân trọng,<br/>QASystem Team"
                                    : $"Kính gửi {question.User.UserName},<br/><br/>" +
                                      $"Câu hỏi '<strong>{question.Title}</strong>' của bạn đã được khôi phục sau khi xem xét báo cáo.<br/>" +
                                      "Cảm ơn bạn đã tuân thủ hướng dẫn cộng đồng.<br/><br/>" +
                                      "Trân trọng,<br/>QASystem Team";

                            await _emailService.SendEmailAsync(question.User.Email, emailSubject, emailBody);
                        }
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

                        if (oldStatus != status)
                        {
                            var emailSubject = status == "Accepted"
                                ? "Câu trả lời của bạn đã bị vô hiệu hóa"
                                : status == "Pending"
                                    ? "Câu trả lời của bạn đang được xem xét lại"
                                    : "Câu trả lời của bạn đã được khôi phục";
                            var emailBody = status == "Accepted"
                                ? $"Kính gửi {answer.User.UserName},<br/><br/>" +
                                  $"Câu trả lời của bạn đã bị vô hiệu hóa do vi phạm quy định.<br/>" +
                                  "Vui lòng xem lại hướng dẫn cộng đồng. Nếu có thắc mắc, liên hệ đội hỗ trợ.<br/><br/>" +
                                  "Trân trọng,<br/>QASystem Team"
                                : status == "Pending"
                                    ? $"Kính gửi {answer.User.UserName},<br/><br/>" +
                                      $"Câu trả lời của bạn đang được xem xét lại sau báo cáo.<br/>" +
                                      "Chúng tôi sẽ thông báo kết quả sau khi hoàn tất đánh giá.<br/><br/>" +
                                      "Trân trọng,<br/>QASystem Team"
                                    : $"Kính gửi {answer.User.UserName},<br/><br/>" +
                                      $"Câu trả lời của bạn đã được khôi phục sau khi xem xét báo cáo.<br/>" +
                                      "Cảm ơn bạn đã tuân thủ hướng dẫn cộng đồng.<br/><br/>" +
                                      "Trân trọng,<br/>QASystem Team";

                            await _emailService.SendEmailAsync(answer.User.Email, emailSubject, emailBody);
                        }
                    }
                }

                // Lưu thay đổi
                await _context.SaveChangesAsync();

                // Gửi thông báo SignalR
                await _reportContext.Clients.All.SendAsync("ReportAccept");

                TempData["Success"] = $"Báo cáo đã được cập nhật thành: {(status == "Accepted" ? "chấp nhận" : status == "Disabled" ? "từ chối" : "đang chờ xử lý")}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi khi xử lý báo cáo: {ex.Message}";
            }

            return RedirectToAction("ManageReports");
        }

        // Hủy báo cáo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReport(int reportId)
        {
            var report = await _context.Reports.FindAsync(reportId);
            if (report == null)
            {
                TempData["Error"] = "Report not found.";
                return RedirectToAction("ManageReports");
            }

            _context.Reports.Remove(report);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Report has been canceled.";
            return RedirectToAction("ManageReports");
        }
    }
}