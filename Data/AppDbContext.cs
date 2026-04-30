using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using diplom.Models;

namespace diplom.Data
{
    public class AppDbContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public DbSet<Student> Students { get; set; }
        public DbSet<Lecturer> Lecturers { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<StudentGroup> StudentGroups { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Discipline> Disciplines { get; set; }
        public DbSet<Material> Materials { get; set; }
        public DbSet<Test> Tests { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<TestResult> TestResults { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<ChatDialog> ChatDialogs { get; set; }
        public DbSet<RagDocument> RagDocuments { get; set; }
        public DbSet<RagChunk> RagChunks { get; set; }
        public DbSet<RagQueryLog> RagQueryLogs { get; set; }
        public DbSet<ChatHistory> ChatHistories { get; set; }

        public DbSet<DisciplineLecturer> DisciplineLecturers { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Настройки приложения
            builder.Entity<AppSetting>().HasData(new AppSetting { Id = 1, VerificationCode = "VLSU12" });

            // Дискриминатор для трёх ролей
            builder.Entity<User>()
                .HasDiscriminator<string>("Role")
                .HasValue<Student>("Student")
                .HasValue<Lecturer>("Lecturer")
                .HasValue<Admin>("Admin");

            // ==================== СВЯЗИ ====================

            // Course → StudentGroups (один курс → много групп)
            builder.Entity<Course>()
                .HasMany(c => c.StudentGroups)
                .WithOne(g => g.Course)
                .HasForeignKey(g => g.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            // Course → Disciplines (один курс → много дисциплин)
            builder.Entity<Course>()
                .HasMany(c => c.Disciplines)
                .WithOne(d => d.Course)
                .HasForeignKey(d => d.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Discipline → OpenGroups (многие-ко-многим, без истории)
            builder.Entity<Discipline>()
                .HasMany(d => d.OpenGroups)
                .WithMany(g => g.Disciplines)
                .UsingEntity(j => j.ToTable("DisciplineGroupAccess"));

            // Material → Discipline
            builder.Entity<Material>()
                .HasOne(m => m.Discipline)
                .WithMany(d => d.Materials)
                .HasForeignKey(m => m.DisciplineId)
                .OnDelete(DeleteBehavior.Cascade);

            // Material → RagDocument
            builder.Entity<Material>()
                .HasOne(m => m.RagDocument)
                .WithOne(r => r.Material)
                .HasForeignKey<RagDocument>(r => r.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);

            // Test → Discipline
            builder.Entity<Test>()
                .HasOne(t => t.Discipline)
                .WithMany(d => d.Tests)
                .HasForeignKey(t => t.DisciplineId)
                .OnDelete(DeleteBehavior.Cascade);

            // Question → Test
            builder.Entity<Question>()
                .HasOne(q => q.Test)
                .WithMany(t => t.Questions)
                .HasForeignKey(q => q.TestId)
                .OnDelete(DeleteBehavior.Cascade);

            // Student → StudentGroup
            builder.Entity<Student>()
                .HasOne(s => s.StudentGroup)
                .WithMany(g => g.Students)
                .HasForeignKey(s => s.StudentGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            // TestResult → Student
            builder.Entity<TestResult>()
                .HasOne(tr => tr.Student)
                .WithMany(s => s.TestResults)
                .HasForeignKey(tr => tr.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            // TestResult → Test
            builder.Entity<TestResult>()
                .HasOne(tr => tr.Test)
                .WithMany(t => t.TestResults)
                .HasForeignKey(tr => tr.TestId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==================== RAG ====================
            builder.Entity<RagChunk>()
                .HasOne(rc => rc.Document)
                .WithMany(d => d.RagChunks)
                .HasForeignKey(rc => rc.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<RagChunk>()
                .HasIndex(rc => rc.DocumentId);

            builder.Entity<RagDocument>()
                .HasIndex(r => new { r.DisciplineId, r.AccessLevel });

            // ==================== ЧАТ ====================
            builder.Entity<ChatMessage>()
                .HasOne(cm => cm.Sender)
                .WithMany()
                .HasForeignKey(cm => cm.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChatMessage>()
                .HasOne(cm => cm.Recipient)
                .WithMany()
                .HasForeignKey(cm => cm.RecipientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChatDialog>()
                .HasOne(cd => cd.User1)
                .WithMany()
                .HasForeignKey(cd => cd.User1Id)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChatDialog>()
                .HasOne(cd => cd.User2)
                .WithMany()
                .HasForeignKey(cd => cd.User2Id)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ChatDialog>()
                .HasIndex(cd => new { cd.User1Id, cd.User2Id })
                .IsUnique();

            builder.Entity<DisciplineLecturer>()
                .HasOne(dl => dl.Discipline)
                .WithMany(d => d.DisciplineLecturers)
                .HasForeignKey(dl => dl.DisciplineId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DisciplineLecturer>()
                .HasOne(dl => dl.Lecturer)
                .WithMany(l => l.DisciplineLecturers)
                .HasForeignKey(dl => dl.LecturerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<DisciplineLecturer>()
                .HasIndex(dl => new { dl.DisciplineId, dl.LecturerId })
                .IsUnique();
        }
    }
}