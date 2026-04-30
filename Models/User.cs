using Microsoft.AspNetCore.Identity;

namespace diplom.Models
{
    public class User : IdentityUser<int>
    {
        public string FullName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        // Удаляем эти поля
        // public bool IsApproved { get; set; } = false;
        // public DateTime? ApprovedAt { get; set; }
        // public int? ApprovedById { get; set; }
        // public string? RequestedRole { get; set; }
        // public string? RejectionReason { get; set; }

        // public User? ApprovedBy { get; set; }
    }
}