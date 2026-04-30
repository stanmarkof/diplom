using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using diplom.Models;
using diplom.ViewModels;
using diplom.Data;
using System.Security.Claims;

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

        public AdminController(
            UserManager<User> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            AppDbContext context,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _logger = logger;
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
    }
}