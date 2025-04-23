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
    }
}
