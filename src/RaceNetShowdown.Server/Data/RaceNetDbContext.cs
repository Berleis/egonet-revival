using Microsoft.EntityFrameworkCore;

namespace RaceNetShowdown.Server.Data;

public sealed class RaceNetDbContext(DbContextOptions<RaceNetDbContext> options) : DbContext(options)
{
    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();

    public DbSet<PlayerFriend> PlayerFriends => Set<PlayerFriend>();

    public DbSet<RaceNetSession> RaceNetSessions => Set<RaceNetSession>();

    public DbSet<ChallengeRecord> Challenges => Set<ChallengeRecord>();

    public DbSet<GhostRecord> Ghosts => Set<GhostRecord>();

    public DbSet<ChallengeResultRecord> ChallengeResults => Set<ChallengeResultRecord>();

    public DbSet<Grid2GlobalEventRecord> Grid2GlobalEvents => Set<Grid2GlobalEventRecord>();

    public DbSet<Grid2GlobalRaceRecord> Grid2GlobalRaces => Set<Grid2GlobalRaceRecord>();

    public DbSet<Grid2GlobalScoreRecord> Grid2GlobalScores => Set<Grid2GlobalScoreRecord>();

    public DbSet<Grid2RivalSessionDataRecord> Grid2RivalSessionData => Set<Grid2RivalSessionDataRecord>();

    public DbSet<Grid2RivalOpponentRecord> Grid2RivalOpponents => Set<Grid2RivalOpponentRecord>();

    public DbSet<Grid2RivalAssignmentRecord> Grid2RivalAssignments => Set<Grid2RivalAssignmentRecord>();

    public DbSet<RaceNetCallRecord> RaceNetCalls => Set<RaceNetCallRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerProfile>(entity =>
        {
            entity.HasIndex(value => value.ExternalId).IsUnique();
            entity.Property(value => value.ExternalId).HasMaxLength(128);
            entity.Property(value => value.DisplayName).HasMaxLength(128);
        });

        modelBuilder.Entity<PlayerFriend>(entity =>
        {
            entity.HasIndex(value => new { value.PlayerProfileId, value.SteamId }).IsUnique();
            entity.Property(value => value.SteamId).HasMaxLength(32);
            entity.Property(value => value.DisplayName).HasMaxLength(128);

            entity
                .HasOne(value => value.PlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.PlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(value => value.FriendPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.FriendPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.HasIndex(value => value.GhostSlotId).IsUnique();
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

        modelBuilder.Entity<GhostRecord>(entity =>
        {
            entity.HasIndex(value => value.GhostSlotId).IsUnique();

            entity
                .HasOne(value => value.OwnerPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.OwnerPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(value => value.ChallengeRecord)
                .WithMany()
                .HasForeignKey(value => value.ChallengeRecordId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChallengeResultRecord>(entity =>
        {
            entity.Property(value => value.RawPayloadHex).HasMaxLength(16_384);
        });

        modelBuilder.Entity<Grid2GlobalEventRecord>(entity =>
        {
            entity.HasIndex(value => value.RaceNetEventId).IsUnique();
            entity.HasIndex(value => value.Status);
            entity.Property(value => value.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<Grid2GlobalRaceRecord>(entity =>
        {
            entity.HasIndex(value => new { value.Grid2GlobalEventRecordId, value.RaceNetRaceId }).IsUnique();

            entity
                .HasOne(value => value.Grid2GlobalEvent)
                .WithMany(value => value.Races)
                .HasForeignKey(value => value.Grid2GlobalEventRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Grid2GlobalScoreRecord>(entity =>
        {
            entity.HasIndex(value => new { value.RaceNetEventId, value.RaceNetRaceId });
            entity.HasIndex(value => new { value.PlayerProfileId, value.RaceNetEventId, value.RaceNetRaceId });

            entity
                .HasOne(value => value.PlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.PlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Grid2RivalSessionDataRecord>(entity =>
        {
            entity.ToTable("Grid2RivalSessionData");
            entity.HasIndex(value => value.PlayerProfileId).IsUnique();

            entity
                .HasOne(value => value.PlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.PlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Grid2RivalOpponentRecord>(entity =>
        {
            entity.ToTable("Grid2RivalOpponents");
            entity.HasIndex(value => new { value.PlayerProfileId, value.OpponentPlayerProfileId }).IsUnique();
            entity.HasIndex(value => value.LastSeenAt);

            entity
                .HasOne(value => value.PlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.PlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(value => value.OpponentPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.OpponentPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Grid2RivalAssignmentRecord>(entity =>
        {
            entity.ToTable("Grid2RivalAssignments");
            entity.HasIndex(value => new { value.PlayerProfileId, value.StartsAt, value.Type }).IsUnique();
            entity.HasIndex(value => value.ExpiresAt);

            entity
                .HasOne(value => value.PlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.PlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(value => value.RivalPlayerProfile)
                .WithMany()
                .HasForeignKey(value => value.RivalPlayerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
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
