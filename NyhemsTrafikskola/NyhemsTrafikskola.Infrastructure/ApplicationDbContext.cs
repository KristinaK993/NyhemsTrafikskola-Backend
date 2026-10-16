using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NyhemsTrafikskola.Domain;

namespace NyhemsTrafikskola.Infrastructure;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<InstructorProfile> InstructorProfiles => Set<InstructorProfile>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSession> CourseSessions => Set<CourseSession>();
    public DbSet<CourseBooking> CourseBookings => Set<CourseBooking>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.IsActive)
                .HasDefaultValue(true);
        });

        builder.Entity<StudentProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstName)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(x => x.LastName)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(x => x.Email)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.UserId)
                .HasMaxLength(450);

            entity.HasIndex(x => x.UserId)
                .IsUnique(false);

            entity.HasIndex(x => new { x.UserId, x.Email })
                .IsUnique();
        });

        builder.Entity<InstructorProfile>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstName)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(x => x.LastName)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(x => x.Email)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.UserId)
                .HasMaxLength(450);

            entity.HasIndex(x => x.UserId)
                .IsUnique(false);

            entity.HasIndex(x => new { x.UserId, x.Email })
                .IsUnique();
        });

        builder.Entity<Course>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.Price)
                .HasColumnType("decimal(18,2)")
                .IsRequired();
            entity.Property(x => x.IsActive)
                .HasDefaultValue(true);

            entity.HasIndex(x => x.Type);
            entity.HasIndex(x => x.IsActive);
        });

        builder.Entity<CourseSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StartTime)
                .IsRequired();
            entity.Property(x => x.EndTime)
                .IsRequired();
            entity.Property(x => x.Capacity)
                .IsRequired();
            entity.Property(x => x.IsPublished)
                .HasDefaultValue(false);
            entity.Property(x => x.IsCancelled)
                .HasDefaultValue(false);
            entity.Property(x => x.IsArchived)
                .HasDefaultValue(false);

            entity.HasIndex(x => x.CourseId);
            entity.HasIndex(x => new { x.CourseId, x.StartTime });
            entity.HasOne<Course>()
                .WithMany()
                .HasForeignKey(x => x.CourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CourseBooking>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CustomerName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.Email)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(x => x.Phone)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(x => x.Status)
                .IsRequired();
            entity.Property(x => x.CreatedAt)
                .IsRequired();
            entity.Property(x => x.CreatedByUserId)
                .HasMaxLength(450);
            entity.Property(x => x.TermsVersion)
                .HasMaxLength(32);
            entity.Property(x => x.CancelledByUserId)
                .HasMaxLength(450);

            entity.HasIndex(x => x.CourseSessionId);
            entity.HasIndex(x => x.StudentProfileId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => new { x.CourseSessionId, x.Status });

            entity.HasOne<CourseSession>()
                .WithMany()
                .HasForeignKey(x => x.CourseSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<StudentProfile>()
                .WithMany()
                .HasForeignKey(x => x.StudentProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StudentProfile>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<InstructorProfile>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
