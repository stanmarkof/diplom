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
        private readonly HttpClient _httpClient;
        private readonly string _ollamaUrl = "http://localhost:11434";
        private readonly string _ollamaModel = "llama3.2:3b";

        public RagService(IServiceScopeFactory scopeFactory, ILogger<RagService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
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

                // Получаем доступные чанки для пользователя
                var chunks = await GetAccessibleChunksForUserAsync(context, userId);

                if (!chunks.Any())
                {
                    return "По вашему запросу ничего не найдено в базе знаний.";
                }

                // Поиск релевантных чанков (простой поиск по словам)
                var results = SearchRelevantChunks(query, chunks);

                if (!results.Any())
                {
                    return "Не удалось найти информацию по вашему запросу.";
                }

                // Формируем контекст
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("Вот информация из базы знаний по вашему вопросу:\n");
                foreach (var result in results.Take(5))
                {
                    contextBuilder.AppendLine($"- {result.Text.Substring(0, Math.Min(500, result.Text.Length))}");
                    contextBuilder.AppendLine();
                }

                // Отправляем запрос в Ollama
                return await GetOllamaResponseAsync(query, contextBuilder.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AskBotAsync");
                return "Произошла ошибка при поиске информации. Попробуйте позже.";
            }
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

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = new RagChunk
                {
                    Text = $"[МАТЕРИАЛ: {material.Title}] {chunks[i]}",
                    ChunkIndex = i,
                    CreatedAt = DateTime.UtcNow,
                    DisciplineId = material.DisciplineId,
                    MaterialId = material.Id,
                    DocumentId = -1000 - material.Id,
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
        }

        public async Task RebuildAllIndexesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _logger.LogInformation("🔄 Начало перестроения индексов...");

            await context.Database.ExecuteSqlRawAsync("DELETE FROM RagChunks");

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
            var user = await context.Users.FindAsync(userId);
            if (user == null) return new List<RagChunk>();

            var isAdmin = await context.UserRoles
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Admin");

            if (isAdmin)
            {
                return await context.RagChunks.ToListAsync();
            }

            var isLecturer = await context.UserRoles
                .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Lecturer");

            if (isLecturer)
            {
                var disciplineIds = await context.DisciplineLecturers
                    .Where(dl => dl.LecturerId == userId)
                    .Select(dl => dl.DisciplineId)
                    .ToListAsync();

                return await context.RagChunks
                    .Where(c => c.DisciplineId.HasValue && disciplineIds.Contains(c.DisciplineId.Value))
                    .ToListAsync();
            }

            var student = await context.Students
                .Include(s => s.StudentGroup)
                .FirstOrDefaultAsync(s => s.Id == userId);

            if (student?.StudentGroup != null)
            {
                var accessibleDisciplines = await context.Disciplines
                    .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                    .Select(d => d.Id)
                    .ToListAsync();

                return await context.RagChunks
                    .Where(c => c.DisciplineId.HasValue && accessibleDisciplines.Contains(c.DisciplineId.Value))
                    .ToListAsync();
            }

            return new List<RagChunk>();
        }

        private List<RagChunk> SearchRelevantChunks(string query, List<RagChunk> chunks)
        {
            var queryLower = query.ToLower();
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();

            var results = new List<(RagChunk Chunk, int Score)>();

            foreach (var chunk in chunks)
            {
                int score = 0;
                var chunkLower = chunk.Text.ToLower();

                foreach (var word in queryWords)
                {
                    if (chunkLower.Contains(word))
                    {
                        score++;
                    }
                }

                if (score > 0)
                {
                    results.Add((chunk, score));
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
                // Проверяем доступность Ollama
                var healthCheck = await _httpClient.GetAsync("/api/tags");
                if (!healthCheck.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ollama не доступен");
                    return FormatFallbackResponse(context);
                }

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
2. НЕ выдумывай факты, которых нет в контексте
3. Если информации нет - скажи: 'Информация не найдена в доступных материалах'
4. Отвечай кратко, понятно и по делу
5. Используй русский язык"
                        },
                        new
                        {
                            role = "user",
                            content = $"КОНТЕКСТ ИЗ БАЗЫ ЗНАНИЙ:\n{context}\n\nВОПРОС: {query}\n\nОтветь на основе информации из контекста:"
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
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/chat", content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);
                    var answer = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
                    return answer ?? "Не удалось получить ответ от модели.";
                }

                _logger.LogError($"Ollama error: {response.StatusCode}");
                return FormatFallbackResponse(context);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Ollama connection error");
                return "Не удалось подключиться к AI-помощнику. Убедитесь, что Ollama запущена (ollama serve).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama request error");
                return FormatFallbackResponse(context);
            }
        }

        private string FormatFallbackResponse(string context)
        {
            var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var relevantParts = lines.Take(5);
            return string.Join("\n", relevantParts);
        }

        private async Task<string> ExtractTextFromFileAsync(string filePath)
        {
            try
            {
                // Упрощенная версия - для .txt файлов
                return await Task.FromResult($"Содержимое файла: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error extracting text from {filePath}");
                return $"Ошибка извлечения текста: {ex.Message}";
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
    }
}