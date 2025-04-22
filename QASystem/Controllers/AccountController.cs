using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;

namespace QASystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly QasystemContext _context;

        public AccountController(UserManager<User> userManager, SignInManager<User> signInManager, QasystemContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
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
    }
}