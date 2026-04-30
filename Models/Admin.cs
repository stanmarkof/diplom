using diplom.Models;

namespace diplom.Models
{
    public class Admin : User
    {
        public string? AccessLevel { get; set; }
    }
}