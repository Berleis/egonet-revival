using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceNetShowdown.Server.RaceNet;

internal sealed record Dirt4CareerDeltaEntry(uint CareerStageId, long CareerLeaderboardId,
    uint VehicleId, uint LocationId, uint CountryDbId, uint WeatherId, uint TimeOfDayId,
    int LengthMeters, IReadOnlyList<long> SeedTopTimesMs, string Provenance,
    uint NameRegionId, uint NameFlavourId, uint NameStyleId,
    bool NameIsReversed, int NameStageIndex)
{
    internal Dirt4CommunityEvents.TrackgenName TrackgenName =>
        new(NameRegionId, NameFlavourId, NameStyleId, NameIsReversed, NameStageIndex);
}

internal static class Dirt4CareerDeltaCatalog
{
    internal static readonly IReadOnlyList<Dirt4CareerDeltaEntry> Entries = Load();
    internal static readonly IReadOnlyList<Dirt4DeltaCareerReference> References = Entries.Select(e =>
        new Dirt4DeltaCareerReference(e.CareerStageId, e.VehicleId, e.WeatherId, e.TimeOfDayId,
            3, 200, e.CareerLeaderboardId, e.SeedTopTimesMs, null, e.Provenance)).ToArray();

    private static IReadOnlyList<Dirt4CareerDeltaEntry> Load()
    {
        using var stream = typeof(Dirt4CareerDeltaCatalog).Assembly.GetManifestResourceStream(
            "RaceNetShowdown.Server.RaceNet.dirt4-career-delta.json")
            ?? throw new InvalidDataException("Missing DiRT 4 career Delta catalog.");
        var entries = JsonSerializer.Deserialize<List<Dirt4CareerDeltaEntry>>(stream,
            new JsonSerializerOptions { RespectRequiredConstructorParameters = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty DiRT 4 career Delta catalog.");
        if (entries.Count == 0 || entries.Any(e => e.CareerStageId == 0 || e.CareerLeaderboardId <= 0 ||
                e.VehicleId == 0 || e.LocationId == 0 || e.CountryDbId == 0 || e.WeatherId == 0 ||
                e.TimeOfDayId is < 1 or > 10 || e.LengthMeters <= 0 ||
                e.SeedTopTimesMs is not { Count: > 0 and <= 3 } ||
                e.SeedTopTimesMs.Any(t => t is <= 0 or > uint.MaxValue) ||
                !e.SeedTopTimesMs.SequenceEqual(e.SeedTopTimesMs.Order()) ||
                e.TrackgenName == new Dirt4CommunityEvents.TrackgenName(0, 0, 0, false, 0) ||
                string.IsNullOrWhiteSpace(e.Provenance)) ||
            entries.Select(e => (e.CareerStageId, e.VehicleId)).Distinct().Count() != entries.Count ||
            entries.Select(e => e.CareerLeaderboardId).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Invalid DiRT 4 career Delta catalog.");
        return entries;
    }
}
