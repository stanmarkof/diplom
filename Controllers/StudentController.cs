using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Models;
using diplom.Data;
using System.Text.Json;

namespace diplom.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly AppDbContext _context;
        private readonly ILogger<StudentController> _logger;

        public StudentController(
            UserManager<User> userManager,
            AppDbContext context,
            ILogger<StudentController> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        // ==================== МОИ ДИСЦИПЛИНЫ ====================

        [HttpGet]
        public async Task<IActionResult> MyDisciplines()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student?.StudentGroup == null)
            {
                TempData["ErrorMessage"] = "Вы не прикреплены ни к одной группе. Обратитесь к администратору.";
                return View(new List<Discipline>());
            }

            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.Sections)
                    .ThenInclude(s => s.Materials)
                .Include(d => d.Sections)
                    .ThenInclude(s => s.Tests)
                .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                .OrderBy(d => d.Name)
                .ToListAsync();

            return View(disciplines);
        }

        // ==================== ПРОСМОТР ДИСЦИПЛИНЫ ====================

        [HttpGet]
        public async Task<IActionResult> DisciplineDetails(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student?.StudentGroup == null)
            {
                TempData["ErrorMessage"] = "Вы не прикреплены ни к одной группе.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            var discipline = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.Sections.OrderBy(s => s.Order))
                    .ThenInclude(s => s.Materials)
                .Include(d => d.Sections)
                    .ThenInclude(s => s.Tests)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            // Проверяем доступ к дисциплине
            var hasAccess = await _context.Disciplines
                .AnyAsync(d => d.Id == id && d.OpenGroups.Any(g => g.Id == student.StudentGroupId));

            if (!hasAccess)
            {
                TempData["ErrorMessage"] = "Дисциплина не доступна для вашей группы.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            return View(discipline);
        }

        // ==================== ПРОХОЖДЕНИЕ ТЕСТА ====================

        [HttpGet]
        public async Task<IActionResult> TakeTest(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student?.StudentGroup == null)
            {
                TempData["ErrorMessage"] = "Вы не прикреплены ни к одной группе.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            var test = await _context.Tests
                .Include(t => t.Questions)
                .Include(t => t.Discipline)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null)
            {
                return NotFound();
            }

            // Проверяем доступ к тесту
            var hasAccess = await _context.Disciplines
                .AnyAsync(d => d.Id == test.DisciplineId && d.OpenGroups.Any(g => g.Id == student.StudentGroupId));

            if (!hasAccess || !test.IsPublished || !test.IsVisible)
            {
                TempData["ErrorMessage"] = "Тест не доступен.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            // Проверяем дедлайн
            if (test.Deadline.HasValue && test.Deadline.Value < DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "Дедлайн теста истек.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            // Проверяем, не проходил ли уже тест
            var existingResult = await _context.TestResults
                .FirstOrDefaultAsync(r => r.StudentId == student.Id && r.TestId == test.Id);

            if (existingResult != null)
            {
                TempData["ErrorMessage"] = "Вы уже прошли этот тест.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            ViewBag.Test = test;
            ViewBag.Questions = test.Questions.OrderBy(q => q.Id).ToList();

            return View(test);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitTest(int testId, IFormCollection form)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var test = await _context.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == testId);

            if (test == null)
            {
                return NotFound();
            }

            // Проверяем, не проходил ли уже тест
            var existingResult = await _context.TestResults
                .FirstOrDefaultAsync(r => r.StudentId == student.Id && r.TestId == testId);

            if (existingResult != null)
            {
                TempData["ErrorMessage"] = "Вы уже прошли этот тест.";
                return RedirectToAction(nameof(MyDisciplines));
            }

            // Подсчет баллов
            int totalScore = 0;
            var answers = new Dictionary<int, string>();

            foreach (var question in test.Questions)
            {
                var answer = form[$"question_{question.Id}"].ToString();
                answers[question.Id] = answer;

                if (string.IsNullOrEmpty(answer))
                {
                    continue;
                }

                // Проверка ответа
                if (question.OptionsJson != null)
                {
                    // Вопрос с вариантами
                    if (answer.Equals(question.CorrectAnswer, StringComparison.OrdinalIgnoreCase))
                    {
                        totalScore += question.Points;
                    }
                }
                else
                {
                    // Открытый вопрос (регистронезависимое сравнение)
                    if (answer.Trim().Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        totalScore += question.Points;
                    }
                }
            }

            // Сохраняем результат
            var result = new TestResult
            {
                StudentId = student.Id,
                TestId = testId,
                Score = totalScore,
                MaxScore = test.MaxScore,
                AnswersJson = JsonSerializer.Serialize(answers),
                CompletedAt = DateTime.UtcNow
            };

            _context.TestResults.Add(result);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Тест завершен! Ваш результат: {totalScore} из {test.MaxScore} баллов.";
            return RedirectToAction(nameof(TestResult), new { id = result.Id });
        }

        // ==================== РЕЗУЛЬТАТЫ ТЕСТОВ ====================

        [HttpGet]
        public async Task<IActionResult> MyResults()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var results = await _context.TestResults
                .Include(r => r.Test)
                    .ThenInclude(t => t.Discipline)
                .Where(r => r.StudentId == student.Id)
                .OrderByDescending(r => r.CompletedAt)
                .ToListAsync();

            return View(results);
        }

        [HttpGet]
        public async Task<IActionResult> TestResult(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == currentUser.Id);

            if (student == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _context.TestResults
                .Include(r => r.Test)
                    .ThenInclude(t => t.Questions)
                .FirstOrDefaultAsync(r => r.Id == id && r.StudentId == student.Id);

            if (result == null)
            {
                return NotFound();
            }

            // Десериализуем ответы
            var answers = string.IsNullOrEmpty(result.AnswersJson)
                ? new Dictionary<int, string>()
                : JsonSerializer.Deserialize<Dictionary<int, string>>(result.AnswersJson);

            ViewBag.Answers = answers;
            ViewBag.Questions = result.Test.Questions.OrderBy(q => q.Id).ToList();

            return View(result);
        }

        // ==================== ОБРАТНАЯ СВЯЗЬ ====================

        [HttpGet]
        public IActionResult Feedback()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitFeedback(string message, int? rating)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                TempData["ErrorMessage"] = "Сообщение не может быть пустым.";
                return RedirectToAction(nameof(Feedback));
            }

            var feedback = new Feedback
            {
                UserId = currentUser.Id,
                Message = message,
                Rating = rating,
                CreatedAt = DateTime.UtcNow
            };

            _context.Feedbacks.Add(feedback);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Спасибо за ваш отзыв!";
            return RedirectToAction(nameof(Feedback));
        }
    }
}