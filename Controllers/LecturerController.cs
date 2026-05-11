using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Models;
using diplom.ViewModels;
using diplom.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using diplom.Services;
//using diplom.Services;

namespace diplom.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly AppDbContext _context;
        private readonly ILogger<LecturerController> _logger;
        private readonly IRagService _ragService;


        private readonly IWebHostEnvironment _env;
        // private readonly IRagService _ragService;

        public LecturerController(
            UserManager<User> userManager,
            AppDbContext context,
            ILogger<LecturerController> logger,
            IWebHostEnvironment env,
             IRagService ragService)
            //IRagService ragService)       // Добавить
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
            _env = env;
            _ragService = ragService;
            // Добавить
                                     // _ragService = ragService;     // Добавить
        }

        // ==================== СПИСОК ДИСЦИПЛИН ====================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                .Where(d => d.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
                .OrderBy(d => d.Name)
                .ToListAsync();

            // Передаем данные для модального окна создания
            ViewBag.CoursesList = await _context.Courses.ToListAsync();

            // Получаем всех пользователей для выбора преподавателей
            var allUsers = await _context.Users.ToListAsync();
            ViewBag.Lecturers = new SelectList(allUsers, "Id", "FullName");

            return View(disciplines);
        }

        // Удалите эти методы из контроллера LecturerController:
        // [HttpGet]
        // public async Task<IActionResult> CreateDiscipline()
        // 
        // [HttpPost]
        // [ValidateAntiForgeryToken]
        // public async Task<IActionResult> CreateDiscipline(Discipline discipline, int[] lecturerIds)

        // Вместо них добавьте один метод для создания дисциплины из модального окна:

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDiscipline(string Name, string Description, int CourseId, int CourseNumber, int Semester, int[] LecturerIds)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            if (string.IsNullOrEmpty(Name))
            {
                TempData["ErrorMessage"] = "Название дисциплины обязательно";
                TempData["OpenCreateModal"] = true;
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var discipline = new Discipline
                {
                    Name = Name,
                    Description = Description ?? "",
                    CourseId = CourseId,
                    CourseNumber = CourseNumber,
                    Semester = Semester,
                    DisciplineLecturers = new List<DisciplineLecturer>()
                };

                // Добавляем текущего преподавателя
                discipline.DisciplineLecturers.Add(new DisciplineLecturer
                {
                    LecturerId = currentUser.Id,
                    Discipline = discipline
                });

                // Добавляем выбранных преподавателей
                if (LecturerIds != null && LecturerIds.Any())
                {
                    foreach (var lecturerId in LecturerIds)
                    {
                        if (lecturerId != currentUser.Id)
                        {
                            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == lecturerId))
                            {
                                discipline.DisciplineLecturers.Add(new DisciplineLecturer
                                {
                                    LecturerId = lecturerId,
                                    Discipline = discipline
                                });
                            }
                        }
                    }
                }

                _context.Disciplines.Add(discipline);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Дисциплина \"{Name}\" успешно создана";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании дисциплины");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                TempData["OpenCreateModal"] = true;
                return RedirectToAction(nameof(Index));
            }
        }

        // ==================== ПРОСМОТР ДИСЦИПЛИНЫ (КАК СТУДЕНТ) ====================

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                Console.WriteLine($"=== Details вызван с id: {id} ===");

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var discipline = await _context.Disciplines
                    .Include(d => d.Course)
                    .Include(d => d.DisciplineLecturers)
                        .ThenInclude(dl => dl.Lecturer)
                    .Include(d => d.Sections.OrderBy(s => s.Order))
                        .ThenInclude(s => s.Materials)
                    .Include(d => d.Sections)
                        .ThenInclude(s => s.Tests)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (discipline == null)
                {
                    TempData["ErrorMessage"] = "Дисциплина не найдена";
                    return RedirectToAction(nameof(Index));
                }

                var hasAccess = discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id);
                if (!hasAccess)
                {
                    TempData["ErrorMessage"] = "У вас нет доступа к этой дисциплине";
                    return RedirectToAction(nameof(Index));
                }

                return View(discipline);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                _logger.LogError(ex, "Ошибка в Details");
                TempData["ErrorMessage"] = "Произошла ошибка при загрузке дисциплины";
                return RedirectToAction(nameof(Index));
            }
        }

        // ==================== РЕДАКТИРОВАНИЕ ДИСЦИПЛИНЫ ====================

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .Include(d => d.Sections.OrderBy(s => s.Order))
                    .ThenInclude(s => s.Materials)
                .Include(d => d.Sections)
                    .ThenInclude(s => s.Tests)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
            {
                return Forbid();
            }

            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", discipline.CourseId);
            ViewBag.Sections = discipline.Sections; // Передаем разделы в View

            var courseGroups = await _context.StudentGroups
                .Where(g => g.CourseId == discipline.CourseId)
                .Include(g => g.Students)
                .ToListAsync();

            var openGroupIds = discipline.OpenGroups.Select(g => g.Id).ToHashSet();

            var model = new EditDisciplineViewModel
            {
                Id = discipline.Id,
                Name = discipline.Name,
                Description = discipline.Description,
                CourseId = discipline.CourseId,
                CourseNumber = discipline.CourseNumber,
                Semester = discipline.Semester,
                Groups = courseGroups.Select(g => new GroupAccessViewModel
                {
                    GroupId = g.Id,
                    GroupName = g.Name,
                    StudentCount = g.Students?.Count ?? 0,
                    IsOpen = openGroupIds.Contains(g.Id)
                }).ToList()
            };

            return View(model);
        }

        // В контроллер LecturerController добавить:

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EditDisciplineViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);

            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Обновляем основные поля
                    discipline.Name = model.Name;
                    discipline.Description = model.Description;
                    discipline.CourseId = model.CourseId;
                    discipline.CourseNumber = model.CourseNumber;
                    discipline.Semester = model.Semester;

                    // Обновляем доступ групп
                    discipline.OpenGroups.Clear();
                    foreach (var group in model.Groups.Where(g => g.IsOpen))
                    {
                        var dbGroup = await _context.StudentGroups.FindAsync(group.GroupId);
                        if (dbGroup != null)
                        {
                            discipline.OpenGroups.Add(dbGroup);
                        }
                    }

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Изменения сохранены";
                    return RedirectToAction(nameof(Details), new { id = discipline.Id });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Disciplines.Any(e => e.Id == id))
                    {
                        return NotFound();
                    }
                    throw;
                }
            }

            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", model.CourseId);
            return View(model);
        }


        // ==================== УПРАВЛЕНИЕ ТЕСТАМИ ====================

        [HttpGet]
        public async Task<IActionResult> EditTest(int id)
        {
            var test = await _context.Tests
                .Include(t => t.Questions)
                .Include(t => t.Section)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null)
            {
                return NotFound();
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == test.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            return View(test);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTest(int id, string title, string description, int durationMinutes, int maxScore, DateTime? deadline, bool isPublished, bool isVisible)
        {
            var test = await _context.Tests.FindAsync(id);

            if (test == null)
            {
                return NotFound();
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == test.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(title))
            {
                TempData["ErrorMessage"] = "Название теста обязательно";
                return RedirectToAction(nameof(EditTest), new { id });
            }

            test.Title = title;
            test.Description = description ?? "";
            test.DurationMinutes = durationMinutes;
            test.MaxScore = maxScore;
            test.Deadline = deadline;
            test.IsPublished = isPublished;
            test.IsVisible = isVisible;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Тест успешно обновлен";
            return RedirectToAction(nameof(Edit), new { id = test.DisciplineId });
        }

        // ==================== УПРАВЛЕНИЕ ВОПРОСАМИ ====================

        [HttpGet]
        public async Task<IActionResult> AddQuestion(int testId)
        {
            var test = await _context.Tests.FindAsync(testId);
            if (test == null)
            {
                return NotFound();
            }

            ViewBag.TestId = testId;
            ViewBag.TestTitle = test.Title;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestion(int testId, string text, string optionsJson, string correctAnswer, int points)
        {
            var test = await _context.Tests.FindAsync(testId);
            if (test == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(text))
            {
                TempData["ErrorMessage"] = "Текст вопроса обязателен";
                return RedirectToAction(nameof(AddQuestion), new { testId });
            }

            // Важно: если correctAnswer пришел пустым, но есть optionsJson, 
            // значит правильный ответ должен быть извлечен из формы
            if (string.IsNullOrEmpty(correctAnswer) && !string.IsNullOrEmpty(optionsJson))
            {
                TempData["ErrorMessage"] = "Выберите правильный вариант ответа";
                return RedirectToAction(nameof(AddQuestion), new { testId });
            }

            var question = new Question
            {
                Text = text,
                OptionsJson = string.IsNullOrEmpty(optionsJson) ? null : optionsJson,
                CorrectAnswer = correctAnswer ?? "", // Убеждаемся, что не null
                Points = points > 0 ? points : 1,
                TestId = testId
            };

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            // Обновляем максимальный балл теста
            test.MaxScore = await _context.Questions.Where(q => q.TestId == testId).SumAsync(q => q.Points);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Вопрос успешно добавлен";
            return RedirectToAction(nameof(EditTest), new { id = testId });
        }

        [HttpGet]
        public async Task<IActionResult> EditQuestion(int id)
        {
            var question = await _context.Questions
                .Include(q => q.Test)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            return View(question);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuestion(int id, string text, string optionsJson, string correctAnswer, int points)
        {
            var question = await _context.Questions
                .Include(q => q.Test)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(text))
            {
                TempData["ErrorMessage"] = "Текст вопроса обязателен";
                return RedirectToAction(nameof(EditQuestion), new { id });
            }

            question.Text = text;
            question.OptionsJson = optionsJson;
            question.CorrectAnswer = correctAnswer;
            question.Points = points > 0 ? points : 1;

            await _context.SaveChangesAsync();

            // Обновляем максимальный балл теста
            var test = question.Test;
            test.MaxScore = await _context.Questions.Where(q => q.TestId == test.Id).SumAsync(q => q.Points);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Вопрос успешно обновлен";
            return RedirectToAction(nameof(EditTest), new { id = question.TestId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteQuestion(int id)
        {
            var question = await _context.Questions
                .Include(q => q.Test)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return Json(new { success = false, message = "Вопрос не найден" });
            }

            var testId = question.TestId;
            _context.Questions.Remove(question);
            await _context.SaveChangesAsync();

            // Обновляем максимальный балл теста
            var test = question.Test;
            if (test != null)
            {
                test.MaxScore = await _context.Questions.Where(q => q.TestId == test.Id).SumAsync(q => q.Points);
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, message = "Вопрос удален" });
        }

        // ==================== УПРАВЛЕНИЕ секциями  ====================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSection(int disciplineId, string name, string description)
        {
            try
            {
                Console.WriteLine($"=== AddSection ===");
                Console.WriteLine($"disciplineId: {disciplineId}");
                Console.WriteLine($"name: {name}");

                // Получаем текущее максимальное значение Order
                var maxOrder = await _context.Sections
                    .Where(s => s.DisciplineId == disciplineId)
                    .MaxAsync(s => (int?)s.Order) ?? 0;

                var section = new Section
                {
                    Name = name,
                    Description = description,
                    DisciplineId = disciplineId,
                    Order = maxOrder + 1
                };

                _context.Sections.Add(section);
                await _context.SaveChangesAsync();
                Console.WriteLine($"✅ Section saved with ID: {section.Id}");

                TempData["SuccessMessage"] = $"Раздел \"{name}\" успешно создан!";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
        }
        // ==================== УПРАВЛЕНИЕ МАТЕРИАЛАМИ ====================

        [HttpGet]
        public async Task<IActionResult> EditMaterial(int id)
        {
            var material = await _context.Materials
                .Include(m => m.Section)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (material == null)
            {
                return NotFound();
            }

            // Проверяем доступ к дисциплине
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == material.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            return View(material);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMaterial(int id, string title, string content, bool isVisible)
        {
            var material = await _context.Materials.FindAsync(id);

            if (material == null)
            {
                return NotFound();
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == material.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(title))
            {
                TempData["ErrorMessage"] = "Название материала обязательно";
                return RedirectToAction(nameof(EditMaterial), new { id });
            }

            material.Title = title;
            material.Content = content ?? "";
            material.IsVisible = isVisible;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Материал успешно обновлен";
            return RedirectToAction(nameof(Edit), new { id = material.DisciplineId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMaterialFile(int id)
        {
            var material = await _context.Materials.FindAsync(id);

            if (material == null)
            {
                return Json(new { success = false, message = "Материал не найден" });
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == material.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Json(new { success = false, message = "Нет доступа" });
            }

            try
            {
                // Удаляем файл с диска
                if (!string.IsNullOrEmpty(material.FilePath))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, material.FilePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }

                // Очищаем путь к файлу в БД
                material.FilePath = null;
                material.FileType = null;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Файл удален" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении файла");
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMaterialToSection(int disciplineId, int sectionId, string title, string content, bool isIndexed)
        {
            try
            {
                Console.WriteLine($"=== AddMaterialToSection ===");
                Console.WriteLine($"disciplineId: {disciplineId}");
                Console.WriteLine($"sectionId: {sectionId}");
                Console.WriteLine($"title: {title}");
                Console.WriteLine($"isIndexed: {isIndexed}");

                var material = new Material
                {
                    Title = title,
                    Content = content ?? "",
                    DisciplineId = disciplineId,
                    SectionId = sectionId,
                    UploadedAt = DateTime.UtcNow,
                    IsIndexed = isIndexed
                };

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                // Индексация для AI, если включено
                if (isIndexed)
                {
                    await _ragService.IndexMaterialAsync(material);
                }

                TempData["SuccessMessage"] = $"Материал \"{title}\" успешно добавлен!";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTestToSection(int disciplineId, int sectionId, string title, string description, int durationMinutes, int maxScore, DateTime? deadline)
        {
            try
            {
                Console.WriteLine($"=== AddTestToSection ===");
                Console.WriteLine($"disciplineId: {disciplineId}");
                Console.WriteLine($"sectionId: {sectionId}");
                Console.WriteLine($"title: {title}");

                var test = new Test
                {
                    Title = title,
                    Description = description ?? "",
                    DurationMinutes = durationMinutes,
                    MaxScore = maxScore,
                    Deadline = deadline,
                    DisciplineId = disciplineId,
                    SectionId = sectionId,
                    IsPublished = false
                };

                _context.Tests.Add(test);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Тест \"{title}\" успешно создан!";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id = disciplineId });
            }
        }


        // ==================== УПРАВЛЕНИЕ РАЗДЕЛАМИ ====================

        [HttpGet]
        public async Task<IActionResult> EditSection(int id)
        {
            var section = await _context.Sections
                .Include(s => s.Discipline)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (section == null)
            {
                return NotFound();
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == section.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            return View(section);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSection(int id, string name, string description)
        {
            var section = await _context.Sections
                .Include(s => s.Discipline)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (section == null)
            {
                return NotFound();
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == section.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Forbid();
            }

            if (string.IsNullOrEmpty(name))
            {
                TempData["ErrorMessage"] = "Название раздела обязательно";
                return RedirectToAction(nameof(EditSection), new { id });
            }

            section.Name = name;
            section.Description = description;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Раздел успешно обновлен";
            return RedirectToAction(nameof(Edit), new { id = section.DisciplineId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSection(int id)
        {
            var section = await _context.Sections
                .Include(s => s.Materials)
                .Include(s => s.Tests)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (section == null)
            {
                return Json(new { success = false, message = "Раздел не найден" });
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == section.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Json(new { success = false, message = "Нет доступа" });
            }

            var disciplineId = section.DisciplineId;

            // Удаляем все материалы раздела
            if (section.Materials != null && section.Materials.Any())
            {
                _context.Materials.RemoveRange(section.Materials);
            }

            // Удаляем все тесты раздела
            if (section.Tests != null && section.Tests.Any())
            {
                _context.Tests.RemoveRange(section.Tests);
            }

            _context.Sections.Remove(section);
            await _context.SaveChangesAsync();

            // Перенумеровываем порядок оставшихся разделов
            var remainingSections = await _context.Sections
                .Where(s => s.DisciplineId == disciplineId)
                .OrderBy(s => s.Order)
                .ToListAsync();

            for (int i = 0; i < remainingSections.Count; i++)
            {
                remainingSections[i].Order = i + 1;
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Раздел и все его содержимое успешно удалены";
            return Json(new { success = true, message = "Раздел удален" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMaterial(int id)
        {
            var material = await _context.Materials
                .Include(m => m.Discipline)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (material == null)
            {
                return Json(new { success = false, message = "Материал не найден" });
            }

            // Проверяем доступ
            var currentUser = await _userManager.GetUserAsync(User);
            var hasAccess = await _context.DisciplineLecturers
                .AnyAsync(dl => dl.DisciplineId == material.DisciplineId && dl.LecturerId == currentUser.Id);

            if (!hasAccess)
            {
                return Json(new { success = false, message = "Нет доступа" });
            }

            var disciplineId = material.DisciplineId;

            // Удаляем файл с диска
            if (!string.IsNullOrEmpty(material.FilePath))
            {
                var fullPath = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, material.FilePath.TrimStart('/'));
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }

            // Удаляем индексы из RAG
            if (material.IsIndexed)
            {
                await _ragService.DeleteMaterialIndexAsync(material.Id);
            }

            // Удаляем материал из БД
            _context.Materials.Remove(material);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Материал успешно удален";
            return Json(new { success = true, message = "Материал удален", redirect = Url.Action("Edit", new { id = disciplineId }) });
        }


        // ==================== УПРАВЛЕНИЕ ГРУППАМИ (AJAX) ====================

        [HttpPost]
        public async Task<IActionResult> ToggleGroupAccess(int disciplineId, int groupId, bool isOpen)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .FirstOrDefaultAsync(d => d.Id == disciplineId);

            if (discipline == null)
            {
                return Json(new { success = false, message = "Дисциплина не найдена" });
            }

            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
            {
                return Json(new { success = false, message = "Нет доступа" });
            }

            var group = await _context.StudentGroups.FindAsync(groupId);
            if (group == null)
            {
                return Json(new { success = false, message = "Группа не найдена" });
            }

            if (isOpen && !discipline.OpenGroups.Contains(group))
            {
                discipline.OpenGroups.Add(group);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Группа {group.Name} добавлена" });
            }
            else if (!isOpen && discipline.OpenGroups.Contains(group))
            {
                discipline.OpenGroups.Remove(group);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Группа {group.Name} удалена" });
            }

            return Json(new { success = true, message = "Изменений не требуется" });
        }
    }

}