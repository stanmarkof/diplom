using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using diplom.Data;
using diplom.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace diplom.Controllers
{
    [Authorize(Roles = "Lecturer,Admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class FileUploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _context;
        private readonly ILogger<FileUploadController> _logger;

        public FileUploadController(
            IWebHostEnvironment env,
            AppDbContext context,
            ILogger<FileUploadController> logger)
        {
            _env = env;
            _context = context;
            _logger = logger;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(10485760)]
        public async Task<IActionResult> UploadFile(IFormFile file, [FromForm] int materialId)
        {
            try
            {
                Console.WriteLine("=== UPLOAD FILE START ===");
                Console.WriteLine($"materialId: {materialId}");
                Console.WriteLine($"file is null: {file == null}");

                if (file != null)
                {
                    Console.WriteLine($"FileName: {file.FileName}");
                    Console.WriteLine($"Length: {file.Length}");
                    Console.WriteLine($"ContentType: {file.ContentType}");
                }

                if (file == null || file.Length == 0)
                {
                    Console.WriteLine("File is null or empty");
                    return BadRequest(new { error = "Файл не выбран" });
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    Console.WriteLine($"File too large: {file.Length}");
                    return BadRequest(new { error = "Файл слишком большой. Максимальный размер: 10 MB" });
                }

                var extension = Path.GetExtension(file.FileName).ToLower();
                Console.WriteLine($"Extension: {extension}");

                var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
                if (!allowedExtensions.Contains(extension))
                {
                    Console.WriteLine($"Extension not allowed: {extension}");
                    return BadRequest(new { error = "Недопустимый тип файла. Разрешены: PDF, DOCX, TXT" });
                }

                // Определяем папку uploads
                var webRootPath = _env.WebRootPath ?? _env.ContentRootPath;
                var uploadsFolder = Path.Combine(webRootPath, "uploads");
                Console.WriteLine($"Uploads folder: {uploadsFolder}");

                if (!Directory.Exists(uploadsFolder))
                {
                    Console.WriteLine($"Creating uploads folder");
                    Directory.CreateDirectory(uploadsFolder);
                }

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                Console.WriteLine($"File path: {filePath}");

                // Сохраняем файл
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                    Console.WriteLine($"File saved, stream length: {stream.Length}");
                }

                Console.WriteLine("File saved successfully");

                // Находим материал в БД
                var material = await _context.Materials.FindAsync(materialId);
                if (material == null)
                {
                    Console.WriteLine($"Material not found: {materialId}");
                    // Удаляем сохранённый файл
                    if (System.IO.File.Exists(filePath))
                        System.IO.File.Delete(filePath);
                    return NotFound(new { error = "Материал не найден" });
                }

                Console.WriteLine($"Material found: {material.Title}");

                // Обновляем путь к файлу
                material.FilePath = $"/uploads/{uniqueFileName}";
                material.FileType = extension;

                await _context.SaveChangesAsync();
                Console.WriteLine($"✅ Database updated: FilePath = {material.FilePath}");

                Console.WriteLine("=== UPLOAD FILE SUCCESS ===");
                return Ok(new
                {
                    success = true,
                    message = "Файл успешно загружен",
                    fileUrl = material.FilePath
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CRITICAL ERROR in UploadFile: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                _logger.LogError(ex, "Error uploading file");
                return StatusCode(500, new { error = $"Ошибка при загрузке файла: {ex.Message}" });
            }
        }

        [HttpDelete("delete/{materialId}")]
        public async Task<IActionResult> DeleteFile(int materialId)
        {
            try
            {
                Console.WriteLine($"=== DELETE FILE: materialId={materialId} ===");

                var material = await _context.Materials.FindAsync(materialId);
                if (material == null)
                    return NotFound(new { error = "Материал не найден" });

                if (!string.IsNullOrEmpty(material.FilePath))
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, "uploads");
                    var filePath = Path.Combine(uploadsFolder, Path.GetFileName(material.FilePath));

                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                        Console.WriteLine($"File deleted: {filePath}");
                    }

                    material.FilePath = null;
                    material.FileType = null;
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Database updated");
                }

                return Ok(new { success = true, message = "Файл удален" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deleting file: {ex.Message}");
                _logger.LogError(ex, "Error deleting file");
                return StatusCode(500, new { error = "Ошибка при удалении файла" });
            }
        }
    }
}