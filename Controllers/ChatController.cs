using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using diplom.Models;
using diplom.Models.ViewModels;
using diplom.Data;

namespace diplom.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<User> _userManager;

        public ChatController(AppDbContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            // Получаем все диалоги текущего пользователя
            var dialogs = await _context.ChatDialogs
                .Include(d => d.User1)
                .Include(d => d.User2)
                .Where(d => d.User1Id == currentUser.Id || d.User2Id == currentUser.Id)
                .Select(d => new ChatDialogViewModel
                {
                    DialogId = d.Id,
                    OtherUserId = d.User1Id == currentUser.Id ? d.User2Id : d.User1Id,
                    OtherUserName = (d.User1Id == currentUser.Id ? d.User2.UserName : d.User1.UserName) ?? string.Empty,
                    OtherUserFullName = (d.User1Id == currentUser.Id ? d.User2.FullName : d.User1.FullName) ?? string.Empty,
                    LastMessage = _context.ChatMessages
                        .Where(m => (m.SenderId == currentUser.Id && m.RecipientId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id)) ||
                                    (m.SenderId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id) && m.RecipientId == currentUser.Id))
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Message)
                        .FirstOrDefault() ?? string.Empty,
                    LastMessageAt = d.LastMessageAt,
                    UnreadCount = _context.ChatMessages
                        .Count(m => m.RecipientId == currentUser.Id &&
                                    m.SenderId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id) &&
                                    !m.IsRead),
                    Role = (d.User1Id == currentUser.Id ? d.User2 : d.User1).GetType().Name
                })
                .OrderByDescending(d => d.LastMessageAt)
                .ToListAsync();

            // Список всех пользователей для нового чата (кроме себя)
            var allUsers = await _userManager.Users
                .Where(u => u.Id != currentUser.Id)
                .Select(u => new { u.Id, u.FullName, u.UserName, Role = u.GetType().Name })
                .ToListAsync();

            ViewBag.AllUsers = allUsers;
            ViewBag.CurrentUserId = currentUser.Id;

            return View(dialogs);
        }
        [HttpGet]
        public async Task<IActionResult> GetDialogs()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false });

            var dialogs = await _context.ChatDialogs
                .Where(d => d.User1Id == currentUser.Id || d.User2Id == currentUser.Id)
                .Select(d => new
                {
                    DialogId = d.Id,
                    OtherUserId = d.User1Id == currentUser.Id ? d.User2Id : d.User1Id,
                    OtherUserName = (d.User1Id == currentUser.Id ? d.User2.FullName : d.User1.FullName) ?? "",
                    UserRole = (d.User1Id == currentUser.Id ? d.User2 : d.User1).GetType().Name,
                    LastMessage = _context.ChatMessages
                        .Where(m => (m.SenderId == currentUser.Id && m.RecipientId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id)) ||
                                    (m.SenderId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id) && m.RecipientId == currentUser.Id))
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => m.Message)
                        .FirstOrDefault() ?? "",
                    UnreadCount = _context.ChatMessages
                        .Count(m => m.RecipientId == currentUser.Id && m.SenderId == (d.User1Id == currentUser.Id ? d.User2Id : d.User1Id) && !m.IsRead)
                })
                .OrderByDescending(d => d.LastMessage)
                .ToListAsync();

            return Json(new { success = true, dialogs });
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false });

            var users = await _userManager.Users
                .Where(u => u.Id != currentUser.Id)
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    FullName = u.FullName ?? u.UserName,
                    Role = u.GetType().Name
                })
                .ToListAsync();

            return Json(new { success = true, users });
        }

        [HttpGet]
        public async Task<IActionResult> GetMessages(int userId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false });

            // Помечаем сообщения как прочитанные
            var unreadMessages = await _context.ChatMessages
                .Where(m => m.RecipientId == currentUser.Id && m.SenderId == userId && !m.IsRead)
                .ToListAsync();
            foreach (var msg in unreadMessages) msg.IsRead = true;
            await _context.SaveChangesAsync();

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == currentUser.Id && m.RecipientId == userId) ||
                            (m.SenderId == userId && m.RecipientId == currentUser.Id))
                .OrderBy(m => m.SentAt)
                .Select(m => new
                {
                    m.Id,
                    m.Message,
                    m.SentAt,
                    IsMine = m.SenderId == currentUser.Id,
                    IsRead = m.IsRead
                })
                .ToListAsync();

            return Json(new { success = true, messages });
        }
        [HttpGet]
        public async Task<IActionResult> Dialog(int userId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false });

            // Находим или создаем диалог
            var dialog = await _context.ChatDialogs
                .FirstOrDefaultAsync(d => (d.User1Id == currentUser.Id && d.User2Id == userId) ||
                                          (d.User1Id == userId && d.User2Id == currentUser.Id));

            if (dialog == null)
            {
                dialog = new ChatDialog
                {
                    User1Id = currentUser.Id,
                    User2Id = userId,
                    LastMessageAt = DateTime.UtcNow
                };
                _context.ChatDialogs.Add(dialog);
                await _context.SaveChangesAsync();
            }

            // Помечаем сообщения как прочитанные
            var unreadMessages = await _context.ChatMessages
                .Where(m => m.RecipientId == currentUser.Id && m.SenderId == userId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }
            await _context.SaveChangesAsync();

            // Получаем сообщения
            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == currentUser.Id && m.RecipientId == userId) ||
                            (m.SenderId == userId && m.RecipientId == currentUser.Id))
                .OrderBy(m => m.SentAt)
                .Select(m => new ChatMessageViewModel
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    SenderName = m.Sender.FullName ?? m.Sender.UserName ?? string.Empty,
                    Message = m.Message,
                    SentAt = m.SentAt,
                    IsRead = m.IsRead,
                    IsMine = m.SenderId == currentUser.Id
                })
                .ToListAsync();

            var otherUser = await _userManager.FindByIdAsync(userId.ToString());

            return Json(new
            {
                success = true,
                dialogId = dialog.Id,
                messages = messages,
                otherUser = new
                {
                    id = otherUser?.Id,
                    name = otherUser?.FullName ?? otherUser?.UserName,
                    role = otherUser?.GetType().Name
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false, error = "Not authenticated" });

            if (string.IsNullOrWhiteSpace(request.Message))
                return Json(new { success = false, error = "Message is empty" });

            // Находим или создаем диалог
            var dialog = await _context.ChatDialogs
                .FirstOrDefaultAsync(d => (d.User1Id == currentUser.Id && d.User2Id == request.RecipientId) ||
                                          (d.User1Id == request.RecipientId && d.User2Id == currentUser.Id));

            if (dialog == null)
            {
                dialog = new ChatDialog
                {
                    User1Id = currentUser.Id,
                    User2Id = request.RecipientId,
                    LastMessageAt = DateTime.UtcNow
                };
                _context.ChatDialogs.Add(dialog);
                await _context.SaveChangesAsync();
            }

            var chatMessage = new ChatMessage
            {
                SenderId = currentUser.Id,
                RecipientId = request.RecipientId,
                Message = request.Message,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.ChatMessages.Add(chatMessage);
            dialog.LastMessageAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = new
                {
                    id = chatMessage.Id,
                    message = chatMessage.Message,
                    sentAt = chatMessage.SentAt,
                    isMine = true
                }
            });
        }

        public class SendMessageRequest
        {
            public int RecipientId { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int senderId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { success = false });

            var unreadMessages = await _context.ChatMessages
                .Where(m => m.RecipientId == currentUser.Id && m.SenderId == senderId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Json(new { count = 0 });

            var count = await _context.ChatMessages
                .CountAsync(m => m.RecipientId == currentUser.Id && !m.IsRead);

            return Json(new { count });
        }
    }

}