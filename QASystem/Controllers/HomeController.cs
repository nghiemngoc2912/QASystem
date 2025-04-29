using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

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

        public async Task<IActionResult> Index(int page = 1, string keyword = null, string tag = null, string username = null, string dateSortOrder = "desc", string voteSortOrder = "desc")
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

                // Apply filters
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

                // Calculate total vote count and latest answer time for sorting
                var questionsWithStats = questionsQuery
                    .Select(q => new
                    {
                        Question = q,
                        LatestAnswerTime = q.Answers.OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault() ?? q.CreatedAt,
                        VoteCount = q.Votes.Sum(v => v.VoteType)
                    });

                // Apply sorting
                var sortedQuery = dateSortOrder.ToLower() == "asc"
                    ? questionsWithStats.OrderBy(q => q.LatestAnswerTime)
                    : questionsWithStats.OrderByDescending(q => q.LatestAnswerTime);

                // Apply secondary sorting by votes
                sortedQuery = voteSortOrder.ToLower() == "asc"
                    ? sortedQuery.ThenBy(q => q.VoteCount)
                    : sortedQuery.ThenByDescending(q => q.VoteCount);

                var totalQuestions = await questionsQuery.CountAsync();

                questions = await sortedQuery
                    .Skip((page - 1) * PageSize)
                    .Take(PageSize)
                    .Select(q => q.Question)
                    .ToListAsync();

                // Recent answers
                var recentAnswers = await _context.Answers
                    .Include(a => a.User)
                    .Include(a => a.Question)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                // Question stats
                var questionStats = questions.ToDictionary(
                    q => q.QuestionId,
                    q => new QuestionStatsViewModel
                    {
                        VoteCount = q.Votes?.Sum(v => v.VoteType) ?? 0,
                        AnswerCount = q.Answers?.Count() ?? 0,
                        LatestAnswerTime = q.Answers?.OrderByDescending(a => a.CreatedAt).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault()
                    }
                );

                // Pass parameters to ViewBag
                ViewBag.RecentAnswers = recentAnswers;
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling(totalQuestions / (double)PageSize);
                ViewBag.Keyword = keyword;
                ViewBag.SelectedTag = tag;
                ViewBag.Username = username;
                ViewBag.DateSortOrder = dateSortOrder;
                ViewBag.VoteSortOrder = voteSortOrder;
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
                ViewBag.DateSortOrder = dateSortOrder;
                ViewBag.VoteSortOrder = voteSortOrder;
                ViewBag.QuestionStats = new Dictionary<int, QuestionStatsViewModel>();
                TempData["Error"] = "An error occurred while loading questions: " + ex.Message;
                questions = new List<Question>();
            }

            return View(questions);
        }

        // Hàm phụ để kiểm tra content chỉ toàn HTML rỗng hoặc space
        private bool IsHtmlContentEmpty(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return true;

            // Xóa các thẻ HTML
            string text = Regex.Replace(html, "<.*?>", string.Empty);

            // Thay thế các ký tự không nhìn thấy như &nbsp; hoặc space
            text = text.Replace("&nbsp;", "").Replace("\u00A0", "").Trim();

            return string.IsNullOrWhiteSpace(text);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuestion(string title, string content, string tags, IFormFile image)
        {
            if (string.IsNullOrWhiteSpace(title) || IsHtmlContentEmpty(content))
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

            // Handle image upload
            if (image != null && image.Length > 0)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/questions", fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                question.ImageUrl = "/images/questions/" + fileName;
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