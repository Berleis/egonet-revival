using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;

namespace RaceNetShowdown.Server.Data;

public interface IRaceNetStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<RaceNetSessionInfo> EnsureSessionAsync(HttpContext context, CancellationToken cancellationToken);

    Task RecordCallAsync(
        HttpContext context,
        CapturedBody body,
        RaceNetResponse response,
        CancellationToken cancellationToken);

    Task<long> GetHighestChallengeIdAsync(CancellationToken cancellationToken);

    Task<RaceNetChallengeSnapshot> GetChallengeSnapshotAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken);
}

public sealed record RaceNetSessionInfo(
    string SessionId,
    long PlayerProfileId,
    string PlayerExternalId,
    string DisplayName);
