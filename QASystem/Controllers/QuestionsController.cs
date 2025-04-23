using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QASystem.Hubs;
using QASystem.Models;
using QASystem.Services;

namespace QASystem.Controllers
{
    public class QuestionsController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IHubContext<QuestionHub> _hubContext;
        private readonly IEmailService _emailService;
        public QuestionsController(QasystemContext context, UserManager<User> userManager, IHubContext<QuestionHub> hubContext, IEmailService emailService)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
            _emailService = emailService;
        }
        public async Task<IActionResult> Details(int id, int page = 1)
        {
            var question = await _context.Questions
                .Include(q => q.User)
                .Include(q => q.Tags)
                .Include(q => q.Answers).ThenInclude(a => a.User)
                .FirstOrDefaultAsync(q => q.QuestionId == id);

            if (question == null)
            {
                return NotFound();
            }

            const int pageSize = 5;
            var totalAnswers = question.Answers.Count;
            var answers = question.Answers
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            question.Answers = answers;
            ViewBag.QuestionVoteCount = await _context.Votes
                .Where(v => v.QuestionId == id)
                .SumAsync(v => v.VoteType);
            ViewBag.AnswerVoteCounts = await _context.Votes
                .Where(v => v.AnswerId.HasValue && answers.Select(a => a.AnswerId).Contains(v.AnswerId.Value))
                .GroupBy(v => v.AnswerId)
                .ToDictionaryAsync(g => g.Key.Value, g => g.Sum(v => v.VoteType));
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalAnswers / (double)pageSize);
            ViewBag.TotalAnswers = totalAnswers;

            return View(question);
        }

        [HttpPost]
        public async Task<IActionResult> Vote(int? questionId, int? answerId, int voteType)
        {
            var user = await _userManager.GetUserAsync(User);
            var vote = await _context.Votes
                .FirstOrDefaultAsync(v => v.UserId == user.Id && (questionId.HasValue ? v.QuestionId == questionId : v.AnswerId == answerId));

            if (vote == null)
            {
                vote = new Vote
                {
                    UserId = user.Id,
                    QuestionId = questionId,
                    AnswerId = answerId,
                    VoteType = voteType
                };
                _context.Votes.Add(vote);
            }
            else
            {
                vote.VoteType = voteType;
            }

            await _context.SaveChangesAsync();

            // Tính lại vote count
            var voteCount = await _context.Votes
                .Where(v => (questionId.HasValue ? v.QuestionId == questionId : v.AnswerId == answerId))
                .SumAsync(v => v.VoteType);

            // Gửi thông báo SignalR
            await _hubContext.Clients.Group($"Question_{(questionId ?? (await _context.Answers.FindAsync(answerId)).QuestionId)}")
                .SendAsync("ReceiveVoteUpdate", answerId, voteCount);

            return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId)).QuestionId });
        }

        [HttpPost]
        public async Task<IActionResult> Answer(int questionId, string content, IFormFile image)
        {
            var user = await _userManager.GetUserAsync(User);
            var answer = new Answer
            {
                QuestionId = questionId,
                UserId = user.Id,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            if (image != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads", image.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                answer.ImageUrl = "/uploads/" + image.FileName;
            }

            _context.Answers.Add(answer);
            await _context.SaveChangesAsync();

            // Gửi thông báo SignalR
            await _hubContext.Clients.Group($"Question_{questionId}")
                .SendAsync("ReceiveAnswer", user.UserName, content);

            return RedirectToAction("Details", new { id = questionId });
        }
    }
}
