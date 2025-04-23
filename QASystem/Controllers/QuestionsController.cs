using Microsoft.AspNetCore.Mvc;

namespace QASystem.Controllers
{
    public class QuestionsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
