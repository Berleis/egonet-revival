using Microsoft.EntityFrameworkCore;

namespace RaceNetShowdown.Server.Data;

public sealed class RaceNetDbContext(DbContextOptions<RaceNetDbContext> options) : DbContext(options)
{
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();

    public DbSet<RaceNetSession> RaceNetSessions => Set<RaceNetSession>();

    public DbSet<ChallengeRecord> Challenges => Set<ChallengeRecord>();

    public DbSet<ChallengeResultRecord> ChallengeResults => Set<ChallengeResultRecord>();

    public DbSet<RaceNetCallRecord> RaceNetCalls => Set<RaceNetCallRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerProfile>(entity =>
        {
            entity.HasIndex(value => value.ExternalId).IsUnique();
            entity.Property(value => value.ExternalId).HasMaxLength(128);
            entity.Property(value => value.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<RaceNetSession>(entity =>
        {
            entity.HasIndex(value => value.SessionId).IsUnique();
            entity.Property(value => value.SessionId).HasMaxLength(128);
            entity.Property(value => value.RemoteAddress).HasMaxLength(128);
            entity.Property(value => value.UserAgent).HasMaxLength(256);
        });

        modelBuilder.Entity<ChallengeRecord>(entity =>
        {
            entity.HasIndex(value => value.EgoNetChallengeId).IsUnique();
            entity.Property(value => value.EventKey).HasMaxLength(128);
            entity.Property(value => value.VehicleKey).HasMaxLength(128);
            entity.Property(value => value.Status).HasMaxLength(32);

            entity
                .HasOne(value => value.IssuerPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.IssuerPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(value => value.TargetPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.TargetPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChallengeResultRecord>(entity =>
        {
            entity.Property(value => value.RawPayloadHex).HasMaxLength(16_384);
        });

        modelBuilder.Entity<RaceNetCallRecord>(entity =>
        {
            entity.HasIndex(value => value.Time);
            entity.HasIndex(value => value.EgoNetFunction);
            entity.Property(value => value.RemoteAddress).HasMaxLength(128);
            entity.Property(value => value.Method).HasMaxLength(16);
            entity.Property(value => value.Host).HasMaxLength(256);
            entity.Property(value => value.Path).HasMaxLength(512);
            entity.Property(value => value.QueryString).HasMaxLength(1024);
            entity.Property(value => value.EgoNetFunction).HasMaxLength(128);
            entity.Property(value => value.EgoNetSessionId).HasMaxLength(128);
            entity.Property(value => value.BodyPreview).HasMaxLength(16_384);
            entity.Property(value => value.BodyHexPreview).HasMaxLength(16_384);
            entity.Property(value => value.ResponseContentType).HasMaxLength(128);
        });
    }
}
