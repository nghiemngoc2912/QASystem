using System.Diagnostics;
using Firebase.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;

namespace QASystem.Controllers
{
    public class MaterialsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        private readonly QasystemContext _context;
        private readonly FirebaseStorage _firebaseStorage;

        public MaterialsController(QasystemContext context, FirebaseStorage firebaseStorage)
        {
            _context = context;
            _firebaseStorage = firebaseStorage;
        }

        [HttpGet]
        public IActionResult AddMaterial()
        {
            return View(new Material());
        }

        [HttpPost]
        public async Task<IActionResult> AddMaterial(Material material, IFormFile file)
        {
            // Debug: Ghi log dữ liệu nhận được từ form
            Debug.WriteLine("POST AddMaterial called.");
            Debug.WriteLine($"Received Title: {material.Title}");
            Debug.WriteLine($"Received Description: {material.Description}");
            Debug.WriteLine($"Received File: {(file != null ? file.FileName : "No file")}");

            // Kiểm tra dữ liệu đầu vào
            if (!ModelState.IsValid)
            {
                // Ghi log chi tiết lỗi trong ModelState
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { x.Key, x.Value.Errors })
                    .ToList();

                foreach (var error in errors)
                {
                    foreach (var errorMessage in error.Errors)
                    {
                        Debug.WriteLine($"ModelState Error - Key: {error.Key}, Error: {errorMessage.ErrorMessage}");
                    }
                }

                ModelState.AddModelError("", "Invalid material data. Please check the form for errors.");
                return View(material);
            }

            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("file", "Please select a file to upload.");
                return View(material);
            }

            // Kiểm tra kích thước file (ví dụ: tối đa 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                ModelState.AddModelError("file", "File size exceeds 10MB limit.");
                return View(material);
            }

            try
            {
                // Tạo tên file duy nhất
                var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                var stream = file.OpenReadStream();


                // Debug: Ghi log thông tin file
                Console.WriteLine($"Uploading file: {fileName}, Size: {file.Length} bytes");

                // Upload file lên Firebase Storage
                var uploadTask = await _firebaseStorage
                    .Child("materials")
                    .Child(fileName)
                    .PutAsync(stream);

                // Debug: Ghi log kết quả upload
                Console.WriteLine($"Upload completed. Metadata: {uploadTask}");

                // Lấy URL tải xuống từ Firebase Storage
                var downloadUrl = await _firebaseStorage
                    .Child("materials")
                    .Child(fileName)
                    .GetDownloadUrlAsync();

                // Debug: Ghi log URL
                Console.WriteLine($"Download URL: {downloadUrl}");

                // Lưu thông tin tài liệu vào cơ sở dữ liệu
                material.FileLink = downloadUrl;
                material.CreatedAt = DateTime.Now;
                material.Downloads = 0;
                material.UserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new Exception("User not authenticated."));

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                return RedirectToAction("Profile", "Account");
            }
            catch (Exception ex)
            {
                // Ghi log chi tiết lỗi
                Console.WriteLine($"Error uploading file: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                ModelState.AddModelError("", $"Error uploading file: {ex.Message}");
                return View(material);
            }
        }
        public IActionResult Manage()
        {
            return View();
        }
        public IActionResult Details(int id)
        {
            var material = _context.Materials.Find(id);
            if (material == null)
            {
                return NotFound();
            }
            return View(material);
        }
    }
}
