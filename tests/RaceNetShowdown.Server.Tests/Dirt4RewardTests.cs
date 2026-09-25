using System.Text.Json;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4RewardTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Player = "driver";

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ColdLoginDiscoversOnlyOwnCompletedExpiredResults(int slot)
    {
        var (store, round) = CreateRound(Now, slot);
        Finish(store, round, Now, Player);
        Assert.Empty(store.PendingResults(Player, Now));
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        store = new Dirt4DailyStore(store.Export());
        Assert.Equal(round.EventId, Assert.Single(store.PendingResults(Player, ended)).EventId);
        var events = Format(Dirt4CommunityEvents.Build(ended, store, Player, []));
        Assert.Contains("EventDescs: vvtr count=6", events);
        Assert.Contains($"EventId: si64 value={round.EventId}", events);
        Assert.Contains("EventStatus: si32 value=3", events);
        Assert.Empty(store.PendingResults("other-driver", ended));
        Assert.DoesNotContain($"EventId: si64 value={round.EventId}",
            Format(Dirt4CommunityEvents.Build(ended, store, "other-driver", [])));
        Assert.Single(new Dirt4DailyStore(store.Export()).PendingResults(Player, ended));
    }

    [Fact]
    public void IssuedResultsStopAutomaticDiscoveryButExplicitRetryIsIdentical()
    {
        var (store, round) = CreateRound(Now);
        Finish(store, round, Now, Player);
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        var first = Dirt4CommunityEvents.BuildResults(ended, store, Player, [round.EventId]);
        Assert.Contains("ActCredReward: si32 value=", Format(first));
        store = new Dirt4DailyStore(store.Export());
        Assert.Empty(store.PendingResults(Player, ended));
        Assert.DoesNotContain($"EventId: si64 value={round.EventId}",
            Format(Dirt4CommunityEvents.Build(ended, store, Player, [])));
        Assert.Equal(first, Dirt4CommunityEvents.BuildResults(ended, store, Player, [round.EventId, round.EventId]));
        Assert.Contains($"EventId: si64 value={round.EventId}",
            Format(Dirt4CommunityEvents.Build(ended, store, Player, [round.EventId], omitScores: true)));
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(ended, store, Player, [])));
        Assert.True(store.Finish(round.StageLeaderboard(0), Player, 100_000, ended, Vehicle(round)));
        Assert.Empty(store.PendingResults(Player, ended));
    }

    [Fact]
    public void ResultRequestsDoNotConsumeUnrelatedPlayersOrEvents()
    {
        var (store, round) = CreateRound(Now);
        Finish(store, round, Now, Player);
        Finish(store, round, Now, "other-driver");
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        var unchanged = store.Export();
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(
            ended, store, Player, [1_790_305_972])));
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(
            Now, store, Player, [round.EventId])));
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(
            ended, store, "stranger", [round.EventId])));
        Assert.Equal(unchanged, store.Export());
        Dirt4CommunityEvents.BuildResults(ended, store, Player, []);
        store = new Dirt4DailyStore(store.Export());
        Assert.Empty(store.PendingResults(Player, ended));
        Assert.Single(store.PendingResults("other-driver", ended));
    }

    [Fact]
    public void PartialAndUnfinishedAttemptsAreNotOfferedAsRewards()
    {
        var (store, round) = CreateRound(Now, 2);
        var vehicle = Vehicle(round);
        Assert.True(store.Start(round.StageLeaderboard(0), Player, Now, vehicle));
        Assert.True(store.Finish(round.StageLeaderboard(0), Player, 100_000, Now.AddSeconds(1), vehicle));
        Assert.True(store.Start(round.StageLeaderboard(1), Player, Now.AddSeconds(2), vehicle));
        store = new Dirt4DailyStore(store.Export());
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        Assert.Empty(store.PendingResults(Player, ended));
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(ended, store, Player, [])));
    }

    [Fact]
    public void LegacyHistoryIsNotAssumedUnpaidButExplicitResultsRemainAvailable()
    {
        var (store, round) = CreateRound(Now);
        Finish(store, round, Now, Player);
        var state = JsonSerializer.Deserialize<Dirt4PersistentState>(store.Export())!;
        var saved = state.CommunityEvents!.Single();
        saved = saved with { Runs = new Dictionary<string, Dirt4DailyRun>(saved.Runs)
            { [Player] = saved.Runs[Player] with { ResultDelivery = Dirt4ResultDelivery.Untracked } } };
        var json = JsonSerializer.Serialize(new[] { saved });
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node[0]!["Runs"]![Player]!.AsObject().Remove("ResultDelivery");
        store = new Dirt4DailyStore(node.ToJsonString());
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        Assert.Empty(store.PendingResults(Player, ended));
        Assert.Contains("Results: vvtr count=0", Format(Dirt4CommunityEvents.BuildResults(ended, store, Player, [])));
        Assert.Contains("ActCredReward: si32 value=", Format(Dirt4CommunityEvents.BuildResults(
            ended, store, Player, [round.EventId])));
        Assert.Equal(Dirt4ResultDelivery.Issued, new Dirt4DailyStore(store.Export()).Find(round.EventId)!.Runs[Player].ResultDelivery);
    }

    internal static (Dirt4DailyStore Store, Dirt4DailyRound Round) CreateRound(DateTimeOffset now, int slot = 0)
    {
        var template = Dirt4CommunityEvents.Template(slot);
        var window = Dirt4EventCalendar.Window(now, template.EventType);
        var id = 30_000_000L + slot;
        var shift = (id - template.EventId) << 9;
        var round = new Dirt4DailyRound(id, window.Start, window.End, new Dictionary<string, Dirt4DailyRun>())
        {
            TemplateIndex = slot, CalendarAligned = true,
            EventLeaderboardId = template.LeaderboardId + shift,
            StageLeaderboardIds = template.StageLeaderboards.Select(lb => lb + shift).ToArray()
        };
        return (new Dirt4DailyStore(JsonSerializer.Serialize(new[] { round })), round);
    }

    internal static void Finish(Dirt4DailyStore store, Dirt4DailyRound round, DateTimeOffset now, string player)
    {
        var vehicle = Vehicle(round);
        for (var i = 0; i < round.StageCount; i++)
        {
            var at = now.AddSeconds(i * 2);
            Assert.True(store.Start(round.StageLeaderboard(i), player, at, vehicle));
            Assert.True(store.Finish(round.StageLeaderboard(i), player, 100_000 + i, at.AddSeconds(1), vehicle));
        }
    }

    private static long Vehicle(Dirt4DailyRound round) =>
        Dirt4CommunityEvents.Template(round).VehicleIds.FirstOrDefault() is > 0 and var vehicle ? vehicle :
        Dirt4CommunityEvents.Definition(round).Restrictions.VehicleClassIds[0].ID switch
            { 101 => 468, 73 => 537, 97 => 390, _ => throw new InvalidDataException() };

    private static string Format(byte[] bytes)
    {
        var text = EgoNetBinaryFormatter.Format(bytes);
        Assert.DoesNotContain("parse-stopped", text);
        Assert.DoesNotContain("trailing-bytes", text);
        return text;
    }
}
