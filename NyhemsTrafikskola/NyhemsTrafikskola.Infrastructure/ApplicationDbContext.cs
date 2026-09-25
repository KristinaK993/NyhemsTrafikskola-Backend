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
