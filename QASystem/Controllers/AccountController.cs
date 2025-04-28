using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using QASystem.Services;

namespace QASystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly QasystemContext _context;
        private readonly IEmailService _emailService;

        public AccountController(UserManager<User> userManager, SignInManager<User> signInManager, QasystemContext context, IEmailService emailService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _emailService = emailService;
        }

        // Class để định nghĩa hoạt động
        public class Activity
        {
            public string Type { get; set; }
            public string Content { get; set; }
            public DateTime? CreatedAt { get; set; }
            public int Id { get; set; }
        }

        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy các bài viết (questions) của người dùng
            var userQuestions = await _context.Questions
                .Where(q => q.UserId == user.Id && !q.IsDisabled)
                .OrderByDescending(q => q.CreatedAt)
                .Take(5)
                .ToListAsync();

            // Lấy các hoạt động gần đây
            var recentActivities = new List<Activity>();

            // Questions
            var recentQuestions = await _context.Questions
                .Where(q => q.UserId == user.Id && !q.IsDisabled)
                .Select(q => new Activity
                {
                    Type = "Question",
                    Content = q.Title,
                    CreatedAt = q.CreatedAt,
                    Id = q.QuestionId
                })
                .ToListAsync();

            // Answers
            var recentAnswers = await _context.Answers
                .Where(a => a.UserId == user.Id && !a.IsDisabled)
                .Select(a => new Activity
                {
                    Type = "Answer",
                    Content = a.Content.Substring(0, Math.Min(50, a.Content.Length)) + "...",
                    CreatedAt = a.CreatedAt,
                    Id = a.QuestionId
                })
                .ToListAsync();

            // Votes
            var votes = await _context.Votes
                .Where(v => v.UserId == user.Id)
                .ToListAsync(); // Lấy dữ liệu trước

            var recentVotes = votes.Select(v =>
            {
                string content;
                int id;
                if (v.QuestionId.HasValue)
                {
                    var question = _context.Questions.FirstOrDefault(q => q.QuestionId == v.QuestionId);
                    content = $"Voted on question: {question?.Title ?? "Unknown question"}";
                    id = v.QuestionId.Value;
                }
                else
                {
                    var answer = _context.Answers.FirstOrDefault(a => a.AnswerId == v.AnswerId);
                    content = $"Voted on answer: {(answer != null ? answer.Content.Substring(0, Math.Min(50, answer.Content.Length)) + "..." : "Unknown answer")}";
                    id = answer?.QuestionId ?? 0;
                }

                return new Activity
                {
                    Type = "Vote",
                    Content = content,
                    CreatedAt = DateTime.Now, // Tạm dùng DateTime.Now, thay bằng v.CreatedAt nếu có
                    Id = id
                };
            }).ToList();
            // Lấy danh sách tài liệu (Materials) của người dùng
            var userMaterials = await _context.Materials
                .Where(m => m.UserId == user.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(4) 
                .ToListAsync();
            // Tính tổng số tài liệu và tổng lượt tải
            var totalMaterials = await _context.Materials
                .Where(m => m.UserId == user.Id)
                .CountAsync();

            // Gộp và sắp xếp hoạt động
            recentActivities.AddRange(recentQuestions);
            recentActivities.AddRange(recentAnswers);
            recentActivities.AddRange(recentVotes);
            ViewBag.RecentActivities = recentActivities
                .OrderByDescending(a => a.CreatedAt)
                .Take(5)
                .ToList();

            ViewBag.UserQuestions = userQuestions;

            return View(user);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string email, IFormFile avatar)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "Email is required.";
                return View("Profile", user);
            }

            if (user.Email != email)
            {
                var setEmailResult = await _userManager.SetEmailAsync(user, email);
                if (!setEmailResult.Succeeded)
                {
                    TempData["Error"] = "Failed to update email.";
                    return View("Profile", user);
                }
            }

            if (avatar != null && avatar.Length > 0)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(avatar.FileName);
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images", fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await avatar.CopyToAsync(stream);
                }
                user.AvatarUrl = "/images/" + fileName;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    TempData["Error"] = "Failed to update avatar.";
                    return View("Profile", user);
                }
            }

            TempData["Success"] = "Profile updated successfully.";
            return RedirectToAction("Profile");
        }

		[Authorize]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
			{
				return NotFound();
			}

			if (newPassword != confirmPassword)
			{
				TempData["Error"] = "New password and confirmation do not match.";
				return RedirectToAction("Profile");
			}

			var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
			if (result.Succeeded)
			{
				// Đăng nhập lại người dùng để cập nhật phiên đăng nhập
				await _signInManager.RefreshSignInAsync(user);
				TempData["Success"] = "Password changed successfully.";
				return RedirectToAction("Profile");
			}

			TempData["Error"] = "Failed to change password: " + string.Join(", ", result.Errors.Select(e => e.Description));
			return RedirectToAction("Profile");
		}

		// GET: Đăng ký
		[HttpGet]
        public IActionResult Register()
        {
            return View(new User());
        }

        // POST: Xử lý đăng ký
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(User user, string password, string confirmPassword)
        {
            if (password != confirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                return View(user);
            }

            // Kiểm tra định dạng Email
            if (!IsValidEmail(user.Email))
            {
                ModelState.AddModelError("Email", "Invalid email format.");
                return View(user);
            }

            // Kiểm tra Email đã tồn tại chưa
            var existingUser = await _userManager.FindByEmailAsync(user.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "Email is already taken.");
                return View(user);
            }

            if (ModelState.IsValid)
            {
                user.CreatedAt = DateTime.Now;
                user.Reputation = 0;
                var result = await _userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return RedirectToAction("Index", "Home");
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(user);
        }

        // Hàm kiểm tra định dạng Email
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }


        // GET: Đăng nhập
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: Đăng nhập
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string userName, string password, bool rememberMe = false)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Username and password are required.");
                return View();
            }

            var result = await _signInManager.PasswordSignInAsync(userName, password, rememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }
            ModelState.AddModelError("", "Invalid username or password.");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError("", "Email is required.");
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["Success"] = "If the email exists, a password reset link has been sent.";
                return RedirectToAction("Login");
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.Action("ResetPassword", "Account", new { userId = user.Id, token = token }, protocol: Request.Scheme);

            try
            {
                await _emailService.SendEmailAsync(
                    email,
                    "Password Reset Request",
                    $"Please reset your password by clicking <a href='{callbackUrl}'>here</a>."
                );

                TempData["Success"] = "If the email exists, a password reset link has been sent.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Failed to send email: {ex.Message}");
                return View();
            }
        }

        [HttpGet]
        public IActionResult ResetPassword(string userId, string token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return BadRequest("User ID and token are required.");
            }
            ViewBag.UserId = userId;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string userId, string token, string password, string confirmPassword)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return BadRequest("User ID and token are required.");
            }

            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword))
            {
                ModelState.AddModelError("", "Password and confirmation are required.");
                ViewBag.UserId = userId;
                ViewBag.Token = token;
                return View();
            }

            if (password != confirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                ViewBag.UserId = userId;
                ViewBag.Token = token;
                return View();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Success"] = "Password has been reset.";
                return RedirectToAction("Login");
            }

            var result = await _userManager.ResetPasswordAsync(user, token, password);
            if (result.Succeeded)
            {
                TempData["Success"] = "Password has been reset.";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
            ViewBag.UserId = userId;
            ViewBag.Token = token;
            return View();
        }
        public async Task<IActionResult> PublicProfile(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString()); 
            if (user == null)
            {
                return NotFound();
            }

            var userMaterials = await _context.Materials
                .Where(m => m.UserId == user.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(4)
                .ToListAsync();

            var userQuestions = _context.Questions
                .Where(q => q.UserId == id)
                .ToList();

            ViewBag.UserMaterials = userMaterials;
            ViewBag.UserQuestions = userQuestions;
            ViewBag.TotalMaterials = userMaterials.Count;
            ViewBag.TotalDownloads = userMaterials.Sum(m => m.Downloads);

            return View(user);
        }
    }
}