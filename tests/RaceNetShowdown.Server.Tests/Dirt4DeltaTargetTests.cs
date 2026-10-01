using System.Text.Json;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4DeltaTargetTests
{
    private static readonly Dirt4CommunityEvents.RotationEntry Entry =
        Dirt4CommunityEvents.RotationFor(1).First(e =>
            e.Id.StartsWith("v3/delta/", StringComparison.Ordinal));
    private static readonly Dirt4CommunityEvents.EventDefinition Definition = Entry.Event;
    private static Dirt4DeltaCareerReference Reference()
    {
        var s = Definition.StageData.Stages.Single();
        return new(s.CareerStageId, (uint)Definition.Restrictions.VehicleIds.Single().ID,
            s.WeatherId, s.TimeOfDayId, s.GameOptions, Definition.EventMeta.GameOptions, 123456,
            [200000, 300000, 400001], 2, "ProfileResultList");
    }

    private static DateTimeOffset Occurrence(DateTimeOffset from) =>
        Enumerable.Range(0, 700)
            .Select(days => from.Date.AddDays(days).AddHours(12))
            .First(candidate =>
            {
                var window = Dirt4EventCalendar.Window(candidate, 0);
                return Dirt4CommunityEvents.SelectRotation(1, window.Start).Id == Entry.Id;
            });

    [Fact]
    public void UsesSaveWhenMappedBoardHasNoMatchingTimes()
    {
        var r = Reference();
        var target = Dirt4DeltaTargets.Resolve(Definition, [r],
            [new(r.CareerLeaderboardId!.Value, new Dictionary<string, Dirt4StandaloneRun>
                { ["other-car"] = new(100000, r.VehicleId + 1, 0, 1) })]);
        Assert.Equal(new(300000, "ImportedTopTimes", r.CareerLeaderboardId, 3), target);
    }

    [Fact]
    public void UsesVerifiedSavePodiumWithoutInventingLeaderboardId()
    {
        var r = Reference() with { CareerLeaderboardId = null };
        var target = Dirt4DeltaTargets.Resolve(Definition, [r], []);
        Assert.Equal(new(300000, "SaveTop3", null, 3), target);
    }

    [Theory]
    [InlineData(1, 100000)]
    [InlineData(2, 150001)]
    public void AveragesOneBestTimePerPlayerFromExactBoard(int players, long expected)
    {
        var r = Reference();
        var runs = new Dictionary<string, Dirt4StandaloneRun> { ["a"] = new(100000, r.VehicleId, 0, 1) };
        if (players == 2) runs["b"] = new(200001, r.VehicleId, 0, 1);
        var target = Dirt4DeltaTargets.Resolve(Definition, [r], [new(r.CareerLeaderboardId!.Value, runs)]);
        Assert.Equal(new(expected, "CareerLeaderboard", r.CareerLeaderboardId, players), target);
    }

    [Fact]
    public void AveragesOnlyTheThreeFastestCareerTimes()
    {
        var r = Reference();
        var runs = new Dictionary<string, Dirt4StandaloneRun>
        {
            ["a"] = new(400000, r.VehicleId, 0, 1),
            ["b"] = new(100000, r.VehicleId, 0, 1),
            ["c"] = new(300000, r.VehicleId, 0, 1),
            ["d"] = new(200000, r.VehicleId, 0, 1)
        };
        var target = Dirt4DeltaTargets.Resolve(Definition, [r],
            [new(r.CareerLeaderboardId!.Value, runs)]);
        Assert.Equal(new(200000, "CareerLeaderboard", r.CareerLeaderboardId, 3), target);
    }

    [Fact]
    public void DoesNotBorrowReferenceFromOtherConditionsOrHandling()
    {
        var r = Reference();
        foreach (var mismatch in new[] { r with { WeatherId = r.WeatherId + 1 },
            r with { CareerStageId = r.CareerStageId + 1 }, r with { TimeOfDayId = r.TimeOfDayId + 1 },
            r with { StageGameOptions = (ushort)(r.StageGameOptions ^ 1) },
            r with { EventGameOptions = (ushort)(r.EventGameOptions ^ 1) } })
            Assert.Equal("Provisional", Dirt4DeltaTargets.Resolve(Definition, [mismatch], [])!.Source);
    }


    [Fact]
    public void RejectsInvalidOrDuplicateImports()
    {
        var r = Reference();
        foreach (var list in new[] { new[] { r, r }, new[] { r with { SaveTop3TimeMs = [0, 2, 3] } },
            new[] { r with { SaveTop3TimeMs = [3, 2, 1] } },
            new[] { r with { SaveTop3TimeMs = [] } },
            new[] { r with { SaveTop3TimeMs = [1, 2, 3, 4] } },
            new[] { r with { PlayerPodiumPosition = 4 } },
            new[] { r with { Provenance = "" } }, new[] { r with { CareerLeaderboardId = 0 } } })
            Assert.Throws<InvalidDataException>(() => Dirt4DeltaTargets.ReadReferences(
                JsonSerializer.Serialize(new { DeltaCareerReferences = list })));
    }

    [Fact]
    public void SnapshotFreezesAtOpeningAndNextRoundRefreshesCareerMean()
    {
        var r = Reference();
        var state = JsonSerializer.Serialize(new Dirt4PersistentState([], [], [], [r]));
        var store = new Dirt4DailyStore(state);
        var now = Occurrence(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var first = store.Current(now, 1);
        Assert.Equal("ImportedTopTimes", first.DeltaTarget!.Source);
        Assert.True(store.Finish(r.CareerLeaderboardId!.Value, "driver", 200000, now, r.VehicleId));
        store = new Dirt4DailyStore(store.Export());
        Assert.Equal(300000, store.Current(now.AddSeconds(1), 1).DeltaTarget!.TimeMs);
        var nextAt = Occurrence(DateTimeOffset.FromUnixTimeSeconds(first.ExpiresAt).AddDays(1));
        var next = store.Current(nextAt, 1);
        Assert.Equal(new(200000, "CareerLeaderboard", r.CareerLeaderboardId, 1), next.DeltaTarget);
        Assert.NotEqual(first.EventId, next.EventId);
        var persisted = JsonSerializer.Deserialize<Dirt4PersistentState>(store.Export())!
            .DeltaCareerReferences!.Single(reference => reference.CareerLeaderboardId == r.CareerLeaderboardId);
        Assert.Equal(r with { SaveTop3TimeMs = persisted.SaveTop3TimeMs }, persisted);
        Assert.Equal(r.SaveTop3TimeMs, persisted.SaveTop3TimeMs);
    }

    [Fact]
    public void StandingsAndRewardUseFrozenTargetNotNewDeltaResults()
    {
        var r = Reference();
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new Dirt4PersistentState([], [], [], [r])));
        var now = Occurrence(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var round = store.Current(now, 1);
        foreach (var player in new[] { "a", "b" })
        {
            Assert.True(store.Start(round.LeaderboardId, player, now, r.VehicleId));
            Assert.True(store.Finish(round.LeaderboardId, player, 400000, now.AddSeconds(1), r.VehicleId));
        }
        store = new Dirt4DailyStore(store.Export());
        var standings = EgoNetBinaryFormatter.Format(Dirt4CommunityEvents.Build(now.AddSeconds(2), store, "a", [round.EventId]));
        Assert.Contains("DeltaBest: si64 value=300000", standings);
        var result = Dirt4CommunityEvents.BuildResults(DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt), store, "a", [round.EventId]);
        Assert.Contains("EventTargetTime: si64 value=300000", EgoNetBinaryFormatter.Format(result));
        var expected = EgoNetBinary.Dictionary(EgoNetBinary.Dict("TierResult",
            EgoNetBinary.Si32("ActCredReward", round.Definition!.Rewards.TierRewards.Single(t => t.TierId == 2).MinCredits),
            EgoNetBinary.Si32("TierId", 2)));
        Assert.True(result.AsSpan().IndexOf(expected.AsSpan(8)) >= 0);
    }
}


