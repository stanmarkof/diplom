using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Data;
using diplom.Models;
using diplom.Services;
using System.Text.Json;

namespace diplom.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("api/[controller]")]
    [ApiController]
    public class FillEmbeddingsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmbeddingService _embeddingService;
        private readonly ILogger<FillEmbeddingsController> _logger;

        public FillEmbeddingsController(
            AppDbContext context,
            IEmbeddingService embeddingService,
            ILogger<FillEmbeddingsController> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _logger = logger;
        }

        [HttpGet("instructions")]
        public async Task<IActionResult> FillInstructionsEmbeddings()
        {
            try
            {
                var instructions = await _context.BotInstructions.ToListAsync();
                var count = 0;

                foreach (var instruction in instructions)
                {
                    if (!string.IsNullOrEmpty(instruction.Embedding) && instruction.Embedding != "[]")
                        continue;

                    // Создаём текст для эмбеддинга
                    var textForEmbedding = $"{instruction.Title}\n{instruction.Answer}";
                    if (instruction.Keywords != null && instruction.Keywords.Any())
                    {
                        textForEmbedding += $"\nКлючевые слова: {string.Join(", ", instruction.Keywords)}";
                    }

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(textForEmbedding);
                    instruction.Embedding = JsonSerializer.Serialize(embedding);
                    count++;

                    Console.WriteLine($"✅ Индексирована инструкция: {instruction.Title}");
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Индексировано {count} инструкций" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("chunks")]
        public async Task<IActionResult> FillChunksEmbeddings()
        {
            try
            {
                var chunks = await _context.RagChunks.ToListAsync();
                var count = 0;

                foreach (var chunk in chunks)
                {
                    if (!string.IsNullOrEmpty(chunk.Embedding) && chunk.Embedding != "[]")
                        continue;

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk.Text);
                    chunk.Embedding = JsonSerializer.Serialize(embedding);
                    count++;

                    if (count % 10 == 0)
                        Console.WriteLine($"✅ Индексировано {count} чанков...");
                }

                await _context.SaveChangesAsync();
                return Ok(new { message = $"Индексировано {count} чанков" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("all")]
        public async Task<IActionResult> FillAllEmbeddings()
        {
            try
            {
                await FillInstructionsEmbeddings();
                await FillChunksEmbeddings();
                return Ok(new { message = "Все эмбеддинги заполнены" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var instructionsCount = await _context.BotInstructions.CountAsync();
            var instructionsWithEmbeddings = await _context.BotInstructions.CountAsync(i => i.Embedding != null && i.Embedding != "[]");
            var chunksCount = await _context.RagChunks.CountAsync();
            var chunksWithEmbeddings = await _context.RagChunks.CountAsync(c => c.Embedding != null && c.Embedding != "[]");

            return Ok(new
            {
                instructions = new { total = instructionsCount, withEmbeddings = instructionsWithEmbeddings },
                chunks = new { total = chunksCount, withEmbeddings = chunksWithEmbeddings }
            });
        }
    }
}