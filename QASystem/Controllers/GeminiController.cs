using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QASystem.Models;
using QASystem.Services;

namespace QASystem.Controllers
{
    public class GeminiController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly UserManager<User> _userManager;

        public GeminiController(GeminiService geminiService, UserManager<User> userManager)
        {
            _geminiService = geminiService;
            _userManager = userManager;
        }

        // GET: /Gemini/Index
        public IActionResult Index()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["UserId"] = userId;
            return View();
        }

        // POST: /Gemini/GenerateAjax (cho AJAX requests)
        [HttpPost]
        public async Task<IActionResult> GenerateAjax([FromBody] GenerateRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Prompt))
            {
                return BadRequest(new { success = false, error = "Yêu cầu không hợp lệ hoặc thiếu nội dung." });
            }

            try
            {
                var result = await _geminiService.GenerateContentAsync(request.Prompt);
                return Ok(new { success = true, result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = $"Lỗi khi gọi Gemini API: {ex.Message}" });
            }
        }
    }

    public class GenerateRequest
    {
        public string Prompt { get; set; }
    }
}
