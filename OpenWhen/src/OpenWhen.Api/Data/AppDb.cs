using Microsoft.EntityFrameworkCore;

namespace OpenWhen.Api.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Streak> Streaks => Set<Streak>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<MemberMessage> Messages => Set<MemberMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasMaxLength(128);
            entity.Property(user => user.TimeZone).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Streak>(entity =>
        {
            entity.HasKey(streak => streak.Id);
            entity.Property(streak => streak.HabitName).HasMaxLength(80).IsRequired();
            entity.Property(streak => streak.JoinCode).HasMaxLength(64).IsRequired();
            entity.Property(streak => streak.CreatedByUserId).HasMaxLength(128).IsRequired();
            entity.HasIndex(streak => streak.JoinCode).IsUnique();
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.HasKey(membership => membership.Id);
            entity.Property(membership => membership.UserId).HasMaxLength(128).IsRequired();
            entity.HasIndex(membership => new { membership.StreakId, membership.UserId }).IsUnique();
            entity.HasOne(membership => membership.Streak)
                .WithMany(streak => streak.Members)
                .HasForeignKey(membership => membership.StreakId);
            entity.HasOne(membership => membership.User)
                .WithMany()
                .HasForeignKey(membership => membership.UserId);
        });

        modelBuilder.Entity<CheckIn>(entity =>
        {
            entity.HasKey(checkIn => checkIn.Id);
            entity.HasIndex(checkIn => new { checkIn.MembershipId, checkIn.LocalDate }).IsUnique();
            entity.HasOne(checkIn => checkIn.Membership)
                .WithMany(membership => membership.CheckIns)
                .HasForeignKey(checkIn => checkIn.MembershipId);
        });

        modelBuilder.Entity<MemberMessage>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.FromUserId).HasMaxLength(128).IsRequired();
            entity.Property(message => message.ToUserId).HasMaxLength(128).IsRequired();
            entity.Property(message => message.Body).HasMaxLength(2000).IsRequired();
            entity.HasOne(message => message.Streak)
                .WithMany(streak => streak.Messages)
                .HasForeignKey(message => message.StreakId);
        });
    }
}
