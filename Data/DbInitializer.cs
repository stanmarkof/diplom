using Microsoft.AspNetCore.Identity;
using diplom.Models;

namespace diplom.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();

            // Создаём роли
            string[] roles = { "Admin", "Lecturer", "Student" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
                }
            }

            // Создаём администратора по умолчанию
            var adminEmail = "admin@example.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                var admin = new Admin
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "Системный администратор",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(admin, "Admin123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                    Console.WriteLine("Администратор успешно создан!");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"Ошибка: {error.Description}");
                    }
                }
            }

            // Создаём преподавателя по умолчанию
            var lecturerEmail = "lecturer@example.com";
            var lecturerUser = await userManager.FindByEmailAsync(lecturerEmail);

            if (lecturerUser == null)
            {
                var lecturer = new Lecturer
                {
                    UserName = lecturerEmail,
                    Email = lecturerEmail,
                    FullName = "Преподаватель",
                    Department = "Информационных технологий",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(lecturer, "Lecturer123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(lecturer, "Lecturer");
                    Console.WriteLine("Преподаватель успешно создан!");
                }
            }
        }
    }
}