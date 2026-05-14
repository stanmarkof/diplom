using diplom.Data;
using diplom.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Security.Claims;

namespace diplom.Services
{
    public interface IRagService
    {
        Task<string> AskBotAsync(string query, int userId, bool useAI = true);
        Task<string> AskGuestBotAsync(string query);
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
        private readonly IConfiguration _configuration;
        private readonly string _ollamaModel;
        private readonly IEmbeddingService _embeddingService;

        private static readonly Dictionary<string, string[]> SearchKeywordBoosts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["дисциплин"] = new[] { "дисциплина", "курс", "семестр" },
            ["тест"] = new[] { "тесты", "экзамен", "вопрос" },
            ["материал"] = new[] { "файл", "лекци", "конспект", "загруз" },
            ["регистра"] = new[] { "код", "аккаунт", "подтвержден" },
            ["вход"] = new[] { "login", "авторизац", "логин" },
            ["препод"] = new[] { "лектор", "ведёт", "преподавател" },
            ["студент"] = new[] { "групп", "зачётк", "студенческ" },
            ["админ"] = new[] { "управлен", "администратор" },
            ["навига"] = new[] { "страниц", "раздел", "меню", "ссылк" },
        };

        public RagService(
            IServiceScopeFactory scopeFactory,
            ILogger<RagService> logger,
            IWebHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IEmbeddingService embeddingService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _env = env;
            _httpClient = httpClientFactory.CreateClient("ollama");
            _configuration = configuration;
            _ollamaModel = configuration["Ollama:Model"] ?? "llama3.2:3b";
            _embeddingService = embeddingService;
        }

        public async Task<string> AskBotAsync(string query, int userId, bool useAI = true)
        {
            var totalStopwatch = Stopwatch.StartNew();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                Console.WriteLine($"");
                Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║  🤖 ASK BOT - {(useAI ? "РЕЖИМ НЕЙРОСЕТИ" : "РЕЖИМ ЭМБЕДДИНГОВ")}                               ║");
                Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
                Console.WriteLine($"📝 Вопрос: \"{query}\"");
                Console.WriteLine($"👤 UserId: {userId}");
                Console.WriteLine($"🎮 Режим: {(useAI ? "Ollama (нейросеть)" : "Только эмбеддинги")}");
                Console.WriteLine($"");

                // Получаем роль пользователя
                var user = await context.Users.FindAsync(userId);

                var isAdmin = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Admin");

                var isLecturer = await context.UserRoles
                    .Join(context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
                    .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Lecturer");

                var isStudent = !isAdmin && !isLecturer && await context.Students.AsNoTracking().AnyAsync(s => s.Id == userId);

                var userRole = isAdmin ? "Admin" : isLecturer ? "Lecturer" : "Student";
                Console.WriteLine($"✅ Роль пользователя: {userRole}");

                // ==================== 1. БЫСТРЫЕ ОТВЕТЫ ====================

                var queryLower = query.ToLower().Trim();

                if (queryLower == "привет" || queryLower == "здравствуй" || queryLower == "hello")
                {
                    return GetGreetingMessage(userRole, user?.UserName);
                }

                if (queryLower == "помощь" || queryLower == "help" || queryLower == "команды")
                {
                    return GetHelpMessage(userRole);
                }

                // ==================== 1.5 ТОЧНЫЕ СОВПАДЕНИЯ ====================

               
                // ==================== 2. РЕЖИМ ТОЛЬКО ЭМБЕДДИНГИ ====================

                if (!useAI)
                {
                    // РЕЖИМ ТОЛЬКО ЭМБЕДДИНГИ - точный поиск по базе знаний
                    Console.WriteLine($"");
                    Console.WriteLine($"🔍 РЕЖИМ ЭМБЕДДИНГОВ: Поиск точных совпадений...");

                    var semanticResult = await SemanticSearchAllAsync(query, userRole);

                    if (semanticResult.HasValue && semanticResult.Value.Score > 0.55f)
                    {
                        totalStopwatch.Stop();
                        Console.WriteLine($"✅ НАЙДЕНО ПО СМЫСЛУ!");
                        Console.WriteLine($"   Релевантность: {semanticResult.Value.Score:P0}");
                        Console.WriteLine($"   Источник: {semanticResult.Value.Source}");
                        Console.WriteLine($"   Время: {totalStopwatch.ElapsedMilliseconds} мс");

                        await SaveToHistory(context, userId, query, semanticResult.Value.Answer, $"{semanticResult.Value.Source} (эмбеддинги)");
                        return semanticResult.Value.Answer;
                    }

                    // Проверяем личные данные из БД
                    var personalAnswer = await GetPersonalDataAnswerAsync(context, userId, query, isAdmin, isLecturer, isStudent);
                    if (personalAnswer != null)
                    {
                        totalStopwatch.Stop();
                        Console.WriteLine($"✅ Найдено в личных данных!");
                        await SaveToHistory(context, userId, query, personalAnswer, "Личные данные (эмбеддинги)");
                        return personalAnswer;
                    }

                    // Если ничего не найдено
                    totalStopwatch.Stop();
                    var notFoundMessage = "❌ **Информация не найдена**\n\n" +
                                         "В базе знаний нет информации по вашему вопросу.\n\n" +
                                         "💡 **Что делать?**\n" +
                                         "• Попробуйте переформулировать вопрос\n" +
                                         "• Включите режим нейросети для генерации ответа\n" +
                                         "• Обратитесь к администратору за помощью";

                    await SaveToHistory(context, userId, query, notFoundMessage, "Не найдено");
                    return notFoundMessage;
                }

                // ==================== 3. РЕЖИМ НЕЙРОСЕТИ (OLLAMA) ====================

                Console.WriteLine($"");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"🚀 РЕЖИМ НЕЙРОСЕТИ: Будет вызван Ollama");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                var ollamaStopwatch = Stopwatch.StartNew();

                // Сбор данных из БД
                Console.WriteLine($"📊 Сбор данных из БД для контекста...");
                var dbData = await CollectDatabaseDataAsync(context, userId, isAdmin, isLecturer, isStudent);
                Console.WriteLine($"✅ Данные из БД собраны. Размер: {dbData.Length} символов");

                // Поиск в учебных материалах
                Console.WriteLine($"📚 Поиск в учебных материалах (RAG)...");
                var chunks = await GetAccessibleChunksForUserAsync(context, userId);
                var knowledgeContext = new StringBuilder();

                if (chunks.Any())
                {
                    var results = SemanticSearchChunks(query, chunks, take: 5);
                    knowledgeContext.AppendLine("=== УЧЕБНЫЕ МАТЕРИАЛЫ ===\n");
                    foreach (var result in results)
                    {
                        knowledgeContext.AppendLine(result.Text);
                        knowledgeContext.AppendLine();
                    }
                    Console.WriteLine($"✅ Найдено релевантных чанков: {results.Count}");
                }
                else
                {
                    Console.WriteLine($"⚠️ Чанки не найдены. База знаний пуста.");
                }

                // История диалога
                var historyContext = await GetRecentHistoryContextAsync(context, userId);
                if (!string.IsNullOrEmpty(historyContext))
                {
                    Console.WriteLine($"📜 История диалога загружена. Размер: {historyContext.Length} символов");
                }

                // Формируем контекст
                var fullContext = new StringBuilder();
                fullContext.AppendLine(historyContext);
                fullContext.AppendLine(dbData);
                fullContext.AppendLine(knowledgeContext.ToString());

                var contextStr = fullContext.ToString();
                if (contextStr.Length > 4000)
                {
                    Console.WriteLine($"⚠️ Контекст слишком большой ({contextStr.Length} символов), сокращаем до 4000");
                    contextStr = contextStr.Substring(0, 4000) + "...(контекст сокращён)";
                }
                Console.WriteLine($"📦 Итоговый контекст: {contextStr.Length} символов");

                // Отправляем в Ollama
                var systemPrompt = $"Ты — AI-помощник системы обучения. Пользователь: {(isAdmin ? "Админ" : isLecturer ? "Преподаватель" : "Студент")}. Отвечай на основе контекста. Будь краток.";
                var userPayload = $"ВОПРОС: {query}\n\nКОНТЕКСТ:\n{contextStr}";

                Console.WriteLine($"");
                Console.WriteLine($"🤖 ОТПРАВКА ЗАПРОСА В OLLAMA...");
                Console.WriteLine($"📋 Модель: {_ollamaModel}");
                Console.WriteLine($"⏰ Время отправки: {DateTime.Now:HH:mm:ss.fff}");
                Console.WriteLine($"");

                var answer = await CallOllamaChatAsync(systemPrompt, userPayload, temperature: 0.3, maxTokens: 800);

                ollamaStopwatch.Stop();
                Console.WriteLine($"");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"✅ OLLAMA ОТВЕТИЛ за {ollamaStopwatch.ElapsedMilliseconds} мс");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"");

                if (answer != null)
                {
                    Console.WriteLine($"📝 Ответ получен, длина: {answer.Length} символов");
                    await SaveToHistory(context, userId, query, answer, "RAG+Ollama");
                    Console.WriteLine($"⏱️ ОБЩЕЕ ВРЕМЯ: {totalStopwatch.ElapsedMilliseconds} мс");
                    return answer;
                }

                return "Извините, нейросеть временно недоступна. Попробуйте позже или переключитесь в режим без нейросети.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                _logger.LogError(ex, "Error in AskBotAsync");
                return "Произошла ошибка. Пожалуйста, попробуйте еще раз.";
            }
        }

        private string NormalizeQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return query;

            var normalized = query.ToLower().Trim();

            // Удаляем лишние слова-паразиты
            var stopWords = new[] { "мне", "меня", "мене", "тебе", "ему", "ей", "нам", "вам", "им" };
            foreach (var word in stopWords)
            {
                normalized = normalized.Replace($" {word} ", " ");
                if (normalized.StartsWith($"{word} ")) normalized = normalized.Substring(word.Length + 1);
                if (normalized.EndsWith($" {word}")) normalized = normalized.Substring(0, normalized.Length - word.Length - 1);
            }

            // Заменяем синонимы
            var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["создать"] = "создать",
                ["сделать"] = "создать",
                ["добавить"] = "создать",
                ["создай"] = "создать",
                ["как мне"] = "как",
                ["как можно"] = "как",
                ["каким образом"] = "как",
                ["расскажи как"] = "как",
                ["объясни как"] = "как"
            };

            foreach (var syn in synonyms)
            {
                if (normalized.Contains(syn.Key))
                {
                    normalized = normalized.Replace(syn.Key, syn.Value);
                }
            }

            return normalized;
        }
        // ==================== СЕМАНТИЧЕСКИЙ ПОИСК ====================

        // Вместо (string Answer, string Source, float Score)? используйте отдельный класс или кортеж с именами полей
        private async Task<(string Answer, string Source, float Score)?> SemanticSearchAllAsync(string query, string userRole)
        {
            var stopwatch = Stopwatch.StartNew();

            // Нормализуем запрос для лучшего поиска
            var normalizedQuery = NormalizeQuery(query);
            Console.WriteLine($"  📝 Нормализованный запрос: '{normalizedQuery}'");

            // 1. Получаем эмбеддинг вопроса (используем оригинальный и нормализованный)
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
            var normalizedQueryEmbedding = await _embeddingService.GenerateEmbeddingAsync(normalizedQuery);

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var results = new List<(float Score, string Answer, string Source, string Title, string Keywords)>();

            // 2. Поиск по инструкциям
            Console.WriteLine($"  📋 Поиск по инструкциям...");
            var instructions = await context.BotInstructions
                .Where(i => i.IsActive && (i.Role == "All" || i.Role == userRole))
                .ToListAsync();

            foreach (var instr in instructions)
            {
                // Проверяем точное совпадение в заголовке (быстрый путь)
                var instrTitleNorm = NormalizeQuery(instr.Title);
                if (instrTitleNorm.Contains(normalizedQuery) || normalizedQuery.Contains(instrTitleNorm))
                {
                    Console.WriteLine($"    ✅ Точное совпадение заголовка: {instr.Title}");
                    results.Add((1.0f, instr.Answer, "Инструкция", instr.Title, instr.Title));
                    continue;
                }

                float similarity = 0;

                // Используем нормализованный эмбеддинг если есть
                if (!string.IsNullOrEmpty(instr.Embedding) && instr.Embedding != "[]")
                {
                    try
                    {
                        var instrEmbedding = JsonSerializer.Deserialize<float[]>(instr.Embedding);
                        if (instrEmbedding != null)
                        {
                            // Берем максимум из двух эмбеддингов
                            var sim1 = CosineSimilarity(queryEmbedding, instrEmbedding);
                            var sim2 = CosineSimilarity(normalizedQueryEmbedding, instrEmbedding);
                            similarity = Math.Max(sim1, sim2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠️ Ошибка десериализации эмбеддинга для {instr.Title}: {ex.Message}");
                    }
                }

                // Вычисляем текстовую релевантность для обоих вариантов
                var textRelevance1 = CalculateTextRelevance(query, instr.Title + " " + instr.Answer);
                var textRelevance2 = CalculateTextRelevance(normalizedQuery, instr.Title + " " + instr.Answer);
                float textRelevance = Math.Max(textRelevance1, textRelevance2);

                // Итоговая оценка
                float finalScore = (similarity * 0.5f) + (textRelevance * 0.5f);

                if (finalScore > 0.4f)
                {
                    results.Add((finalScore, instr.Answer, "Инструкция", instr.Title, instr.Title));
                    Console.WriteLine($"    - {instr.Title}: эмбеддинг={similarity:F2}, текст={textRelevance:F2}, итого={finalScore:F2}");
                }
            }

            // 3. Сортируем и выбираем лучший
            var sortedResults = results.OrderByDescending(r => r.Score).ToList();

            Console.WriteLine($"  🎯 Всего результатов: {results.Count}");
            foreach (var r in sortedResults.Take(3))
            {
                Console.WriteLine($"    - {r.Source}: '{r.Title}' (оценка: {r.Score:P1})");
            }

            var best = sortedResults.FirstOrDefault();

            stopwatch.Stop();

            if (best.Score > 0.55f)
            {
                Console.WriteLine($"  ✅ Лучший результат: {best.Source} - {best.Title} (оценка: {best.Score:P1})");
                return (best.Answer, best.Source, best.Score);
            }

            return null;
        }

        // Новый метод для вычисления текстовой релевантности
        private float CalculateTextRelevance(string query, string text)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
                return 0;

            var queryLower = query.ToLower();
            var textLower = text.ToLower();

            // Разбиваем на слова
            var queryWords = queryLower.Split(new[] { ' ', ',', '.', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var textWords = textLower.Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            if (queryWords.Length == 0)
                return 0;

            // Используем float для поддержки дробных значений
            float totalScore = 0;

            foreach (var qWord in queryWords)
            {
                if (qWord.Length < 3) continue; // Игнорируем короткие слова

                // Точное совпадение
                if (textLower.Contains(qWord))
                {
                    totalScore += 1.0f;
                }
                // Частичное совпадение для длинных слов
                else if (qWord.Length > 5)
                {
                    bool found = false;
                    foreach (var tWord in textWords)
                    {
                        if (tWord.Contains(qWord) || qWord.Contains(tWord))
                        {
                            totalScore += 0.5f;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        // Проверяем вхождение в текст целиком
                        if (textLower.Contains(qWord.Substring(0, qWord.Length - 1)))
                        {
                            totalScore += 0.3f;
                        }
                    }
                }
                else if (qWord.Length >= 3 && qWord.Length <= 5)
                {
                    // Для коротких слов проверяем точное вхождение
                    if (textLower.Contains(qWord))
                    {
                        totalScore += 0.8f;
                    }
                }
            }

            // Нормализуем (максимальное значение не должно превышать количество слов)
            float relevance = totalScore / queryWords.Length;

            // Дополнительный буст для фразовых совпадений
            if (queryLower.Length > 10 && textLower.Contains(queryLower))
            {
                relevance += 0.2f;
            }

            // Ограничиваем максимальное значение 1.0
            return Math.Min(relevance, 1.0f);
        }

        // Новый метод для расчета буста по ключевым словам
        private float CalculateKeywordBoost(string query, string title, string answer)
        {
            var queryLower = query.ToLower();
            var titleLower = title.ToLower();
            var answerLower = answer.ToLower();

            float boost = 0;

            // Словарь синонимов и связанных терминов
            var synonymMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["курс"] = new[] { "направление", "специальность", "course", "направлен" },
                ["дисциплин"] = new[] { "предмет", "discipline", "subject" },
                ["создать"] = new[] { "добавить", "создани", "создайте", "как создать" },
                ["групп"] = new[] { "группа", "group", "команда" },
                ["студент"] = new[] { "учащийся", "student" },
                ["преподавател"] = new[] { "учитель", "lecturer", "teacher" },
                ["тест"] = new[] { "экзамен", "test", "quiz", "проверк" },
                ["материал"] = new[] { "файл", "material", "документ" }
            };

            // Проверяем наличие ключевых слов в заголовке
            foreach (var kvp in synonymMap)
            {
                if (queryLower.Contains(kvp.Key))
                {
                    foreach (var synonym in kvp.Value)
                    {
                        if (titleLower.Contains(synonym) || answerLower.Contains(synonym))
                        {
                            boost += 0.3f;
                            break;
                        }
                    }
                }
            }

            // Точное совпадение слов
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in queryWords)
            {
                if (word.Length > 2)
                {
                    if (titleLower.Contains(word)) boost += 0.2f;
                    if (answerLower.Contains(word)) boost += 0.1f;
                }
            }

            return Math.Min(boost, 0.5f); // Максимальный буст 0.5
        }

        private float CalculateKeywordBoostForChunk(string query, string chunkText)
        {
            var queryLower = query.ToLower();
            var chunkLower = chunkText.ToLower();

            float boost = 0;

            // Проверяем специальные паттерны
            if (queryLower.Contains("как создать курс") && chunkLower.Contains("как создать дисциплину"))
            {
                // Это неправильный ответ - штрафуем
                return -0.3f;
            }

            if (queryLower.Contains("курс") && !chunkLower.Contains("дисциплин"))
            {
                boost += 0.2f;
            }

            if (queryLower.Contains("создать курс") && chunkLower.Contains("курс"))
            {
                boost += 0.3f;
            }

            // Обычная проверка ключевых слов
            var queryWords = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in queryWords)
            {
                if (word.Length > 2 && chunkLower.Contains(word))
                {
                    boost += 0.1f;
                }
            }

            return Math.Min(boost, 0.5f);
        }

       

        // Добавьте метод для расчета схожести строк
        private double CalculateLevenshteinSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            int maxLen = Math.Max(s1.Length, s2.Length);
            int distance = LevenshteinDistance(s1, s2);
            return 1.0 - (double)distance / maxLen;
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            int[,] dp = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                dp[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                dp[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[s1.Length, s2.Length];
        }

        private List<RagChunk> SemanticSearchChunks(string query, List<RagChunk> chunks, int take)
        {
            if (chunks.Count == 0) return new List<RagChunk>();

            // Упрощённый поиск по ключевым словам (если нет эмбеддингов)
            var queryLower = query.ToLower();
            var results = new List<(RagChunk Chunk, int Score)>();

            foreach (var chunk in chunks)
            {
                int score = 0;
                var chunkLower = chunk.Text.ToLower();

                // Простой поиск по ключевым словам
                var words = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    if (word.Length > 2 && chunkLower.Contains(word))
                    {
                        score++;
                    }
                }

                if (score > 0)
                {
                    results.Add((chunk, score));
                }
            }

            if (!results.Any())
                return chunks.Take(take).ToList();

            return results
                .OrderByDescending(r => r.Score)
                .Take(take)
                .Select(r => r.Chunk)
                .ToList();
        }

        private float CosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1 == null || vec2 == null || vec1.Length == 0 || vec2.Length == 0 || vec1.Length != vec2.Length)
                return 0;

            float dot = 0, norm1 = 0, norm2 = 0;
            for (int i = 0; i < vec1.Length; i++)
            {
                dot += vec1[i] * vec2[i];
                norm1 += vec1[i] * vec1[i];
                norm2 += vec2[i] * vec2[i];
            }

            if (norm1 == 0 || norm2 == 0) return 0;

            float result = dot / (float)(Math.Sqrt(norm1) * Math.Sqrt(norm2));

            // Корректируем возможные ошибки округления
            if (result > 1) result = 1;
            if (result < -1) result = -1;
            if (float.IsNaN(result)) result = 0;

            return result;
        }

        // ==================== ЛИЧНЫЕ ДАННЫЕ ====================

        private async Task<string?> GetPersonalDataAnswerAsync(AppDbContext context, int userId, string query, bool isAdmin, bool isLecturer, bool isStudent)
        {
            var queryLower = query.ToLower();

            if (queryLower.Contains("дисциплин") || queryLower.Contains("предмет"))
            {
                Console.WriteLine($"  → Запрос к БД: дисциплины");
                return await GetUserDisciplinesAsync(context, userId);
            }

            if (queryLower.Contains("дедлайн") || queryLower.Contains("срок") || queryLower.Contains("сдавать"))
            {
                Console.WriteLine($"  → Запрос к БД: дедлайны");
                return await GetTestDeadlinesAsync(context, userId);
            }

            if (queryLower.Contains("результат") || queryLower.Contains("оценк") || queryLower.Contains("балл"))
            {
                Console.WriteLine($"  → Запрос к БД: результаты");
                return await GetTestResultsAsync(context, userId);
            }

            if (queryLower.Contains("группа"))
            {
                Console.WriteLine($"  → Запрос к БД: группа");
                return await GetUserGroupAsync(context, userId);
            }

            if (queryLower.Contains("тест") && (queryLower.Contains("доступн") || queryLower.Contains("какие")))
            {
                Console.WriteLine($"  → Запрос к БД: доступные тесты");
                return await GetAvailableTestsAsync(context, userId);
            }

            if (isAdmin && (queryLower.Contains("статистик") || queryLower.Contains("сколько")))
            {
                Console.WriteLine($"  → Запрос к БД: статистика");
                var groupsCount = await context.StudentGroups.CountAsync();
                var coursesCount = await context.Courses.CountAsync();
                var disciplinesCount = await context.Disciplines.CountAsync();
                var usersCount = await context.Users.CountAsync();
                var materialsCount = await context.Materials.CountAsync();
                var testsCount = await context.Tests.CountAsync();

                return $@"
📊 **Статистика системы:**

👥 Пользователей: {usersCount}
👨‍🏫 Преподавателей: {await context.Lecturers.CountAsync()}
👨‍🎓 Студентов: {await context.Students.CountAsync()}

📚 Учебные данные:
• Дисциплин: {disciplinesCount}
• Курсов: {coursesCount}
• Групп: {groupsCount}
• Материалов: {materialsCount}
• Тестов: {testsCount}
";
            }

            return null;
        }

        // ==================== БЫСТРЫЕ ОТВЕТЫ ====================

        private string GetGreetingMessage(string role, string? userName)
        {
            var roleText = role == "Admin" ? "Администратор" : role == "Lecturer" ? "Преподаватель" : "Студент";
            return $@"
🤖 **Привет, {userName ?? "пользователь"}!**

Я AI-помощник. Ваша роль: **{roleText}**

💡 **Что я могу:**
• Отвечать на вопросы по вашим дисциплинам, тестам и материалам
• Помогать с навигацией по платформе
• Подсказывать, как создавать материалы и тесты

📝 **Попробуйте спросить:**
• ""как создать материал""
• ""как создать тест""
• ""мои дисциплины""
• ""дедлайны""
• ""помощь""
";
        }

        private string GetHelpMessage(string role)
        {
            if (role == "Admin")
            {
                return @"
📋 **Справка для администратора**

**Управление:**
• Все пользователи - /Admin/AllUsers
• Создать преподавателя - /Admin/CreateLecturer
• Создать администратора - /Admin/CreateAdmin
• Группы - /Admin/ManageGroups
• Курсы - /Admin/ManageCourses
• Дисциплины - /Admin/ManageDisciplines

**Настройки:**
• Код регистрации - /Admin/ChangeVerificationCode
• Обратная связь - /Admin/ViewFeedbacks

**Быстрые команды:**
• ""как создать группу""
• ""как создать курс""
• ""как создать дисциплину""
";
            }
            else if (role == "Lecturer")
            {
                return @"
📋 **Справка для преподавателя**

**Мои дисциплины:** /Lecturer/Index

**Возможности:**
• Добавлять учебные материалы
• Создавать тесты и вопросы
• Управлять разделами дисциплин
• Загружать файлы

**Быстрые команды:**
• ""как создать материал""
• ""как создать тест""
• ""как загрузить файл""
• ""мои дисциплины""
";
            }
            else
            {
                return @"
📋 **Справка для студента**

**Разделы:**
• Мои дисциплины - в боковом меню
• Мои тесты - в боковом меню

**Что можно:**
• Проходить доступные тесты
• Скачивать учебные материалы
• Смотреть результаты

**Быстрые команды:**
• ""мои дисциплины""
• ""дедлайны""
• ""мои результаты""
";
            }
        }

        // ==================== СБОР ДАННЫХ ИЗ БД ====================

        private async Task<string> CollectDatabaseDataAsync(AppDbContext context, int userId, bool isAdmin, bool isLecturer, bool isStudent)
        {
            var dbData = new StringBuilder();
            dbData.AppendLine("=== ДАННЫЕ ИЗ СИСТЕМЫ ===\n");

            var user = await context.Users.FindAsync(userId);
            dbData.AppendLine($"Пользователь: {user?.UserName}, ID: {userId}");
            dbData.AppendLine($"Роль: {(isAdmin ? "Администратор" : isLecturer ? "Преподаватель" : isStudent ? "Студент" : "Пользователь")}");

            if (isAdmin)
            {
                var groupsCount = await context.StudentGroups.CountAsync();
                var coursesCount = await context.Courses.CountAsync();
                var disciplinesCount = await context.Disciplines.CountAsync();
                var usersCount = await context.Users.CountAsync();

                dbData.AppendLine($"\n📊 Статистика системы:");
                dbData.AppendLine($"• Групп: {groupsCount}");
                dbData.AppendLine($"• Курсов: {coursesCount}");
                dbData.AppendLine($"• Дисциплин: {disciplinesCount}");
                dbData.AppendLine($"• Пользователей: {usersCount}");
            }
            else if (isLecturer)
            {
                var lecturer = await context.Lecturers.FirstOrDefaultAsync(l => l.Id == userId);
                dbData.AppendLine($"\n👨‍🏫 Преподаватель: {lecturer?.FullName}");
                dbData.AppendLine($"Кафедра: {lecturer?.Department ?? "не указана"}");

                var myDisciplines = await context.Disciplines
                    .Where(d => d.DisciplineLecturers.Any(dl => dl.LecturerId == userId))
                    .ToListAsync();

                dbData.AppendLine($"\n📚 Ваши дисциплины ({myDisciplines.Count}):");
                foreach (var d in myDisciplines)
                {
                    var materialsCount = await context.Materials.CountAsync(m => m.DisciplineId == d.Id);
                    var testsCount = await context.Tests.CountAsync(t => t.DisciplineId == d.Id);
                    dbData.AppendLine($"  • {d.Name} (материалов: {materialsCount}, тестов: {testsCount})");
                }
            }
            else if (isStudent)
            {
                var student = await context.Students
                    .Include(s => s.StudentGroup)
                    .FirstOrDefaultAsync(s => s.Id == userId);

                if (student?.StudentGroup != null)
                {
                    dbData.AppendLine($"\n👨‍🎓 Студент: {student.FullName}");
                    dbData.AppendLine($"Группа: {student.StudentGroup.Name}");

                    var accessibleDisciplines = await context.Disciplines
                        .Where(d => d.OpenGroups.Any(g => g.Id == student.StudentGroupId))
                        .ToListAsync();

                    dbData.AppendLine($"\n📚 Доступные дисциплины ({accessibleDisciplines.Count}):");
                    foreach (var d in accessibleDisciplines)
                    {
                        dbData.AppendLine($"  • {d.Name}");
                    }
                }
                else
                {
                    dbData.AppendLine("Вы пока не прикреплены к группе.");
                }
            }

            dbData.AppendLine("\n=== КОНЕЦ ДАННЫХ ===\n");
            return dbData.ToString();
        }

        public async Task<string> AskGuestBotAsync(string query)
        {
            try
            {
                Console.WriteLine($"=== GUEST BOT ===");
                Console.WriteLine($"Query: {query}");

                var semanticResult = await SemanticSearchAllAsync(query, "Guest");
                if (semanticResult.HasValue && semanticResult.Value.Score > 0.55f)
                {
                    return semanticResult.Value.Answer;
                }

                return @"
🤖 **Гостевой режим**

Для доступа к учебным материалам необходимо:
1. Зарегистрироваться - /Account/Register
2. Войти в систему - /Account/Login

Если у вас есть код верификации, введите его при регистрации.

❓ Вопросы о платформе? Напишите ""помощь"" после входа в систему.
";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AskGuestBotAsync");
                return "Произошла ошибка. Попробуйте позже.";
            }
        }

        private async Task<string?> CallOllamaChatAsync(string systemPrompt, string userContent, double temperature, int maxTokens)
        {
            try
            {
                var request = new
                {
                    model = _ollamaModel,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userContent }
                    },
                    stream = false,
                    options = new
                    {
                        temperature,
                        num_predict = maxTokens,
                        top_p = 0.9,
                        num_ctx = 2048
                    }
                };

                var json = JsonSerializer.Serialize(request);
                using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("api/chat", httpContent);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"  ❌ Ollama HTTP ошибка: {response.StatusCode}");
                    _logger.LogWarning("Ollama HTTP {Code}: {Error}", response.StatusCode, error);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonResponse);
                return doc.RootElement.GetProperty("message").GetProperty("content").GetString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Ошибка вызова Ollama: {ex.Message}");
                _logger.LogError(ex, "Ollama call failed");
                return null;
            }
        }

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

            // Единый формат чанка: совпадает с парсерами и с текстовыми инструкциями бота
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = new RagChunk
                {
                    DocumentId = ragDocument.Id,
                    Text =
                        $"ИСТОЧНИК: {material.Title}\n" +
                        $"ID МАТЕРИАЛА: {material.Id}\n" +
                        "СОДЕРЖАНИЕ:\n" +
                        $"{chunks[i].Trim()}\n" +
                        "--- конец фрагмента ---\n",
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

                var hiddenMaterialIds = await context.Materials
                    .AsNoTracking()
                    .Where(m => !m.IsVisible && accessibleDisciplineIds.Contains(m.DisciplineId))
                    .Select(m => m.Id)
                    .ToListAsync();

                var studentChunks = await context.RagChunks
                    .Where(c => c.DisciplineId.HasValue && accessibleDisciplineIds.Contains(c.DisciplineId.Value))
                    .ToListAsync();

                if (hiddenMaterialIds.Count > 0)
                {
                    studentChunks = studentChunks
                        .Where(c => !c.MaterialId.HasValue || !hiddenMaterialIds.Contains(c.MaterialId.Value))
                        .ToList();
                }

                Console.WriteLine($"Student - found {studentChunks.Count} chunks (скрытые материалы исключены)");

                return studentChunks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAccessibleChunksForUserAsync: {ex.Message}");
                _logger.LogError(ex, "Error in GetAccessibleChunksForUserAsync");
                return new List<RagChunk>();
            }
        }

        private static List<string> TokenizeForSearch(string query)
        {
            var lower = query.ToLowerInvariant();
            var words = Regex.Matches(lower, @"\p{L}[\p{L}\p{Nd}]*")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(w => w.Length >= 2)
                .ToList();

            var extra = new List<string>();
            foreach (var w in words)
            {
                if (w.Length > 4)
                    extra.Add(w[..^1]);
            }

            foreach (var kv in SearchKeywordBoosts)
            {
                if (lower.Contains(kv.Key, StringComparison.Ordinal))
                    extra.AddRange(kv.Value);
            }

            return words.Concat(extra).Distinct().ToList();
        }

        private static int ScoreTextAgainstTokens(string text, List<string> tokens)
        {
            if (tokens.Count == 0)
                return 0;

            var lower = text.ToLowerInvariant();
            var score = 0;
            foreach (var t in tokens)
            {
                if (lower.Contains(t, StringComparison.Ordinal))
                    score += t.Length >= 4 ? 2 : 1;
            }

            return score;
        }

        private List<RagChunk> SearchRelevantChunks(string query, List<RagChunk> chunks, int take)
        {
            if (chunks.Count == 0)
                return new List<RagChunk>();

            var tokens = TokenizeForSearch(query);
            var cleanPhrase = new string(query.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray())
                .Trim()
                .ToLowerInvariant();

            var results = new List<(RagChunk Chunk, int Score)>();
            foreach (var chunk in chunks)
            {
                int score = ScoreTextAgainstTokens(chunk.Text, tokens);
                var chunkLower = chunk.Text.ToLowerInvariant();
                if (cleanPhrase.Length >= 4 && chunkLower.Contains(cleanPhrase))
                    score += 4;

                if (score > 0)
                    results.Add((chunk, score));
            }

            if (!results.Any())
                return chunks.OrderBy(c => c.DocumentId).ThenBy(c => c.ChunkIndex).Take(Math.Min(take, chunks.Count)).ToList();

            return results
                .OrderByDescending(r => r.Score)
                .Take(take)
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

                var response = await _httpClient.PostAsync("api/chat", httpContent);

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

        private List<string> SplitIntoChunks(string text, int chunkSize = 900, int overlap = 150)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text))
                return chunks;

            overlap = Math.Max(0, Math.Min(overlap, chunkSize - 1));
            var step = chunkSize - overlap;
            for (var i = 0; i < text.Length; i += step)
            {
                var len = Math.Min(chunkSize, text.Length - i);
                chunks.Add(text.Substring(i, len));
                if (i + chunkSize >= text.Length)
                    break;
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