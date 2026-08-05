using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;

namespace RaceNetShowdown.Server.Data;

public sealed class LocalRaceNetStore : IRaceNetStore
{
    private static readonly RaceNetSessionInfo LocalSession = new(
        "local-racenet-session",
        0,
        "local-player",
        "Local Player");

    private static readonly RaceNetChallengeSnapshot LocalSnapshot = RaceNetChallengeSeed.CreateSnapshot();

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<RaceNetSessionInfo> EnsureSessionAsync(HttpContext context, CancellationToken cancellationToken)
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
}
