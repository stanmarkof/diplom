using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using diplom.Data;
using diplom.Models;
using diplom.Services;
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
        private readonly IRagService _ragService;

        public FileUploadController(
            IWebHostEnvironment env,
            AppDbContext context,
            ILogger<FileUploadController> logger,
            IRagService ragService)
        {
            _env = env;
            _context = context;
            _logger = logger;
            _ragService = ragService;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(10485760)]
        public async Task<IActionResult> UploadFile(IFormFile file, [FromForm] int materialId, [FromForm] bool isIndexed)
        {
            string uniqueFileName = "";
            string filePath = "";
            string extension = "";

            try
            {
                Console.WriteLine("=== UPLOAD FILE START ===");
                Console.WriteLine($"materialId: {materialId}");
                Console.WriteLine($"isIndexed from form: {isIndexed}");
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

                extension = Path.GetExtension(file.FileName).ToLower();
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

                uniqueFileName = $"{Guid.NewGuid()}{extension}";
                filePath = Path.Combine(uploadsFolder, uniqueFileName);
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
                Console.WriteLine($"Current IsIndexed: {material.IsIndexed}");

                // Обновляем путь к файлу
                material.FilePath = $"/uploads/{uniqueFileName}";
                material.FileType = extension;

                // Обновляем флаг индексации, если он был передан
                if (isIndexed != material.IsIndexed)
                {
                    material.IsIndexed = isIndexed;
                    Console.WriteLine($"Updating IsIndexed to: {isIndexed}");
                }

                await _context.SaveChangesAsync();
                Console.WriteLine($"✅ Database updated: FilePath = {material.FilePath}, IsIndexed = {material.IsIndexed}");

                // Если материал помечен для индексации, индексируем его
                if (material.IsIndexed)
                {
                    Console.WriteLine("📚 Начинаем индексацию материала после загрузки файла...");
                    await _ragService.IndexMaterialAsync(material, forceReindex: true);
                    Console.WriteLine("✅ Индексация завершена");
                }

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
                // Если произошла ошибка и файл был создан - удаляем его
                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        Console.WriteLine($"Deleted file after error: {filePath}");
                    }
                    catch { }
                }

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

                    // Если материал был проиндексирован, обновляем индекс
                    if (material.IsIndexed)
                    {
                        Console.WriteLine("📚 Переиндексируем материал после удаления файла...");
                        await _ragService.IndexMaterialAsync(material, forceReindex: true);
                        Console.WriteLine("✅ Переиндексация завершена");
                    }
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