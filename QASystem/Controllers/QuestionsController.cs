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


            //Add notification
            var question = await _context.Questions
                .Include(q => q.User)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            // Notify chủ question (nếu muốn)
            if (question != null && question.UserId != user.Id)
            {
                var notifyQ = new Notification
                {
                    Type = NotificationType.CommentOnQuestion,
                    QuestionId = questionId,
                    UserId = question.UserId,
                    Message = $"{user.UserName} đã bình luận {content} .",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Notifications.Add(notifyQ);
            }

            await _context.SaveChangesAsync();

            // Gửi thông báo SignalR
            await _hubContext.Clients.Group($"Question_{questionId}")
                .SendAsync("ReceiveAnswer", user.UserName, content);

            return RedirectToAction("Details", new { id = questionId });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Report(int? questionId, int? answerId, string reason)
        {
            if (!questionId.HasValue && !answerId.HasValue)
            {
                TempData["Error"] = "Must provide either questionId or answerId.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId ?? 0 });
            }

            if (string.IsNullOrEmpty(reason))
            {
                TempData["Error"] = "Reason for reporting is required.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId)).QuestionId });
            }

            var user = await _userManager.GetUserAsync(User);
            var existingReport = await _context.Reports
                .FirstOrDefaultAsync(r => r.UserId == user.Id &&
                                         (questionId.HasValue ? r.QuestionId == questionId : r.AnswerId == answerId));

            if (existingReport != null)
            {
                TempData["Error"] = "You have already reported this content.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId)).QuestionId });
            }

            var report = new Report
            {
                UserId = user.Id,
                QuestionId = questionId,
                AnswerId = answerId,
                Reason = reason,
                ReportedAt = DateTime.Now
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();


            if (questionId.HasValue)
            {
                var question = await _context.Questions.Include(q => q.User).FirstOrDefaultAsync(q => q.QuestionId == questionId);
                if (question != null)
                {
                    await _emailService.SendEmailAsync(
                        question.User.Email,
                        "Your Question Has Been Reported",
                        $"Dear {question.User.UserName},<br/><br/>" +
                        $"Your question titled '<strong>{question.Title}</strong>' has been reported for the following reason: {reason}.<br/>" +
                        "Please review our community guidelines. If you have any questions, contact our support team.<br/><br/>" +
                        "Regards,<br/>QASystem Team"
                    );
                }
            }
            else if (answerId.HasValue)
            {
                var answer = await _context.Answers.Include(a => a.User).FirstOrDefaultAsync(a => a.AnswerId == answerId);
                if (answer != null)
                {
                    await _emailService.SendEmailAsync(
                        answer.User.Email,
                        "Your Answer Has Been Reported",
                        $"Dear {answer.User.UserName},<br/><br/>" +
                        $"Your answer to a question has been reported for the following reason: {reason}.<br/>" +
                        "Please review our community guidelines. If you have any questions, contact our support team.<br/><br/>" +
                        "Regards,<br/>QASystem Team"
                    );
                }
            }



            TempData["Success"] = "Your report has been submitted.";
            var redirectId = questionId ?? (await _context.Answers.FindAsync(answerId)).QuestionId;
            return RedirectToAction("Details", new { id = redirectId });
        }

        // Action chỉnh sửa câu hỏi
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuestion(int questionId, string title, string content, IFormFile image)
        {
            var user = await _userManager.GetUserAsync(User);
            var question = await _context.Questions.FindAsync(questionId);

            if (question == null)
            {
                return NotFound();
            }

            if (question.UserId != user.Id)
            {
                return Forbid(); // Chỉ người tạo mới được chỉnh sửa
            }

            question.Title = title;
            question.Content = content;

            if (image != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/questions", image.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                question.ImageUrl = "/images/questions/" + image.FileName;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Details", new { id = questionId });
        }

        // Action chỉnh sửa câu trả lời
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAnswer(int answerId, int questionId, string content, IFormFile image)
        {
            var user = await _userManager.GetUserAsync(User);
            var answer = await _context.Answers.FindAsync(answerId);

            if (answer == null)
            {
                return NotFound();
            }

            if (answer.UserId != user.Id)
            {
                return Forbid(); // Chỉ người tạo mới được chỉnh sửa
            }

            answer.Content = content;

            if (image != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/comments", image.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }
                answer.ImageUrl = "/images/comments/" + image.FileName;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("Details", new { id = questionId });
        }


    }
}
