using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using diplom.Services;
using System.Threading.Tasks;

namespace diplom.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly IRagService _ragService;

        public BotController(IRagService ragService)
        {
            _ragService = ragService;
        }

        [Authorize]
        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] BotRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { answer = "Введите вопрос." });
            }

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                return Unauthorized(new { answer = "Пользователь не авторизован." });
            }

            // Передаем режим использования нейросети
            var answer = await _ragService.AskBotAsync(request.Query, userId, request.UseAI);
            return Ok(new { answer });
        }

        [AllowAnonymous]
        [HttpPost("guest/ask")]
        public async Task<IActionResult> AskGuest([FromBody] BotRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(new { answer = "Введите вопрос." });

            var answer = await _ragService.AskGuestBotAsync(request.Query);
            return Ok(new { answer });
        }
    }

    public class BotRequest
    {
        public string Query { get; set; } = string.Empty;
        public bool UseAI { get; set; } = true; // По умолчанию используем нейросеть
    }
}