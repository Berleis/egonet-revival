using System.Text.Json;

namespace RaceNetShowdown.Server.RaceNet;

// Import only verified career podiums and exact career-board mappings, never inferred leaderboard IDs.
internal sealed record Dirt4DeltaCareerReference(uint CareerStageId, uint VehicleId,
    uint WeatherId, uint TimeOfDayId, ushort StageGameOptions, ushort EventGameOptions,
    long? CareerLeaderboardId, IReadOnlyList<long> SaveTop3TimeMs, int? PlayerPodiumPosition,
    string Provenance);

internal sealed record Dirt4DeltaTarget(long TimeMs, string Source, long? CareerLeaderboardId, int Samples);

internal static class Dirt4DeltaTargets
{
    internal static IReadOnlyList<Dirt4DeltaCareerReference> ReadReferences(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson)) return [];
        using var json = JsonDocument.Parse(stateJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("DeltaCareerReferences", out var value) ||
            value.ValueKind == JsonValueKind.Null) return [];
        var references = value.Deserialize<List<Dirt4DeltaCareerReference>>()
            ?? throw new InvalidDataException("Invalid Delta career references.");
        if (references.Any(r => r is null || r.CareerStageId == 0 || r.VehicleId == 0 ||
                r.CareerLeaderboardId is <= 0 || r.SaveTop3TimeMs is null || r.SaveTop3TimeMs.Count is < 1 or > 3 ||
                (r.CareerLeaderboardId is null && r.SaveTop3TimeMs.Count != 3) ||
                r.SaveTop3TimeMs.Any(t => t is <= 0 or > uint.MaxValue) ||
                !r.SaveTop3TimeMs.SequenceEqual(r.SaveTop3TimeMs.Order()) ||
                r.PlayerPodiumPosition is < 1 or > 3 || string.IsNullOrWhiteSpace(r.Provenance)) ||
            references.GroupBy(r => (r.CareerStageId, r.VehicleId, r.WeatherId, r.TimeOfDayId,
                r.StageGameOptions, r.EventGameOptions)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Invalid or ambiguous Delta career references.");
        return references;
    }

    internal static Dirt4DeltaTarget? Resolve(Dirt4CommunityEvents.EventDefinition definition,
        IReadOnlyList<Dirt4DeltaCareerReference> references,
        IReadOnlyList<Dirt4StandaloneLeaderboard> boards)
    {
        if (definition.EventMeta.EventType != 3 || definition.EventMeta.EventCompType != 1) return null;
        var stage = definition.StageData.Stages.Single();
        var vehicle = definition.Restrictions.VehicleIds.Single().ID;
        var reference = references.SingleOrDefault(r => r.CareerStageId == stage.CareerStageId &&
            r.VehicleId == vehicle && r.WeatherId == stage.WeatherId && r.TimeOfDayId == stage.TimeOfDayId &&
            r.StageGameOptions == stage.GameOptions && r.EventGameOptions == definition.EventMeta.GameOptions);
        // Legacy catalogue compatibility only, not a substitute for a missing imported career podium.
        if (reference is null) return new(stage.T1T2BarrierTime, "Provisional", null, 0);
        var board = reference.CareerLeaderboardId is { } leaderboardId
            ? boards.SingleOrDefault(b => b.LeaderboardId == leaderboardId)
            : null;
        var times = board?.Runs.Where(r => r.Value.VehicleId == reference.VehicleId &&
                r.Value.TimeMs is > 0 and <= uint.MaxValue)
            .Select(r => r.Value.TimeMs).Order().Take(3).ToArray() ?? [];
        return times.Length == 0
            ? new(Mean(reference.SaveTop3TimeMs), reference.CareerLeaderboardId is null
                    ? "SaveTop3" : "ImportedTopTimes",
                reference.CareerLeaderboardId, reference.SaveTop3TimeMs.Count)
            : new((long)Math.Round(times.Average(t => (decimal)t), MidpointRounding.AwayFromZero),
                "CareerLeaderboard", reference.CareerLeaderboardId, times.Length);
    }

    private static long Mean(IEnumerable<long> times) =>
        (long)Math.Round(times.Average(t => (decimal)t), MidpointRounding.AwayFromZero);

    internal static bool Valid(Dirt4DeltaTarget? target) => target is null ||
        (target.TimeMs is > 0 and <= uint.MaxValue && target.Source switch
        {
            "Save" => target.CareerLeaderboardId > 0 && target.Samples == 0, // Frozen legacy round.
            "SaveTop3" => target.CareerLeaderboardId is null or > 0 && target.Samples == 3,
            "ImportedTopTimes" => target.CareerLeaderboardId > 0 && target.Samples is > 0 and <= 3,
            "CareerLeaderboard" => target.CareerLeaderboardId > 0 && target.Samples > 0,
            "Provisional" => target.CareerLeaderboardId is null && target.Samples == 0,
            _ => false
        });
}

