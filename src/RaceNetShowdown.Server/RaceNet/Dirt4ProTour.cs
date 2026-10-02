using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

internal sealed class Dirt4ProTour(ILogger? logger = null)
{
    internal static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(5);
    internal const int BaselineTier = 7;
    // dirt4.exe 0x14010de37 initializes the SessionList capacity to 0x14.
    internal const int MaxSearchResults = 20;
    private readonly object _gate = new();
    private readonly Dictionary<string, Lobby> _lobbies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StartedSession> _startedSessions = new(StringComparer.Ordinal);

    internal byte[] GetSessionList(CapturedBody body, string? owner, DateTimeOffset now)
    {
        var location = Unsigned(body, "SessionLocation");
        var reputation = Unsigned(body, "SessionRep");
        var handling = AltHandling(body);
        Lobby[] matches;
        lock (_gate)
        {
            Expire(now);
            // A fresh search abandons this client's previous advertisement.
            if (owner is not null) _lobbies.Remove(owner);
            matches = owner is null || !HasSearchFields(body) ? [] : _lobbies.Values
                .Where(lobby => lobby.AltHandling == handling)
                // Location/reputation are preferences, not isolated matchmaking pools.
                .OrderBy(lobby => lobby.Location == location ? 0 : 1)
                .ThenBy(lobby => Math.Abs((long)lobby.Reputation - reputation))
                .ThenBy(lobby => lobby.CreatedAt)
                .Take(MaxSearchResults)
                .ToArray();
        }
        logger?.LogInformation("DiRT 4 Pro Tour search: location {Location}, reputation {Reputation}, gamer {Gamer}, candidates {Count}",
            location, reputation, handling, matches.Length);
        // 0x1401296e0 registers the search filters as request-only fields. The native
        // response reader (0x140bcceb0) cannot skip their values if we echo them here.
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("SessionList", matches.Select(lobby => EgoNetBinary.DictValue(
                // 0x140122c50 -> 0x140bcf1d0 -> 0x1403e7190 requires si32 exactly;
                // unlike the search request fields, these are not ui32.
                EgoNetBinary.Si32("SessionDataLen", lobby.Data.Length),
                EgoNetBinary.Si32("SessionLocation", unchecked((int)lobby.Location)),
                EgoNetBinary.Si32("HostReputation", unchecked((int)lobby.Reputation)),
                // Only the host advertises; live membership is handled by the Steam lobby.
                EgoNetBinary.Si32("SessionPlayers", 1),
                EgoNetBinary.Si32("SessionTier", BaselineTier),
                EgoNetBinary.Blob("SessionData", lobby.Data),
                EgoNetBinary.Bool("isAltHandling", lobby.AltHandling))).ToArray()));
    }

    internal static byte[] SessionConfig(DateTimeOffset now) => Dirt4CommunityEvents.BuildProTourConfig(now);

    internal byte[] SubmitSession(CapturedBody body, string? owner, DateTimeOffset now)
    {
        var data = SessionData(body);
        var accepted = false;
        lock (_gate)
        {
            Expire(now);
            if (owner is not null && data.Length is > 0 and <= 1024 &&
                EgoNetRequestParser.ReadTopLevelInteger(body, "SessionDataLen") == data.Length &&
                HasSearchFields(body) &&
                !_lobbies.Any(pair => pair.Key != owner && pair.Value.Data.AsSpan().SequenceEqual(data)))
            {
                var created = _lobbies.TryGetValue(owner, out var previous) && previous.Data.AsSpan().SequenceEqual(data)
                    ? previous.CreatedAt : now;
                _lobbies[owner] = new(data, Unsigned(body, "SessionLocation"), Unsigned(body, "SessionRep"),
                    AltHandling(body), created, now);
                accepted = true;
            }
        }
        logger?.LogInformation("DiRT 4 Pro Tour advertise: accepted {Accepted}, data bytes {Bytes}, location {Location}, reputation {Reputation}, gamer {Gamer}",
            accepted, data.Length, Unsigned(body, "SessionLocation"), Unsigned(body, "SessionRep"), AltHandling(body));
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Ui32("SessionRep", Unsigned(body, "SessionRep")),
            EgoNetBinary.Ui32("SessionLocation", Unsigned(body, "SessionLocation")),
            EgoNetBinary.Bool("IsAltHandling", AltHandling(body)));
    }

    internal byte[] SubmitSessionScores(CapturedBody body, string? owner, DateTimeOffset now,
        Dirt4DailyStore daily)
    {
        var data = SessionData(body);
        var scores = EgoNetRequestParser.ReadDirt4ProTourScores(body);
        var startedPlayers = EgoNetRequestParser.ReadTopLevelBoolean(body, "IsHost") == true
            ? Complete(owner, data, now) : 0;
        var accepted = startedPlayers is >= 4 and <= 8;
        var recorded = accepted && daily.RecordProTourScores(data, startedPlayers, scores, now);
        logger?.LogInformation("DiRT 4 Pro Tour scores: accepted {Accepted}, recorded {Recorded}, started {StartedPlayers}, submitted {SubmittedPlayers}",
            accepted, recorded, startedPlayers, scores.Count);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Vector("SessionScores"),
            EgoNetBinary.Bool("IsHost", EgoNetRequestParser.ReadTopLevelBoolean(body, "IsHost") ?? true));
    }

    internal byte[] SessionStart(CapturedBody body, string? owner, DateTimeOffset now)
    {
        var data = SessionData(body);
        Start(owner, data, checked((int)Unsigned(body, "SessionPlayers")), now);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Ui32("SessionPlayers", Unsigned(body, "SessionPlayers")));
    }

    internal byte[] QuitSession(CapturedBody body, string? owner, DateTimeOffset now)
    {
        var data = SessionData(body);
        Remove(owner, data, now, "quit");
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)));
    }

    internal static byte[] PenalisePlayer(CapturedBody body) => EgoNetBinary.Dictionary(
        EgoNetBinary.Bool("IsAltHandling", AltHandling(body)));

    internal void Tick(string? owner, DateTimeOffset now)
    {
        lock (_gate)
        {
            Expire(now);
            if (owner is not null && _lobbies.TryGetValue(owner, out var lobby))
                _lobbies[owner] = lobby with { LastSeenAt = now };
            if (owner is not null)
                foreach (var key in _startedSessions.Where(pair => pair.Value.Owner == owner)
                             .Select(pair => pair.Key).ToArray())
                    _startedSessions[key] = _startedSessions[key] with { LastSeenAt = now };
        }
    }

    private void Start(string? owner, byte[] data, int players, DateTimeOffset now)
    {
        var started = false;
        lock (_gate)
        {
            Expire(now);
            if (owner is not null && _lobbies.TryGetValue(owner, out var lobby) &&
                lobby.Data.AsSpan().SequenceEqual(data) && players is >= 4 and <= 8)
            {
                _lobbies.Remove(owner);
                _startedSessions[Convert.ToHexString(data)] = new(owner, players, now, now);
                started = true;
            }
        }
        logger?.LogInformation("DiRT 4 Pro Tour start: accepted {Accepted}, players {Players}", started, players);
    }

    private int Complete(string? owner, byte[] data, DateTimeOffset now)
    {
        lock (_gate)
        {
            Expire(now);
            var key = Convert.ToHexString(data);
            if (owner is null || !_startedSessions.TryGetValue(key, out var started) || started.Owner != owner)
                return 0;
            _startedSessions.Remove(key);
            return started.Players;
        }
    }
    private void Remove(string? owner, byte[] data, DateTimeOffset now, string reason)
    {
        var removed = false;
        lock (_gate)
        {
            Expire(now);
            // A guest quitting or a delayed request for an old room must not remove the host's room.
            if (owner is not null && _lobbies.TryGetValue(owner, out var lobby) &&
                lobby.Data.AsSpan().SequenceEqual(data)) removed = _lobbies.Remove(owner);
            var key = Convert.ToHexString(data);
            if (owner is not null && _startedSessions.TryGetValue(key, out var started) && started.Owner == owner)
                removed |= _startedSessions.Remove(key);
        }
        logger?.LogInformation("DiRT 4 Pro Tour {Reason}: removed own advertisement {Removed}", reason, removed);
    }

    private void Expire(DateTimeOffset now)
    {
        foreach (var owner in _lobbies.Where(pair => now - pair.Value.LastSeenAt >= SessionTimeout)
                     .Select(pair => pair.Key).ToArray()) _lobbies.Remove(owner);
        foreach (var key in _startedSessions.Where(pair => now - pair.Value.LastSeenAt >= SessionTimeout)
                     .Select(pair => pair.Key).ToArray()) _startedSessions.Remove(key);
    }

    private static bool HasSearchFields(CapturedBody body) =>
        EgoNetRequestParser.ReadTopLevelInteger(body, "SessionLocation") is >= 0 and <= uint.MaxValue &&
        EgoNetRequestParser.ReadTopLevelInteger(body, "SessionRep") is >= 0 and <= uint.MaxValue &&
        (EgoNetRequestParser.ReadTopLevelBoolean(body, "IsAltHandling") ??
         EgoNetRequestParser.ReadTopLevelBoolean(body, "isAltHandling")) is not null;

    private sealed record Lobby(byte[] Data, uint Location, uint Reputation, bool AltHandling,
        DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
    private sealed record StartedSession(string Owner, int Players, DateTimeOffset StartedAt, DateTimeOffset LastSeenAt);

    private static byte[] SessionData(CapturedBody body) =>
        EgoNetRequestParser.ReadTopLevelBlob(body, "SessionData") ?? [];

    private static bool AltHandling(CapturedBody body) =>
        EgoNetRequestParser.ReadTopLevelBoolean(body, "IsAltHandling") ??
        EgoNetRequestParser.ReadTopLevelBoolean(body, "isAltHandling") ?? false;

    private static uint Unsigned(CapturedBody body, string field) =>
        checked((uint)Math.Clamp(EgoNetRequestParser.ReadTopLevelInteger(body, field) ?? 0, 0, uint.MaxValue));
}
