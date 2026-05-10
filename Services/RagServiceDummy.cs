using diplom.Models;

namespace diplom.Services
{
    public class RagServiceDummy : IRagService
    {
        private readonly ILogger<RagServiceDummy> _logger;

        public RagServiceDummy(ILogger<RagServiceDummy> logger)
        {
            _logger = logger;
        }

        public async Task<string> AskBotAsync(string query, int userId)
        {
            await Task.Delay(100);
            return "AI помощник временно недоступен. Попробуйте позже.";
        }

        public async Task IndexMaterialAsync(Material material, bool forceReindex = false)
        {
            _logger.LogInformation($"Индексация материала {material.Title} (заглушка)");
            await Task.CompletedTask;
        }

        public async Task DeleteMaterialIndexAsync(int materialId)
        {
            await Task.CompletedTask;
        }

        public async Task RebuildAllIndexesAsync()
        {
            await Task.CompletedTask;
        }
    }
}