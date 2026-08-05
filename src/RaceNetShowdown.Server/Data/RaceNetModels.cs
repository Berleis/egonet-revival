namespace RaceNetShowdown.Server.Data;

public sealed class PlayerProfile
{
    public long Id { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public List<RaceNetSession> Sessions { get; set; } = [];
}

public sealed class PlayerFriend
{
    public long Id { get; set; }

    public long PlayerProfileId { get; set; }

    public PlayerProfile? PlayerProfile { get; set; }

    public long FriendPlayerProfileId { get; set; }

    public PlayerProfile? FriendPlayerProfile { get; set; }

    public string SteamId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class RaceNetSession
{
    public long Id { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public long PlayerProfileId { get; set; }

    public PlayerProfile? PlayerProfile { get; set; }

    public string RemoteAddress { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class ChallengeRecord
{
    public long Id { get; set; }

    public long EgoNetChallengeId { get; set; }

    public long IssuerPlayerProfileId { get; set; }

    public PlayerProfile? IssuerPlayerProfile { get; set; }

    public long? TargetPlayerProfileId { get; set; }

    public PlayerProfile? TargetPlayerProfile { get; set; }

    public string EventKey { get; set; } = "local-default-event";

    public string VehicleKey { get; set; } = string.Empty;

    public long GhostSlotId { get; set; }

    public int CareerEventId { get; set; }

    public int GridPosition { get; set; }

    public int Difficulty { get; set; }

    public long ResultToBeat { get; set; }

    public bool TimeBased { get; set; }

    public int VehicleId { get; set; }

    public int LiveryId { get; set; }

    public int Strength { get; set; }

    public int Power { get; set; }

    public int Handling { get; set; }

    public long Score { get; set; }

    public TimeSpan? LapTime { get; set; }

    public string Status { get; set; } = "open";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<ChallengeResultRecord> Results { get; set; } = [];
}

public sealed class GhostRecord
{
    public long Id { get; set; }

    public long GhostSlotId { get; set; }

    public long OwnerPlayerProfileId { get; set; }

    public PlayerProfile? OwnerPlayerProfile { get; set; }

    public long? ChallengeRecordId { get; set; }

    public ChallengeRecord? ChallengeRecord { get; set; }

    public byte[] Data { get; set; } = [];

    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class ChallengeResultRecord
{
    public long Id { get; set; }

    public long ChallengeRecordId { get; set; }

    public ChallengeRecord? ChallengeRecord { get; set; }

    public long PlayerProfileId { get; set; }

    public PlayerProfile? PlayerProfile { get; set; }

    public long Score { get; set; }

    public TimeSpan? LapTime { get; set; }

    public bool BeatChallenge { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public string RawPayloadHex { get; set; } = string.Empty;
}

public sealed class RaceNetCallRecord
{
    public long Id { get; set; }

    public DateTimeOffset Time { get; set; }

    public string RemoteAddress { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string QueryString { get; set; } = string.Empty;

    public string EgoNetFunction { get; set; } = string.Empty;

    public string EgoNetSessionId { get; set; } = string.Empty;

    public long BodyLength { get; set; }

    public string BodyPreview { get; set; } = string.Empty;

    public string BodyHexPreview { get; set; } = string.Empty;

    public int ResponseStatus { get; set; }

    public string ResponseContentType { get; set; } = string.Empty;
}
