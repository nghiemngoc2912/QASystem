using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QASystem.Hubs;
using QASystem.Models;
using QASystem.Services;
using System.Net;
using System.Text.RegularExpressions;

namespace QASystem.Controllers
{
    public class QuestionsController : Controller
    {
        private readonly QasystemContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IHubContext<QuestionHub> _hubContext;
        private readonly IHubContext<NotificationHub> _notiContext;
        private readonly IHubContext<ReportHub> _reportContext;
        private readonly IEmailService _emailService;
        public QuestionsController(QasystemContext context, UserManager<User> userManager, IHubContext<QuestionHub> hubContext, IEmailService emailService, IHubContext<NotificationHub> notiContext, IHubContext<ReportHub> reportContext)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
            _emailService = emailService;
            _notiContext = notiContext;
            _reportContext = reportContext;
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
                vote.VoteType = vote.VoteType == voteType ? 0 : voteType;
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

        private string RemoveHtmlTags(string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        [HttpPost]
        public async Task<IActionResult> Answer(int questionId, string content, IFormFile image)
        {
            // 1. Loại bỏ tag HTML, decode & trim
            var cleanContent = RemoveHtmlTags(content ?? string.Empty);
            cleanContent = WebUtility.HtmlDecode(cleanContent).Trim();

            // 2. Nếu sau khi clean không còn gì thì trả về trang Details với thông báo lỗi
            if (string.IsNullOrWhiteSpace(cleanContent))
            {
                // Có thể dùng TempData để hiển thị flash message
                TempData["ErrorMessage"] = "Vui lòng nhập nội dung trả lời.";
                return RedirectToAction("Details", new { id = questionId });
            }

            // 3. Tạo answer bình thường (vẫn dùng 'content' gốc để lưu nếu bạn muốn giữ HTML)
            var user = await _userManager.GetUserAsync(User);
            var answer = new Answer
            {
                QuestionId = questionId,
                UserId = user.Id,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            // 4. Xử lý upload image…
            if (image != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads", image.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                    await image.CopyToAsync(stream);
                answer.ImageUrl = "/uploads/" + image.FileName;
            }

            _context.Answers.Add(answer);
            await _context.SaveChangesAsync();

            var answerId = answer.AnswerId;

            // 5. Thêm notification nếu cần…
            var question = await _context.Questions
                .Include(q => q.User)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            if (question != null && question.UserId != user.Id)
            {
                var notifyQ = new Notification
                {
                    Type = NotificationType.CommentOnQuestion,
                    QuestionId = questionId,
                    AnswerId = answerId,
                    UserId = question.UserId,
                    Message = $"{user.UserName} đã bình luận \"{cleanContent}\".",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Notifications.Add(notifyQ);
                await _context.SaveChangesAsync();

                await _notiContext.Clients.Group(question.UserId.ToString())
                    .SendAsync("NewNotification");
            }

            await _hubContext.Clients.Group($"Question_{questionId}")
                .SendAsync("ReceiveAnswer", user.UserName, content);

            return RedirectToAction("Details", new { id = questionId });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Report(int? questionId, int? answerId, string reason)
        {
            Console.WriteLine("************************************************************************************************************************************************************************************************************************");
            Console.WriteLine($"Question ID : {questionId}, AnswerID : {answerId}");
            // Kiểm tra phải cung cấp questionId hoặc answerId
            if (!questionId.HasValue && !answerId.HasValue)
            {
                TempData["Error"] = "Must provide either questionId or answerId.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId ?? 0 });
            }

            // Kiểm tra lý do báo cáo
            if (string.IsNullOrEmpty(reason))
            {
                TempData["Error"] = "Reason for reporting is required.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId });
            }

            // Lấy thông tin người dùng
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId });
            }

            // Kiểm tra báo cáo trùng lặp
            var existingReport = await _context.Reports
                .FirstOrDefaultAsync(r => r.UserId == user.Id &&
                                         (questionId.HasValue ? r.QuestionId == questionId : r.AnswerId == answerId));
            if (existingReport != null)
            {
                TempData["Error"] = "You have already reported this content.";
                return RedirectToAction("Details", new { id = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId });
            }

            // Tạo báo cáo mới
            var report = new Report
            {
                UserId = user.Id,
                QuestionId = questionId,
                AnswerId = answerId,
                Reason = reason.Trim(),
                ReportedAt = DateTime.UtcNow,
                Status = "Pending" 
            };

            // Lưu báo cáo
            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            // Gửi email thông báo
            if (questionId.HasValue)
            {
                var question = await _context.Questions
                    .Include(q => q.User)
                    .FirstOrDefaultAsync(q => q.QuestionId == questionId);
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
                var answer = await _context.Answers
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.AnswerId == answerId);
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
            await _reportContext.Clients.All.SendAsync("ReceiveReport");
            TempData["Success"] = "Your report has been submitted.";
            var redirectId = questionId ?? (await _context.Answers.FindAsync(answerId))?.QuestionId;
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

            // Kiểm tra Title và Content
            if (string.IsNullOrWhiteSpace(title) || IsHtmlContentEmpty(content))
            {
                TempData["ErrorMessage"] = "Title và Content không được để trống.";
                return RedirectToAction("Details", new { id = questionId });
            }

            question.Title = title.Trim();
            question.Content = content.Trim();

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
            TempData["SuccessMessage"] = "Cập nhật câu hỏi thành công!";
            return RedirectToAction("Details", new { id = questionId });
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

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAnswer(int answerId, int questionId)
        {
            var user = await _userManager.GetUserAsync(User);
            var answer = await _context.Answers.FindAsync(answerId);

            if (answer == null)
            {
                TempData["Error"] = "Answer not found.";
                return RedirectToAction("Details", new { id = questionId });
            }

            if (answer.UserId != user.Id)
            {
                TempData["Error"] = "You are not authorized to delete this answer.";
                return Forbid();
            }

            // Remove related votes, notifications, and reports
            var votes = _context.Votes.Where(v => v.AnswerId == answerId);
            var notifications = _context.Notifications.Where(n => n.AnswerId == answerId);
            var reports = _context.Reports.Where(r => r.AnswerId == answerId);

            _context.Votes.RemoveRange(votes);
            _context.Notifications.RemoveRange(notifications);
            _context.Reports.RemoveRange(reports);
            _context.Answers.Remove(answer);

            await _context.SaveChangesAsync();

            // Notify clients about the deletion
            await _hubContext.Clients.Group($"Question_{questionId}")
                .SendAsync("AnswerDeleted", answerId);

            TempData["Success"] = "Answer deleted successfully.";
            return RedirectToAction("Details", new { id = questionId });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteQuestion(int questionId)
        {
            var user = await _userManager.GetUserAsync(User);
            var question = await _context.Questions
                .Include(q => q.Answers)
                .Include(q => q.Votes)
                .Include(q => q.Reports)
                .Include(q => q.Notifications)
                .Include(q => q.Tags)
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            if (question == null)
            {
                TempData["Error"] = "Question not found.";
                return RedirectToAction("Index");
            }

            if (question.UserId != user.Id)
            {
                TempData["Error"] = "You are not authorized to delete this question.";
                return Forbid();
            }

            // Remove related data
            var answerIds = question.Answers.Select(a => a.AnswerId).ToList();
            var answerVotes = _context.Votes.Where(v => v.QuestionId == questionId || answerIds.Contains(v.AnswerId.Value));
            var answerNotifications = _context.Notifications.Where(n => n.QuestionId == questionId || answerIds.Contains(n.AnswerId.Value));
            var answerReports = _context.Reports.Where(r => r.QuestionId == questionId || answerIds.Contains(r.AnswerId.Value));

            _context.Votes.RemoveRange(answerVotes);
            _context.Notifications.RemoveRange(answerNotifications);
            _context.Reports.RemoveRange(answerReports);
            _context.Answers.RemoveRange(question.Answers);
            //_context.Tags.RemoveRange(question.Tags);
            question.Tags.Clear();
            await _context.SaveChangesAsync();


            _context.Questions.Remove(question);


            await _context.SaveChangesAsync();

            // Notify clients about the deletion
            await _hubContext.Clients.Group($"Question_{questionId}")
                .SendAsync("QuestionDeleted", questionId);

            TempData["Success"] = "Question deleted successfully.";
            return RedirectToAction("Index", "Home");
        }


    }
}
