using System.Collections.Concurrent;
using RaceNetShowdown.Server.RaceNet;
using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.Data;

public sealed class LocalRaceNetStore : IRaceNetStore
{
    private static readonly RaceNetSessionInfo LocalSession = new(
        "local-racenet-session",
        0,
        "local-player",
        "Local Player");

    private static readonly ConcurrentDictionary<string, IReadOnlyList<RaceNetPrincipal>> PrincipalsBySession = new();
    private static readonly ConcurrentDictionary<string, List<RaceNetIssuedChallenge>> IssuedChallengesBySession = new();
    private static readonly ConcurrentDictionary<long, byte[]> GhostDataBySlot = new();
    private static byte[]? _lastUploadedGhostData;
    private static long _nextIssuedChallengeId = 10_000;

    private RaceNetChallengeSnapshot LocalSnapshot { get; set; } = RaceNetChallengeSeed.CreateSnapshot();

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<RaceNetSessionInfo> EnsureSessionAsync(
        HttpContext context,
        CapturedBody body,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(LocalSession);
    }

    public Task RecordCallAsync(
        HttpContext context,
        CapturedBody body,
        RaceNetResponse response,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<long> GetHighestChallengeIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(LocalSnapshot.HighChallengeId);
    }

    public Task<RaceNetChallengeSnapshot> GetChallengeSnapshotAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(LocalSnapshot);
    }

    public Task SavePrincipalsAsync(
        RaceNetSessionInfo session,
        IReadOnlyList<RaceNetPrincipal> principals,
        CancellationToken cancellationToken)
    {
        if (principals.Count > 0)
        {
            PrincipalsBySession[session.SessionId] = principals;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RaceNetPrincipal>> GetPrincipalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            PrincipalsBySession.TryGetValue(session.SessionId, out var principals)
                ? principals
                : Array.Empty<RaceNetPrincipal>());
    }

    public Task<RaceNetIssuedChallenge> IssueChallengeAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal target,
        RaceNetChallengeDraft challengeData,
        CancellationToken cancellationToken)
    {
        var challengeId = Interlocked.Increment(ref _nextIssuedChallengeId);
        var issuedChallenge = new RaceNetIssuedChallenge(
            challengeId,
            ResolveEgonetId(session.SessionId, target),
            target,
            challengeData,
            DateTimeOffset.UtcNow.AddDays(30),
            challengeId);

        var issuedChallenges = IssuedChallengesBySession.GetOrAdd(session.SessionId, _ => []);
        lock (issuedChallenges)
        {
            issuedChallenges.Add(issuedChallenge);
        }

        return Task.FromResult(issuedChallenge);
    }

    public Task<IReadOnlyList<RaceNetIssuedChallenge>> GetIssuedChallengesAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal? target,
        CancellationToken cancellationToken)
    {
        if (!IssuedChallengesBySession.TryGetValue(session.SessionId, out var issuedChallenges))
        {
            return Task.FromResult<IReadOnlyList<RaceNetIssuedChallenge>>([]);
        }

        lock (issuedChallenges)
        {
            var filtered = target is null
                ? issuedChallenges
                : issuedChallenges
                    .Where(value =>
                        value.Presence.SteamId == target.SteamId ||
                        string.Equals(value.Presence.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            return Task.FromResult<IReadOnlyList<RaceNetIssuedChallenge>>(
                filtered
                    .OrderByDescending(value => value.ChallengeId)
                    .Take(20)
                    .ToArray());
        }
    }

    public Task SaveGhostDataAsync(
        RaceNetSessionInfo session,
        long ghostSlotId,
        byte[] ghostData,
        CancellationToken cancellationToken)
    {
        var copy = ghostData.ToArray();
        GhostDataBySlot[ghostSlotId] = copy;
        Interlocked.Exchange(ref _lastUploadedGhostData, copy);
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetGhostDataAsync(
        long ghostSlotId,
        CancellationToken cancellationToken)
    {
        if (GhostDataBySlot.TryGetValue(ghostSlotId, out var ghostData))
        {
            return Task.FromResult<byte[]?>(ghostData.ToArray());
        }

        var fallback = Interlocked.CompareExchange(ref _lastUploadedGhostData, null, null);
        return Task.FromResult<byte[]?>(fallback?.ToArray());
    }

    public Task SaveChallengeResultAsync(
        RaceNetSessionInfo session,
        EgoNetSubmittedChallengeResult result,
        CancellationToken cancellationToken)
    {
        if (result.Result <= 0)
        {
            return Task.CompletedTask;
        }

        var friends = LocalSnapshot.Friends
            .Select((friend, index) => index == 0
                ? friend with
                {
                    ChallengeId = Math.Max(friend.ChallengeId, result.ChallengeId),
                    BestResult = Math.Max(friend.BestResult, result.Result),
                    YourBestResult = Math.Max(friend.YourBestResult, result.Result),
                    Tally = Math.Max(friend.Tally, 1)
                }
                : friend)
            .ToArray();

        LocalSnapshot = LocalSnapshot with
        {
            HighChallengeId = Math.Max(LocalSnapshot.HighChallengeId, result.ChallengeId),
            ChallengeCount = Math.Max(LocalSnapshot.ChallengeCount, 1),
            OverallTally = Math.Max(LocalSnapshot.OverallTally, 1),
            BestResult = Math.Max(LocalSnapshot.BestResult, result.Result),
            Friends = friends
        };

        return Task.CompletedTask;
    }

    private static long ResolveEgonetId(string sessionId, RaceNetPrincipal target)
    {
        if (PrincipalsBySession.TryGetValue(sessionId, out var principals))
        {
            for (var i = 0; i < principals.Count; i++)
            {
                if (principals[i].SteamId == target.SteamId)
                {
                    return 10_000L + i + 1;
                }
            }
        }

        return 10_000L + Math.Abs((long)(target.SteamId % 100_000));
    }
}
