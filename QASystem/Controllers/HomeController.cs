using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using System.Diagnostics;

namespace QASystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private const int PageSize = 5;

        public HomeController(QasystemContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }
        public async Task<IActionResult> Index(int page = 1, string keyword = null, string tag = null, string username = null)
        {
            List<Question> questions = new List<Question>();
            bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Moderator");

            try
            {
                var questionsQuery = _context.Questions
                    .Where(q => isAdmin || !q.IsDisabled)
                    .Include(q => q.User)
                    .Include(q => q.Tags)
                    .Include(q => q.Votes)
                    .Include(q => q.Answers)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(keyword))
                {
                    keyword = keyword.ToLower();
                    questionsQuery = questionsQuery.Where(q => q.Title.ToLower().Contains(keyword) || q.Content.ToLower().Contains(keyword));
                }

                if (!string.IsNullOrEmpty(tag))
                {
                    questionsQuery = questionsQuery.Where(q => q.Tags.Any(t => t.Name.ToLower() == tag.ToLower()));
                }

                if (!string.IsNullOrEmpty(username))
                {
                    questionsQuery = questionsQuery.Where(q => q.User.UserName.ToLower().Contains(username.ToLower()));
                }

                var totalQuestions = await questionsQuery.CountAsync();

                questions = await questionsQuery
                    .Select(q => new
                    {
                        Question = q,
                        LatestAnswerTime = q.Answers.OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault() ?? q.CreatedAt
                    })
                    .OrderByDescending(q => q.LatestAnswerTime) // Sắp xếp theo thời gian câu trả lời gần nhất
                    .Skip((page - 1) * PageSize)
                    .Take(PageSize)
                    .Select(q => q.Question)
                    .ToListAsync();

                // Lấy các câu trả lời gần đây
                var recentAnswers = await _context.Answers
                    .Include(a => a.User)
                    .Include(a => a.Question)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                // Thống kê cho từng câu hỏi
                var questionStats = questions.ToDictionary(
                    q => q.QuestionId,
                    q => new QuestionStatsViewModel
                    {
                        VoteCount = q.Votes?.Sum(v => v.VoteType) ?? 0,
                        AnswerCount = q.Answers?.Count() ?? 0,
                        LatestAnswerTime = q.Answers?.OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault()
                    }
                );

                ViewBag.RecentAnswers = recentAnswers;
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling(totalQuestions / (double)PageSize);
                ViewBag.Keyword = keyword;
                ViewBag.SelectedTag = tag;
                ViewBag.Username = username;
                ViewBag.QuestionStats = questionStats;
            }
            catch (Exception ex)
            {
                ViewBag.RecentAnswers = new List<Answer>();
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 1;
                ViewBag.Keyword = keyword;
                ViewBag.SelectedTag = tag;
                ViewBag.Username = username;
                ViewBag.QuestionStats = new Dictionary<int, QuestionStatsViewModel>();
                TempData["Error"] = "An error occurred while loading questions: " + ex.Message;
                questions = new List<Question>();
            }

            return View(questions);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuestion(string title, string content, string tags, IFormFile image)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(content))
            {
                TempData["Error"] = "Title and content are required.";
                return RedirectToAction("Index");
            }

            var user = await _userManager.GetUserAsync(User);
            var question = new Question
            {
                UserId = user.Id,
                Title = title,
                Content = content,
                CreatedAt = DateTime.Now
            };

            // Xử lý upload hình ảnh
            if (image != null && image.Length > 0)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/questions", fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                question.ImageUrl = "/images/questions" + fileName;
            }

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(tags))
            {
                var tagNames = tags.Split(',').Select(t => t.Trim()).ToList();
                foreach (var tagName in tagNames)
                {
                    var tagEntity = await _context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                    if (tagEntity == null)
                    {
                        tagEntity = new Tag { Name = tagName };
                        _context.Tags.Add(tagEntity);
                        await _context.SaveChangesAsync();
                    }
                    question.Tags.Add(tagEntity);
                }
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        public IActionResult NotFound(int? statusCode = null)
        {
            if (statusCode.HasValue)
            {
                Response.StatusCode = statusCode.Value;
            }
            return View("~/Views/Shared/NotFound.cshtml");
        }

        public IActionResult AccessDenied(int? statusCode = null)
        {
            if (statusCode.HasValue)
            {
                Response.StatusCode = statusCode.Value;
            }
            return View("~/Views/Shared/AccessDenied.cshtml");
        }
        public IActionResult Privacy()
        {
            return View();
        }
    }
}
