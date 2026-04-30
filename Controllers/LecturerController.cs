using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Models;
using diplom.ViewModels;
using diplom.Data;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace diplom.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly AppDbContext _context;
        private readonly ILogger<LecturerController> _logger;

        public LecturerController(
            UserManager<User> userManager,
            AppDbContext context,
            ILogger<LecturerController> logger)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
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

            return View(disciplines);
        }

        // ==================== ПРОСМОТР ДИСЦИПЛИНЫ (КАК СТУДЕНТ) ====================

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var discipline = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.Materials)
                .Include(d => d.Tests)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            // Проверяем, что преподаватель имеет доступ к этой дисциплине
            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
            {
                return Forbid();
            }

            return View(discipline);  // Возвращаем одну дисциплину, а не список
        }

        // ==================== РЕДАКТИРОВАНИЕ ДИСЦИПЛИНЫ ====================

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
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

            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", discipline.CourseId);

            // Получаем все группы курса
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditDisciplineViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", model.CourseId);
                return View(model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .FirstOrDefaultAsync(d => d.Id == model.Id);

            if (discipline == null)
            {
                return NotFound();
            }

            if (!discipline.DisciplineLecturers.Any(dl => dl.LecturerId == currentUser.Id))
            {
                return Forbid();
            }

            // Обновляем основные поля
            discipline.Name = model.Name;
            discipline.Description = model.Description;
            discipline.CourseId = model.CourseId;
            discipline.CourseNumber = model.CourseNumber;
            discipline.Semester = model.Semester;

            // Обновляем доступ для групп
            discipline.OpenGroups.Clear();
            foreach (var group in model.Groups.Where(g => g.IsOpen))
            {
                var studentGroup = await _context.StudentGroups.FindAsync(group.GroupId);
                if (studentGroup != null)
                {
                    discipline.OpenGroups.Add(studentGroup);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Дисциплина успешно обновлена!";
            return RedirectToAction(nameof(Details), new { id = discipline.Id });
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