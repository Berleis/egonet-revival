using System.Text.Json;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4RotationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Variants() => Enumerable.Range(0, 5).SelectMany(slot =>
        Enumerable.Range(0, Dirt4CommunityEvents.RotationFor(slot).Count).Select(index => new object[] { slot, index }));

    [Fact]
    public void CatalogHasDistinctOptionsAndOnlyFiveLiveSlots()
    {
        Assert.Equal(new[] { 28, 24, 2, 2, 3 }, Enumerable.Range(0, 5)
            .Select(i => Dirt4CommunityEvents.RotationFor(i).Count));
        var store = new Dirt4DailyStore();
        var formatted = Format(Dirt4CommunityEvents.Build(Now, store, "driver", []));
        Assert.Contains("EventDescs: vvtr count=5", formatted);
        foreach (var slot in Enumerable.Range(0, 5))
        {
            var entries = Dirt4CommunityEvents.RotationFor(slot);
            Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
            Assert.Equal(entries.Count, entries.Select(e => string.Join("/", e.Event.StageData.Stages
                .Select(s => $"{s.TrackModelId}:{s.TrackGenValue}"))).Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EveryVariantPersistsCompletesAndPaysFromItsSnapshot(int slot, int index)
    {
        var round = Round(slot, index);
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new[] { round }));
        var restrictions = round.Definition!.Restrictions;
        var vehicle = restrictions.VehicleIds.Length > 0 ? restrictions.VehicleIds[0].ID :
            restrictions.VehicleClassIds[0].ID switch { 101 => 468, 73 => 537, 97 => 390, _ => throw new InvalidDataException() };
        for (var stage = 0; stage < round.StageCount; stage++)
        {
            var at = Now.AddSeconds(stage * 2);
            Assert.True(store.Start(round.StageLeaderboard(stage), "driver", at, vehicle));
            Assert.True(store.Finish(round.StageLeaderboard(stage), "driver", 100_000 + stage, at.AddSeconds(1), vehicle));
            store = new Dirt4DailyStore(store.Export());
            var board = store.Leaderboard(round.LeaderboardId, "driver", true);
            Assert.Equal(stage == round.StageCount - 1 ? 1 : 0, board!.Entries.Count);
        }
        Assert.Equal(round.RotationId, store.Find(round.EventId)!.RotationId);
        var events = Dirt4CommunityEvents.Build(Now, store, "driver", [round.EventId]);
        Assert.Contains($"TrackModelId: ui32 value={round.Definition!.StageData.Stages[0].TrackModelId}", Format(events));
        Assert.Equal(events, Dirt4CommunityEvents.Build(Now, new Dirt4DailyStore(store.Export()), "driver", [round.EventId]));
        Assert.Empty(store.Completed("driver", Now, []));
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        Assert.Single(store.Completed("driver", ended, []));
        var results = Dirt4CommunityEvents.BuildResults(ended, store, "driver", [round.EventId]);
        Assert.Contains("ActCredReward: si32 value=", Format(results));
        Assert.Equal(results, Dirt4CommunityEvents.BuildResults(ended, new Dirt4DailyStore(store.Export()), "driver", [round.EventId]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ActiveLegacyRoundsAreNotReplacedByTheNewCatalog(int slot)
    {
        var template = Dirt4CommunityEvents.Template(slot);
        var window = Dirt4EventCalendar.Window(Now, template.EventType);
        var id = 30_000_000 + slot;
        var shift = (id - template.EventId) << 9;
        var legacy = new Dirt4DailyRound(id, window.Start, window.End, new Dictionary<string, Dirt4DailyRun>())
        {
            TemplateIndex = slot, CalendarAligned = true,
            EventLeaderboardId = template.LeaderboardId + shift,
            StageLeaderboardIds = template.StageLeaderboards.Select(x => x + shift).ToArray()
        };
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new[] { legacy }));
        Assert.Equal(id, store.Current(Now, slot).EventId);
        Assert.Null(store.Current(Now, slot).Definition);
        var before = Dirt4CommunityEvents.Build(Now, store, "driver", [id]);
        var next = store.Current(DateTimeOffset.FromUnixTimeSeconds(window.End), slot);
        Assert.NotEqual(id, next.EventId);
        Assert.NotNull(next.Definition);
        Assert.Equal(before, Dirt4CommunityEvents.Build(Now, new Dirt4DailyStore(store.Export()), "driver", [id]));
    }

    [Fact]
    public void SnapshotOutlivesChangesToTheRotationCatalog()
    {
        var round = Round(0, 0);
        var definition = round.Definition!;
        var stage = definition.StageData.Stages[0] with { WeatherId = 12, TrackModelId = 536 };
        var rewards = definition.Rewards.TierRewards.Select(r => r with { MinCredits = 123, MaxCredits = 123 }).ToArray();
        round = round with { RotationId = "retired-catalog/custom-event", Definition = definition with
        { StageData = definition.StageData with { Stages = [stage] }, Rewards = new(false, rewards) } };
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new[] { round }));
        Assert.Equal(536U, store.Current(Now).Definition!.StageData.Stages[0].TrackModelId);
        Assert.True(store.Start(round.LeaderboardId, "driver", Now, 504));
        Assert.True(store.Finish(round.LeaderboardId, "driver", 100_000, Now.AddSeconds(1), 504));
        store = new Dirt4DailyStore(store.Export());
        Assert.Contains("ActCredReward: si32 value=123", Format(Dirt4CommunityEvents.BuildResults(
            DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt), store, "driver", [round.EventId])));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ConsecutivePeriodsChangeContentWithoutChangingCurrentRound(int slot)
    {
        var store = new Dirt4DailyStore();
        var round = store.Current(Now, slot);
        Assert.Equal(round.EventId, store.Current(Now.AddMinutes(1), slot).EventId);
        Assert.Equal(round.RotationId, new Dirt4DailyStore().Current(Now.AddMinutes(1), slot).RotationId);
        var next = store.Current(DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt), slot);
        Assert.NotEqual(round.RotationId, next.RotationId);
        Assert.Empty(round.StageLeaderboardIds!.Intersect(next.StageLeaderboardIds!));
    }

    [Fact]
    public void TestDailyChangesContentAtFiveMinuteRollover()
    {
        var store = new Dirt4DailyStore(dailyTestSeconds: 300);
        var first = store.Current(Now);
        Assert.Equal(300, first.ExpiresAt - first.OpenedAt);
        Assert.Equal(first.EventId, new Dirt4DailyStore(store.Export(), 300).Current(Now.AddSeconds(299)).EventId);
        var next = store.Current(Now.AddMinutes(5));
        Assert.NotEqual(first.RotationId, next.RotationId);
        Assert.Equal(first.ExpiresAt, next.OpenedAt);
    }

    [Fact]
    public void WeeklySlotsAreDifferentAndIdsDoNotCollideOverAYear()
    {
        var store = new Dirt4DailyStore();
        var ids = new Dictionary<long, long>();
        for (var day = 0; day < 370; day++)
        {
            var rounds = Enumerable.Range(0, 5).Select(slot => store.Current(Now.AddDays(day), slot)).ToArray();
            Assert.NotEqual(rounds[2].Definition!.StageData.Stages[0].TrackGenValue,
                rounds[3].Definition!.StageData.Stages[0].TrackGenValue);
            foreach (var round in rounds)
                foreach (var leaderboard in round.StageLeaderboardIds!)
                    if (!ids.TryAdd(leaderboard, round.EventId)) Assert.Equal(round.EventId, ids[leaderboard]);
        }
        Assert.NotEmpty(new Dirt4DailyStore(store.Export()).Export());
    }

    [Fact]
    public void DailyVehicleRestrictionUsesSelectedVariantNotOriginalFiesta()
    {
        var round = Round(0, 1);
        var vehicle = Dirt4CommunityEvents.Template(round).VehicleIds.Single();
        Assert.NotEqual(504, vehicle);
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new[] { round }));
        Assert.False(store.Start(round.LeaderboardId, "driver", Now, 504));
        Assert.True(store.Start(round.LeaderboardId, "driver", Now, vehicle));
    }

    [Fact]
    public void InvalidSnapshotIsRejectedInsteadOfSilentlyResettingState()
    {
        var round = Round(0, 0);
        var invalid = round with { Definition = round.Definition! with
        { StageData = round.Definition.StageData with { Stages = [] } } };
        Assert.Throws<InvalidDataException>(() => new Dirt4DailyStore(JsonSerializer.Serialize(new[] { invalid })));
    }

    [Fact]
    public void RallySeedsKeepTheirCapturedLocationNameAndConditions()
    {
        using var source = typeof(RaceNetOptions).Assembly.GetManifestResourceStream(
            "RaceNetShowdown.Server.RaceNet.dirt4-community-events.json")!;
        var original = JsonSerializer.Deserialize<Dirt4CommunityEvents.EventDefinition[]>(source)!;
        var tuples = original.SelectMany(e => e.StageData.Stages).Where(s => s.TrackGenValue > 0)
            .Select(s => (s.TrackGenValue, s.LocationId, s.TimeOfDayId, s.WeatherId, s.TrackgenName)).ToHashSet();
        foreach (var entry in Enumerable.Range(0, 5).SelectMany(Dirt4CommunityEvents.RotationFor))
            foreach (var stage in entry.Event.StageData.Stages.Where(s => s.TrackGenValue > 0))
                Assert.Contains((stage.TrackGenValue, stage.LocationId, stage.TimeOfDayId, stage.WeatherId, stage.TrackgenName), tuples);
        var weekly = Dirt4CommunityEvents.RotationFor(2)[0].Event;
        Assert.Equal(original[4].StageData.Stages.Take(6).Select(s => s.T3T4BarrierTime),
            weekly.StageData.Stages.Select(s => s.T3T4BarrierTime));
    }

    [Fact]
    public void OriginalFullEventsAreExcludedFromEveryActiveSlot()
    {
        using var source = typeof(RaceNetOptions).Assembly.GetManifestResourceStream(
            "RaceNetShowdown.Server.RaceNet.dirt4-community-events.json")!;
        var original = JsonSerializer.Deserialize<Dirt4CommunityEvents.EventDefinition[]>(source)!;
        var referenceRoutes = original.Select(e => e.StageData.Stages.Select(s =>
            (s.TrackModelId, s.TrackGenValue, s.LocationId, s.TimeOfDayId, s.WeatherId)).ToArray()).ToArray();
        foreach (var entry in Enumerable.Range(0, 5).SelectMany(Dirt4CommunityEvents.RotationFor))
        {
            var route = entry.Event.StageData.Stages.Select(s =>
                (s.TrackModelId, s.TrackGenValue, s.LocationId, s.TimeOfDayId, s.WeatherId)).ToArray();
            Assert.DoesNotContain(referenceRoutes, captured => captured.SequenceEqual(route));
        }
    }

    [Theory]
    [InlineData(0, "v1/daily-rx-hell")]
    [InlineData(1, "v1/owners-62203-01")]
    [InlineData(2, "v1/weekly-0")]
    [InlineData(3, "v1/weekly2-3")]
    [InlineData(4, "v1/monthly-michigan")]
    public void DisablingOriginalEventsDoesNotReplaceIssuedSnapshots(int slot, string retiredId)
    {
        var template = Dirt4CommunityEvents.Template(slot);
        var round = Round(slot, 0);
        using var source = typeof(RaceNetOptions).Assembly.GetManifestResourceStream(
            "RaceNetShowdown.Server.RaceNet.dirt4-community-events.json")!;
        var original = JsonSerializer.Deserialize<Dirt4CommunityEvents.EventDefinition[]>(source)!;
        var shift = (round.EventId - template.EventId) << 9;
        round = round with
        {
            RotationId = retiredId, Definition = original[slot],
            EventLeaderboardId = template.LeaderboardId + shift,
            StageLeaderboardIds = template.StageLeaderboards.Select(id => id + shift).ToArray()
        };
        var store = new Dirt4DailyStore(JsonSerializer.Serialize(new[] { round }));
        Assert.DoesNotContain(Dirt4CommunityEvents.RotationFor(slot), e => e.Id == retiredId);
        Assert.Equal(round.EventId, store.Current(Now, slot).EventId);
        Assert.Equal(retiredId, store.Current(Now, slot).RotationId);
        var before = Dirt4CommunityEvents.Build(Now, store, "driver", [round.EventId]);
        var ended = DateTimeOffset.FromUnixTimeSeconds(round.ExpiresAt);
        var next = store.Current(ended, slot);
        Assert.NotEqual(retiredId, next.RotationId);
        Assert.Equal(before, Dirt4CommunityEvents.Build(Now, new Dirt4DailyStore(store.Export()), "driver", [round.EventId]));
    }

    [Fact]
    public void ProTourStillUsesTheOriginalReferenceConfiguration()
    {
        var text = Format(Dirt4CommunityEvents.BuildProTourConfig(Now));
        Assert.Contains("EventType: si32 value=3", text);
        Assert.Contains("TrackGenValue: si64 value=96702706731492361", text);
        Assert.Contains("ID: si32 value=74", text);
        Assert.Contains("TotalStages: ui32 value=1", text);
    }

    [Theory]
    [InlineData("2026-09-24T09:59:59Z", 0, "2026-09-23T10:00:00Z", "2026-09-24T10:00:00Z")]
    [InlineData("2026-09-28T10:00:00Z", 1, "2026-09-28T10:00:00Z", "2026-10-05T10:00:00Z")]
    [InlineData("2026-09-28T09:59:59Z", 5, "2026-09-21T10:00:00Z", "2026-09-28T10:00:00Z")]
    [InlineData("2028-02-29T20:00:00Z", 2, "2028-02-01T10:00:00Z", "2028-03-01T10:00:00Z")]
    [InlineData("2027-01-01T09:59:59Z", 2, "2026-12-01T10:00:00Z", "2027-01-01T10:00:00Z")]
    public void ResetsRemainAtOriginalUtcBoundary(string at, int type, string start, string end)
    {
        var window = Dirt4EventCalendar.Window(DateTimeOffset.Parse(at), type);
        Assert.Equal(DateTimeOffset.Parse(start).ToUnixTimeSeconds(), window.Start);
        Assert.Equal(DateTimeOffset.Parse(end).ToUnixTimeSeconds(), window.End);
    }

    private static Dirt4DailyRound Round(int slot, int index)
    {
        var entry = Dirt4CommunityEvents.RotationFor(slot)[index];
        var template = Dirt4CommunityEvents.Template(entry.Event);
        var window = Dirt4EventCalendar.Window(Now, template.EventType);
        var id = 30_000_000 + slot * 100 + index;
        var shift = (id - template.EventId) << 9;
        return new(id, window.Start, window.End, new Dictionary<string, Dirt4DailyRun>())
        {
            TemplateIndex = slot, CalendarAligned = true, RotationId = entry.Id, Definition = entry.Event,
            EventLeaderboardId = template.LeaderboardId + shift,
            StageLeaderboardIds = template.StageLeaderboards.Select(lb => lb + shift).ToArray()
        };
    }

    private static string Format(byte[] bytes)
    {
        var text = EgoNetBinaryFormatter.Format(bytes);
        Assert.DoesNotContain("parse-stopped", text);
        Assert.DoesNotContain("trailing-bytes", text);
        return text;
    }
}
