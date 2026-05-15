
namespace diplom.Models.ViewModels
{
    public class ChatDialogViewModel
    {
        public int DialogId { get; set; }
        public int OtherUserId { get; set; }
        public string OtherUserName { get; set; } = string.Empty;
        public string OtherUserFullName { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public DateTime LastMessageAt { get; set; }
        public int UnreadCount { get; set; }
        public string? UserAvatar { get; set; }
        public string Role { get; set; } = string.Empty;
    }

    public class ChatMessageViewModel
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        public bool IsMine { get; set; }
    }
}