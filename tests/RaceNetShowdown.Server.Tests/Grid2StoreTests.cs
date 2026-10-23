using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Grid2StoreTests
{
    private const ulong OwnId = 76561198000000101;
    private const ulong FriendId = 76561198000000102;
    private static readonly CancellationToken Token = CancellationToken.None;

    [Fact]
    public async Task ObservingSelfAndFriendPreservesProfilesAndScoresAndExcludesSelfFromFriends()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        var friend = await fixture.AddProfile("Friend", "name:friend");
        var session = Session(self);
        var current = (await fixture.Store.GetGrid2CurrentGlobalEventAsync(session, Token))!;
        await fixture.Store.SaveGrid2GlobalScoreAsync(session,
            new(123_456, 4, current.RaceNetEventId, current.Races[0].RaceNetRaceId, 7), Token);
        var scoreBefore = await fixture.Db.Grid2GlobalScores.AsNoTracking().SingleAsync();

        var resolved = await fixture.Store.ObserveGrid2PresencesAsync(session,
            [new(OwnId, "Driver"), new(FriendId, "Friend")], Token);

        Assert.Equal(OwnId, resolved!.SteamId);
        var storedSelf = await fixture.Db.PlayerProfiles.AsNoTracking().SingleAsync(value => value.Id == self.Id);
        var storedFriend = await fixture.Db.PlayerProfiles.AsNoTracking().SingleAsync(value => value.Id == friend.Id);
        Assert.Equal("name:driver", storedSelf.ExternalId);
        Assert.Equal("name:friend", storedFriend.ExternalId);
        Assert.Equal(2, await fixture.Db.PlayerProfiles.CountAsync());
        var friendship = await fixture.Db.PlayerFriends.AsNoTracking().SingleAsync();
        Assert.Equal(self.Id, friendship.PlayerProfileId);
        Assert.Equal(friend.Id, friendship.FriendPlayerProfileId);
        Assert.Equal(FriendId.ToString(), friendship.SteamId);
        Assert.NotEqual(friendship.PlayerProfileId, friendship.FriendPlayerProfileId);
        var scoreAfter = await fixture.Db.Grid2GlobalScores.AsNoTracking().SingleAsync();
        Assert.Equal(scoreBefore.Id, scoreAfter.Id);
        Assert.Equal(self.Id, scoreAfter.PlayerProfileId);
        Assert.Equal(123_456, scoreAfter.Score);
        Assert.Equal(scoreBefore.RaceNetEventId, scoreAfter.RaceNetEventId);
        Assert.Equal(scoreBefore.RaceNetRaceId, scoreAfter.RaceNetRaceId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AmbiguousOrAlreadyClaimedSelfIdentityDoesNotCreateFriendships(int conflict)
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        await fixture.AddProfile("Friend", "name:friend");
        var presences = new List<RaceNetPrincipal> { new(OwnId, "Driver"), new(FriendId, "Friend") };
        if (conflict == 0)
            presences.Add(new(FriendId + 1, " DRIVER "));
        else if (conflict == 1)
            await fixture.AddProfile(" DRIVER ", "name:other-driver");
        else
            await fixture.AddProfile("Different Driver", $"steam:{OwnId}");

        Assert.Null(await fixture.Store.ObserveGrid2PresencesAsync(Session(self), presences, Token));
        Assert.Empty(await fixture.Db.PlayerFriends.ToListAsync());
        Assert.Equal("name:driver",
            (await fixture.Db.PlayerProfiles.AsNoTracking().SingleAsync(value => value.Id == self.Id)).ExternalId);
    }

    [Fact]
    public async Task ObservedNamesDoNotOverwriteOrBorrowAnEstablishedSteamIdentity()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", $"steam:{OwnId}");
        var establishedFriend = await fixture.AddProfile("Old Friend Name", $"steam:{FriendId}");
        await fixture.AddProfile("Namesake", "name:namesake-one");
        await fixture.AddProfile("Namesake", "name:namesake-two");
        var unrelated = await fixture.AddProfile("Protected Driver", $"steam:{FriendId + 3}");

        await fixture.Store.ObserveGrid2PresencesAsync(Session(self),
            [new(OwnId, "Renamed Driver"), new(FriendId, "Renamed Friend"),
             new(FriendId + 1, "Namesake"), new(FriendId + 2, "Protected Driver")], Token);

        var friendship = await fixture.Db.PlayerFriends.AsNoTracking().SingleAsync();
        Assert.Equal(establishedFriend.Id, friendship.FriendPlayerProfileId);
        Assert.Equal(FriendId.ToString(), friendship.SteamId);
        Assert.Equal("Old Friend Name",
            (await fixture.Db.PlayerProfiles.AsNoTracking().SingleAsync(value => value.Id == establishedFriend.Id)).DisplayName);
        Assert.Equal($"steam:{FriendId + 3}",
            (await fixture.Db.PlayerProfiles.AsNoTracking().SingleAsync(value => value.Id == unrelated.Id)).ExternalId);
    }

    [Fact]
    public async Task FriendWithUnicodeAndPunctuationBecomesSocialRivalOnlyAfterGrid2Activity()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        var friend = await fixture.AddProfile("João ★ [BR]", "name:friend");
        var session = Session(self);
        await fixture.Store.ObserveGrid2PresencesAsync(session,
            [new(FriendId, friend.DisplayName), new(OwnId, self.DisplayName)], Token);

        Assert.Empty((await fixture.Store.GetGrid2RivalsAsync(session, Token)).Rivals);
        await fixture.Store.SaveGrid2RivalSessionDataAsync(Session(friend), [1], Token);
        var rival = Assert.Single((await fixture.Store.GetGrid2RivalsAsync(session, Token)).Rivals);

        Assert.Equal(friend.Id, rival.EgonetId);
        Assert.Equal(friend.DisplayName, rival.Name);
        Assert.Equal(FriendId, rival.SteamId);
        Assert.Equal(2, rival.Type);
    }

    [Fact]
    public async Task ObservingANewActiveFriendDoesNotReplaceExistingWeeklyAssignments()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        var oldRival = await fixture.AddProfile("Old Rival", "name:old-rival");
        var newFriend = await fixture.AddProfile("New Friend", "name:new-friend");
        var session = Session(self);
        await fixture.Store.SaveGrid2RivalSessionDataAsync(Session(oldRival), [1], Token);
        var before = await fixture.Store.GetGrid2RivalsAsync(session, Token);
        Assert.Equal(oldRival.Id, Assert.Single(before.Rivals).EgonetId);

        await fixture.Store.ObserveGrid2PresencesAsync(session,
            [new(OwnId, self.DisplayName), new(FriendId, newFriend.DisplayName)], Token);
        await fixture.Store.SaveGrid2RivalSessionDataAsync(Session(newFriend), [2], Token);
        var after = await fixture.Store.GetGrid2RivalsAsync(session, Token);

        Assert.Equal(before.StartsAt, after.StartsAt);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
        Assert.Equal(before.Rivals.ToArray(), after.Rivals.ToArray());
        Assert.Single(await fixture.Db.Grid2RivalAssignments.ToListAsync());
    }

    [Fact]
    public async Task RequestedArchivedEventReturnsItsOwnScoresAndDoesNotFallBackForUnknownOrActiveIds()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        var session = Session(self);
        var current = (await fixture.Store.GetGrid2CurrentGlobalEventAsync(session, Token))!;
        var previous = (await fixture.Store.GetGrid2PreviousGlobalEventAsync(session, Token))!;
        var archived = new Grid2GlobalEventRecord
        {
            RaceNetEventId = 111,
            Status = "archived",
            StartsAt = current.StartsAt.AddDays(-21),
            ExpiresAt = current.StartsAt.AddDays(-14),
            CreatedAt = current.StartsAt.AddDays(-21),
            ClosedAt = current.StartsAt.AddDays(-14),
            Races = [new() { RaceNetRaceId = 222, SortOrder = 1 }]
        };
        fixture.Db.Grid2GlobalEvents.Add(archived);
        fixture.Db.Grid2GlobalScores.Add(new()
        {
            PlayerProfileId = self.Id,
            RaceNetEventId = 111,
            RaceNetRaceId = 222,
            Score = 1234,
            SubmittedAt = archived.StartsAt.AddHours(1)
        });
        await fixture.Db.SaveChangesAsync();

        var result = (await fixture.Store.GetGrid2PreviousGlobalEventAsync(session, Token, 111))!;

        Assert.Equal(111, result.RaceNetEventId);
        Assert.Equal(222, Assert.Single(result.Races).RaceNetRaceId);
        var score = Assert.Single(result.LeaderboardEntries);
        Assert.Equal(self.Id, score.EgonetId);
        Assert.Equal(1234, score.PersonalBest);
        var payload = await fixture.StoreResponse(session, "RaceNetGlobalDomination.GetPreviousEvent",
            EgoNetBinary.Dictionary(EgoNetBinary.Si64("RaceNetEventId", 111),
                Presences(new RaceNetPrincipal(OwnId, self.DisplayName))));
        Assert.Contains("RaceNetId: si64 value=222", payload);
        Assert.Contains($"SteamId: ui64 value={OwnId}", payload);
        Assert.Contains("PersonalBest: si64 value=1234", payload);
        Assert.Equal(previous.RaceNetEventId,
            (await fixture.Store.GetGrid2PreviousGlobalEventAsync(session, Token))!.RaceNetEventId);
        Assert.Null(await fixture.Store.GetGrid2PreviousGlobalEventAsync(session, Token, 999_999));
        Assert.Null(await fixture.Store.GetGrid2PreviousGlobalEventAsync(session, Token, current.RaceNetEventId));
        Assert.Single(await fixture.Db.Grid2GlobalScores.ToListAsync());
        Assert.Empty(await fixture.Db.Grid2RivalAssignments.Where(value => value.StartsAt == archived.StartsAt).ToListAsync());
    }

    [Fact]
    public async Task GlobalChallengePayloadUsesObservedSelfAndFriendSteamIdsWithoutMigratingTheirScores()
    {
        await using var fixture = await Fixture.Create();
        var self = await fixture.AddProfile("Driver", "name:driver");
        var friend = await fixture.AddProfile("Friend", "name:friend");
        var session = Session(self);
        var current = (await fixture.Store.GetGrid2CurrentGlobalEventAsync(null, Token))!;
        await fixture.Store.SaveGrid2GlobalScoreAsync(session,
            new(1234, 4, current.RaceNetEventId, current.Races[0].RaceNetRaceId, 7), Token);
        await fixture.Store.SaveGrid2GlobalScoreAsync(Session(friend),
            new(2345, 4, current.RaceNetEventId, current.Races[0].RaceNetRaceId, 7), Token);
        await fixture.Store.SaveGrid2RivalSessionDataAsync(Session(friend), [1], Token);

        var payload = await fixture.StoreResponse(session, "RaceNetGlobalDomination.GetEvent",
            EgoNetBinary.Dictionary(Presences(new RaceNetPrincipal(OwnId, self.DisplayName),
                new RaceNetPrincipal(FriendId, friend.DisplayName))));

        Assert.Contains("Leaderboard: vvtr count=2", payload);
        Assert.Contains($"SteamId: ui64 value={OwnId}", payload);
        Assert.Contains($"SteamId: ui64 value={FriendId}", payload);
        Assert.Contains("PersonalBest: si64 value=1234", payload);
        Assert.Contains("PersonalBest: si64 value=2345", payload);
        Assert.Equal("name:driver", (await fixture.Db.PlayerProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == self.Id)).ExternalId);
        Assert.Equal("name:friend", (await fixture.Db.PlayerProfiles.AsNoTracking()
            .SingleAsync(value => value.Id == friend.Id)).ExternalId);
        Assert.Equal(new[] { self.Id, friend.Id }, await fixture.Db.Grid2GlobalScores
            .OrderBy(value => value.PlayerProfileId).Select(value => value.PlayerProfileId).ToArrayAsync());
    }

    private static EgoNetField Presences(params RaceNetPrincipal[] presences)
        => EgoNetBinary.Vector("Presence", presences.Select(value => EgoNetBinary.DictValue(
            EgoNetBinary.Ui64("SteamId", value.SteamId), EgoNetBinary.Dstr("Name", value.Name))).ToArray());

    private static RaceNetSessionInfo Session(PlayerProfile profile)
        => new("session-" + profile.Id, profile.Id, profile.ExternalId, profile.DisplayName);

    private sealed class Fixture(SqliteConnection connection, RaceNetDbContext db, EntityFrameworkRaceNetStore store)
        : IAsyncDisposable
    {
        public RaceNetDbContext Db { get; } = db;
        public EntityFrameworkRaceNetStore Store { get; } = store;

        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new RaceNetDbContext(new DbContextOptionsBuilder<RaceNetDbContext>()
                .UseSqlite(connection).Options);
            var store = new EntityFrameworkRaceNetStore(db, new TestEnvironment(),
                NullLogger<EntityFrameworkRaceNetStore>.Instance);
            await db.Database.EnsureCreatedAsync(Token);
            return new(connection, db, store);
        }

        public async Task<PlayerProfile> AddProfile(string name, string externalId)
        {
            var profile = new PlayerProfile
            {
                DisplayName = name,
                ExternalId = externalId,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            Db.PlayerProfiles.Add(profile);
            await Db.SaveChangesAsync();
            return profile;
        }

        public async Task<string> StoreResponse(RaceNetSessionInfo session, string function, byte[] bytes)
        {
            var body = new CapturedBody(bytes.Length, string.Empty, string.Empty, false, bytes, bytes);
            var response = await Grid2EgoNetPayloads.TryBuildAsync(function, body, session,
                new Dictionary<string, string>(), Store, Token);
            var formatted = EgoNetBinaryFormatter.Format(response!.BodyBytes);
            Assert.DoesNotContain("parse-stopped", formatted);
            return formatted;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Grid2StoreTests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "egonet-grid2-test-" + Guid.NewGuid().ToString("N"));
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
