using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using diplom.Models;
using diplom.ViewModels;
using diplom.Data;
using System.Security.Claims;
using diplom.Services;

namespace diplom.Controllers
{
    [Authorize(Roles = "Admin")]
    [AutoValidateAntiforgeryToken]
    public class AdminController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        private readonly AppDbContext _context;
        private readonly ILogger<AdminController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IRagService _ragService;

        public AdminController(
            UserManager<User> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            AppDbContext context,
            ILogger<AdminController> logger,
            IWebHostEnvironment env,           // Добавить
        IRagService ragService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _logger = logger;
            _env = env;                        // Добавить
            _ragService = ragService;
        }

        // ==================== УПРАВЛЕНИЕ ПОЛЬЗОВАТЕЛЯМИ ====================

        [HttpGet]
        public async Task<IActionResult> AllUsers(string searchUserId = null, string searchRole = null, string searchFullName = null, string searchGroupId = null)
        {
            var users = _userManager.Users.AsQueryable();

            // Фильтрация по ID
            if (!string.IsNullOrEmpty(searchUserId) && int.TryParse(searchUserId, out int userId))
            {
                users = users.Where(u => u.Id == userId);
            }

            // Фильтрация по ФИО
            if (!string.IsNullOrEmpty(searchFullName))
            {
                users = users.Where(u => u.FullName.Contains(searchFullName));
            }

            var userList = await users.ToListAsync();
            var filteredUsers = new List<User>();

            foreach (var user in userList)
            {
                var roles = await _userManager.GetRolesAsync(user);

                if (!string.IsNullOrEmpty(searchRole) && !roles.Contains(searchRole))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(searchGroupId) && int.TryParse(searchGroupId, out int groupId))
                {
                    var student = user as Student;
                    if (student == null || student.StudentGroupId != groupId)
                    {
                        continue;
                    }
                }

                filteredUsers.Add(user);
            }

            var model = new List<UserRoleViewModel>();
            foreach (var user in filteredUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var student = user as Student;
                string groupName = null;
                int? groupId = null;

                if (student?.StudentGroupId.HasValue == true)
                {
                    groupId = student.StudentGroupId.Value;
                    var group = await _context.StudentGroups.FindAsync(student.StudentGroupId.Value);
                    groupName = group?.Name;
                }

                model.Add(new UserRoleViewModel
                {
                    UserId = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    FullName = user.FullName,
                    Roles = roles.ToList(),
                    GroupName = groupName,
                    GroupId = groupId
                });
            }

            // Для фильтра по группам
            var allGroups = await _context.StudentGroups.ToListAsync();
            ViewBag.AllGroups = allGroups; // Все группы для выпадающего списка
            ViewBag.GroupsForFilter = new SelectList(allGroups, "Id", "Name");
            ViewBag.Groups = new SelectList(allGroups, "Id", "Name");

            ViewData["SearchUserId"] = searchUserId;
            ViewData["SearchRole"] = searchRole;
            ViewData["SearchFullName"] = searchFullName;
            ViewData["SearchGroupId"] = searchGroupId;

            return View(model);
        }

        // ==================== ИЗМЕНЕНИЕ РОЛИ ПОЛЬЗОВАТЕЛЯ ====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(int userId, string newRole, int? groupId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user == null)
                {
                    return Json(new { success = false, message = "Пользователь не найден" });
                }

                // Обновляем Identity Roles
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, newRole);

                // Прямое обновление Discriminator в базе
                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "UPDATE AspNetUsers SET Role = @role WHERE Id = @userId";
                command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@role", newRole));
                command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@userId", userId));

                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                    await _context.Database.GetDbConnection().OpenAsync();

                await command.ExecuteNonQueryAsync();

                return Json(new { success = true, message = "Роль успешно изменена" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class ChangeRoleRequest
        {
            public int UserId { get; set; }
            public string? NewRole { get; set; }
            public int? GroupId { get; set; }
        }

        // ==================== РЕДАКТИРОВАНИЕ ПОЛЬЗОВАТЕЛЯ ====================

        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var nameParts = user.FullName?.Split(' ') ?? new string[0];

            var model = new EditUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FirstName = nameParts.Length > 1 ? nameParts[1] : "",
                LastName = nameParts.Length > 0 ? nameParts[0] : "",
                MiddleName = nameParts.Length > 2 ? nameParts[2] : "",
                CurrentRole = roles.FirstOrDefault() ?? string.Empty
            };

            ViewBag.Roles = new SelectList(new[] { "Admin", "Lecturer", "Student" });
            ViewBag.Groups = new SelectList(await _context.StudentGroups.ToListAsync(), "Id", "Name");

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> EditUser(EditUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new SelectList(new[] { "Admin", "Lecturer", "Student" });
                ViewBag.Groups = new SelectList(await _context.StudentGroups.ToListAsync(), "Id", "Name");
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.Id.ToString());
            if (user == null)
            {
                return NotFound();
            }

            // Обновляем данные (без логина и пароля)
            user.Email = model.Email;
            user.FullName = $"{model.LastName} {model.FirstName} {model.MiddleName}".Trim();

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                // Если пользователь студент, обновляем группу
                if (model.CurrentRole == "Student" && model.GroupId.HasValue)
                {
                    var student = await _context.Students.FirstOrDefaultAsync(s => s.Id == user.Id);
                    if (student != null)
                    {
                        student.StudentGroupId = model.GroupId;
                        _context.Update(student);
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Данные пользователя успешно обновлены!";
                return RedirectToAction(nameof(AllUsers));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            ViewBag.Roles = new SelectList(new[] { "Admin", "Lecturer", "Student" });
            ViewBag.Groups = new SelectList(await _context.StudentGroups.ToListAsync(), "Id", "Name");
            return View(model);
        }

        // ==================== УДАЛЕНИЕ ПОЛЬЗОВАТЕЛЯ ====================

        [HttpPost]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null)
            {
                return NotFound();
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Пользователь {user.FullName} успешно удалён!";
            }
            else
            {
                TempData["ErrorMessage"] = "Ошибка при удалении пользователя.";
            }

            return RedirectToAction(nameof(AllUsers));
        }

        [HttpPost]
        public async Task<IActionResult> AssignGroup([FromBody] AssignGroupRequest request)
        {
            var student = await _context.Students.FirstOrDefaultAsync(s => s.Id == request.UserId);
            if (student == null)
            {
                return Json(new { success = false, message = "Студент не найден" });
            }

            student.StudentGroupId = request.GroupId;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Группа успешно изменена" });
        }

        public class AssignGroupRequest
        {
            public int UserId { get; set; }
            public int? GroupId { get; set; }
        }
        // ==================== УПРАВЛЕНИЕ ГРУППАМИ ====================
        [HttpGet]
        public async Task<IActionResult> ManageGroups()
        {
            var groups = await _context.StudentGroups
                .Include(g => g.Course)
                .Include(g => g.Students)
                .OrderBy(g => g.Name)
                .ToListAsync();

            // Заполняем ViewBag.Courses для выпадающих списков
            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name");

            return View(groups);
        }

        [HttpGet]
        public IActionResult CreateGroup()
        {
            ViewBag.Courses = new SelectList(_context.Courses, "Id", "Name");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup(StudentGroup group)
        {
            // Убираем валидацию навигационного свойства
            ModelState.Remove("Course");

            if (ModelState.IsValid)
            {
                _context.StudentGroups.Add(group);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Группа {group.Name} успешно создана!";

                // Если пришли со страницы курса, возвращаемся обратно
                if (group.CourseId > 0)
                {
                    return RedirectToAction(nameof(ManageCourseGroups), new { courseId = group.CourseId });
                }
                return RedirectToAction(nameof(ManageGroups));
            }

            // Если ошибка, перезагружаем список курсов
            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", group.CourseId);
            return View(group);
        }

        [HttpGet]
        public async Task<IActionResult> EditGroup(int id)
        {
            var group = await _context.StudentGroups.FindAsync(id);
            if (group == null)
            {
                return NotFound();
            }
            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", group.CourseId);
            return View(group);
        }

        [HttpPost]
        public async Task<IActionResult> EditGroup(StudentGroup group)
        {
            // Убираем валидацию навигационного свойства
            ModelState.Remove("Course");

            if (ModelState.IsValid)
            {
                _context.Update(group);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Группа {group.Name} успешно обновлена!";
                return RedirectToAction(nameof(ManageGroups));
            }

            // Если ошибка, перезагружаем список курсов
            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", group.CourseId);
            return View(group);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteGroup(int id)
        {
            var group = await _context.StudentGroups.FindAsync(id);
            if (group != null)
            {
                _context.StudentGroups.Remove(group);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Группа {group.Name} удалена!";
            }
            return RedirectToAction(nameof(ManageGroups));
        }

        // ==================== УПРАВЛЕНИЕ КУРСАМИ ====================

        [HttpGet]
        public async Task<IActionResult> ManageCourses()
        {
            var courses = await _context.Courses
                .Include(c => c.StudentGroups)
                .Include(c => c.Disciplines)
                .OrderBy(c => c.Code)
                .ToListAsync();
            return View(courses);
        }

        [HttpGet]
        public IActionResult CreateCourse()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateCourse(Course course)
        {
            if (ModelState.IsValid)
            {
                _context.Courses.Add(course);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Курс {course.Name} успешно создан!";
                return RedirectToAction(nameof(ManageCourses));
            }
            return View(course);
        }

        [HttpGet]
        public async Task<IActionResult> EditCourse(int id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null)
            {
                return NotFound();
            }
            return View(course);
        }

        [HttpPost]
        public async Task<IActionResult> EditCourse(Course course)
        {
            if (ModelState.IsValid)
            {
                _context.Update(course);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Курс {course.Name} успешно обновлён!";
                return RedirectToAction(nameof(ManageCourses));
            }
            return View(course);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course != null)
            {
                _context.Courses.Remove(course);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Курс {course.Name} удалён!";
            }
            return RedirectToAction(nameof(ManageCourses));
        }

        // ==================== ПРИКРЕПЛЕНИЕ ГРУПП К КУРСУ ====================
        [HttpGet]
        public async Task<IActionResult> ManageCourseGroups(int courseId)
        {
            var course = await _context.Courses
                .Include(c => c.StudentGroups)
                    .ThenInclude(g => g.Students)  // Загружаем студентов для групп курса
                .FirstOrDefaultAsync(c => c.Id == courseId);

            if (course == null)
            {
                return NotFound();
            }

            // Загружаем ВСЕ группы с их студентами
            var allGroups = await _context.StudentGroups
                .Include(g => g.Students)  // Обязательно загружаем студентов
                .ToListAsync();

            var assignedGroupIds = course.StudentGroups.Select(g => g.Id).ToHashSet();

            ViewBag.Course = course;
            ViewBag.AllGroups = allGroups;
            ViewBag.AssignedGroups = assignedGroupIds;

            return View();
        }

        /*  [HttpPost]
          public async Task<IActionResult> AssignGroupToCourse(int courseId, int groupId, bool assign)
          {
              var course = await _context.Courses
                  .Include(c => c.StudentGroups)
                  .FirstOrDefaultAsync(c => c.Id == courseId);

              if (course == null)
              {
                  return NotFound();
              }

              var group = await _context.StudentGroups.FindAsync(groupId);
              if (group == null)
              {
                  return NotFound();
              }

              if (assign && !course.StudentGroups.Contains(group))
              {
                  course.StudentGroups.Add(group);
              }
              else if (!assign && course.StudentGroups.Contains(group))
              {
                  course.StudentGroups.Remove(group);
              }

              await _context.SaveChangesAsync();
              TempData["SuccessMessage"] = $"Группа {group.Name} {(assign ? "добавлена к" : "удалена из")} курса {course.Name}!";

              return RedirectToAction(nameof(ManageCourseGroups), new { courseId });
          } */

        // ==================== УПРАВЛЕНИЕ ДИСЦИПЛИНАМИ ====================

        [HttpGet]
        public async Task<IActionResult> ManageDisciplines()
        {
            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .OrderBy(d => d.Name)
                .ToListAsync();

            ViewBag.CoursesList = await _context.Courses.ToListAsync();
            ViewBag.Lecturers = new SelectList(await _context.Lecturers.ToListAsync(), "Id", "FullName");

            return View(disciplines);
        }

        [HttpGet]
        public async Task<IActionResult> CreateDiscipline()
        {
            // Возвращаем ManageDisciplines вместо отдельного представления
            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .OrderBy(d => d.Name)
                .ToListAsync();

            ViewBag.CoursesList = await _context.Courses.ToListAsync();
            ViewBag.Lecturers = new SelectList(await _context.Lecturers.ToListAsync(), "Id", "FullName");

            TempData["OpenCreateModal"] = true; // Открываем модальное окно при загрузке
            return View("ManageDisciplines", disciplines);
        }

        [HttpPost]
        public async Task<IActionResult> CreateDiscipline(Discipline discipline, int[] lecturerIds)
        {
            // Убираем валидацию навигационных свойств
            ModelState.Remove("Course");

            if (ModelState.IsValid)
            {
                _context.Disciplines.Add(discipline);
                await _context.SaveChangesAsync();

                // Добавляем преподавателей
                if (lecturerIds != null && lecturerIds.Any())
                {
                    foreach (var lecturerId in lecturerIds)
                    {
                        _context.DisciplineLecturers.Add(new DisciplineLecturer
                        {
                            DisciplineId = discipline.Id,
                            LecturerId = lecturerId,
                            AssignedAt = DateTime.UtcNow
                        });
                    }
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"Дисциплина {discipline.Name} успешно создана!";
                return RedirectToAction(nameof(ManageDisciplines));
            }

            // Выводим ошибки валидации
            foreach (var key in ModelState.Keys)
            {
                var errors = ModelState[key].Errors;
                foreach (var error in errors)
                {
                    Console.WriteLine($"Ошибка валидации в поле {key}: {error.ErrorMessage}");
                }
            }

            // Если ошибка, возвращаемся к списку
            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .OrderBy(d => d.Name)
                .ToListAsync();

            ViewBag.CoursesList = await _context.Courses.ToListAsync();
            ViewBag.Lecturers = new SelectList(await _context.Lecturers.ToListAsync(), "Id", "FullName");

            return View("ManageDisciplines", disciplines);
        }

        [HttpGet]
        public async Task<IActionResult> EditDiscipline(int id)
        {
            // Возвращаем ManageDisciplines с открытием модального окна редактирования
            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .OrderBy(d => d.Name)
                .ToListAsync();

            ViewBag.CoursesList = await _context.Courses.ToListAsync();
            ViewBag.Lecturers = new SelectList(await _context.Lecturers.ToListAsync(), "Id", "FullName");
            ViewBag.EditDisciplineId = id; // ID дисциплины для редактирования

            return View("ManageDisciplines", disciplines);
        }

        [HttpPost]
        public async Task<IActionResult> EditDiscipline(Discipline discipline, int[] lecturerIds)
        {
            ModelState.Remove("Course");

            if (ModelState.IsValid)
            {
                var existingDiscipline = await _context.Disciplines
                    .Include(d => d.DisciplineLecturers)
                    .FirstOrDefaultAsync(d => d.Id == discipline.Id);

                if (existingDiscipline == null)
                {
                    return NotFound();
                }

                // Обновляем основные поля
                existingDiscipline.Name = discipline.Name;
                existingDiscipline.Description = discipline.Description;
                existingDiscipline.CourseId = discipline.CourseId;
                existingDiscipline.CourseNumber = discipline.CourseNumber;
                existingDiscipline.Semester = discipline.Semester;

                // Обновляем преподавателей
                existingDiscipline.DisciplineLecturers.Clear();

                if (lecturerIds != null && lecturerIds.Any())
                {
                    foreach (var lecturerId in lecturerIds)
                    {
                        existingDiscipline.DisciplineLecturers.Add(new DisciplineLecturer
                        {
                            DisciplineId = discipline.Id,
                            LecturerId = lecturerId,
                            AssignedAt = DateTime.UtcNow
                        });
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Дисциплина {discipline.Name} успешно обновлена!";
                return RedirectToAction(nameof(ManageDisciplines));
            }

            // Если ошибка, возвращаемся к списку
            var disciplines = await _context.Disciplines
                .Include(d => d.Course)
                .Include(d => d.DisciplineLecturers)
                    .ThenInclude(dl => dl.Lecturer)
                .OrderBy(d => d.Name)
                .ToListAsync();

            ViewBag.CoursesList = await _context.Courses.ToListAsync();
            ViewBag.Lecturers = new SelectList(await _context.Lecturers.ToListAsync(), "Id", "FullName");
            ViewBag.EditDisciplineId = discipline.Id;

            return View("ManageDisciplines", disciplines);
        }

        // ==================== НАСТРОЙКА КОДА РЕГИСТРАЦИИ ====================

        [HttpGet]
        public async Task<IActionResult> ChangeVerificationCode()
        {
            var appSetting = await _context.AppSettings.FindAsync(1);
            if (appSetting == null)
            {
                return NotFound();
            }

            var model = new ChangeVerificationCodeViewModel
            {
                CurrentVerificationCode = appSetting.VerificationCode
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ChangeVerificationCode(ChangeVerificationCodeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.NewVerificationCode != model.ConfirmNewVerificationCode)
            {
                ModelState.AddModelError(string.Empty, "Новый код и подтвержденный код не совпадают.");
                return View(model);
            }

            var appSetting = await _context.AppSettings.FindAsync(1);
            if (appSetting == null)
            {
                return NotFound();
            }

            appSetting.VerificationCode = model.NewVerificationCode;
            _context.Update(appSetting);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Код верификации успешно изменён!";
            return RedirectToAction(nameof(ChangeVerificationCode));
        }

        // ==================== УПРАВЛЕНИЕ ОБРАТНОЙ СВЯЗЬЮ ====================

        [HttpGet]
        public async Task<IActionResult> ViewFeedbacks()
        {
            var feedbacks = await _context.Feedbacks
                .Include(f => f.User)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
            return View(feedbacks);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteFeedback(int id)
        {
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback != null)
            {
                _context.Feedbacks.Remove(feedback);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Отзыв успешно удалён!";
            }
            return RedirectToAction(nameof(ViewFeedbacks));
        }



        // ==================== СОЗДАНИЕ АДМИНИСТРАТОРА ====================

        [HttpGet]
        public IActionResult CreateAdmin()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAdmin(CreateUserViewModel model)
        {
            // Убираем валидацию роли, так как мы её устанавливаем сами
            ModelState.Remove("Role");
            ModelState.Remove("Department");
            ModelState.Remove("Position");

            if (ModelState.IsValid)
            {
                // Проверяем, не существует ли пользователь с таким логином
                var existingUser = await _userManager.FindByNameAsync(model.UserName);
                if (existingUser != null)
                {
                    ModelState.AddModelError("UserName", "Пользователь с таким логином уже существует");
                    return View(model);
                }

                // Проверяем, не существует ли пользователь с таким email
                var existingEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Пользователь с таким email уже существует");
                    return View(model);
                }

                var admin = new Admin
                {
                    UserName = model.UserName,
                    Email = model.Email,
                    FullName = $"{model.LastName} {model.FirstName} {model.MiddleName}".Trim(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(admin, model.Password);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(admin, "Admin");

                    TempData["SuccessMessage"] = $"Администратор {admin.FullName} успешно создан!";
                    return RedirectToAction(nameof(AllUsers));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // ==================== СОЗДАНИЕ ПРЕПОДАВАТЕЛЯ ====================

        [HttpGet]
        public IActionResult CreateLecturer()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLecturer(CreateUserViewModel model)
        {
            // Убираем валидацию роли, так как мы её устанавливаем сами
            ModelState.Remove("Role");

            if (ModelState.IsValid)
            {
                // Проверяем, не существует ли пользователь с таким логином
                var existingUser = await _userManager.FindByNameAsync(model.UserName);
                if (existingUser != null)
                {
                    ModelState.AddModelError("UserName", "Пользователь с таким логином уже существует");
                    return View(model);
                }

                // Проверяем, не существует ли пользователь с таким email
                var existingEmail = await _userManager.FindByEmailAsync(model.Email);
                if (existingEmail != null)
                {
                    ModelState.AddModelError("Email", "Пользователь с таким email уже существует");
                    return View(model);
                }

                var lecturer = new Lecturer
                {
                    UserName = model.UserName,
                    Email = model.Email,
                    FullName = $"{model.LastName} {model.FirstName} {model.MiddleName}".Trim(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    Department = model.Department,
                    Position = model.Position
                };

                var result = await _userManager.CreateAsync(lecturer, model.Password);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(lecturer, "Lecturer");

                    TempData["SuccessMessage"] = $"Преподаватель {lecturer.FullName} успешно создан!";
                    return RedirectToAction(nameof(AllUsers));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }







        // ==================== УПРАВЛЕНИЕ ДИСЦИПЛИНАМИ (ПОЛНЫЙ ФУНКЦИОНАЛ) ====================
        // ==================== ВСЕ ДИСЦИПЛИНЫ (КАК У ЛЕКТОРА, НО ДЛЯ АДМИНА) ====================

       [HttpGet]
public async Task<IActionResult> IndexDisc()
{
    // Получаем ВСЕ дисциплины
    var disciplines = await _context.Disciplines
        .Include(d => d.Course)
        .Include(d => d.DisciplineLecturers)
            .ThenInclude(dl => dl.Lecturer)
        .OrderBy(d => d.Name)
        .ToListAsync();

    // Получаем ВСЕ курсы (даже те, у которых нет дисциплин)
    var allCourses = await _context.Courses
        .OrderBy(c => c.Code)
        .ToListAsync();

    // Передаем данные для модального окна создания
    ViewBag.CoursesList = allCourses;

    // Получаем всех пользователей для выбора преподавателей
    var allUsers = await _context.Users.ToListAsync();
    ViewBag.Lecturers = new SelectList(allUsers, "Id", "FullName");

            // Группируем дисциплины по курсам (исправлено: убрали .HasValue)
            // Группируем дисциплины по курсам (для int, не nullable)
            var disciplinesByCourse = disciplines
                .Where(d => d.CourseId > 0) // Просто проверяем, что ID > 0
                .GroupBy(d => d.CourseId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Передаем в представление словарь дисциплин
            ViewBag.DisciplinesByCourse = disciplinesByCourse;
    
    return View(allCourses);
}
        // Метод для создания дисциплины (из модального окна)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDisciplineFromModal(string Name, string Description, int CourseId, int CourseNumber, int Semester, int[] LecturerIds)
        {
            if (string.IsNullOrEmpty(Name))
            {
                TempData["ErrorMessage"] = "Название дисциплины обязательно";
                TempData["OpenCreateModal"] = true;
                return RedirectToAction(nameof(IndexDisc));
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

                // Добавляем выбранных преподавателей
                if (LecturerIds != null && LecturerIds.Any())
                {
                    foreach (var lecturerId in LecturerIds)
                    {
                        discipline.DisciplineLecturers.Add(new DisciplineLecturer
                        {
                            LecturerId = lecturerId,
                            Discipline = discipline
                        });
                    }
                }

                _context.Disciplines.Add(discipline);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Дисциплина \"{Name}\" успешно создана";
                return RedirectToAction(nameof(IndexDisc));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании дисциплины");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                TempData["OpenCreateModal"] = true;
                return RedirectToAction(nameof(IndexDisc));
            }
        }

        // Просмотр дисциплины (единый метод)
        [HttpGet]
        public async Task<IActionResult> DisciplineDetails(int id)
        {
            try
            {
                var discipline = await _context.Disciplines
                    .Include(d => d.Course)
                    .Include(d => d.DisciplineLecturers)
                        .ThenInclude(dl => dl.Lecturer)
                    .Include(d => d.Sections.OrderBy(s => s.Order))
                        .ThenInclude(s => s.Materials)
                    .Include(d => d.Sections)
                        .ThenInclude(s => s.Tests)
                            .ThenInclude(t => t.Questions)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (discipline == null)
                {
                    TempData["ErrorMessage"] = "Дисциплина не найдена";
                    return RedirectToAction(nameof(IndexDisc));
                }

                return View(discipline);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в Details");
                TempData["ErrorMessage"] = "Произошла ошибка при загрузке дисциплины";
                return RedirectToAction(nameof(IndexDisc));
            }
        }

        // Редактирование дисциплины (полная версия как у лектора)
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .Include(d => d.Sections.OrderBy(s => s.Order))
                    .ThenInclude(s => s.Materials)
                .Include(d => d.Sections)
                    .ThenInclude(s => s.Tests)
                        .ThenInclude(t => t.Questions)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            ViewBag.Courses = new SelectList(await _context.Courses.ToListAsync(), "Id", "Name", discipline.CourseId);
            ViewBag.Sections = discipline.Sections;

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
        public async Task<IActionResult> Edit(int id, EditDisciplineViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            var discipline = await _context.Disciplines
                .Include(d => d.DisciplineLecturers)
                .Include(d => d.OpenGroups)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (discipline == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    discipline.Name = model.Name;
                    discipline.Description = model.Description;
                    discipline.CourseId = model.CourseId;
                    discipline.CourseNumber = model.CourseNumber;
                    discipline.Semester = model.Semester;

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
                    return RedirectToAction(nameof(DisciplineDetails), new { id = discipline.Id });
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

        // ==================== УПРАВЛЕНИЕ РАЗДЕЛАМИ ====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSection(int disciplineId, string name, string description)
        {
            try
            {
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

                TempData["SuccessMessage"] = $"Раздел \"{name}\" успешно создан!";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании раздела");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
        }

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

            if (string.IsNullOrEmpty(name))
            {
                TempData["ErrorMessage"] = "Название раздела обязательно";
                return RedirectToAction(nameof(EditSection), new { id });
            }

            section.Name = name;
            section.Description = description;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Раздел успешно обновлен";
            return RedirectToAction(nameof(DisciplineDetails), new { id = section.DisciplineId });
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

            var disciplineId = section.DisciplineId;

            // Удаляем все материалы раздела
            if (section.Materials != null && section.Materials.Any())
            {
                // Сначала удаляем индексы материалов
                foreach (var material in section.Materials)
                {
                    if (material.IsIndexed)
                    {
                        await _ragService.DeleteMaterialIndexAsync(material.Id);
                    }

                    // Удаляем файлы
                    if (!string.IsNullOrEmpty(material.FilePath))
                    {
                        var fullPath = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, material.FilePath.TrimStart('/'));
                        if (System.IO.File.Exists(fullPath))
                        {
                            System.IO.File.Delete(fullPath);
                        }
                    }
                }
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
            return Json(new { success = true, redirect = Url.Action("Edit", new { id = disciplineId }) });
        }

        // ==================== УПРАВЛЕНИЕ МАТЕРИАЛАМИ ====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMaterialToSection(int disciplineId, int sectionId, string title, string content, bool isIndexed)
        {
            try
            {
                var material = new Material
                {
                    Title = title,
                    Content = content ?? "",
                    DisciplineId = disciplineId,
                    SectionId = sectionId,
                    UploadedAt = DateTime.UtcNow,
                    IsIndexed = isIndexed,
                    IsVisible = true
                };

                _context.Materials.Add(material);
                await _context.SaveChangesAsync();

                if (isIndexed)
                {
                    await _ragService.IndexMaterialAsync(material);
                }

                TempData["SuccessMessage"] = $"Материал \"{title}\" успешно добавлен!";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении материала");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
        }

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
            return RedirectToAction(nameof(DisciplineDetails), new { id = material.DisciplineId });
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

            // Возвращаем JSON для AJAX запроса
            return Json(new { success = true, redirect = Url.Action("Edit", new { id = disciplineId }) });
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

            try
            {
                if (!string.IsNullOrEmpty(material.FilePath))
                {
                    var fullPath = Path.Combine(_env.WebRootPath, material.FilePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }

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

        // ==================== УПРАВЛЕНИЕ ТЕСТАМИ ====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTestToSection(int disciplineId, int sectionId, string title, string description, int durationMinutes, int maxScore, DateTime? deadline)
        {
            try
            {
                var test = new Test
                {
                    Title = title,
                    Description = description ?? "",
                    DurationMinutes = durationMinutes,
                    MaxScore = maxScore,
                    Deadline = deadline,
                    DisciplineId = disciplineId,
                    SectionId = sectionId,
                    IsPublished = false,
                    IsVisible = true
                };

                _context.Tests.Add(test);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Тест \"{title}\" успешно создан!";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании теста");
                TempData["ErrorMessage"] = $"Ошибка: {ex.Message}";
                return RedirectToAction(nameof(DisciplineDetails), new { id = disciplineId });
            }
        }

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
            return RedirectToAction(nameof(DisciplineDetails), new { id = test.DisciplineId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTest(int id)
        {
            var test = await _context.Tests
                .Include(t => t.Questions)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (test == null)
            {
                return Json(new { success = false, message = "Тест не найден" });
            }

            var disciplineId = test.DisciplineId;

            // Удаляем все вопросы теста
            if (test.Questions != null && test.Questions.Any())
            {
                _context.Questions.RemoveRange(test.Questions);
            }

            _context.Tests.Remove(test);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Тест успешно удален";
            return Json(new { success = true, redirect = Url.Action("Edit", new { id = disciplineId }) });
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

            var question = new Question
            {
                Text = text,
                OptionsJson = string.IsNullOrEmpty(optionsJson) ? null : optionsJson,
                CorrectAnswer = correctAnswer ?? "",
                Points = points > 0 ? points : 1,
                TestId = testId
            };

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

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

            var test = question.Test;
            test.MaxScore = await _context.Questions.Where(q => q.TestId == test.Id).SumAsync(q => q.Points);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Вопрос успешно обновлен";
            return RedirectToAction(nameof(EditTest), new { id = question.TestId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
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

            TempData["SuccessMessage"] = "Вопрос успешно удален";
            return Json(new { success = true, redirect = Url.Action("EditTest", new { id = testId }) });
        }

        // ==================== УПРАВЛЕНИЕ ДОСТУПОМ ГРУПП (AJAX) ====================

        [HttpPost]
        public async Task<IActionResult> ToggleGroupAccess(int disciplineId, int groupId, bool isOpen)
        {
            var discipline = await _context.Disciplines
                .Include(d => d.OpenGroups)
                .FirstOrDefaultAsync(d => d.Id == disciplineId);

            if (discipline == null)
            {
                return Json(new { success = false, message = "Дисциплина не найдена" });
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

        // ==================== ПРОСМОТР ТЕСТА С РЕЗУЛЬТАТАМИ ДЛЯ АДМИНА ====================

        [HttpGet]
        public async Task<IActionResult> TestWithResults(int id)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var test = await _context.Tests
                    .Include(t => t.Discipline)
                    .Include(t => t.Section)
                    .Include(t => t.Questions)
                    .Include(t => t.TestResults)
                        .ThenInclude(tr => tr.Student)
                            .ThenInclude(s => s.StudentGroup)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    TempData["ErrorMessage"] = "Тест не найден";
                    return RedirectToAction("IndexDisc");
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в TestWithResults");
                TempData["ErrorMessage"] = "Произошла ошибка при загрузке теста";
                return RedirectToAction("IndexDisc");
            }
        }

        // ==================== ПРОСМОТР ДЕТАЛЕЙ РЕЗУЛЬТАТА ДЛЯ АДМИНА ====================

        [HttpGet]
        public async Task<IActionResult> TestResultDetails(int id)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var result = await _context.TestResults
                    .Include(r => r.Student)
                    .Include(r => r.Test)
                        .ThenInclude(t => t.Questions)
                    .Include(r => r.Test)
                        .ThenInclude(t => t.Discipline)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (result == null)
                {
                    TempData["ErrorMessage"] = "Результат не найден";
                    return RedirectToAction("IndexDisc");
                }

                // Десериализуем ответы
                var answers = string.IsNullOrEmpty(result.AnswersJson)
                    ? new Dictionary<int, string>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(result.AnswersJson);

                ViewBag.Answers = answers;
                ViewBag.Questions = result.Test.Questions.OrderBy(q => q.Id).ToList();

                return View(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в TestResultDetails");
                TempData["ErrorMessage"] = "Произошла ошибка при загрузке результата";
                return RedirectToAction("IndexDisc");
            }
        }
    }


}