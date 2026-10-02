using System.Text.Json;
using RaceNetShowdown.Server.RaceNet;
using Xunit;
using static RaceNetShowdown.Server.Tests.Dirt4ProTourTests;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4ProTourProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CapturedScoreSchemaIsParsedAndRankedByTime()
    {
        var scores = Scores([("Third", 303UL, 792000UL), ("First", 101UL, 449934UL),
            ("Fourth", 404UL, 1027000UL), ("Second", 202UL, 470863UL)]);
        var parsed = EgoNetRequestParser.ReadDirt4ProTourScores(scores);

        Assert.Equal(4, parsed.Count);
        Assert.Equal(("Third", 303UL, 792000UL),
            (parsed[0].Presence.Name, parsed[0].Presence.NetworkId, parsed[0].ScoreMs));
        var store = new Dirt4DailyStore();
        Assert.True(store.RecordProTourScores([1], 4, parsed, Now));
        Assert.Equal(3, store.ProTourProgress("First").Points);
        Assert.Equal(1, store.ProTourProgress("Second").Points);
        Assert.Equal(-1, store.ProTourProgress("Third").Points);
        Assert.Equal(-3, store.ProTourProgress("Fourth").Points);
    }

    [Fact]
    public void OnlyHostCanRecordScoresForItsStartedSession()
    {
        var tour = new Dirt4ProTour();
        var store = new Dirt4DailyStore();
        var scores = Scores(DefaultScores);

        tour.SubmitSessionScores(scores, "host", Now, store);
        Assert.Equal(0, store.ProTourProgress("First").EventsDone);
        tour.SubmitSession(Advertisement(), "host", Now);
        tour.SessionStart(Connection(), "host", Now);
        tour.SubmitSessionScores(scores, "guest", Now.AddMinutes(1), store);
        Assert.Equal(0, store.ProTourProgress("First").EventsDone);
        tour.SubmitSessionScores(scores, "host", Now.AddMinutes(1), store);
        Assert.Equal(1, store.ProTourProgress("First").EventsDone);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void SessionsOutsideTheSupportedStartingFieldCannotRecord(int players)
    {
        var tour = new Dirt4ProTour();
        var store = new Dirt4DailyStore();
        tour.SubmitSession(Advertisement(), "host", Now);
        tour.SessionStart(Connection(players: (uint)players), "host", Now);

        tour.SubmitSessionScores(Scores(DefaultScores), "host", Now.AddMinutes(1), store);

        Assert.Equal(0, store.ProTourProgress("First").EventsDone);
    }

    [Fact]
    public void DuplicateRaceIsIgnoredAndThreeWinsPromoteFromTierSeven()
    {
        var store = new Dirt4DailyStore();
        var parsed = EgoNetRequestParser.ReadDirt4ProTourScores(Scores(DefaultScores));

        Assert.True(store.RecordProTourScores([1], 4, parsed, Now));
        Assert.False(store.RecordProTourScores([1], 4, parsed, Now.AddSeconds(1)));
        Assert.True(store.RecordProTourScores([2], 4, parsed, Now.AddMinutes(1)));
        Assert.True(store.RecordProTourScores([3], 4, parsed, Now.AddMinutes(2)));

        var progress = store.ProTourProgress("First");
        Assert.Equal(3, progress.Division);
        Assert.Equal(6, progress.Tier);
        Assert.Equal(2, progress.Points);
        Assert.Equal(6, progress.PreviousPoints);
        Assert.Equal(3, progress.EventsDone);
        Assert.Equal(12, progress.PromotionPoints);
        Assert.Equal(-16, progress.DemotionPoints);
    }

    [Fact]
    public void ProgressAndProcessedSessionsSurviveStateReload()
    {
        var store = new Dirt4DailyStore();
        var parsed = EgoNetRequestParser.ReadDirt4ProTourScores(Scores(DefaultScores));
        Assert.True(store.RecordProTourScores([1, 2, 3], 4, parsed, Now));

        store = new Dirt4DailyStore(store.Export());

        var progress = store.ProTourProgress("first");
        Assert.Equal((7, 3, 0, 1, 101UL),
            (progress.Tier, progress.Points, progress.PreviousPoints, progress.EventsDone, progress.NetworkId));
        Assert.False(store.RecordProTourScores([1, 2, 3], 4, parsed, Now.AddDays(1)));
        var ladder = Format(Dirt4EgoNetPayloads.BuildLiveLadder(Now, progress));
        Assert.Contains("Tier: si32 value=7", ladder);
        Assert.Contains("Points: si32 value=3", ladder);
        Assert.Contains("PrevPoints: si32 value=0", ladder);
        Assert.Contains("EventsDone: si32 value=1", ladder);
        Assert.Contains("PromotionPoints: si32 value=7", ladder);
        Assert.Equal(Dirt4CommunityEvents.ProTourVehicleClass(Now),
            EgoNetRequestParser.ReadTopLevelInteger(
                Body(Dirt4EgoNetPayloads.BuildLiveLadder(Now, progress)), "CurrentVehClass"));
    }

    [Theory]
    [MemberData(nameof(ScoringTables))]
    public void FullFieldUsesRecoveredScoringTable(int players, int[] expected)
    {
        var submitted = Enumerable.Range(1, players)
            .Select(index => ($"Player{index}", (ulong)index, (ulong)index * 100_000)).ToArray();
        var store = new Dirt4DailyStore();

        Assert.True(store.RecordProTourScores([(byte)players], players,
            EgoNetRequestParser.ReadDirt4ProTourScores(Scores(submitted)), Now));

        Assert.Equal(expected, submitted.Select(score =>
        {
            var progress = store.ProTourProgress(score.Item1);
            return progress.Points + (progress.Tier == 6 ? 7 : 0);
        }).ToArray());
    }

    [Fact]
    public void ClassifiedSurvivorsKeepTheScoringTableFromTheStartingField()
    {
        var submitted = DefaultScores.Take(3).ToArray();
        var store = new Dirt4DailyStore();

        Assert.True(store.RecordProTourScores([9], 4,
            EgoNetRequestParser.ReadDirt4ProTourScores(Scores(submitted)), Now));

        Assert.Equal([3, 1, -1],
            submitted.Select(score => store.ProTourProgress(score.Name).Points).ToArray());
    }

    [Theory]
    [InlineData(4, 6, 0)]
    [InlineData(5, 6, 1)]
    [InlineData(9, 5, 0)]
    [InlineData(10, 5, 1)]
    public void PromotionHandlesExactThresholdOverThresholdAndRemainder(
        int initialPoints, int expectedTier, int expectedPoints)
    {
        var store = StoreWith(new(3, initialPoints < 7 ? 7 : 6, initialPoints, 0, 0, 101, "First"));
        var parsed = EgoNetRequestParser.ReadDirt4ProTourScores(Scores(DefaultScores));

        Assert.True(store.RecordProTourScores([(byte)(20 + initialPoints)], 4, parsed, Now));

        var progress = store.ProTourProgress("First");
        Assert.Equal(expectedTier, progress.Tier);
        Assert.Equal(expectedPoints, progress.Points);
        Assert.Equal(initialPoints, progress.PreviousPoints);
    }

    [Fact]
    public void TierSixDemotesAtMinusSixteen()
    {
        var store = StoreWith(new(3, 6, -15, 0, 2, 303, "Third"));
        var parsed = EgoNetRequestParser.ReadDirt4ProTourScores(Scores(DefaultScores));

        Assert.True(store.RecordProTourScores([40], 4, parsed, Now));

        var progress = store.ProTourProgress("Third");
        Assert.Equal(7, progress.Tier);
        Assert.Equal(0, progress.Points);
        Assert.Equal(-15, progress.PreviousPoints);
        Assert.Equal(3, progress.EventsDone);
    }

    public static TheoryData<int, int[]> ScoringTables => new()
    {
        { 4, [3, 1, -1, -3] },
        { 5, [4, 2, 0, -2, -4] },
        { 6, [5, 3, 1, -1, -3, -5] },
        { 7, [6, 4, 2, 0, -2, -4, -6] },
        { 8, [7, 5, 3, 1, -1, -3, -5, -7] }
    };

    private static Dirt4DailyStore StoreWith(Dirt4ProTourProgress progress)
    {
        var state = new Dirt4PersistentState([], [], [], null,
            new Dictionary<string, Dirt4ProTourProgress>(StringComparer.Ordinal)
            {
                [progress.DisplayName.ToUpperInvariant()] = progress
            }, []);
        return new(JsonSerializer.Serialize(state));
    }

    internal static readonly (string Name, ulong NetworkId, ulong ScoreMs)[] DefaultScores =
    [
        ("First", 101, 100000),
        ("Second", 202, 200000),
        ("Third", 303, 300000),
        ("Fourth", 404, 400000)
    ];

    internal static RaceNetShowdown.Server.Infrastructure.CapturedBody Scores(
        IReadOnlyList<(string Name, ulong NetworkId, ulong ScoreMs)> scores) =>
        Body(EgoNetBinary.Dictionary(
            EgoNetBinary.Bool("IsHost", true),
            EgoNetBinary.Blob("SessionData", [1, 2, 3, 4, 5, 6, 7, 8]),
            EgoNetBinary.Si32("SessionDataLen", 8),
            EgoNetBinary.Vector("SessionScores", scores.Select(score => EgoNetBinary.DictValue(
                EgoNetBinary.Dict("Presence",
                    EgoNetBinary.Bool("IsCrossPlatform", false),
                    EgoNetBinary.Si64("EgonetId", 0),
                    EgoNetBinary.Si64("AccountRef", 0),
                    EgoNetBinary.Ui64("NetworkId", score.NetworkId),
                    EgoNetBinary.Dstr("Name", score.Name)),
                EgoNetBinary.Ui64("ScoreMS", score.ScoreMs))).ToArray())));
}
