namespace RaceNetShowdown.Server.RaceNet;

internal static partial class Dirt4CommunityEvents
{
    private static readonly Lazy<RotationEntry[][]> Rotation = new(BuildRotation);
    // Original full events remain available as source data and for old snapshots, not for new rounds.
    private static readonly HashSet<string> ReferenceOnlyRotations = new(StringComparer.Ordinal)
    {
        "v1/daily-rx-hell",
        "v1/daily-62203-01",
        "v1/owners-62203-01",
        "v1/weekly-0",
        "v1/weekly-1",
        "v1/weekly2-2",
        "v1/weekly2-3",
        "v1/monthly-michigan"
    };

    internal sealed record RotationEntry(string Id, EventDefinition Event);
    private readonly record struct StageSource(int Template, int Index);

    private sealed record ClassChoice(string Key, int Id, int VehicleId);
    // DiRT 4 base.ctpk vehicle_class IDs and one retail vehicle from each class.
    private static readonly ClassChoice[] RallyClasses =
    [
        new("h1-fwd", 101, 468), new("r2", 99, 532),
        new("h2-rwd", 97, 390), new("group-a", 72, 389),
        new("h2-fwd", 100, 534), new("r5", 93, 529),
        new("h3-rwd", 98, 396), new("nr4-r4", 96, 482),
        new("group-b-rwd", 74, 480), new("up-to-2000cc", 94, 490),
        new("f2-kit-car", 86, 483), new("group-b-4wd", 73, 537)
    ];
    private static readonly ClassChoice[] RallycrossClasses =
        [new("rx-supercars", 78, 504), new("rx-super1600", 92, 513), new("rx-lites", 102, 541)];
    private enum Conditions { Sunny, Cloudy, Rain }

    internal static IReadOnlyList<RotationEntry> RotationFor(int slot) => Rotation.Value[slot];

    internal static RotationEntry SelectRotation(int slot, long openedAt, long? testPeriod = null)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(openedAt);
        var period = testPeriod ?? Events[slot].EventMeta.EventType switch
        {
            2 => date.Year * 12L + date.Month - 1,
            1 or 5 => (openedAt - 4 * 86400L - 10 * 3600L) / (7 * 86400L),
            _ => (openedAt - 10 * 3600L) / 86400L
        };
        var entries = Rotation.Value[slot];
        return entries[(int)((period % entries.Length + entries.Length) % entries.Length)];
    }

    private static RotationEntry[][] BuildRotation()
    {
        // Keep captured route seeds and generated-name parameters together.
        var rally = Enumerable.Range(0, 12).SelectMany(stage => new[] { 1, 2, 3, 4 }
            .Where(template => stage < Events[template].StageData.Stages.Length)
            .Select(template => new StageSource(template, stage))).ToArray();
        var daily = new List<RotationEntry>();
        var owners = new List<RotationEntry>();
        int[] cars = [0, 399, 468, 537, 390];
        // Full circuits verified in DiRT 4's local base.ctpk (not DiRT Rally 2 IDs).
        (string Name, uint Track, uint Country, int Length)[] circuits =
        [
            ("lydden-hill", 436, 11, 1300),
            ("holjes", 476, 13, 1200),
            ("hell", 478, 14, 1000),
            ("loheac", 536, 17, 1065),
            ("montalegre", 537, 18, 1000)
        ];
        for (var i = 0; i < rally.Length; i++)
        {
            if (i % 5 == 0)
            {
                var circuit = circuits[i / 5];
                daily.Add(Circuit(circuit.Name, circuit.Track, circuit.Country, circuit.Length));
            }
            var source = rally[i];
            var key = $"{Events[source.Template].EventMeta.EventId}-{source.Index + 1:00}";
            daily.Add(Compose(0, $"daily-{key}", [source], cars[source.Template]));
            owners.Add(Compose(1, $"owners-{key}", [source], cars[source.Template]));
        }

        StageSource[] Block(int template, int start, int count) =>
            Enumerable.Range(start, count).Select(i => new StageSource(template, i)).ToArray();
        StageSource[][] weeks = [Block(2, 0, 6), Block(3, 0, 6), Block(4, 0, 6), Block(4, 6, 6)];
        var weekly = Enumerable.Range(0, 4).Select(i => Compose(2, $"weekly-{i}", weeks[i])).ToArray();
        var weekly2 = Enumerable.Range(0, 4).Select(i => Compose(3, $"weekly2-{i}", weeks[(i + 2) % 4])).ToArray();
        RotationEntry[] monthly =
        [
            Compose(4, "monthly-michigan", Block(4, 0, 12)),
            Compose(4, "monthly-spain-wales", [.. weeks[0], .. weeks[1]]),
            Compose(4, "monthly-wales-michigan", [.. weeks[1], .. weeks[2]]),
            Compose(4, "monthly-michigan-spain", [.. weeks[3], .. weeks[0]])
        ];
        RotationEntry[][] result = [daily.ToArray(), owners.ToArray(), weekly, weekly2, monthly];
        result = result.Select(entries => entries.Where(e => !ReferenceOnlyRotations.Contains(e.Id)).ToArray()).ToArray();
        // The remaining weekly layouts are Michigan's two halves; keep the slots one week apart.
        Array.Reverse(result[3]);
        result = result.Select((entries, slot) => ExpandConditionsAndClasses(entries, slot)).ToArray();
        for (var slot = 0; slot < result.Length; slot++)
            foreach (var entry in result[slot])
                if (!ValidSnapshot(new(1_000_000, 0, 1, new Dictionary<string, Dirt4DailyRun>())
                    { TemplateIndex = slot, RotationId = entry.Id, Definition = entry.Event }))
                    throw new InvalidDataException($"Invalid DiRT 4 rotation {entry.Id}.");
        return result;
    }

    private static RotationEntry[] ExpandConditionsAndClasses(RotationEntry[] routes, int slot)
    {
        var count = (slot < 2 ? routes.Length : RallyClasses.Length) * 3;
        var result = new RotationEntry[count];
        // Complementary four-day cycles guarantee a sunny Daily or Owners Club every day.
        Conditions[] daily = [Conditions.Sunny, Conditions.Cloudy, Conditions.Sunny, Conditions.Rain];
        Conditions[] owners = [Conditions.Rain, Conditions.Sunny, Conditions.Cloudy, Conditions.Sunny];
        for (var index = 0; index < count; index++)
        {
            var cycle = index / (slot < 2 ? routes.Length : RallyClasses.Length);
            var source = routes[(index + cycle) % routes.Length];
            var classOffset = slot is 1 or 3 ? 5 : slot == 4 ? 9 : 0;
            var carClass = source.Event.StageData.Stages[0].TrackModelId > 0
                ? RallycrossClasses[cycle]
                : RallyClasses[(index + (slot < 2 ? cycle * 2 : 0) + classOffset) % RallyClasses.Length];
            var conditions = slot switch
            {
                0 => daily[index % daily.Length],
                1 => owners[index % owners.Length],
                _ => (Conditions)((index + cycle + (slot == 3 ? 1 : 0)) % 3)
            };
            uint[] weather = conditions switch
            {
                Conditions.Sunny => [1],
                Conditions.Cloudy => [2, 3, 4],
                _ => [5, 15, 7]
            };
            // WeatherId uses preset IDs (4F12F227), not weather-setting IDs (C09C7101).
            // Presets 1/2/3/4 are clear/partly cloudy/cloudy/overcast; 5/15/7 are rain.
            // Time IDs 3..6 are morning through late afternoon, keeping clear stages in daylight.
            var stages = source.Event.StageData.Stages.Select((stage, i) => stage with
                { WeatherId = weather[i % weather.Length], TimeOfDayId = (uint)(3 + i % 4) }).ToArray();
            var restrictions = source.Event.Restrictions with
            {
                VehicleIds = slot < 2 ? [new(carClass.VehicleId)] : [],
                VehicleClassIds = slot < 2 ? [] : [new(carClass.Id)]
            };
            // Times and payouts remain provisional capture-derived estimates for feedback tuning.
            result[index] = new($"v2/{source.Id[3..]}/{carClass.Key}/{conditions.ToString().ToLowerInvariant()}",
                source.Event with { Restrictions = restrictions,
                    StageData = source.Event.StageData with { Stages = stages } });
        }
        return result;
    }

    private static RotationEntry Compose(int slot, string key, StageSource[] sources, int? vehicle = null)
    {
        var baseline = Events[slot];
        var first = Events[sources[0].Template];
        long best = 0, target = 0, tier1 = 0, tier2 = 0, tier3 = 0;
        var stages = sources.Select((source, index) =>
        {
            var original = Events[source.Template].StageData.Stages;
            var stage = original[source.Index];
            var previous = source.Index == 0 ? null : original[source.Index - 1];
            best += stage.StageBest;
            target += stage.TargetTime.OverallTime - (previous?.TargetTime.OverallTime ?? 0);
            // Captured tier barriers are cumulative, not individual stage times.
            tier1 += stage.T1T2BarrierTime - (previous?.T1T2BarrierTime ?? 0);
            tier2 += stage.T2T3BarrierTime - (previous?.T2T3BarrierTime ?? 0);
            tier3 += stage.T3T4BarrierTime - (previous?.T3T4BarrierTime ?? 0);
            return stage with
            {
                StageId = checked((byte)(index + 1)), LeaderboardId = StageId(baseline, index),
                HasServiceArea = index % 2 == 0,
                StageOverall = best, TargetTime = stage.TargetTime with { OverallTime = target },
                // Differences of cumulative percentiles can cross for an isolated stage.
                T1T2BarrierTime = tier1, T2T3BarrierTime = Math.Max(tier1, tier2),
                T3T4BarrierTime = Math.Max(tier1, Math.Max(tier2, tier3)),
                PlayerBest = 0, PlayerOverall = 0, PlayerRank = 0,
                DeltaBest = 0, Percentile = 0, DeltaPercentile = 0
            };
        }).ToArray();
        var countries = sources.Select(s => Events[s.Template].StageData.CountryDBId).Distinct().ToArray();
        var restrictions = vehicle is { } car
            ? first.Restrictions with { VehicleIds = [new(car)], VehicleClassIds = [] }
            : first.Restrictions;
        return new($"v1/{key}", baseline with
        {
            EventMeta = baseline.EventMeta with
            {
                DisciplineId = first.EventMeta.DisciplineId, LeaderboardId = stages[^1].LeaderboardId,
                PersonalBest = 0, EventStatus = 0, RanLastEvent = false
            },
            StageData = new(countries.Length == 1 ? countries[0] : 9,
                (uint)stages.Length, (uint)stages.Length, stages),
            Restrictions = restrictions
        });
    }

    private static RotationEntry Circuit(string key, uint track, uint country, int length)
    {
        var entry = Compose(0, $"daily-rx-{key}", [new(0, 0)], 504);
        var stage = entry.Event.StageData.Stages[0];
        long Scale(long value) => checked(value * length / 1000);
        // Reference times are estimates scaled from Hell, not recovered historic records.
        stage = stage with
        {
            TrackModelId = track,
            StageBest = Scale(stage.StageBest), StageOverall = Scale(stage.StageOverall),
            T1T2BarrierTime = Scale(stage.T1T2BarrierTime),
            T2T3BarrierTime = Scale(stage.T2T3BarrierTime), T3T4BarrierTime = Scale(stage.T3T4BarrierTime)
        };
        return entry with { Event = entry.Event with { StageData = new(country, 1, 1, [stage]) } };
    }

    private static long StageId(EventDefinition baseline, int index) =>
        (baseline.EventMeta.LeaderboardId & ~511L) | ((index + 1L) << 5) | (baseline.EventMeta.LeaderboardId & 31);

    internal static bool ValidSnapshot(Dirt4DailyRound round)
    {
        if (round.Definition is not { } e) return round.RotationId is null;
        if (string.IsNullOrWhiteSpace(round.RotationId) || e.EventMeta is not { } meta ||
            e.StageData?.Stages is not { Length: > 0 and <= 12 } stages ||
            e.Restrictions is not { VehicleIds: not null, VehicleClassIds: not null,
                MftrCountryIds: not null, DriveTrainIds: not null, ManufacturerIds: not null } ||
            e.Rewards?.TierRewards is not { Length: 4 } rewards) return false;
        var baseline = Events[round.TemplateIndex];
        if (meta.EventId != baseline.EventMeta.EventId || meta.EventType != baseline.EventMeta.EventType ||
            meta.Name != baseline.EventMeta.Name || meta.SponsorIds is null ||
            e.StageData.TotalStages != stages.Length || e.StageData.AvailableStages != stages.Length ||
            stages.Length != (round.TemplateIndex < 2 ? 1 : round.TemplateIndex < 4 ? 6 : 12) ||
            stages.Any(s => s is null || s.TargetTime is null || s.TrackgenName is null) ||
            meta.LeaderboardId != stages[^1].LeaderboardId) return false;
        return !stages.Where((s, i) => s.StageId != i + 1 || s.LeaderboardId != StageId(baseline, i) ||
            (s.TrackModelId == 0 && (s.TrackGenValue <= 0 || s.LocationId == 0)) ||
            s.T1T2BarrierTime <= 0 || s.T2T3BarrierTime < s.T1T2BarrierTime ||
            s.T3T4BarrierTime < s.T2T3BarrierTime).Any() &&
            rewards.All(r => r is not null && r.MinCredits >= 0 && r.MaxCredits >= r.MinCredits) &&
            rewards.Select(r => r.TierId).Order().SequenceEqual([1, 2, 3, 4]);
    }
}
