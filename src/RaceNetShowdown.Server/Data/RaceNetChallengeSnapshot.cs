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

public sealed record Grid2GlobalEventSnapshot(
    long RaceNetEventId,
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Grid2GlobalRaceSnapshot> Races,
    IReadOnlyList<Grid2GlobalLeaderboardEntry> LeaderboardEntries);

public sealed record Grid2RivalsSnapshot(
    DateTimeOffset StartsAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<Grid2RivalSnapshot> Rivals);

public sealed record Grid2RivalSnapshot(
    ulong SteamId,
    string Name,
    long EgonetId,
    int Type);

public sealed record Grid2RivalSessionDataSnapshot(
    long EgonetId,
    byte[] SessionData);

public sealed record Grid2MultiplayerEventSubmission(
    long? SaveGameId,
    long? RaceNetId,
    IReadOnlyList<Grid2RaceParticipant> HumanParticipants);

public sealed record Grid2RaceParticipant(
    ulong SteamId,
    string Name,
    int? PlayerId,
    int? Position,
    int? Status,
    long? Result,
    bool? Host,
    int? VehicleId);

public sealed record Grid2GlobalRaceSnapshot(
    long RaceNetRaceId,
    bool HigherIsBetter,
    int LocationId,
    int TrackModelId,
    int TrackModelDlcId,
    int ConditionsId,
    int RaceTypeId,
    long RaceDuration,
    int VehicleTierId,
    int VehicleClassId,
    int VehicleId,
    int VehicleDlcId,
    bool SpecialRace,
    long GhostSlotId,
    int SortOrder);

public sealed record Grid2GlobalScoreSubmission(
    long Score,
    short GameId,
    long RaceNetEventId,
    long RaceNetRaceId,
    int VehicleId);

public sealed record Grid2GlobalLeaderboardEntry(
    long RaceNetEventId,
    long RaceNetRaceId,
    RaceNetPrincipal Presence,
    long EgonetId,
    long PersonalBest,
    int VehicleId,
    DateTimeOffset SubmittedAt);
