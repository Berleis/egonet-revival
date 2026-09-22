using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;

namespace RaceNetShowdown.Server.Data;

public interface IRaceNetStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<RaceNetSessionInfo> EnsureSessionAsync(
        HttpContext context,
        CapturedBody body,
        CancellationToken cancellationToken);

    Task RecordCallAsync(
        HttpContext context,
        CapturedBody body,
        RaceNetResponse response,
        CancellationToken cancellationToken);

    Task<long> GetHighestChallengeIdAsync(CancellationToken cancellationToken);

    Task<RaceNetChallengeSnapshot> GetChallengeSnapshotAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken);

    Task SavePrincipalsAsync(
        RaceNetSessionInfo session,
        IReadOnlyList<RaceNetPrincipal> principals,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RaceNetPrincipal>> GetPrincipalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken);

    Task<RaceNetIssuedChallenge> IssueChallengeAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal target,
        RaceNetChallengeDraft challengeData,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RaceNetIssuedChallenge>> GetIssuedChallengesAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal? target,
        CancellationToken cancellationToken);

    Task SaveGhostDataAsync(
        RaceNetSessionInfo session,
        long ghostSlotId,
        byte[] ghostData,
        CancellationToken cancellationToken);

    Task<byte[]?> GetGhostDataAsync(
        long ghostSlotId,
        CancellationToken cancellationToken);

    Task SaveChallengeResultAsync(
        RaceNetSessionInfo session,
        EgoNetSubmittedChallengeResult result,
        CancellationToken cancellationToken);

    Task SaveGrid2GlobalScoreAsync(
        RaceNetSessionInfo session,
        Grid2GlobalScoreSubmission submission,
        CancellationToken cancellationToken);

    Task<Grid2GlobalEventSnapshot?> GetGrid2CurrentGlobalEventAsync(
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken);

    Task<Grid2GlobalEventSnapshot?> GetGrid2PreviousGlobalEventAsync(
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken);

    Task SaveGrid2MultiplayerEventAsync(
        RaceNetSessionInfo session,
        Grid2MultiplayerEventSubmission submission,
        CancellationToken cancellationToken);

    Task<Grid2RivalsSnapshot> GetGrid2RivalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken);

    Task SaveGrid2RivalSessionDataAsync(
        RaceNetSessionInfo session,
        byte[] sessionData,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Grid2RivalSessionDataSnapshot>> GetGrid2RivalSessionDataAsync(
        RaceNetSessionInfo session,
        IReadOnlyCollection<long> egonetIds,
        CancellationToken cancellationToken);
}

public sealed record RaceNetSessionInfo(
    string SessionId,
    long PlayerProfileId,
    string PlayerExternalId,
    string DisplayName);
