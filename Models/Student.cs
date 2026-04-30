using diplom.Models;

public class Student : User
{
    public string? StudentId { get; set; }
    public int? StudentGroupId { get; set; }  // ← именно так
    public StudentGroup? StudentGroup { get; set; }

    public ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
}