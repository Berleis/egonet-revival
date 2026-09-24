using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceNetShowdown.Server.RaceNet;

// The name and JSON list format are retained for migration from the Daily prototype.
internal sealed class Dirt4DailyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly int _dailyTestSeconds;
    private List<Dirt4DailyRound> _rounds;
    private List<Dirt4StandaloneLeaderboard> _leaderboards;
    private Dictionary<string, Dirt4LeaderboardPresence> _presences;

    internal Dirt4DailyStore(string? stateJson = null, int dailyTestSeconds = 0)
    {
        if (dailyTestSeconds is < 0 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(dailyTestSeconds));
        _dailyTestSeconds = dailyTestSeconds;
        (_rounds, _leaderboards, _presences) = Load(stateJson);
        if (_rounds.Select(r => r.EventId).Distinct().Count() != _rounds.Count || _rounds.Any(r => !Valid(r)))
            throw new InvalidDataException("Invalid DiRT 4 community state; the existing file was not reset.");
        if (_leaderboards.Select(l => l.LeaderboardId).Distinct().Count() != _leaderboards.Count ||
            _leaderboards.Any(l => l.LeaderboardId <= 0 || l.Runs is null || l.Runs.Any(run =>
                string.IsNullOrWhiteSpace(run.Key) || run.Value is null || run.Value.TimeMs is <= 0 or > uint.MaxValue ||
                run.Value.VehicleId <= 0 || run.Value.SubmittedAt <= 0)))
            throw new InvalidDataException("Invalid DiRT 4 standalone leaderboard state.");
    }

    private static (List<Dirt4DailyRound> Rounds, List<Dirt4StandaloneLeaderboard> Leaderboards,
        Dictionary<string, Dirt4LeaderboardPresence> Presences) Load(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson)) return ([], [], []);

        using var json = JsonDocument.Parse(stateJson);
        if (json.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacy = JsonSerializer.Deserialize<List<Dirt4DailyRound>>(stateJson)
                ?? throw new InvalidDataException("Empty DiRT 4 community state.");
            return (legacy, [], []);
        }

        var state = JsonSerializer.Deserialize<Dirt4PersistentState>(stateJson)
            ?? throw new InvalidDataException("Empty DiRT 4 state.");
        return (state.CommunityEvents ?? [], state.StandaloneLeaderboards ?? [], state.Presences ?? []);
    }

    private static bool Valid(Dirt4DailyRound round)
    {
        if (round.EventId < 1_000_000 || round.EventId > int.MaxValue || round.OpenedAt >= round.ExpiresAt ||
            round.TemplateIndex < 0 || round.TemplateIndex >= Dirt4CommunityEvents.TemplateCount || round.Runs is null)
            return false;
        var template = Dirt4CommunityEvents.Template(round.TemplateIndex);
        if (round.StageLeaderboardIds is null)
        {
            if (round.TemplateIndex != 0 || round.CalendarAligned || round.ExpiresAt - round.OpenedAt != 300)
                return false;
        }
        else if (!round.StageLeaderboardIds.SequenceEqual(template.StageLeaderboards.Select(id =>
                     id + ((round.EventId - template.EventId) << 9))) ||
                 round.LeaderboardId != template.LeaderboardId + ((round.EventId - template.EventId) << 9))
            return false;

        foreach (var (player, run) in round.Runs)
        {
            if (string.IsNullOrEmpty(player) || run is null || run.StartedAt < round.OpenedAt ||
                run.StartedAt >= round.ExpiresAt || run.TimeMs is <= 0) return false;
            var stages = round.Stages(player);
            if (stages.Count == 0 || stages.Count > round.StageCount || stages.Any(s => s is null)) return false;
            for (var i = 0; i < stages.Count; i++)
                if (stages[i].StartedAt < run.StartedAt || stages[i].StartedAt >= round.ExpiresAt ||
                    stages[i].TimeMs is <= 0 or > uint.MaxValue || (i < stages.Count - 1 && stages[i].TimeMs is null))
                    return false;
            var completed = stages.Count == round.StageCount && stages.All(s => s.TimeMs.HasValue);
            if (completed != run.TimeMs.HasValue || (completed && stages.Sum(s => s.TimeMs!.Value) != run.TimeMs))
                return false;
        }
        return true;
    }

    internal Dirt4DailyRound Current(DateTimeOffset now, int templateIndex = 0)
    {
        lock (_gate)
        {
            var seconds = now.ToUnixTimeSeconds();
            var template = Dirt4CommunityEvents.Template(templateIndex);
            var testing = templateIndex == 0 && _dailyTestSeconds > 0;
            // Let an already issued five-minute Daily finish; do not discard pending rewards.
            var legacy = _rounds.LastOrDefault(r => r.TemplateIndex == templateIndex && !r.CalendarAligned &&
                r.OpenedAt <= seconds && seconds < r.ExpiresAt);
            if (legacy is not null) return legacy;
            var (start, end) = testing ? (seconds, seconds + _dailyTestSeconds) :
                Dirt4EventCalendar.Window(now, template.EventType);
            var current = _rounds.FirstOrDefault(r => r.TemplateIndex == templateIndex &&
                r.OpenedAt == start && r.ExpiresAt == end && r.CalendarAligned == !testing);
            if (current is not null) return current;

            var id = Math.Max(1_000_000 + seconds / 60, _rounds.Select(r => r.EventId).DefaultIfEmpty().Max() + 1);
            if (id > int.MaxValue) throw new InvalidDataException("DiRT 4 event ID limit reached.");
            var shift = (id - template.EventId) << 9;
            var next = new Dirt4DailyRound(id, start, end, new Dictionary<string, Dirt4DailyRun>())
            {
                TemplateIndex = templateIndex, CalendarAligned = !testing,
                EventLeaderboardId = template.LeaderboardId + shift,
                StageLeaderboardIds = template.StageLeaderboards.Select(lb => lb + shift).ToArray()
            };
            Commit([.. _rounds, next]);
            return next;
        }
    }

    internal Dirt4DailyRound? Find(long eventId)
    {
        lock (_gate) return _rounds.FirstOrDefault(r => r.EventId == eventId);
    }

    internal IReadOnlyList<Dirt4DailyRound> Completed(string player, DateTimeOffset now, IReadOnlyList<long> ids)
    {
        lock (_gate)
            return _rounds.Where(r => r.ExpiresAt <= now.ToUnixTimeSeconds() &&
                (ids.Count == 0 || ids.Contains(r.EventId)) && r.Time(player) > 0).ToArray();
    }

    internal bool RanPrevious(Dirt4DailyRound round, string player)
    {
        lock (_gate)
        {
            var weekly = Dirt4CommunityEvents.Template(round.TemplateIndex).EventType is 1 or 5;
            return round.CalendarAligned && _rounds.Any(r => r.CalendarAligned && r.ExpiresAt == round.OpenedAt &&
                (weekly ? Dirt4CommunityEvents.Template(r.TemplateIndex).EventType is 1 or 5 : r.TemplateIndex == round.TemplateIndex) &&
                r.Time(player) > 0);
        }
    }

    internal bool Start(long leaderboardId, string player, DateTimeOffset now, long vehicleId = 504)
    {
        lock (_gate)
        {
            var round = _rounds.FirstOrDefault(r => r.StageIndex(leaderboardId) >= 0);
            var seconds = now.ToUnixTimeSeconds();
            if (round is null || seconds < round.OpenedAt || seconds >= round.ExpiresAt ||
                !AcceptVehicle(round, vehicleId)) return false;
            var index = round.StageIndex(leaderboardId);
            var stages = round.Stages(player).ToList();
            round.Runs.TryGetValue(player, out var run);
            if (run?.VehicleId is > 0 && run.VehicleId != vehicleId) return false;
            if (index < stages.Count) return stages[index].TimeMs is null;
            if (index != stages.Count || stages.Any(s => s.TimeMs is null)) return false;
            stages.Add(new(seconds, null));
            run = run is null ? new(seconds, null) : run;
            Replace(round with { Runs = new Dictionary<string, Dirt4DailyRun>(round.Runs)
                { [player] = run with { Stages = stages, VehicleId = vehicleId } } });
            return true;
        }
    }

    internal bool BindPresence(long leaderboardId, string player, Dirt4LeaderboardPresence presence)
    {
        lock (_gate)
        {
            _presences[player] = presence;
            var round = _rounds.FirstOrDefault(r => r.StageIndex(leaderboardId) >= 0);
            if (round is null) return true;
            if (!round.Runs.TryGetValue(player, out var run)) return false;
            Replace(round with { Runs = new Dictionary<string, Dirt4DailyRun>(round.Runs)
            {
                [player] = run with
                {
                    NetworkId = presence.NetworkId,
                    DisplayName = presence.Name,
                    EgonetId = presence.EgonetId,
                    AccountRef = presence.AccountRef,
                    IsCrossPlatform = presence.IsCrossPlatform
                }
            } });
            return true;
        }
    }

    internal bool BindDisplayName(long leaderboardId, string player, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Equals("DiRT Player", StringComparison.OrdinalIgnoreCase)) return false;
        lock (_gate)
        {
            if (_presences.TryGetValue(player, out var presence))
                _presences[player] = presence with { Name = displayName };
            else
                _presences[player] = new(false, 0, 0, 0, displayName);
            var round = _rounds.FirstOrDefault(r => r.StageIndex(leaderboardId) >= 0);
            if (round is null) return true;
            if (!round.Runs.TryGetValue(player, out var run)) return false;
            if (!string.IsNullOrWhiteSpace(run.DisplayName)) return true;
            Replace(round with { Runs = new Dictionary<string, Dirt4DailyRun>(round.Runs)
                { [player] = run with { DisplayName = displayName } } });
            return true;
        }
    }

    internal bool Finish(long leaderboardId, string player, long timeMs, DateTimeOffset now, long vehicleId = 504,
        uint nationality = 0)
    {
        lock (_gate)
        {
            var round = _rounds.FirstOrDefault(r => r.StageIndex(leaderboardId) >= 0);
            if (round is null)
                return FinishStandalone(leaderboardId, player, timeMs, now, vehicleId, nationality);
            if (timeMs is <= 0 or > uint.MaxValue || !round.Runs.TryGetValue(player, out var run) ||
                !AcceptVehicle(round, vehicleId) || (run.VehicleId is > 0 && run.VehicleId != vehicleId)) return false;
            var index = round.StageIndex(leaderboardId);
            var stages = round.Stages(player).ToArray();
            if (index >= stages.Length) return false;
            var stage = stages[index];
            if (stage.TimeMs.HasValue) return stage.TimeMs == timeMs;
            var seconds = now.ToUnixTimeSeconds();
            if (seconds < stage.StartedAt || seconds >= round.ExpiresAt) return false;
            stages[index] = stage with { TimeMs = timeMs };
            Replace(round with { Runs = new Dictionary<string, Dirt4DailyRun>(round.Runs)
                { [player] = run with { Stages = stages, Nationality = nationality,
                    TimeMs = stages.Length == round.StageCount ? stages.Sum(s => s.TimeMs!.Value) : null } } });
            return true;
        }
    }

    internal Dirt4LeaderboardSnapshot? Leaderboard(long leaderboardId, string player, bool cumulative,
        IReadOnlyList<Dirt4LeaderboardPresence>? presences = null)
    {
        lock (_gate)
        {
            var round = _rounds.FirstOrDefault(r => r.StageIndex(leaderboardId) >= 0);
            if (round is null) return StandaloneLeaderboard(leaderboardId, player, presences);
            var stage = round.StageIndex(leaderboardId);
            HashSet<ulong>? networkIds = null;
            HashSet<string>? names = null;
            if (presences is { Count: > 0 })
            {
                networkIds = presences.Where(p => p.NetworkId > 0).Select(p => p.NetworkId).ToHashSet();
                names = presences.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var ranked = round.Runs
                .Where(pair => round.StageTime(pair.Key, stage) > 0)
                .Select(pair => new
                {
                    Player = pair.Key,
                    Run = pair.Value,
                    StageTime = round.StageTime(pair.Key, stage),
                    CumulativeTime = round.Overall(pair.Key, stage)
                })
                .OrderBy(e => cumulative ? e.CumulativeTime : e.StageTime)
                .ThenBy(e => e.Run.StartedAt)
                .ToArray();
            var bestStage = ranked.Select(e => e.StageTime).DefaultIfEmpty().Min();
            var bestCumulative = ranked.Select(e => e.CumulativeTime).DefaultIfEmpty().Min();
            int RankOf(long stageTime, long cumulativeTime) => 1 + ranked.Count(other =>
                (cumulative ? other.CumulativeTime : other.StageTime) <
                (cumulative ? cumulativeTime : stageTime));
            var allEntries = ranked.Select(e => new Dirt4LeaderboardEntry(
                new Dirt4LeaderboardPresence(e.Run.IsCrossPlatform, e.Run.EgonetId ?? 0, e.Run.AccountRef ?? 0,
                    e.Run.NetworkId ?? 0, e.Run.DisplayName ?? e.Player),
                e.StageTime, e.StageTime - bestStage, e.CumulativeTime, e.CumulativeTime - bestCumulative,
                RankOf(e.StageTime, e.CumulativeTime), checked((uint)(e.Run.VehicleId ?? 0)), e.Run.Nationality))
                .ToArray();
            var entries = presences is null ? allEntries : allEntries.Where(entry => presences.Count > 0 &&
                ((entry.Presence.NetworkId > 0 && networkIds!.Contains(entry.Presence.NetworkId)) ||
                (!string.IsNullOrWhiteSpace(entry.Presence.Name) && names!.Contains(entry.Presence.Name)))).ToArray();
            var current = ranked.FirstOrDefault(e => e.Player == player);
            var playerRank = current is null ? 0 : RankOf(current.StageTime, current.CumulativeTime);
            return new(entries, playerRank);
        }
    }

    private bool FinishStandalone(long leaderboardId, string player, long timeMs, DateTimeOffset now,
        long vehicleId, uint nationality)
    {
        if (leaderboardId <= 0 || string.IsNullOrWhiteSpace(player) || timeMs is <= 0 or > uint.MaxValue ||
            vehicleId is <= 0 or > uint.MaxValue) return false;

        var board = _leaderboards.FirstOrDefault(l => l.LeaderboardId == leaderboardId);
        var runs = board?.Runs.ToDictionary() ?? [];
        if (runs.TryGetValue(player, out var current) && current.TimeMs <= timeMs) return true;
        runs[player] = new(timeMs, checked((uint)vehicleId), nationality, now.ToUnixTimeMilliseconds());
        var updated = new Dirt4StandaloneLeaderboard(leaderboardId, runs);
        _leaderboards = board is null
            ? [.. _leaderboards, updated]
            : _leaderboards.Select(l => l.LeaderboardId == leaderboardId ? updated : l).ToList();
        return true;
    }

    private Dirt4LeaderboardSnapshot? StandaloneLeaderboard(long leaderboardId, string player,
        IReadOnlyList<Dirt4LeaderboardPresence>? requestedPresences)
    {
        var board = _leaderboards.FirstOrDefault(l => l.LeaderboardId == leaderboardId);
        if (board is null) return null;

        HashSet<ulong>? networkIds = null;
        HashSet<string>? names = null;
        if (requestedPresences is { Count: > 0 })
        {
            networkIds = requestedPresences.Where(p => p.NetworkId > 0).Select(p => p.NetworkId).ToHashSet();
            names = requestedPresences.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var ranked = board.Runs
            .OrderBy(pair => pair.Value.TimeMs)
            .ThenBy(pair => pair.Value.SubmittedAt)
            .ToArray();
        var best = ranked[0].Value.TimeMs;
        var allEntries = ranked.Select((pair, index) =>
        {
            var presence = _presences.TryGetValue(pair.Key, out var known)
                ? known
                : new Dirt4LeaderboardPresence(false, 0, 0, 0, pair.Key);
            return new Dirt4LeaderboardEntry(presence, pair.Value.TimeMs, pair.Value.TimeMs - best,
                pair.Value.TimeMs, pair.Value.TimeMs - best, index + 1, pair.Value.VehicleId,
                pair.Value.Nationality);
        }).ToArray();
        var entries = requestedPresences is null ? allEntries : allEntries.Where(entry =>
            requestedPresences.Count > 0 &&
            ((entry.Presence.NetworkId > 0 && networkIds!.Contains(entry.Presence.NetworkId)) ||
             (!string.IsNullOrWhiteSpace(entry.Presence.Name) && names!.Contains(entry.Presence.Name)))).ToArray();
        var playerIndex = Array.FindIndex(ranked, pair => pair.Key == player);
        return new(entries, playerIndex < 0 ? 0 : playerIndex + 1);
    }

    private static bool AcceptVehicle(Dirt4DailyRound round, long vehicleId)
    {
        var allowed = Dirt4CommunityEvents.Template(round.TemplateIndex).VehicleIds;
        return vehicleId > 0 && (allowed.Length == 0 || allowed.Contains(vehicleId));
    }

    private void Replace(Dirt4DailyRound round) =>
        Commit(_rounds.Select(r => r.EventId == round.EventId ? round : r).ToList());

    private void Commit(List<Dirt4DailyRound> rounds)
    {
        _rounds = rounds;
    }

    internal string Export()
    {
        lock (_gate) return JsonSerializer.Serialize(
            new Dirt4PersistentState(_rounds, _leaderboards, _presences), JsonOptions);
    }
}

internal sealed record Dirt4PersistentState(
    List<Dirt4DailyRound>? CommunityEvents,
    List<Dirt4StandaloneLeaderboard>? StandaloneLeaderboards,
    Dictionary<string, Dirt4LeaderboardPresence>? Presences);
internal sealed record Dirt4StandaloneLeaderboard(long LeaderboardId,
    IReadOnlyDictionary<string, Dirt4StandaloneRun> Runs);
internal sealed record Dirt4StandaloneRun(long TimeMs, uint VehicleId, uint Nationality, long SubmittedAt);

internal sealed record Dirt4StageRun(long StartedAt, long? TimeMs);
internal sealed record Dirt4DailyRun(long StartedAt, long? TimeMs)
{
    public IReadOnlyList<Dirt4StageRun>? Stages { get; init; }
    public long? VehicleId { get; init; }
    public ulong? NetworkId { get; init; }
    public string? DisplayName { get; init; }
    public long? EgonetId { get; init; }
    public long? AccountRef { get; init; }
    public bool IsCrossPlatform { get; init; }
    public uint Nationality { get; init; }
}

internal sealed record Dirt4LeaderboardPresence(bool IsCrossPlatform, long EgonetId, long AccountRef,
    ulong NetworkId, string Name);
internal sealed record Dirt4LeaderboardEntry(Dirt4LeaderboardPresence Presence, long PersonalBest, long TimeDiff,
    long CumulativeBest, long CumulativeDiff, int Rank, uint VehicleId, uint Nationality);
internal sealed record Dirt4LeaderboardSnapshot(IReadOnlyList<Dirt4LeaderboardEntry> Entries, int PlayerRank);

internal sealed record Dirt4DailyRound(long EventId, long OpenedAt, long ExpiresAt,
    IReadOnlyDictionary<string, Dirt4DailyRun> Runs)
{
    public int TemplateIndex { get; init; }
    public bool CalendarAligned { get; init; }
    public long? EventLeaderboardId { get; init; }
    public long[]? StageLeaderboardIds { get; init; }
    [JsonIgnore] public long LeaderboardId => EventLeaderboardId ?? 0x1000000000000000L + (EventId << 9) + 33;
    [JsonIgnore] public int StageCount => StageLeaderboardIds?.Length ?? 1;
    internal int StageIndex(long leaderboardId) => StageLeaderboardIds is { } ids ? Array.IndexOf(ids, leaderboardId) :
        leaderboardId == LeaderboardId ? 0 : -1;
    internal long StageLeaderboard(int index) => StageLeaderboardIds?[index] ?? LeaderboardId;
    internal IReadOnlyList<Dirt4StageRun> Stages(string player) => Runs.TryGetValue(player, out var run)
        ? run.Stages ?? [new(run.StartedAt, run.TimeMs)] : [];
    internal long StageTime(string player, int index) => Stages(player).ElementAtOrDefault(index)?.TimeMs ?? 0;
    internal long Overall(string player, int index) => StageTime(player, index) > 0
        ? Stages(player).Take(index + 1).Sum(s => s.TimeMs ?? 0) : 0;
    internal long Time(string player) => Runs.TryGetValue(player, out var run) ? run.TimeMs ?? 0 : 0;
    internal int Rank(string player, int? index = null)
    {
        var stage = index ?? StageCount - 1;
        var time = Overall(player, stage);
        return time > 0 ? 1 + Runs.Keys.Count(p => Overall(p, stage) > 0 && Overall(p, stage) < time) : 0;
    }
    internal float Percent(string player, int? index = null)
    {
        var stage = index ?? StageCount - 1;
        return Overall(player, stage) > 0 ? 100f * (Rank(player, stage) - 1) /
            Math.Max(1, Runs.Keys.Count(p => Overall(p, stage) > 0) - 1) : 0;
    }
}
