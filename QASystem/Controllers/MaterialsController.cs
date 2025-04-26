using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QASystem.Models;
using System;
using System.Threading.Tasks;
using QASystem.ViewModels;

namespace QASystem.Controllers
{
    public class MaterialsController : Controller
    {
        private readonly QasystemContext _context;

        public MaterialsController(QasystemContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult AddMaterial()
        {
            return View(new Material());
        }

        [HttpPost]
        public async Task<IActionResult> AddMaterial(Material material)
        {
            Console.WriteLine($"POST AddMaterial called. Title: {material.Title}, Description: {material.Description}, FileLink: {material.FileLink}");

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { x.Key, x.Value.Errors })
                    .ToList();
                foreach (var error in errors)
                {
                    foreach (var errorMessage in error.Errors)
                    {
                        Console.WriteLine($"ModelState Error - Key: {error.Key}, Error: {errorMessage.ErrorMessage}");
                    }
                }
                return View(material);
            }

            if (string.IsNullOrEmpty(material.FileLink))
            {
                ModelState.AddModelError("FileLink", "Please upload a file.");
                return View(material);
            }

            try
            {
                material.CreatedAt = DateTime.UtcNow;
                material.Downloads = 0;
                material.UserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? throw new UnauthorizedAccessException("User not authenticated."));

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Material saved successfully. ID: {material.MaterialId}");
                return RedirectToAction("Profile", "Account");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Auth Error: {ex.Message}");
                ModelState.AddModelError("", "You must be logged in to upload materials.");
                return View(material);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected Error: {ex.Message}\nStack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                ModelState.AddModelError("", "An unexpected error occurred while saving the material.");
                return View(material);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Manage(string searchTerm = "", string sortOrder = "desc", int pageNumber = 1)
        {
            var viewModel = new MaterialListViewModel
            {
                SearchTerm = searchTerm,
                SortOrder = sortOrder,
                PageNumber = pageNumber
            };

            var query = _context.Materials.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(m => m.Title.ToLower().Contains(searchTerm) ||
                                        m.Description.ToLower().Contains(searchTerm));
            }

            if (sortOrder == "asc")
            {
                query = query.OrderBy(m => m.CreatedAt);
            }
            else
            {
                query = query.OrderByDescending(m => m.CreatedAt);
            }

            viewModel.TotalItems = await query.CountAsync();

            var materials = await query
                .Skip((pageNumber - 1) * viewModel.PageSize)
                .Take(viewModel.PageSize)
                .ToListAsync();

            viewModel.Materials = materials;

            return View(viewModel);
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
        [HttpGet]
        public IActionResult Delete(int id)
        {
            var material = _context.Materials.Find(id);
            if (material == null)
            {
                return NotFound();
            }
            return View(material);
        }

        [HttpPost, ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var material = await _context.Materials.FindAsync(id);
            if (material != null)
            {
                _context.Materials.Remove(material);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Manage));
        }
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            var material = await _context.Materials.FindAsync(id);
            if (material == null)
            {
                return NotFound();
            }

            material.Downloads++;
            await _context.SaveChangesAsync();

            return Redirect(material.FileLink);
        }
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var material = _context.Materials.Find(id);
            if (material == null)
            {
                return NotFound();
            }
            return View(material);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, Material material)
        {
            if (id != material.MaterialId)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
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
                return View(material);
            }

            if (string.IsNullOrEmpty(material.FileLink))
            {
                ModelState.AddModelError("FileLink", "Please upload a file.");
                return View(material);
            }

            try
            {
                var existingMaterial = await _context.Materials.FindAsync(id);
                if (existingMaterial == null)
                {
                    return NotFound();
                }

                existingMaterial.Title = material.Title;
                existingMaterial.Description = material.Description;
                existingMaterial.FileLink = material.FileLink;

                _context.Update(existingMaterial);
                await _context.SaveChangesAsync();

                Debug.WriteLine($"Material updated successfully. ID: {material.MaterialId}");
                return RedirectToAction(nameof(Manage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update Error: {ex.Message}\nStack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                ModelState.AddModelError("", "An error occurred while updating the material.");
                return View(material);
            }
        }
        public IActionResult PublicMaterials(int userId)
        {
            var materials = _context.Materials
                .Where(m => m.UserId == userId)
                .ToList();

            ViewBag.UserId = userId;
            return View(materials);
        }
    }
}