using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using diplom.Models;
using diplom.ViewModels;
using diplom.Data;

namespace diplom.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly ILogger<AccountController> _logger;
        private readonly AppDbContext _context;

        public AccountController(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            ILogger<AccountController> logger,
            AppDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _context = context;
        }

        // ==================== РЕГИСТРАЦИЯ ====================

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // Проверка кода верификации
                var appSetting = await _context.AppSettings.FirstOrDefaultAsync(a => a.Id == 1);
                if (appSetting == null || appSetting.VerificationCode != model.VerificationCode)
                {
                    ModelState.AddModelError(string.Empty, "Неверный код верификации.");
                    return View(model);
                }

                var student = new Student
                {
                    UserName = model.UserName,
                    Email = model.Email,
                    FullName = $"{model.LastName} {model.FirstName} {model.MiddleName}".Trim(),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    StudentGroupId = null // Без группы до назначения админом
                };

                var result = await _userManager.CreateAsync(student, model.Password);

                if (result.Succeeded)
                {
                    // Добавляем роль Student
                    await _userManager.AddToRoleAsync(student, "Student");

                    // Принудительно обновляем Discriminator в базе данных
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE AspNetUsers SET Role = 'Student' WHERE Id = {0}",
                        student.Id);

                    _logger.LogInformation("Новый студент зарегистрирован: {UserName}", model.UserName);

                    TempData["SuccessMessage"] = "Регистрация успешна! Дождитесь назначения группы администратором.";
                    return RedirectToAction("Login", "Account");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        // ==================== ВХОД ====================

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(
                    model.UserName,
                    model.Password,
                    model.RememberMe,
                    lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    _logger.LogInformation("Успешный вход: {UserName}", model.UserName);
                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
            }

            return View(model);
        }

        // ==================== ПРОФИЛЬ ====================

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault();

            var nameParts = user.FullName?.Split(' ') ?? new string[0];
            var lastName = nameParts.Length > 0 ? nameParts[0] : "";
            var firstName = nameParts.Length > 1 ? nameParts[1] : "";
            var middleName = nameParts.Length > 2 ? nameParts[2] : "";

            var model = new ProfileViewModel
            {
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FirstName = firstName,
                LastName = lastName,
                MiddleName = middleName,
                Role = role,
                HasGroup = false
            };

            if (role == "Student")
            {
                var student = await _context.Students
                    .Include(s => s.StudentGroup)
                    .FirstOrDefaultAsync(s => s.Id == user.Id);

                if (student?.StudentGroup != null)
                {
                    model.GroupName = student.StudentGroup.Name;
                    model.HasGroup = true;
                }
                else
                {
                    model.GroupName = "Не назначена";
                    model.HasGroup = false;
                }
            }
            else if (role == "Lecturer")
            {
                var lecturer = await _context.Lecturers
                    .FirstOrDefaultAsync(l => l.Id == user.Id);

                if (lecturer != null)
                {
                    model.Department = lecturer.Department;
                    model.Position = lecturer.Position;
                }
            }

            return View(model);
        }

        // ==================== РЕДАКТИРОВАНИЕ ====================

        [HttpGet]
        public async Task<IActionResult> Edit(string? userId = null)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var userToEdit = currentUser;

            if (!string.IsNullOrEmpty(userId) && User.IsInRole("Admin"))
            {
                userToEdit = await _userManager.FindByIdAsync(userId);
                if (userToEdit == null)
                {
                    return NotFound();
                }
            }

            var nameParts = userToEdit.FullName?.Split(' ') ?? new string[0];
            var lastName = nameParts.Length > 0 ? nameParts[0] : "";
            var firstName = nameParts.Length > 1 ? nameParts[1] : "";
            var middleName = nameParts.Length > 2 ? nameParts[2] : "";

            var model = new EditProfileViewModel
            {
                UserName = userToEdit.UserName ?? string.Empty,
                Email = userToEdit.Email ?? string.Empty,
                LastName = lastName,
                FirstName = firstName,
                MiddleName = middleName
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditProfileViewModel model, string? userId = null)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var userToEdit = currentUser;

            if (!string.IsNullOrEmpty(userId) && User.IsInRole("Admin"))
            {
                userToEdit = await _userManager.FindByIdAsync(userId);
                if (userToEdit == null)
                {
                    return NotFound();
                }
            }

            userToEdit.UserName = model.UserName;
            userToEdit.Email = model.Email;
            userToEdit.FullName = $"{model.LastName} {model.FirstName} {model.MiddleName}".Trim();

            var result = await _userManager.UpdateAsync(userToEdit);

            if (result.Succeeded)
            {
                if (userToEdit.UserName != currentUser?.UserName)
                {
                    await _signInManager.SignInAsync(userToEdit, isPersistent: true);
                }

                if (User.IsInRole("Admin") && !string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("AllUsers", "Admin");
                }

                TempData["SuccessMessage"] = "Профиль успешно обновлён!";
                return RedirectToAction("Profile", "Account");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        // ==================== СМЕНА ПАРОЛЯ ====================

        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);

            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                TempData["SuccessMessage"] = "Пароль успешно изменён!";
                return RedirectToAction("Profile", "Account");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        // ==================== ВЫХОД ====================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("Пользователь вышел из системы");
            return RedirectToAction("Index", "Home");
        }
    }
}