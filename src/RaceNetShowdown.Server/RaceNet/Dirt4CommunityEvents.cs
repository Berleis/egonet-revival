using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceNetShowdown.Server.RaceNet;

internal static partial class Dirt4CommunityEvents
{
    private static readonly EventDefinition[] Events = LoadCatalog();
    internal static int TemplateCount => Events.Length;
    internal static Dirt4EventTemplate Template(int index)
    {
        return Template(Events[index]);
    }
    internal static Dirt4EventTemplate Template(Dirt4DailyRound round) => Template(Definition(round));
    internal static EventDefinition Definition(Dirt4DailyRound round) => round.Definition ?? Events[round.TemplateIndex];
    internal static Dirt4EventTemplate Template(EventDefinition e)
    {
        return new(e.EventMeta.EventId, e.EventMeta.EventType, e.EventMeta.LeaderboardId,
            e.StageData.Stages.Select(s => s.LeaderboardId).ToArray(),
            e.Restrictions.VehicleIds.Select(v => (long)v.ID).ToArray());
    }
    internal static byte[] BuildReference(DateTimeOffset now) => Pack(now, Events.Select(e => Schedule(e, now)));

    internal static byte[] BuildProTourConfig(DateTimeOffset now)
    {
        var source = Events[2];
        var (start, end) = Dirt4EventCalendar.Window(now, 1);
        var stage = source.StageData.Stages[0];
        var configured = source with
        {
            EventMeta = source.EventMeta with
            {
                LeaderboardId = stage.LeaderboardId,
                StartTime = checked((int)start),
                AdvertStartTime = checked((int)start),
                ExpiryTime = checked((int)end),
                EventType = 3,
                PersonalBest = 0,
                EventStatus = 0
            },
            StageData = source.StageData with { TotalStages = 1, AvailableStages = 1, Stages = [stage] },
            Restrictions = source.Restrictions with
            {
                VehicleIds = [],
                VehicleClassIds = [new IdEntry(74)]
            }
        };
        return EgoNetBinary.Dictionary(new EgoNetField("EventConfig", BuildEvent(configured)));
    }

    internal static byte[] Build(DateTimeOffset now, Dirt4DailyStore store, string player,
        IReadOnlyList<long> ids, bool omitScores = false)
    {
        if (ids.Count == 0)
            return Pack(now, Enumerable.Range(0, Events.Length).Select(i =>
                Progress(store.Current(now, i), store, player, omitScores))
                .Concat(store.PendingResults(player, now).Select(r => Progress(r, store, player, omitScores))));

        var events = new List<EventDefinition>();
        foreach (var id in ids.Distinct())
        {
            var round = store.Find(id);
            if (round is not null) events.Add(Progress(round, store, player, omitScores));
        }
        return Pack(now, events);
    }

    internal static byte[] BuildResults(DateTimeOffset now, Dirt4DailyStore store, string player, IReadOnlyList<long> ids)
    {
        var rounds = ids.Count == 0 ? store.PendingResults(player, now) : store.Completed(player, now, ids);
        var results = rounds.Select(round =>
        {
            var e = Definition(round);
            var stage = e.StageData.Stages[^1];
            var time = round.Time(player);
            var tier = time <= stage.T1T2BarrierTime ? 1 : time <= stage.T2T3BarrierTime ? 2 :
                time <= stage.T3T4BarrierTime ? 3 : 4;
            var reward = e.Rewards.TierRewards.Single(r => r.TierId == tier);
            // Local policy: captured time barriers and the tier's minimum credit reward.
            return EgoNetBinary.DictValue(
                EgoNetBinary.Si64("EventId", round.EventId),
                EgoNetBinary.Si64("OverallTime", time),
                EgoNetBinary.Fp32("Percent", round.Percent(player)),
                EgoNetBinary.Fp32("TargetPercent", 0),
                EgoNetBinary.Dict("TierResult", EgoNetBinary.Si32("ActCredReward", reward.MinCredits),
                    EgoNetBinary.Si32("TierId", tier)),
                EgoNetBinary.Si64("T1T2BarrierTime", stage.T1T2BarrierTime),
                EgoNetBinary.Si64("T2T3BarrierTime", stage.T2T3BarrierTime),
                EgoNetBinary.Si64("T3T4BarrierTime", stage.T3T4BarrierTime),
                EgoNetBinary.Si64("EventTargetTime", stage.TargetTime.OverallTime),
                BuildRewards(e.Rewards));
        }).ToArray();
        var response = EgoNetBinary.Dictionary(EgoNetBinary.Dict("Results", EgoNetBinary.Vector("Results", results)));
        store.MarkResultsIssued(player, now, rounds.Select(r => r.EventId).ToArray());
        return response;
    }

    private static byte[] Pack(DateTimeOffset now, IEnumerable<EventDefinition> events) =>
        EgoNetBinary.Dictionary(EgoNetBinary.Tutc("CurTime", now),
            EgoNetBinary.Vector("EventDescs", events.Select(BuildEvent).ToArray()));

    private static EventDefinition Schedule(EventDefinition e, DateTimeOffset now)
    {
        var days = e.EventMeta.EventType switch { 1 or 5 => 7, 2 => 30, _ => 1 };
        return e with { EventMeta = e.EventMeta with {
            ExpiryTime = checked((int)now.AddDays(days).ToUnixTimeSeconds()),
            StartTime = checked((int)now.AddDays(-1).ToUnixTimeSeconds()),
            AdvertStartTime = checked((int)now.AddDays(-2).ToUnixTimeSeconds()) } };
    }

    private static EventDefinition Progress(Dirt4DailyRound round, Dirt4DailyStore store, string player, bool omitScores)
    {
        var e = Definition(round);
        var time = omitScores ? 0 : round.Time(player);
        return e with {
            EventMeta = e.EventMeta with { EventId = round.EventId, LeaderboardId = round.LeaderboardId,
                StartTime = checked((int)round.OpenedAt), AdvertStartTime = checked((int)round.OpenedAt - 86400),
                RanLastEvent = store.RanPrevious(round, player),
                ExpiryTime = checked((int)round.ExpiresAt), PersonalBest = time, EventStatus = time > 0 ? 3 : 0 },
            StageData = e.StageData with { Stages = e.StageData.Stages.Select((s, i) => s with {
                LeaderboardId = round.StageLeaderboard(i), PlayerBest = omitScores ? 0 : round.StageTime(player, i),
                PlayerOverall = omitScores ? 0 : round.Overall(player, i),
                PlayerRank = omitScores ? 0 : round.Rank(player, i),
                Percentile = omitScores ? 0 : (int)round.Percent(player, i) }).ToArray() }
        };
    }

    private static EventDefinition[] LoadCatalog()
    {
        using var stream = typeof(Dirt4CommunityEvents).Assembly.GetManifestResourceStream(
            "RaceNetShowdown.Server.RaceNet.dirt4-community-events.json")
            ?? throw new InvalidDataException("Missing DiRT 4 community event catalog.");
        var events = JsonSerializer.Deserialize<EventDefinition[]>(stream, new JsonSerializerOptions
        {
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Empty DiRT 4 community event catalog.");

        foreach (var e in events)
        {
            var stages = e.StageData.Stages;
            if (stages.Length == 0 || e.StageData.TotalStages != stages.Length ||
                e.StageData.AvailableStages != stages.Length ||
                stages.Select(s => s.LeaderboardId).Distinct().Count() != stages.Length ||
                stages.Where((s, i) => s.StageId != i + 1).Any() ||
                stages.Any(s => s.TrackModelId == 0 && s.TrackGenValue == 0))
            {
                throw new InvalidDataException($"Invalid stages in DiRT 4 event {e.EventMeta.EventId}.");
            }
        }
        return events;
    }

    private static Action<BinaryWriter> BuildEvent(EventDefinition e)
    {
        var meta = e.EventMeta;
        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("EventMeta",
                EgoNetBinary.Si64("EventId", meta.EventId),
                EgoNetBinary.Dstr("Name", meta.Name),
                EgoNetBinary.Ui32("DisciplineId", meta.DisciplineId),
                EgoNetBinary.Si64("LeaderboardId", meta.LeaderboardId),
                EgoNetBinary.Bool("RanLastEvent", meta.RanLastEvent),
                EgoNetBinary.Tutc("ExpiryTime", DateTimeOffset.FromUnixTimeSeconds(meta.ExpiryTime)),
                EgoNetBinary.Tutc("StartTime", DateTimeOffset.FromUnixTimeSeconds(meta.StartTime)),
                EgoNetBinary.Tutc("AdvertStartTime", DateTimeOffset.FromUnixTimeSeconds(meta.AdvertStartTime)),
                EgoNetBinary.Si32("EventType", meta.EventType),
                EgoNetBinary.Si64("PersonalBest", meta.PersonalBest),
                Ids("SponsorIds", meta.SponsorIds),
                EgoNetBinary.Bool("EventRestart", meta.EventRestart),
                EgoNetBinary.Bool("StageRestart", meta.StageRestart),
                EgoNetBinary.Dstr("PromoEventAd", meta.PromoEventAd),
                // An unplayed event uses status 0. Status 3 in the later capture is player progress.
                EgoNetBinary.Si32("EventStatus", meta.EventStatus),
                EgoNetBinary.Si16("EventCompType", meta.EventCompType),
                EgoNetBinary.Ui16("GameOptions", meta.GameOptions),
                EgoNetBinary.Ui08("FearlessCount", meta.FearlessCount)),
            EgoNetBinary.Dict("StageData",
                EgoNetBinary.Ui32("CountryDBId", e.StageData.CountryDBId),
                EgoNetBinary.Ui32("TotalStages", e.StageData.TotalStages),
                EgoNetBinary.Ui32("AvailableStages", e.StageData.AvailableStages),
                EgoNetBinary.Vector("Stages", e.StageData.Stages.Select(BuildStage).ToArray())),
            EgoNetBinary.Dict("Restrictions",
                Ids("VehicleIds", e.Restrictions.VehicleIds),
                Ids("VehicleClassIds", e.Restrictions.VehicleClassIds),
                Ids("MftrCountryIds", e.Restrictions.MftrCountryIds),
                Ids("DriveTrainIds", e.Restrictions.DriveTrainIds),
                Ids("ManufacturerIds", e.Restrictions.ManufacturerIds),
                EgoNetBinary.Si32("MaxBHP", e.Restrictions.MaxBHP)),
            BuildRewards(e.Rewards));
    }

    private static EgoNetField BuildRewards(Rewards rewards) =>
            EgoNetBinary.Dict("Rewards",
                EgoNetBinary.Bool("ContentUnlocked", rewards.ContentUnlocked),
                EgoNetBinary.Vector("TierRewards", rewards.TierRewards.Select(t =>
                    EgoNetBinary.DictValue(
                        EgoNetBinary.Si32("TierId", t.TierId),
                        EgoNetBinary.Si32("MaxCredits", t.MaxCredits),
                        EgoNetBinary.Si32("MinCredits", t.MinCredits),
                        EgoNetBinary.Si32("RewardVehicleId", t.RewardVehicleId))).ToArray()));

    private static EgoNetField Ids(string name, IdEntry[] entries) =>
        EgoNetBinary.Vector(name, entries.Select(e =>
            EgoNetBinary.DictValue(EgoNetBinary.Si32("ID", e.ID))).ToArray());

    private static Action<BinaryWriter> BuildStage(StageDefinition s)
    {
        var name = s.TrackgenName;
        return EgoNetBinary.DictValue(
            EgoNetBinary.Ui08("StageId", s.StageId),
            EgoNetBinary.Ui32("TrackModelId", s.TrackModelId),
            EgoNetBinary.Si64("LeaderboardId", s.LeaderboardId),
            EgoNetBinary.Ui32("LocationId", s.LocationId),
            EgoNetBinary.Ui32("TimeOfDayId", s.TimeOfDayId),
            EgoNetBinary.Ui32("WeatherId", s.WeatherId),
            EgoNetBinary.Bool("HasServiceArea", s.HasServiceArea),
            EgoNetBinary.Dict("TargetTime",
                EgoNetBinary.Si64("OverallTime", s.TargetTime.OverallTime),
                EgoNetBinary.Si64("Split1", s.TargetTime.Split1),
                EgoNetBinary.Si64("Split2", s.TargetTime.Split2),
                EgoNetBinary.Si64("Split3", s.TargetTime.Split3),
                EgoNetBinary.Si64("Split4", s.TargetTime.Split4),
                EgoNetBinary.Si64("Split5", s.TargetTime.Split5),
                EgoNetBinary.Si64("Split6", s.TargetTime.Split6),
                EgoNetBinary.Si64("Split7", s.TargetTime.Split7),
                EgoNetBinary.Si64("Split8", s.TargetTime.Split8),
                EgoNetBinary.Si64("Split9", s.TargetTime.Split9),
                EgoNetBinary.Si64("Split10", s.TargetTime.Split10)),
            EgoNetBinary.Ui16("GameOptions", s.GameOptions),
            EgoNetBinary.Ui32("CareerStageId", s.CareerStageId),
            EgoNetBinary.Si64("TrackGenValue", s.TrackGenValue),
            EgoNetBinary.Si64("StageBest", s.StageBest),
            EgoNetBinary.Si64("StageOverall", s.StageOverall),
            EgoNetBinary.Si64("PlayerBest", s.PlayerBest),
            EgoNetBinary.Si64("PlayerOverall", s.PlayerOverall),
            EgoNetBinary.Si32("PlayerRank", s.PlayerRank),
            EgoNetBinary.Si64("DeltaBest", s.DeltaBest),
            EgoNetBinary.Si32("Percentile", s.Percentile),
            EgoNetBinary.Si32("DeltaPercentile", s.DeltaPercentile),
            EgoNetBinary.Si64("T1T2BarrierTime", s.T1T2BarrierTime),
            EgoNetBinary.Si64("T2T3BarrierTime", s.T2T3BarrierTime),
            EgoNetBinary.Si64("T3T4BarrierTime", s.T3T4BarrierTime),
            EgoNetBinary.Si16("Difficulty", s.Difficulty),
            EgoNetBinary.Si16("HeatLaps", s.HeatLaps),
            EgoNetBinary.Si16("SemiLaps", s.SemiLaps),
            EgoNetBinary.Si16("FinalLaps", s.FinalLaps),
            EgoNetBinary.Dict("TrackgenName",
                EgoNetBinary.Ui32("RegionId", name.RegionId),
                EgoNetBinary.Ui32("FlavourId", name.FlavourId),
                EgoNetBinary.Ui32("NamingStyleId", name.NamingStyleId),
                EgoNetBinary.Bool("IsReversed", name.IsReversed),
                EgoNetBinary.Si32("StageIndex", name.StageIndex)));
    }

    internal sealed record EventDefinition(EventMeta EventMeta, StageData StageData, Restrictions Restrictions, Rewards Rewards);
    internal sealed record EventMeta(long EventId, string Name, uint DisciplineId, long LeaderboardId,
        bool RanLastEvent, int ExpiryTime, int StartTime, int AdvertStartTime, int EventType, long PersonalBest,
        IdEntry[] SponsorIds, bool EventRestart, bool StageRestart, string PromoEventAd, int EventStatus,
        short EventCompType, ushort GameOptions, byte FearlessCount);
    internal sealed record StageData(uint CountryDBId, uint TotalStages, uint AvailableStages, StageDefinition[] Stages);
    internal sealed record Restrictions(IdEntry[] VehicleIds, IdEntry[] VehicleClassIds, IdEntry[] MftrCountryIds,
        IdEntry[] DriveTrainIds, IdEntry[] ManufacturerIds, int MaxBHP);
    internal sealed record IdEntry(int ID);
    internal sealed record Rewards(bool ContentUnlocked, TierReward[] TierRewards);
    internal sealed record TierReward(int TierId, int MaxCredits, int MinCredits, int RewardVehicleId);
    internal sealed record StageDefinition(byte StageId, uint TrackModelId, long LeaderboardId, uint LocationId,
        uint TimeOfDayId, uint WeatherId, bool HasServiceArea, TargetTime TargetTime, ushort GameOptions,
        uint CareerStageId, long TrackGenValue, long StageBest, long StageOverall, long PlayerBest, long PlayerOverall,
        int PlayerRank, long DeltaBest, int Percentile, int DeltaPercentile, long T1T2BarrierTime, long T2T3BarrierTime,
        long T3T4BarrierTime, short Difficulty, short HeatLaps, short SemiLaps, short FinalLaps, TrackgenName TrackgenName);
    internal sealed record TargetTime(long OverallTime, long Split1, long Split2, long Split3, long Split4,
        long Split5, long Split6, long Split7, long Split8, long Split9, long Split10);
    internal sealed record TrackgenName(uint RegionId, uint FlavourId, uint NamingStyleId, bool IsReversed, int StageIndex);
}

internal sealed record Dirt4EventTemplate(long EventId, int EventType, long LeaderboardId,
    long[] StageLeaderboards, long[] VehicleIds);
