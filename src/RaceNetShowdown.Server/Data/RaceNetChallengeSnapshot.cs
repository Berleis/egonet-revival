namespace RaceNetShowdown.Server.Data;

public sealed record RaceNetChallengeSnapshot(
    long HighChallengeId,
    int ChallengeCount,
    long OverallTally,
    long BestResult,
    IReadOnlyList<RaceNetFriendChallenge> Friends);

public sealed record RaceNetFriendChallenge(
    long EgonetId,
    string SteamId,
    string Name,
    int Presence,
    long ChallengeId,
    int RaceEventId,
    int VehicleId,
    long BestResult,
    long YourBestResult,
    long Tally,
    DateTimeOffset ExpiresAt,
    int GhostSlotId);

public sealed record RaceNetPrincipal(
    ulong SteamId,
    string Name);

public sealed record RaceNetChallengeDraft(
    int CareerEventId,
    int GridPosition,
    int Difficulty,
    long ResultToBeat,
    bool TimeBased,
    int VehicleId,
    int LiveryId,
    int Strength,
    int Power,
    int Handling);

public sealed record RaceNetIssuedChallenge(
    long ChallengeId,
    long EgonetId,
    RaceNetPrincipal Presence,
    RaceNetChallengeDraft ChallengeData,
    DateTimeOffset ExpiresAt,
    long GhostSlotId);
