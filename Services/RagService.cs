using diplom.Data;
using diplom.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace diplom.Services
{
    public interface IRagService
    {
        Task<string> AskBotAsync(string query, int userId);
        Task IndexMaterialAsync(Material material, bool forceReindex = false);
        Task DeleteMaterialIndexAsync(int materialId);
        Task RebuildAllIndexesAsync();
    }

    public class RagService : IRagService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RagService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly HttpClient _httpClient;
        private readonly string _ollamaUrl = "http://localhost:11434";
        private readonly string _ollamaModel = "llama3.2:3b";

        public RagService(IServiceScopeFactory scopeFactory, ILogger<RagService> logger, IWebHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_ollamaUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        public async Task<string> AskBotAsync(string query, int userId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                Console.WriteLine($"=== ASK BOT ===");
                Console.WriteLine($"UserId: {userId}");
                Console.WriteLine($"Query: {query}");

                // Получаем информацию о пользователе и его роли
                var user = await context.Users.FindAsync(userId);
                var isAdmin = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Admin");
                var isLecturer = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Lecturer");
                var isStudent = !isAdmin && !isLecturer;

                // ==================== СБОР ДАННЫХ В ЗАВИСИМОСТИ ОТ РОЛИ ====================

                var dbData = new StringBuilder();
                dbData.AppendLine("=== ДАННЫЕ ИЗ СИСТЕМЫ ===\n");

                // 1. Информация о пользователе
                dbData.AppendLine($"Пользователь: {user?.UserName}, ID: {userId}");
                dbData.AppendLine($"Роль: {(isAdmin ? "Администратор" : isLecturer ? "Преподаватель" : "Студент")}");

                // 3. ДАННЫЕ ДЛЯ АДМИНИСТРАТОРА - полный доступ
                if (isAdmin)
                {
                    // Все группы
                    var allGroups = await context.StudentGroups
                        .Include(g => g.Course)
                        .ToListAsync();
                    dbData.AppendLine($"\n=== ВСЕ ГРУППЫ ({allGroups.Count} шт.) ===");
                    foreach (var group in allGroups.Take(20))
                    {
                        var studentCount = await context.Students.CountAsync(s => s.StudentGroupId == group.Id);
                        dbData.AppendLine($"- {group.Name} | Курс: {group.Course?.Name} | Студентов: {studentCount}");
                    }

                    // Все курсы
                    var allCourses = await context.Courses.ToListAsync();
                    dbData.AppendLine($"\n=== ВСЕ КУРСЫ ({allCourses.Count} шт.) ===");
                    foreach (var course in allCourses.Take(20))
                    {
                        var groupsCount = await context.StudentGroups.CountAsync(g => g.CourseId == course.Id);
                        var disciplinesCount = await context.Disciplines.CountAsync(d => d.CourseId == course.Id);
                        dbData.AppendLine($"- {course.Code} | {course.Name} | Групп: {groupsCount} | Дисциплин: {disciplinesCount}");
                    }

                    // Все дисциплины (ИСПРАВЛЕНО)
                    var allDisciplines = await context.Disciplines
                        .Include(d => d.Course)
                        .Include(d => d.DisciplineLecturers)
                            .ThenInclude(dl => dl.Lecturer)
                        .ToListAsync();
                    dbData.AppendLine($"\n=== ВСЕ ДИСЦИПЛИНЫ ({allDisciplines.Count} шт.) ===");
                    foreach (var d in allDisciplines.Take(30))
                    {
                        var materialsCount = await context.Materials.CountAsync(m => m.DisciplineId == d.Id);
                        var testsCount = await context.Tests.CountAsync(t => t.DisciplineId == d.Id);
                        var lecturers = string.Join(", ", d.DisciplineLecturers.Select(dl => dl.Lecturer?.FullName ?? "неизвестно"));
                        dbData.AppendLine($"- {d.Name} ({d.Course?.Code}) | {d.CourseNumber} курс, {d.Semester} семестр");
                        dbData.AppendLine($"  Преподаватель: {lecturers}");
                        dbData.AppendLine($"  Материалов: {materialsCount}, Тестов: {testsCount}");
                    }

                    // Все преподаватели
                    var allLecturers = await context.Lecturers.ToListAsync();
                    dbData.AppendLine($"\n=== ВСЕ ПРЕПОДАВАТЕЛИ ({allLecturers.Count} шт.) ===");
                    foreach (var l in allLecturers)
                    {
                        var disciplinesCount = await context.DisciplineLecturers.CountAsync(dl => dl.LecturerId == l.Id);
                        dbData.AppendLine($"- {l.FullName} | Кафедра: {l.Department ?? "не указана"} | Дисциплин: {disciplinesCount}");
                    }

                    // Все студенты
                    var allStudents = await context.Students.ToListAsync();
                    dbData.AppendLine($"\n=== ВСЕ СТУДЕНТЫ ({allStudents.Count} шт.) ===");
                    foreach (var s in allStudents.Take(30))
                    {
                        var group = await context.StudentGroups.FindAsync(s.StudentGroupId);
                        var testsCompleted = await context.TestResults.CountAsync(tr => tr.StudentId == s.Id);
                        dbData.AppendLine($"- {s.FullName} | Группа: {group?.Name ?? "не назначена"} | Тестов пройдено: {testsCompleted}");
                    }
                }

                // 3. ДАННЫЕ ДЛЯ ПРЕПОДАВАТЕЛЯ - только его дисциплины и связанные группы
                else if (isLecturer)
                {
                    var lecturer = await context.Lecturers.FirstOrDefaultAsync(l => l.Id == userId);
                    dbData.AppendLine($"\n=== ИНФОРМАЦИЯ О ПРЕПОДАВАТЕЛЕ ===");
                    dbData.AppendLine($"ФИО: {lecturer?.FullName}");
                    dbData.AppendLine($"Кафедра: {lecturer?.Department ?? "не указана"}");
                    dbData.AppendLine($"Должность: {lecturer?.Position ?? "не указана"}");

                    // Дисциплины преподавателя
                    var myDisciplines = await context.Disciplines
                        .Include(d => d.Course)
                        .Include(d => d.OpenGroups)
                        .Where(d => d.DisciplineLecturers.Any(dl => dl.LecturerId == userId))
                        .ToListAsync();

                    dbData.AppendLine($"\n=== ВАШИ ДИСЦИПЛИНЫ ({myDisciplines.Count} шт.) ===");
                    foreach (var d in myDisciplines)
                    {
                        var materialsCount = await context.Materials.CountAsync(m => m.DisciplineId == d.Id);
                        var testsCount = await context.Tests.CountAsync(t => t.DisciplineId == d.Id);
                        dbData.AppendLine($"- {d.Name} ({d.Course?.Code}) | {d.CourseNumber} курс, {d.Semester} семестр");
                        dbData.AppendLine($"  Материалов: {materialsCount}, Тестов: {testsCount}");

                        if (d.OpenGroups.Any())
                        {
                            var groupsInfo = new List<string>();
                            foreach (var group in d.OpenGroups)
                            {
                                var studentCount = await context.Students.CountAsync(s => s.StudentGroupId == group.Id);
                                groupsInfo.Add($"{group.Name} ({studentCount} студ.)");
                            }
                            dbData.AppendLine($"  Доступно для групп: {string.Join(", ", groupsInfo)}");
                        }
                        else
                        {
                            dbData.AppendLine($"  Доступно для групп: нет");
                        }
                    }

                    // Студенты в группах преподавателя
                    var allGroups = myDisciplines.SelectMany(d => d.OpenGroups).Distinct().ToList();
                    if (allGroups.Any())
                    {
                        dbData.AppendLine($"\n=== СТУДЕНТЫ В ВАШИХ ГРУППАХ ===");
                        foreach (var group in allGroups)
                        {
                            var students = await context.Students
                                .Where(s => s.StudentGroupId == group.Id)
                                .Select(s => s.FullName)
                                .ToListAsync();

                            dbData.AppendLine($"\nГруппа {group.Name} (всего {students.Count} студентов):");
                            foreach (var student in students.Take(15))
                            {
                                dbData.AppendLine($"  - {student}");
                            }
                            if (students.Count > 15)
                            {
                                dbData.AppendLine($"  ... и ещё {students.Count - 15} студентов");
                            }
                        }
                    }

                    // Результаты тестов студентов
                    var myDisciplineIds = myDisciplines.Select(d => d.Id).ToList();
                    var studentResults = await context.TestResults
                        .Include(tr => tr.Test)
                        .Include(tr => tr.Student)
                        .Where(tr => myDisciplineIds.Contains(tr.Test.DisciplineId))
                        .Take(50)
                        .ToListAsync();

                    if (studentResults.Any())
                    {
                        dbData.AppendLine($"\n=== РЕЗУЛЬТАТЫ СТУДЕНТОВ ПО ВАШИМ ДИСЦИПЛИНАМ ===");
                        foreach (var result in studentResults.GroupBy(r => r.Student).Take(20))
                        {
                            var avgScore = result.Average(r => (double)r.Score / r.MaxScore * 100);
                            dbData.AppendLine($"- {result.Key?.FullName}: средний балл {avgScore:F0}%");
                        }
                    }
                }

                // 4. ДАННЫЕ ДЛЯ СТУДЕНТА - только его группа и доступные дисциплины
                else if (isStudent)
                {
                    var student = await context.Students
                        .Include(s => s.StudentGroup)
                            .ThenInclude(g => g.Course)
                        .FirstOrDefaultAsync(s => s.Id == userId);

                    if (student != null)
                    {
                        dbData.AppendLine($"\n=== ИНФОРМАЦИЯ О СТУДЕНТЕ ===");
                        dbData.AppendLine($"ФИО: {student.FullName}");
                        dbData.AppendLine($"Студенческий ID: {student.StudentId ?? "не указан"}");

                        if (student.StudentGroup != null)
                        {
                            dbData.AppendLine($"Группа: {student.StudentGroup.Name}");
                            dbData.AppendLine($"Направление: {student.StudentGroup.Course?.Code} - {student.StudentGroup.Course?.Name}");
                            dbData.AppendLine($"Год поступления: {student.StudentGroup.YearOfAdmission}");

                            // Дисциплины, доступные группе
                            var accessibleDisciplines = await context.Disciplines
                                .Include(d => d.Course)
                                .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                                .ToListAsync();

                            dbData.AppendLine($"\n=== ДОСТУПНЫЕ ДИСЦИПЛИНЫ ({accessibleDisciplines.Count} шт.) ===");
                            foreach (var d in accessibleDisciplines)
                            {
                                dbData.AppendLine($"- {d.Name} ({d.Course?.Code}) | {d.CourseNumber} курс, {d.Semester} семестр");
                            }

                            // Тесты для его дисциплин
                            var accessibleDisciplineIds = accessibleDisciplines.Select(d => d.Id).ToList();
                            var availableTests = await context.Tests
                                .Include(t => t.Discipline)
                                .Where(t => accessibleDisciplineIds.Contains(t.DisciplineId) && t.IsPublished)
                                .ToListAsync();

                            if (availableTests.Any())
                            {
                                dbData.AppendLine($"\n=== ДОСТУПНЫЕ ТЕСТЫ ({availableTests.Count} шт.) ===");
                                foreach (var t in availableTests)
                                {
                                    var deadline = t.Deadline.HasValue ? $"до {t.Deadline.Value:dd.MM.yyyy}" : "без дедлайна";
                                    dbData.AppendLine($"- {t.Title} ({t.Discipline?.Name}) | {deadline}");
                                }
                            }

                            // Результаты тестов студента
                            var testResults = await context.TestResults
                                .Include(r => r.Test)
                                    .ThenInclude(t => t.Discipline)
                                .Where(r => r.StudentId == userId)
                                .OrderByDescending(r => r.CompletedAt)
                                .ToListAsync();

                            if (testResults.Any())
                            {
                                dbData.AppendLine($"\n=== ВАШИ РЕЗУЛЬТАТЫ ТЕСТОВ ({testResults.Count} шт.) ===");
                                foreach (var r in testResults)
                                {
                                    var percentage = (double)r.Score / r.MaxScore * 100;
                                    var grade = percentage >= 85 ? "отлично" : percentage >= 70 ? "хорошо" : percentage >= 50 ? "удовлетворительно" : "неудовлетворительно";
                                    dbData.AppendLine($"- {r.Test.Title} ({r.Test.Discipline?.Name}): {r.Score}/{r.MaxScore} ({percentage:F0}%) - {grade}");
                                }
                            }

                            // Дедлайны
                            var deadlines = await context.Tests
                                .Include(t => t.Discipline)
                                .Where(t => accessibleDisciplineIds.Contains(t.DisciplineId) &&
                                            t.Deadline.HasValue &&
                                            t.Deadline.Value > DateTime.UtcNow)
                                .OrderBy(t => t.Deadline)
                                .ToListAsync();

                            if (deadlines.Any())
                            {
                                dbData.AppendLine($"\n=== БЛИЖАЙШИЕ ДЕДЛАЙНЫ ===");
                                foreach (var d in deadlines.Take(5))
                                {
                                    var daysLeft = (d.Deadline.Value - DateTime.UtcNow).Days;
                                    dbData.AppendLine($"- {d.Title}: {d.Deadline.Value:dd.MM.yyyy} (осталось {daysLeft} дн.)");
                                }
                            }
                        }
                        else
                        {
                            dbData.AppendLine("Вы ещё не прикреплены к группе. Обратитесь к администратору.");
                        }
                    }
                }

                dbData.AppendLine("\n=== КОНЕЦ ДАННЫХ ИЗ СИСТЕМЫ ===\n");

                // ==================== ПОИСК В УЧЕБНЫХ МАТЕРИАЛАХ (с учётом доступа) ====================

                var chunks = await GetAccessibleChunksForUserAsync(context, userId);
                var knowledgeContext = new StringBuilder();

                if (chunks.Any())
                {
                    var results = SearchRelevantChunks(query, chunks);
                    knowledgeContext.AppendLine("=== ИНФОРМАЦИЯ ИЗ УЧЕБНЫХ МАТЕРИАЛОВ ===\n");
                    foreach (var result in results.Take(5))
                    {
                        knowledgeContext.AppendLine(result.Text);
                        knowledgeContext.AppendLine();
                    }
                    knowledgeContext.AppendLine("=== КОНЕЦ ИНФОРМАЦИИ ИЗ МАТЕРИАЛОВ ===\n");
                }

                // История диалога
                var historyContext = await GetRecentHistoryContextAsync(context, userId);

                // ==================== ОТПРАВКА В NEURAL NETWORK ====================

                var roleDescription = isAdmin ? "Администратор системы" : (isLecturer ? "Преподаватель" : "Студент");

                var systemPrompt = $@"Ты - AI-помощник образовательной платформы. Пользователь является: {roleDescription}.

ПРАВИЛА ДОСТУПА К ДАННЫМ:
1. {roleDescription} имеет доступ ТОЛЬКО к данным, указанным ниже
2. НЕ показывай данные, которые не относятся к роли пользователя
3. Если пользователь спрашивает то, на что у него нет прав - вежливо объясни, что это доступно только администратору
4. Отвечай на русском языке, будь полезным и дружелюбным

ТЫ МОЖЕШЬ ОТВЕЧАТЬ НА ВОПРОСЫ О:
- Личных данных пользователя
- Доступных курсах и дисциплинах
- Тестах и результатах
- Дедлайнах
- Учебных материалах
- Расписании (если есть)

НЕ РАЗГЛАШАЙ ДАННЫЕ ДРУГИХ ПОЛЬЗОВАТЕЛЕЙ, ЕСЛИ НЕТ НА ЭТО ПРАВ.";

                var fullContext = new StringBuilder();
                fullContext.AppendLine(historyContext);
                fullContext.AppendLine(dbData.ToString());
                fullContext.AppendLine(knowledgeContext.ToString());

                var request = new
                {
                    model = _ollamaModel,
                    messages = new[]
                    {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"ВОПРОС: {query}\n\nДОСТУПНЫЕ ДЛЯ ВАС ДАННЫЕ:\n{fullContext}\n\nОтветьте на вопрос, используя только доступные вам данные:" }
            },
                    stream = false,
                    options = new
                    {
                        temperature = 0.3,
                        num_predict = 1500,
                        top_p = 0.9
                    }
                };

                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/chat", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);
                    var answer = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

                    await SaveToHistory(context, userId, query, answer, "Данные с учётом роли пользователя");
                    return answer ?? "Не удалось получить ответ.";
                }

                return "Извините, возникла ошибка. Попробуйте позже.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AskBotAsync");
                return "Произошла ошибка. Пожалуйста, попробуйте еще раз.";
            }
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ БЫСТРЫХ ОТВЕТОВ ====================

        private string GetSourceFromChunk(string chunkText)
        {
            // Ищем ИСТОЧНИК
            var sourceMatch = System.Text.RegularExpressions.Regex.Match(chunkText, @"ИСТОЧНИК:\s*(.+?)(?:\n|$)");
            if (sourceMatch.Success)
            {
                return $"📁 Информация находится в материале: **{sourceMatch.Groups[1].Value.Trim()}**";
            }

            // Ищем Название материала
            var materialMatch = System.Text.RegularExpressions.Regex.Match(chunkText, @"Название материала:\s*(.+?)(?:\n|$)");
            if (materialMatch.Success)
            {
                return $"📁 Информация находится в материале: **{materialMatch.Groups[1].Value.Trim()}**";
            }

            return "📁 Информация найдена в базе знаний, но источник не указан.";
        }

        private string GetShortSummaryFromChunk(string chunkText)
        {
            // Извлекаем содержимое
            var contentMatch = System.Text.RegularExpressions.Regex.Match(chunkText, @"СОДЕРЖАНИЕ:\s*\n║\s*(.+?)(?:\n╚|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
            {
                var content = contentMatch.Groups[1].Value
                    .Replace("\n║ ", " ")
                    .Replace("\n", " ")
                    .Replace("║", "")
                    .Trim();

                // Берем первые 200-300 символов
                if (content.Length > 250)
                {
                    return content.Substring(0, 250).Trim() + "...";
                }
                return content;
            }

            return chunkText.Length > 300 ? chunkText.Substring(0, 300) + "..." : chunkText;
        }

        private string GetDefinitionFromChunk(string chunkText, string query)
        {
            var contentMatch = System.Text.RegularExpressions.Regex.Match(chunkText, @"СОДЕРЖАНИЕ:\s*\n║\s*(.+?)(?:\n╚|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
            {
                var content = contentMatch.Groups[1].Value
                    .Replace("\n║ ", "\n")
                    .Replace("║", "")
                    .Trim();

                var lines = content.Split('\n');
                var firstLine = lines.FirstOrDefault() ?? content;

                // Берем первое предложение или первую строку
                var endIndex = firstLine.IndexOfAny(new[] { '.', '!', '?' });
                if (endIndex > 0 && endIndex < 200)
                {
                    return firstLine.Substring(0, endIndex + 1);
                }

                if (firstLine.Length > 200)
                {
                    return firstLine.Substring(0, 200) + "...";
                }

                return firstLine;
            }

            return chunkText.Length > 200 ? chunkText.Substring(0, 200) + "..." : chunkText;
        }

        private string GetContentFromChunk(string chunkText)
        {
            var contentMatch = System.Text.RegularExpressions.Regex.Match(chunkText, @"СОДЕРЖАНИЕ:\s*\n║\s*(.+?)(?:\n╚|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (contentMatch.Success)
            {
                return contentMatch.Groups[1].Value
                    .Replace("\n║ ", "\n")
                    .Replace("║", "")
                    .Trim();
            }

            return chunkText;
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ ЖИВЫХ ДАННЫХ ====================

        private async Task<string> GetUserDisciplinesAsync(AppDbContext context, int userId)
        {
            var user = await context.Users.FindAsync(userId);
            if (user == null) return "Пользователь не найден.";

            var isAdmin = await context.UserRoles
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Admin");

            if (isAdmin)
            {
                var allDisciplines = await context.Disciplines
                    .Include(d => d.Course)
                    .OrderBy(d => d.Name)
                    .ToListAsync();

                if (!allDisciplines.Any())
                    return "В системе пока нет дисциплин.";

                var result = "📚 **Все дисциплины в системе:**\n\n";
                foreach (var disc in allDisciplines)
                {
                    result += $"• {disc.Name} ({disc.Course?.Code})\n";
                }
                return result;
            }

            var isLecturer = await context.UserRoles
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Lecturer");

            if (isLecturer)
            {
                var disciplines = await context.Disciplines
                    .Include(d => d.Course)
                    .Where(d => d.DisciplineLecturers.Any(dl => dl.LecturerId == userId))
                    .OrderBy(d => d.Name)
                    .ToListAsync();

                if (!disciplines.Any())
                    return "У вас пока нет дисциплин.";

                var result = "📚 **Ваши дисциплины (преподаватель):**\n\n";
                foreach (var disc in disciplines)
                {
                    var materialsCount = await context.Materials.CountAsync(m => m.DisciplineId == disc.Id);
                    var testsCount = await context.Tests.CountAsync(t => t.DisciplineId == disc.Id);
                    result += $"• **{disc.Name}** ({disc.Course?.Code})\n";
                    result += $"  📄 Материалов: {materialsCount} | 📝 Тестов: {testsCount}\n";
                }
                return result;
            }

            var student = await context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == userId);

            if (student?.StudentGroup == null)
                return "Вы пока не прикреплены ни к одной группе. Обратитесь к администратору.";

            var accessibleDisciplines = await context.Disciplines
                .Include(d => d.Course)
                .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                .OrderBy(d => d.Name)
                .ToListAsync();

            if (!accessibleDisciplines.Any())
                return "Для вашей группы пока нет доступных дисциплин.";

            var resultText = "📚 **Ваши дисциплины:**\n\n";
            foreach (var disc in accessibleDisciplines)
            {
                resultText += $"• **{disc.Name}** ({disc.Course?.Code})\n";
                resultText += $"  🎓 {disc.CourseNumber} курс, {disc.Semester} семестр\n";
            }
            return resultText;
        }

        private async Task<string> GetTestDeadlinesAsync(AppDbContext context, int userId)
        {
            var student = await context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == userId);

            if (student?.StudentGroup == null)
                return "Вы не прикреплены к группе.";

            var accessibleDisciplineIds = await context.Disciplines
                .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                .Select(d => d.Id)
                .ToListAsync();

            var testsWithDeadlines = await context.Tests
                .Include(t => t.Discipline)
                .Where(t => accessibleDisciplineIds.Contains(t.DisciplineId) &&
                            t.Deadline.HasValue &&
                            t.Deadline.Value > DateTime.UtcNow)
                .OrderBy(t => t.Deadline)
                .ToListAsync();

            if (!testsWithDeadlines.Any())
                return "У вас нет предстоящих дедлайнов по тестам.";

            var result = "⏰ **Предстоящие дедлайны тестов:**\n\n";
            foreach (var test in testsWithDeadlines)
            {
                var daysLeft = (test.Deadline.Value - DateTime.UtcNow).Days;
                result += $"• **{test.Title}** ({test.Discipline?.Name})\n";
                result += $"  📅 Дедлайн: {test.Deadline.Value:dd.MM.yyyy HH:mm} (осталось {daysLeft} дн.)\n";
            }
            return result;
        }

        private async Task<string> GetTestResultsAsync(AppDbContext context, int userId)
        {
            var results = await context.TestResults
                .Include(r => r.Test)
                    .ThenInclude(t => t.Discipline)
                .Where(r => r.StudentId == userId)
                .OrderByDescending(r => r.CompletedAt)
                .Take(10)
                .ToListAsync();

            if (!results.Any())
                return "Вы ещё не проходили тесты.";

            var result = "📊 **Ваши результаты тестов:**\n\n";
            foreach (var res in results)
            {
                var percentage = (double)res.Score / res.MaxScore * 100;
                var grade = percentage >= 85 ? "5" : percentage >= 70 ? "4" : percentage >= 50 ? "3" : "2";
                result += $"• **{res.Test.Title}** ({res.Test.Discipline?.Name})\n";
                result += $"  📝 Баллы: {res.Score}/{res.MaxScore} ({percentage:F1}%) | Оценка: {grade}\n";
                result += $"  📅 Пройден: {res.CompletedAt:dd.MM.yyyy}\n\n";
            }
            return result;
        }

        private async Task<string> GetUserGroupAsync(AppDbContext context, int userId)
        {
            var student = await context.Students
                .Include(s => s.StudentGroup)
                    .ThenInclude(g => g.Course)
                .FirstOrDefaultAsync(s => s.Id == userId);

            if (student?.StudentGroup == null)
                return "Вы не прикреплены ни к одной группе.";

            return $"🎓 **Ваша группа:** {student.StudentGroup.Name}\n" +
                   $"📚 **Направление:** {student.StudentGroup.Course?.Code} - {student.StudentGroup.Course?.Name}\n" +
                   $"📅 **Год поступления:** {student.StudentGroup.YearOfAdmission}";
        }

        private async Task<string> GetAvailableTestsAsync(AppDbContext context, int userId)
        {
            var student = await context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == userId);

            if (student?.StudentGroup == null)
                return "Вы не прикреплены к группе.";

            var accessibleDisciplineIds = await context.Disciplines
                .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                .Select(d => d.Id)
                .ToListAsync();

            var availableTests = await context.Tests
                .Include(t => t.Discipline)
                .Where(t => accessibleDisciplineIds.Contains(t.DisciplineId) &&
                            t.IsPublished &&
                            (!t.Deadline.HasValue || t.Deadline.Value > DateTime.UtcNow))
                .OrderBy(t => t.Deadline)
                .ToListAsync();

            if (!availableTests.Any())
                return "У вас пока нет доступных тестов.";

            var result = "📝 **Доступные тесты:**\n\n";
            foreach (var test in availableTests)
            {
                var deadline = test.Deadline.HasValue ? $"Дедлайн: {test.Deadline.Value:dd.MM.yyyy}" : "Без дедлайна";
                result += $"• **{test.Title}** ({test.Discipline?.Name})\n";
                result += $"  ⏱ Длительность: {test.DurationMinutes} мин. | {deadline}\n";
            }
            return result;
        }

        public async Task IndexMaterialAsync(Material material, bool forceReindex = false)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!material.IsIndexed && !forceReindex)
            {
                _logger.LogInformation($"Материал {material.Title} не отмечен для индексации");
                return;
            }

            _logger.LogInformation($"📚 Начинаем индексацию материала: {material.Title}");

            // Удаляем старые чанки
            var existingChunks = await context.RagChunks
                .Where(c => c.MaterialId == material.Id)
                .ToListAsync();

            if (existingChunks.Any())
            {
                context.RagChunks.RemoveRange(existingChunks);
                await context.SaveChangesAsync();
                _logger.LogInformation($"🗑️ Удалено {existingChunks.Count} старых чанков");
            }

            // Получаем текст для индексации
            var text = material.Content;
            if (!string.IsNullOrEmpty(material.FilePath))
            {
                text = await ExtractTextFromFileAsync(material.FilePath);
                _logger.LogInformation($"📄 Текст из файла: {text.Length} символов");
            }

            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning($"Нет текста для индексации материала {material.Title}");
                return;
            }

            // Разбиваем на чанки
            var chunks = SplitIntoChunks(text);
            _logger.LogInformation($"📊 Текст разбит на {chunks.Count} чанков");

            // Создаём запись в RagDocuments
            var ragDocument = await context.RagDocuments
                .FirstOrDefaultAsync(d => d.MaterialId == material.Id);

            if (ragDocument == null)
            {
                ragDocument = new RagDocument
                {
                    Title = material.Title,
                    MaterialId = material.Id,
                    DisciplineId = material.DisciplineId,
                    SourceType = "Material",
                    AccessLevel = AccessLevel.Discipline,
                    IndexedAt = DateTime.UtcNow
                };
                context.RagDocuments.Add(ragDocument);
                await context.SaveChangesAsync();
                _logger.LogInformation($"📄 Создан RagDocument с ID: {ragDocument.Id}");
            }

            // Создаём чанки с информацией об источнике (без странных символов)
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = new RagChunk
                {
                    DocumentId = ragDocument.Id,
                    Text = $"""
[ИСТОЧНИК: {material.Title}]
[ID МАТЕРИАЛА: {material.Id}]
--- СОДЕРЖАНИЕ ---
{chunks[i]}
--- КОНЕЦ МАТЕРИАЛА ---
Название: {material.Title}
""",
                    ChunkIndex = i,
                    CreatedAt = DateTime.UtcNow,
                    DisciplineId = material.DisciplineId,
                    MaterialId = material.Id,
                    Embedding = "[]"
                };
                context.RagChunks.Add(chunk);
            }

            material.IsIndexed = true;
            await context.SaveChangesAsync();
            _logger.LogInformation($"✅ Материал {material.Title} проиндексирован ({chunks.Count} чанков)");
        }

        public async Task DeleteMaterialIndexAsync(int materialId)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var chunks = await context.RagChunks
                .Where(c => c.MaterialId == materialId)
                .ToListAsync();

            if (chunks.Any())
            {
                context.RagChunks.RemoveRange(chunks);
                await context.SaveChangesAsync();
                _logger.LogInformation($"🗑️ Индекс материала {materialId} удален");
            }

            var ragDocument = await context.RagDocuments
                .FirstOrDefaultAsync(d => d.MaterialId == materialId);

            if (ragDocument != null)
            {
                context.RagDocuments.Remove(ragDocument);
                await context.SaveChangesAsync();
                _logger.LogInformation($"🗑️ Удалён RagDocument для материала {materialId}");
            }
        }

        public async Task RebuildAllIndexesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _logger.LogInformation("🔄 Начало перестроения индексов...");

            await context.Database.ExecuteSqlRawAsync("DELETE FROM RagChunks");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM RagDocuments");

            var materials = await context.Materials
                .Where(m => m.IsIndexed)
                .ToListAsync();

            foreach (var material in materials)
            {
                await IndexMaterialAsync(material, forceReindex: true);
            }

            _logger.LogInformation($"✅ Перестроение индексов завершено. Обработано {materials.Count} материалов");
        }

        private async Task<List<RagChunk>> GetAccessibleChunksForUserAsync(AppDbContext context, int userId)
        {
            try
            {
                Console.WriteLine($"=== GetAccessibleChunksForUserAsync ===");
                Console.WriteLine($"UserId: {userId}");

                var user = await context.Users.FindAsync(userId);
                if (user == null)
                {
                    Console.WriteLine("User not found");
                    return new List<RagChunk>();
                }

                var isAdmin = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Admin");

                if (isAdmin)
                {
                    Console.WriteLine("Admin - returning all chunks");
                    return await context.RagChunks.ToListAsync();
                }

                var isLecturer = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Lecturer");

                if (isLecturer)
                {
                    // Получаем ID дисциплин, которые ведёт преподаватель
                    var disciplineIds = await context.DisciplineLecturers
                        .Where(dl => dl.LecturerId == userId)
                        .Select(dl => dl.DisciplineId)
                        .ToListAsync();

                    Console.WriteLine($"Lecturer - discipline ids: {string.Join(", ", disciplineIds)}");

                    if (!disciplineIds.Any())
                    {
                        return new List<RagChunk>();
                    }

                    // Получаем чанки для этих дисциплин
                    var chunks = await context.RagChunks
                        .Where(c => c.DisciplineId.HasValue && disciplineIds.Contains(c.DisciplineId.Value))
                        .ToListAsync();

                    Console.WriteLine($"Lecturer - found {chunks.Count} chunks");
                    return chunks;
                }

                // Студент
                Console.WriteLine("Processing as Student");

                var student = await context.Students
                    .Include(s => s.StudentGroup)
                    .FirstOrDefaultAsync(s => s.Id == userId);

                if (student?.StudentGroup == null)
                {
                    Console.WriteLine("Student has no group");
                    return new List<RagChunk>();
                }

                var groupId = student.StudentGroup.Id;
                Console.WriteLine($"Student group ID: {groupId}");

                // Получаем дисциплины, доступные группе студента
                var accessibleDisciplineIds = await context.Disciplines
                    .Where(d => d.OpenGroups.Any(g => g.Id == groupId))
                    .Select(d => d.Id)
                    .ToListAsync();

                Console.WriteLine($"Accessible discipline ids: {string.Join(", ", accessibleDisciplineIds)}");

                if (!accessibleDisciplineIds.Any())
                {
                    return new List<RagChunk>();
                }

                // Получаем чанки для этих дисциплин
                var studentChunks = await context.RagChunks
                    .Where(c => c.DisciplineId.HasValue && accessibleDisciplineIds.Contains(c.DisciplineId.Value))
                    .ToListAsync();

                Console.WriteLine($"Student - found {studentChunks.Count} chunks");

                return studentChunks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAccessibleChunksForUserAsync: {ex.Message}");
                _logger.LogError(ex, "Error in GetAccessibleChunksForUserAsync");
                return new List<RagChunk>();
            }
        }

        private List<RagChunk> SearchRelevantChunks(string query, List<RagChunk> chunks)
        {
            // Очищаем запрос от знаков препинания
            var cleanQuery = new string(query.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
            var queryLower = cleanQuery.ToLower();

            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();

            Console.WriteLine($"Clean query: {cleanQuery}");
            Console.WriteLine($"Query words: {string.Join(", ", queryWords)}");

            var results = new List<(RagChunk Chunk, int Score, string MatchedWord)>();

            foreach (var chunk in chunks)
            {
                int score = 0;
                var chunkLower = chunk.Text.ToLower();
                List<string> matchedWords = new List<string>();

                foreach (var word in queryWords)
                {
                    if (chunkLower.Contains(word))
                    {
                        score++;
                        matchedWords.Add(word);
                        Console.WriteLine($"  Match: '{word}' in chunk for material {chunk.MaterialId}");
                    }
                }

                // Проверяем точное совпадение всей фразы
                if (cleanQuery.Length > 5 && chunkLower.Contains(cleanQuery))
                {
                    score += 3;
                    Console.WriteLine($"  Phrase match: '{cleanQuery}'");
                }

                // Проверяем совпадение без последней буквы (для слов с вопросом)
                foreach (var word in queryWords)
                {
                    if (word.EndsWith("?"))
                    {
                        var wordWithoutQuestion = word.TrimEnd('?');
                        if (chunkLower.Contains(wordWithoutQuestion))
                        {
                            score++;
                            Console.WriteLine($"  Match without ?: '{wordWithoutQuestion}'");
                        }
                    }
                }

                if (score > 0)
                {
                    results.Add((chunk, score, string.Join(", ", matchedWords)));
                    Console.WriteLine($"  Chunk score: {score}, matched: {string.Join(", ", matchedWords)}");
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .Take(5)
                .Select(r => r.Chunk)
                .ToList();
        }

        private async Task<string> GetOllamaResponseAsync(string query, string context)
        {
            try
            {
                var queryLower = query.ToLower();

                // Проверяем, спрашивает ли пользователь об источнике
                if (queryLower.Contains("каком материал") ||
                    queryLower.Contains("каком файл") ||
                    queryLower.Contains("источник") ||
                    queryLower.Contains("где найти") ||
                    queryLower.Contains("каком материале"))
                {
                    var sourceMatch = System.Text.RegularExpressions.Regex.Match(context, @"ИСТОЧНИК:\s*(.+?)(?:\n|$)");
                    if (sourceMatch.Success)
                    {
                        return $"Информация находится в материале: {sourceMatch.Groups[1].Value}";
                    }
                    return "Источник не указан в базе знаний.";
                }

                // Проверяем, спрашивает ли пользователь о содержании
                if (queryLower.Contains("о чем") ||
                    queryLower.Contains("что там") ||
                    queryLower.Contains("содержание") ||
                    queryLower.Contains("что написано"))
                {
                    // Извлекаем содержимое из контекста
                    var contentMatch = System.Text.RegularExpressions.Regex.Match(context, @"СОДЕРЖАНИЕ:\s*\n║\s*(.+?)(?:\n╚|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (contentMatch.Success)
                    {
                        var extractedContent = contentMatch.Groups[1].Value
                            .Replace("\n║ ", "\n")
                            .Replace("║", "")
                            .Trim();
                        return extractedContent.Length > 500 ? extractedContent.Substring(0, 500) + "..." : extractedContent;
                    }

                    // Альтернативный поиск
                    var lines = context.Split('\n');
                    var contentLines = new List<string>();
                    bool inContent = false;
                    foreach (var line in lines)
                    {
                        if (line.Contains("СОДЕРЖАНИЕ:"))
                        {
                            inContent = true;
                            continue;
                        }
                        if (inContent && (line.Contains("═══════════════════════════════════════════════════════════════") || line.Contains("Название материала")))
                        {
                            break;
                        }
                        if (inContent)
                        {
                            var cleanedLine = line.Replace("║", "").Trim();
                            if (!string.IsNullOrEmpty(cleanedLine))
                            {
                                contentLines.Add(cleanedLine);
                            }
                        }
                    }

                    if (contentLines.Any())
                    {
                        return string.Join("\n", contentLines);
                    }
                }

                // Обычный запрос к Ollama
                var request = new
                {
                    model = _ollamaModel,
                    messages = new[]
                    {
                new
                {
                    role = "system",
                    content = @"Ты - помощник учебной платформы.

ПРАВИЛА:
1. Отвечай ТОЛЬКО на основе предоставленного контекста
2. Если пользователь спрашивает 'о чем там' - извлеки основную информацию из контекста
3. Если спрашивают 'в каком материале' - найди строку с ИСТОЧНИК:
4. Отвечай кратко и по делу
5. Используй русский язык"
                },
                new
                {
                    role = "user",
                    content = $"КОНТЕКСТ:\n{context}\n\nВОПРОС: {query}\n\nОтветь на основе контекста:"
                }
            },
                    stream = false,
                    options = new
                    {
                        temperature = 0.1,
                        num_predict = 500
                    }
                };

                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/chat", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);
                    var answer = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
                    return answer ?? "Не удалось получить ответ.";
                }

                // Если Ollama не отвечает, возвращаем извлечённое содержимое
                var fallbackMatch = System.Text.RegularExpressions.Regex.Match(context, @"СОДЕРЖАНИЕ:\s*\n║\s*(.+?)(?:\n╚|$)", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (fallbackMatch.Success)
                {
                    return fallbackMatch.Groups[1].Value.Replace("\n║ ", "\n").Replace("║", "").Trim();
                }

                return "Не удалось извлечь информацию из контекста.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama request error");
                return "Произошла ошибка при обработке запроса.";
            }
        }

        private string GetSourceFromContext(string context)
        {
            var sourceMatch = System.Text.RegularExpressions.Regex.Match(context, @"ИСТОЧНИК:\s*(.+?)(?:\n|$)");
            if (sourceMatch.Success)
            {
                return $"Найдена информация в материале: {sourceMatch.Groups[1].Value}";
            }

            var materialMatch = System.Text.RegularExpressions.Regex.Match(context, @"МАТЕРИАЛ:\s*(.+?)(?:\n|$)");
            if (materialMatch.Success)
            {
                return $"Найдена информация в материале: {materialMatch.Groups[1].Value}";
            }

            return $"Найдена информация в: {context.Split('\n').FirstOrDefault()?.Substring(0, Math.Min(50, context.Length))}...";
        }

        private async Task<string> ExtractTextFromFileAsync(string filePath)
        {
            try
            {
                string fullPath = filePath;
                if (filePath.StartsWith("/uploads/"))
                {
                    var webRootPath = _env.WebRootPath ?? _env.ContentRootPath;
                    fullPath = Path.Combine(webRootPath, filePath.TrimStart('/'));
                }

                if (!System.IO.File.Exists(fullPath))
                {
                    _logger.LogWarning($"Файл не найден: {fullPath}");
                    return "";
                }

                var extension = Path.GetExtension(fullPath).ToLower();

                if (extension == ".txt")
                {
                    return await System.IO.File.ReadAllTextAsync(fullPath);
                }
                else if (extension == ".pdf")
                {
                    return $"PDF файл: {Path.GetFileName(filePath)}";
                }
                else if (extension == ".docx")
                {
                    return $"DOCX файл: {Path.GetFileName(filePath)}";
                }
                else
                {
                    return $"Файл: {Path.GetFileName(filePath)}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error extracting text from {filePath}");
                return "";
            }
        }

        private List<string> SplitIntoChunks(string text, int chunkSize = 500, int overlap = 100)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text)) return chunks;

            for (int i = 0; i < text.Length; i += chunkSize - overlap)
            {
                var chunk = text.Substring(i, Math.Min(chunkSize, text.Length - i));
                chunks.Add(chunk);
                if (i + chunkSize >= text.Length) break;
            }
            return chunks;
        }

        private async Task SaveToHistory(AppDbContext context, int userId, string userMessage, string botResponse, string retrievedContext = null)
        {
            try
            {
                var history = new ChatHistory
                {
                    UserId = userId,
                    UserMessage = userMessage,
                    BotResponse = botResponse,
                    RetrievedContext = retrievedContext,
                    Timestamp = DateTime.UtcNow
                };
                context.ChatHistories.Add(history);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving to history");
            }
        }

        private async Task<string> GetRecentHistoryContextAsync(AppDbContext context, int userId, int maxMessages = 5)
        {
            var history = await context.ChatHistories
                .Where(h => h.UserId == userId)
                .OrderByDescending(h => h.Timestamp)
                .Take(maxMessages)
                .OrderBy(h => h.Timestamp)
                .ToListAsync();

            if (!history.Any()) return "";

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("Вот история предыдущих сообщений от пользователя (для контекста):\n");

            foreach (var item in history)
            {
                contextBuilder.AppendLine($"Пользователь: {item.UserMessage}");
                contextBuilder.AppendLine($"Помощник: {item.BotResponse}");
                contextBuilder.AppendLine();
            }

            return contextBuilder.ToString();
        }
    }
}