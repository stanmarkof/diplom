using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using diplom.Services;
using System.Threading.Tasks;

namespace diplom.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BotController : ControllerBase
    {
        private readonly IRagService _ragService;

        public BotController(IRagService ragService)
        {
            _ragService = ragService;
        }

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

            var answer = await _ragService.AskBotAsync(request.Query, userId);
            return Ok(new { answer });
        }
    }

    public class BotRequest
    {
        public string Query { get; set; } = string.Empty;
    }
}