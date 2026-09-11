using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;

namespace RaceNetShowdown.Server.Data;

public sealed class EntityFrameworkRaceNetStore(
    RaceNetDbContext dbContext,
    ILogger<EntityFrameworkRaceNetStore> logger) : IRaceNetStore
{
    private const string ChallengeStatusOpen = "open";
    private const string ChallengeStatusCompleted = "completed";
    private const string ChallengeStatusExpired = "expired";
    private const string Grid2GlobalEventStatusActive = "active";
    private const string Grid2GlobalEventStatusPrevious = "previous";
    private const string Grid2GlobalEventStatusArchived = "archived";
    private const string Grid2GlobalPreviousEventFunction = "RaceNetGlobalDomination.GetPreviousEvent";
    private const int Grid2GlobalEventResetHourUtc = 10;
    private const DayOfWeek Grid2GlobalEventResetDay = DayOfWeek.Friday;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan Grid2GlobalEventLifetime = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Grid2GlobalEventLock = new(1, 1);
    private static readonly SemaphoreSlim Grid2RivalAllocationLock = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureGrid2GlobalTablesAsync(cancellationToken);
        await EnsureGrid2GlobalEventRotationAsync(cancellationToken);
    }

    public async Task<RaceNetSessionInfo> EnsureSessionAsync(
        HttpContext context,
        CapturedBody body,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = context.Request.Headers["X-EgoNet-SessionID"].ToString();

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var existingSession = await dbContext.RaceNetSessions
                .Include(value => value.PlayerProfile)
                .FirstOrDefaultAsync(value => value.SessionId == sessionId, cancellationToken);

            if (existingSession?.PlayerProfile is not null &&
                IsWireCompatibleSessionId(existingSession.SessionId))
            {
                existingSession.LastSeenAt = now;
                existingSession.PlayerProfile.LastSeenAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);

                return ToSessionInfo(existingSession);
            }
        }

        var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var loginName = EgoNetRequestParser.ReadTopLevelString(body, "Name");

        if (string.IsNullOrWhiteSpace(loginName))
        {
            var recentRemoteSession = await FindRecentRemoteSessionAsync(remoteAddress, now, cancellationToken);
            if (recentRemoteSession is not null)
            {
                return recentRemoteSession;
            }
        }

        var profile = await FindOrCreateProfileAsync(loginName, remoteAddress, now, cancellationToken);
        if (profile.Id > 0)
        {
            var recentProfileSession = await FindRecentProfileSessionAsync(
                profile.Id,
                remoteAddress,
                now,
                cancellationToken);
            if (recentProfileSession is not null)
            {
                return recentProfileSession;
            }
        }

        sessionId = CreateSessionId();
        var session = new RaceNetSession
        {
            SessionId = sessionId,
            PlayerProfile = profile,
            RemoteAddress = remoteAddress,
            UserAgent = userAgent,
            CreatedAt = now,
            LastSeenAt = now
        };

        dbContext.RaceNetSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogDebug("RaceNet profile/session created: {Profile} {Session}", profile.ExternalId, sessionId);

        return ToSessionInfo(session);
    }

    private async Task<RaceNetSessionInfo?> FindRecentProfileSessionAsync(
        long playerProfileId,
        string remoteAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profileSessions = await dbContext.RaceNetSessions
            .Include(value => value.PlayerProfile)
            .Where(value =>
                value.PlayerProfileId == playerProfileId &&
                value.RemoteAddress == remoteAddress)
            .ToListAsync(cancellationToken);
        var recentSession = profileSessions
            .OrderByDescending(value => value.LastSeenAt)
            .FirstOrDefault();

        if (recentSession?.PlayerProfile is null ||
            !IsWireCompatibleSessionId(recentSession.SessionId))
        {
            return null;
        }

        recentSession.LastSeenAt = now;
        recentSession.PlayerProfile.LastSeenAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToSessionInfo(recentSession);
    }

    private async Task<RaceNetSessionInfo?> FindRecentRemoteSessionAsync(
        string remoteAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var remoteSessions = await dbContext.RaceNetSessions
            .Include(value => value.PlayerProfile)
            .Where(value => value.RemoteAddress == remoteAddress)
            .ToListAsync(cancellationToken);
        var recentSession = remoteSessions
            .OrderByDescending(value => value.LastSeenAt)
            .FirstOrDefault();

        if (recentSession?.PlayerProfile is null ||
            !IsWireCompatibleSessionId(recentSession.SessionId))
        {
            return null;
        }

        recentSession.LastSeenAt = now;
        recentSession.PlayerProfile.LastSeenAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToSessionInfo(recentSession);
    }

    public async Task RecordCallAsync(
        HttpContext context,
        CapturedBody body,
        RaceNetResponse response,
        CancellationToken cancellationToken)
    {
        var request = context.Request;

        dbContext.RaceNetCalls.Add(new RaceNetCallRecord
        {
            Time = DateTimeOffset.UtcNow,
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            Method = request.Method,
            Host = request.Host.ToString(),
            Path = request.Path.ToString(),
            QueryString = request.QueryString.ToString(),
            EgoNetFunction = request.Headers["X-EgoNet-Function"].ToString(),
            EgoNetSessionId = request.Headers["X-EgoNet-SessionID"].ToString(),
            BodyLength = body.Length,
            BodyPreview = string.Empty,
            BodyHexPreview = string.Empty,
            ResponseStatus = response.StatusCode,
            ResponseContentType = response.ContentType
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> GetHighestChallengeIdAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Challenges
            .Select(value => (long?)value.EgoNetChallengeId)
            .MaxAsync(cancellationToken) ?? 0;
    }

    public async Task<RaceNetChallengeSnapshot> GetChallengeSnapshotAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ReconcileOpenChallengesAsync(now, cancellationToken);

        var openChallenges = (await dbContext.Challenges
            .Include(value => value.IssuerPlayerProfile)
            .Include(value => value.Results)
            .Where(value =>
                value.TargetPlayerProfileId == session.PlayerProfileId &&
                value.Status == ChallengeStatusOpen)
            .ToListAsync(cancellationToken))
            .Where(value => !IsExpired(value, now))
            .ToArray();

        var scoredChallenges = await dbContext.Challenges
            .Include(value => value.IssuerPlayerProfile)
            .Include(value => value.TargetPlayerProfile)
            .Include(value => value.Results)
            .Where(value =>
                (value.TargetPlayerProfileId == session.PlayerProfileId ||
                 value.IssuerPlayerProfileId == session.PlayerProfileId) &&
                value.Results.Any())
            .ToListAsync(cancellationToken);

        var tallyEntries = scoredChallenges
            .Select(challenge => ToTallyEntry(challenge, session.PlayerProfileId))
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        var tallyByFriend = tallyEntries
            .GroupBy(value => value.FriendPlayerProfileId)
            .ToDictionary(
                value => value.Key,
                value => value.Sum(result => result.TallyDelta));

        var openFriends = openChallenges
            .OrderByDescending(value => value.CreatedAt)
            .Take(20)
            .Select(challenge => ToFriendChallenge(
                challenge,
                session.PlayerProfileId,
                tallyByFriend.GetValueOrDefault(challenge.IssuerPlayerProfileId)))
            .ToArray();

        var openIssuerIds = openFriends
            .Select(value => value.EgonetId)
            .ToHashSet();
        var completedTallyFriends = tallyEntries
            .Where(value => !openIssuerIds.Contains(value.FriendPlayerProfileId))
            .GroupBy(value => value.FriendPlayerProfileId)
            .Select(group =>
            {
                var entry = group
                    .OrderByDescending(value => value.Result.SubmittedAt)
                    .First();
                return ToTallyFriend(entry, tallyByFriend.GetValueOrDefault(group.Key));
            })
            .Where(value => value.Tally != 0)
            .ToArray();

        var friends = openFriends
            .Concat(completedTallyFriends)
            .ToArray();
        var challengedFriendCount = openFriends
            .Select(value => string.IsNullOrWhiteSpace(value.SteamId) ? value.Name : value.SteamId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new RaceNetChallengeSnapshot(
            HighChallengeId: openFriends.Length == 0 ? 0 : openFriends.Max(value => value.ChallengeId),
            ChallengeCount: challengedFriendCount,
            OverallTally: friends.Count(value => value.Tally >= 10),
            BestResult: openFriends.Length == 0 ? 0 : openFriends.Min(value => value.BestResult),
            Friends: friends);
    }

    public async Task SavePrincipalsAsync(
        RaceNetSessionInfo session,
        IReadOnlyList<RaceNetPrincipal> principals,
        CancellationToken cancellationToken)
    {
        if (principals.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var principal in principals)
        {
            var friendProfile = await FindOrCreateSteamProfileAsync(principal, now, cancellationToken);
            var steamId = principal.SteamId.ToString(CultureInfo.InvariantCulture);
            var friend = await dbContext.PlayerFriends
                .FirstOrDefaultAsync(
                    value => value.PlayerProfileId == session.PlayerProfileId && value.SteamId == steamId,
                    cancellationToken);

            if (friend is null)
            {
                friend = new PlayerFriend
                {
                    PlayerProfileId = session.PlayerProfileId,
                    FriendPlayerProfile = friendProfile,
                    SteamId = steamId,
                    DisplayName = principal.Name,
                    LastSeenAt = now
                };
                dbContext.PlayerFriends.Add(friend);
            }
            else
            {
                friend.FriendPlayerProfile = friendProfile;
                friend.DisplayName = principal.Name;
                friend.LastSeenAt = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RaceNetPrincipal>> GetPrincipalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ReconcileOpenChallengesAsync(now, cancellationToken);

        var openChallenges = await dbContext.Challenges
            .AsNoTracking()
            .Where(value =>
                value.TargetPlayerProfileId == session.PlayerProfileId &&
                value.Status == ChallengeStatusOpen)
            .ToArrayAsync(cancellationToken);
        var challengedFriendIds = openChallenges
            .Where(value => !IsExpired(value, now))
            .Select(value => value.IssuerPlayerProfileId)
            .Distinct()
            .ToArray();
        var challengedFriendSet = challengedFriendIds.ToHashSet();
        var friends = await dbContext.PlayerFriends
            .AsNoTracking()
            .Where(value => value.PlayerProfileId == session.PlayerProfileId)
            .ToArrayAsync(cancellationToken);

        return friends
            .OrderByDescending(value => challengedFriendSet.Contains(value.FriendPlayerProfileId))
            .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(value => ulong.TryParse(value.SteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId)
                ? new RaceNetPrincipal(steamId, value.DisplayName)
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    public async Task<RaceNetIssuedChallenge> IssueChallengeAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal target,
        RaceNetChallengeDraft challengeData,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ReconcileOpenChallengesAsync(now, cancellationToken);

        var targetProfile = await FindOrCreateSteamProfileAsync(target, now, cancellationToken);
        var challengeId = await GetNextChallengeIdAsync(cancellationToken);
        var challenge = new ChallengeRecord
        {
            EgoNetChallengeId = challengeId,
            IssuerPlayerProfileId = session.PlayerProfileId,
            TargetPlayerProfile = targetProfile,
            EventKey = challengeData.CareerEventId.ToString(CultureInfo.InvariantCulture),
            VehicleKey = challengeData.VehicleId.ToString(CultureInfo.InvariantCulture),
            GhostSlotId = challengeId,
            CareerEventId = challengeData.CareerEventId,
            GridPosition = challengeData.GridPosition,
            Difficulty = challengeData.Difficulty,
            ResultToBeat = challengeData.ResultToBeat,
            TimeBased = challengeData.TimeBased,
            VehicleId = challengeData.VehicleId,
            LiveryId = challengeData.LiveryId,
            Strength = challengeData.Strength,
            Power = challengeData.Power,
            Handling = challengeData.Handling,
            Score = challengeData.ResultToBeat,
            LapTime = challengeData.TimeBased && challengeData.ResultToBeat > 0
                ? TimeSpan.FromMilliseconds(challengeData.ResultToBeat)
                : null,
            Status = ChallengeStatusOpen,
            CreatedAt = now
        };

        dbContext.Challenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Challenge {ChallengeId} issued from {Issuer} to {Target} result={Result}",
            challengeId,
            session.DisplayName,
            target.Name,
            challengeData.ResultToBeat);

        return ToIssuedChallenge(challenge, targetProfile, target);
    }

    public async Task<IReadOnlyList<RaceNetIssuedChallenge>> GetIssuedChallengesAsync(
        RaceNetSessionInfo session,
        RaceNetPrincipal? target,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ReconcileOpenChallengesAsync(now, cancellationToken);

        var query = dbContext.Challenges
            .Include(value => value.IssuerPlayerProfile)
            .Include(value => value.TargetPlayerProfile)
            .Where(value =>
                (value.IssuerPlayerProfileId == session.PlayerProfileId ||
                 value.TargetPlayerProfileId == session.PlayerProfileId) &&
                value.Status == ChallengeStatusOpen);

        if (target is not null)
        {
            var steamExternalId = BuildSteamExternalId(target.SteamId);
            query = query.Where(value =>
                (value.IssuerPlayerProfileId == session.PlayerProfileId &&
                 value.TargetPlayerProfile != null &&
                 (value.TargetPlayerProfile.ExternalId == steamExternalId ||
                  value.TargetPlayerProfile.DisplayName == target.Name)) ||
                (value.TargetPlayerProfileId == session.PlayerProfileId &&
                 value.IssuerPlayerProfile != null &&
                 (value.IssuerPlayerProfile.ExternalId == steamExternalId ||
                  value.IssuerPlayerProfile.DisplayName == target.Name)));
        }

        var challenges = (await query.ToListAsync(cancellationToken))
            .Where(value => !IsExpired(value, now))
            .ToArray();

        return challenges
            .OrderByDescending(value => value.CreatedAt)
            .Take(20)
            .Select(value =>
            {
                var otherProfile = value.IssuerPlayerProfileId == session.PlayerProfileId
                    ? value.TargetPlayerProfile
                    : value.IssuerPlayerProfile;
                return otherProfile is null ? null : ToIssuedChallenge(value, otherProfile, null);
            })
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    public async Task SaveGhostDataAsync(
        RaceNetSessionInfo session,
        long ghostSlotId,
        byte[] ghostData,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = await dbContext.Challenges
            .FirstOrDefaultAsync(value => value.GhostSlotId == ghostSlotId, cancellationToken);
        var ghost = await dbContext.Ghosts
            .FirstOrDefaultAsync(value => value.GhostSlotId == ghostSlotId, cancellationToken);

        if (ghost is null)
        {
            ghost = new GhostRecord
            {
                GhostSlotId = ghostSlotId,
                OwnerPlayerProfileId = session.PlayerProfileId,
                ChallengeRecord = challenge,
                Data = ghostData.ToArray(),
                UploadedAt = now
            };
            dbContext.Ghosts.Add(ghost);
        }
        else
        {
            ghost.OwnerPlayerProfileId = session.PlayerProfileId;
            ghost.ChallengeRecord = challenge;
            ghost.Data = ghostData.ToArray();
            ghost.UploadedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]?> GetGhostDataAsync(
        long ghostSlotId,
        CancellationToken cancellationToken)
    {
        var ghost = await dbContext.Ghosts
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.GhostSlotId == ghostSlotId, cancellationToken);

        if (ghost is null)
        {
            var ghosts = await dbContext.Ghosts
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            ghost = ghosts
                .OrderByDescending(value => value.UploadedAt)
                .FirstOrDefault();
        }

        return ghost?.Data.ToArray();
    }

    public async Task SaveChallengeResultAsync(
        RaceNetSessionInfo session,
        EgoNetSubmittedChallengeResult result,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = await dbContext.Challenges
            .Include(value => value.Results)
            .FirstOrDefaultAsync(value => value.EgoNetChallengeId == result.ChallengeId, cancellationToken);

        if (challenge is null)
        {
            return;
        }

        if (challenge.Status != ChallengeStatusOpen)
        {
            logger.LogDebug(
                "Ignoring result for closed challenge {ChallengeId} with status {Status}",
                result.ChallengeId,
                challenge.Status);
            return;
        }

        if (IsExpired(challenge, now))
        {
            challenge.Status = ChallengeStatusExpired;
            challenge.CompletedAt = GetChallengeExpiresAt(challenge);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var target = challenge.ResultToBeat > 0
            ? challenge.ResultToBeat
            : EgoNetChallengePayloads.TryGetCatalogResultToBeat(result.ChallengeId) ?? 0;
        var dominated = target <= 0 || IsDominated(challenge.TimeBased, result.Result, target);

        challenge.Results.Add(new ChallengeResultRecord
        {
            PlayerProfileId = session.PlayerProfileId,
            Score = result.Result,
            LapTime = challenge.TimeBased && result.Result > 0 ? TimeSpan.FromMilliseconds(result.Result) : null,
            BeatChallenge = dominated,
            SubmittedAt = now,
            RawPayloadHex = string.Empty
        });

        challenge.Status = ChallengeStatusCompleted;
        challenge.CompletedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveGrid2GlobalScoreAsync(
        RaceNetSessionInfo session,
        Grid2GlobalScoreSubmission submission,
        CancellationToken cancellationToken)
    {
        if (session.PlayerProfileId <= 0 || submission.Score < 0)
        {
            return;
        }

        await EnsureGrid2GlobalEventRotationAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        dbContext.Grid2GlobalScores.Add(new Grid2GlobalScoreRecord
        {
            PlayerProfileId = session.PlayerProfileId,
            RaceNetEventId = submission.RaceNetEventId,
            RaceNetRaceId = submission.RaceNetRaceId,
            GameId = submission.GameId,
            Score = submission.Score,
            VehicleId = submission.VehicleId,
            SubmittedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "GRID 2 global score submitted by {Player}: event={EventId} race={RaceId} score={Score} vehicle={VehicleId}",
            session.DisplayName,
            submission.RaceNetEventId,
            submission.RaceNetRaceId,
            submission.Score,
            submission.VehicleId);
    }

    public async Task<Grid2GlobalEventSnapshot?> GetGrid2CurrentGlobalEventAsync(
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken)
    {
        await EnsureGrid2GlobalEventRotationAsync(cancellationToken);
        var activeEvent = await LoadGrid2GlobalEventByStatusAsync(Grid2GlobalEventStatusActive, cancellationToken);
        return activeEvent is null
            ? null
            : await ToGrid2GlobalEventSnapshotAsync(activeEvent, session, cancellationToken);
    }

    public async Task<Grid2GlobalEventSnapshot?> GetGrid2PreviousGlobalEventAsync(
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken)
    {
        await EnsureGrid2GlobalEventRotationAsync(cancellationToken);
        if (session is null || session.PlayerProfileId <= 0)
        {
            return null;
        }

        var previousEvent = await LoadGrid2GlobalEventByStatusAsync(Grid2GlobalEventStatusPrevious, cancellationToken);
        if (previousEvent is null)
        {
            return null;
        }

        if (await HasGrid2GlobalRewardClaimAsync(session.PlayerProfileId, previousEvent.RaceNetEventId, cancellationToken))
        {
            return null;
        }

        if (await WasGrid2PreviousEventAlreadyReturnedAsync(session, previousEvent, cancellationToken))
        {
            await TryClaimGrid2GlobalRewardAsync(session, previousEvent, "backfilled", cancellationToken);
            return null;
        }

        var snapshot = await ToGrid2GlobalEventSnapshotAsync(previousEvent, session, cancellationToken);
        return await TryClaimGrid2GlobalRewardAsync(session, previousEvent, "delivered", cancellationToken)
            ? snapshot
            : null;
    }

    public async Task SaveGrid2MultiplayerEventAsync(
        RaceNetSessionInfo session,
        Grid2MultiplayerEventSubmission submission,
        CancellationToken cancellationToken)
    {
        var participants = submission.HumanParticipants
            .Where(value => value.SteamId > 0 && !string.IsNullOrWhiteSpace(value.Name))
            .GroupBy(value => value.SteamId)
            .Select(value => value.First())
            .ToArray();

        if (participants.Length < 2)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var participantProfiles = new List<(Grid2RaceParticipant Participant, PlayerProfile Profile)>(participants.Length);
        foreach (var participant in participants)
        {
            var profile = await FindOrCreateSteamProfileAsync(
                new RaceNetPrincipal(participant.SteamId, participant.Name.Trim()),
                now,
                cancellationToken);
            participantProfiles.Add((participant, profile));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var player in participantProfiles)
        {
            foreach (var opponent in participantProfiles)
            {
                if (player.Profile.Id == opponent.Profile.Id)
                {
                    continue;
                }

                await UpsertGrid2RivalOpponentAsync(
                    player,
                    opponent,
                    submission,
                    now,
                    cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "GRID 2 multiplayer event learned: session={SessionPlayer} raceNetId={RaceNetId} saveGameId={SaveGameId} participants={Participants}",
            session.DisplayName,
            submission.RaceNetId?.ToString(CultureInfo.InvariantCulture) ?? "<none>",
            submission.SaveGameId?.ToString(CultureInfo.InvariantCulture) ?? "<none>",
            string.Join(", ", participants.Select(FormatGrid2Participant)));
    }

    public async Task SaveGrid2ProfileSnapshotAsync(
        RaceNetSessionInfo session,
        Grid2ProfileSnapshotSubmission submission,
        CancellationToken cancellationToken)
    {
        if (session.PlayerProfileId <= 0 || submission.XpTotal < 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var currentWeekStartsAt = GetGrid2CurrentGlobalEventStartsAt(now);
        var latest = await dbContext.Grid2ProfileXpSnapshots
            .AsNoTracking()
            .Where(value => value.PlayerProfileId == session.PlayerProfileId)
            .OrderByDescending(value => value.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null &&
            latest.CapturedAt >= currentWeekStartsAt &&
            latest.SaveGameId == submission.SaveGameId &&
            latest.XpTotal == submission.XpTotal &&
            latest.XpLevel == submission.XpLevel)
        {
            return;
        }

        var xpDelta = latest is not null && submission.XpTotal > latest.XpTotal
            ? (long)submission.XpTotal - latest.XpTotal
            : 0;
        var xpDeltaReferenceKey = latest is null
            ? string.Empty
            : $"profile:{latest.Id}:{submission.XpTotal.ToString(CultureInfo.InvariantCulture)}";
        var record = new Grid2ProfileXpSnapshotRecord
        {
            PlayerProfileId = session.PlayerProfileId,
            SaveGameId = submission.SaveGameId,
            XpTotal = submission.XpTotal,
            XpLevel = submission.XpLevel,
            CapturedAt = now
        };

        dbContext.Grid2ProfileXpSnapshots.Add(record);

        await dbContext.SaveChangesAsync(cancellationToken);

        await TrySaveGrid2WeeklyXpDeltaAsync(
            session.PlayerProfileId,
            session.DisplayName,
            now,
            xpDelta,
            "profile",
            xpDeltaReferenceKey,
            cancellationToken);

        logger.LogInformation(
            "GRID 2 profile XP saved for {Player}: saveGameId={SaveGameId} xpTotal={XpTotal} xpLevel={XpLevel} xpDelta={XpDelta}",
            session.DisplayName,
            submission.SaveGameId?.ToString(CultureInfo.InvariantCulture) ?? "<none>",
            submission.XpTotal,
            submission.XpLevel,
            xpDelta);
    }

    public async Task<Grid2RivalsSnapshot> GetGrid2RivalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        await EnsureGrid2GlobalEventRotationAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var startsAt = GetGrid2CurrentGlobalEventStartsAt(now);
        var expiresAt = startsAt.Add(Grid2GlobalEventLifetime);
        var rivals = await LoadGrid2RivalAssignmentsAsync(
            session,
            startsAt,
            expiresAt,
            createIfMissing: true,
            cancellationToken);
        try
        {
            rivals = await LoadGrid2RivalXpAsync(
                session.PlayerProfileId,
                rivals,
                startsAt,
                expiresAt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load GRID 2 rival XP for {Player}", session.DisplayName);
        }

        logger.LogInformation(
            "GRID 2 rivals selected for {Player}: startsAt={StartsAt:o} expiresAt={ExpiresAt:o} rivals={Rivals}",
            session.DisplayName,
            startsAt,
            expiresAt,
            FormatGrid2Rivals(rivals));

        return new Grid2RivalsSnapshot(startsAt, expiresAt, rivals);
    }

    private async Task<IReadOnlyList<Grid2RivalSnapshot>> LoadGrid2RivalAssignmentsAsync(
        RaceNetSessionInfo session,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        if (session.PlayerProfileId <= 0)
        {
            return [];
        }

        var assignedRivals = await LoadGrid2RivalAssignmentsOnlyAsync(
            session.PlayerProfileId,
            startsAt,
            expiresAt,
            cancellationToken);
        if (assignedRivals.Count > 0 || !createIfMissing)
        {
            return assignedRivals;
        }

        await Grid2RivalAllocationLock.WaitAsync(cancellationToken);
        try
        {
            assignedRivals = await LoadGrid2RivalAssignmentsOnlyAsync(
                session.PlayerProfileId,
                startsAt,
                expiresAt,
                cancellationToken);
            if (assignedRivals.Count > 0)
            {
                return assignedRivals;
            }

            var selectedRivals = await SelectGrid2RivalsAsync(session, cancellationToken);
            if (selectedRivals.Count == 0)
            {
                return [];
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var rival in selectedRivals)
            {
                dbContext.Grid2RivalAssignments.Add(new Grid2RivalAssignmentRecord
                {
                    PlayerProfileId = session.PlayerProfileId,
                    RivalPlayerProfileId = rival.EgonetId,
                    Type = rival.Type,
                    StartsAt = startsAt,
                    ExpiresAt = expiresAt,
                    CreatedAt = now
                });
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                foreach (var entry in dbContext.ChangeTracker.Entries<Grid2RivalAssignmentRecord>())
                {
                    if (entry.State == EntityState.Added)
                    {
                        entry.State = EntityState.Detached;
                    }
                }
            }

            return await LoadGrid2RivalAssignmentsOnlyAsync(
                session.PlayerProfileId,
                startsAt,
                expiresAt,
                cancellationToken);
        }
        finally
        {
            Grid2RivalAllocationLock.Release();
        }
    }

    private async Task<IReadOnlyList<Grid2RivalSnapshot>> LoadGrid2RivalAssignmentsOnlyAsync(
        long playerProfileId,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var assignments = await dbContext.Grid2RivalAssignments
            .AsNoTracking()
            .Include(value => value.RivalPlayerProfile)
            .Where(value =>
                value.PlayerProfileId == playerProfileId &&
                value.StartsAt == startsAt &&
                value.ExpiresAt == expiresAt)
            .ToListAsync(cancellationToken);

        return assignments
            .Where(value =>
                value.RivalPlayerProfile is not null &&
                IsGrid2RivalNameAllowed(value.RivalPlayerProfile.DisplayName))
            .OrderBy(value => GetGrid2RivalTypeSortOrder(value.Type))
            .Select(ToGrid2RivalSnapshot)
            .ToArray();
    }

    private async Task<IReadOnlyList<Grid2RivalSnapshot>> LoadGrid2RivalXpAsync(
        long playerProfileId,
        IReadOnlyList<Grid2RivalSnapshot> rivals,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (playerProfileId <= 0 || rivals.Count == 0)
        {
            return rivals;
        }

        var rivalIds = rivals
            .Select(value => value.EgonetId)
            .Where(value => value > 0)
            .Distinct()
            .ToArray();
        if (rivalIds.Length == 0)
        {
            return rivals;
        }

        var profileIds = rivalIds
            .Append(playerProfileId)
            .Distinct()
            .ToArray();
        var xpSnapshots = await dbContext.Grid2ProfileXpSnapshots
            .AsNoTracking()
            .Where(value =>
                profileIds.Contains(value.PlayerProfileId) &&
                value.CapturedAt < expiresAt)
            .ToListAsync(cancellationToken);
        var weeklyXpByProfileId = xpSnapshots
            .GroupBy(value => value.PlayerProfileId)
            .ToDictionary(
                value => value.Key,
                value => CalculateGrid2WeeklyProfileXp(value, startsAt, expiresAt));
        var xpDeltas = await dbContext.Grid2WeeklyXpDeltas
            .AsNoTracking()
            .Where(value =>
                profileIds.Contains(value.PlayerProfileId) &&
                value.WeekStartsAt == startsAt &&
                value.EarnedAt < expiresAt)
            .ToListAsync(cancellationToken);
        var weeklyDeltaXpByProfileId = xpDeltas
            .GroupBy(value => value.PlayerProfileId)
            .ToDictionary(
                value => value.Key,
                value => SumGrid2RivalXp(value.Select(delta => delta.Amount)));

        return rivals
            .Select(rival =>
            {
                var playerXp = MaxGrid2RivalXp(
                    weeklyXpByProfileId.GetValueOrDefault(playerProfileId),
                    weeklyDeltaXpByProfileId.GetValueOrDefault(playerProfileId));
                var rivalXp = MaxGrid2RivalXp(
                    weeklyXpByProfileId.GetValueOrDefault(rival.EgonetId),
                    weeklyDeltaXpByProfileId.GetValueOrDefault(rival.EgonetId));

                return rival with
                {
                    TotalXpWon = AddGrid2RivalXp(playerXp, rivalXp),
                    RivalXpWon = rivalXp
                };
            })
            .ToArray();
    }

    private async Task TrySaveGrid2WeeklyXpDeltaAsync(
        long playerProfileId,
        string displayName,
        DateTimeOffset earnedAt,
        long amount,
        string source,
        string referenceKey,
        CancellationToken cancellationToken)
    {
        if (playerProfileId <= 0 || amount <= 0 || string.IsNullOrWhiteSpace(referenceKey))
        {
            return;
        }

        if (await dbContext.Grid2WeeklyXpDeltas
            .AsNoTracking()
            .AnyAsync(value =>
                value.PlayerProfileId == playerProfileId &&
                value.Source == source &&
                value.ReferenceKey == referenceKey,
                cancellationToken))
        {
            return;
        }

        var weekStartsAt = GetGrid2CurrentGlobalEventStartsAt(earnedAt);
        dbContext.Grid2WeeklyXpDeltas.Add(new Grid2WeeklyXpDeltaRecord
        {
            PlayerProfileId = playerProfileId,
            WeekStartsAt = weekStartsAt,
            Amount = amount,
            Source = source,
            ReferenceKey = referenceKey,
            EarnedAt = earnedAt
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<Grid2WeeklyXpDeltaRecord>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
            }

            logger.LogDebug(
                ex,
                "GRID 2 weekly XP delta skipped for {Player}: source={Source} reference={ReferenceKey}",
                displayName,
                source,
                referenceKey);
            return;
        }

        logger.LogInformation(
            "GRID 2 weekly XP delta saved for {Player}: startsAt={StartsAt:o} amount={Amount} source={Source} reference={ReferenceKey}",
            displayName,
            weekStartsAt,
            amount,
            source,
            referenceKey);
    }

    private async Task<IReadOnlyList<Grid2RivalSnapshot>> SelectGrid2RivalsAsync(
        RaceNetSessionInfo session,
        CancellationToken cancellationToken)
    {
        if (session.PlayerProfileId <= 0)
        {
            return [];
        }

        var sessionDataRecords = await dbContext.Grid2RivalSessionData
            .AsNoTracking()
            .Select(value => new
            {
                value.PlayerProfileId,
                value.SessionData,
                value.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        var sessionDataUpdatedByProfileId = sessionDataRecords
            .Where(value => IsGrid2RivalSessionDataMeaningful(value.SessionData))
            .GroupBy(value => value.PlayerProfileId)
            .ToDictionary(
                value => value.Key,
                value => value.Max(record => record.UpdatedAt));

        var friendProfileIds = await dbContext.PlayerFriends
            .AsNoTracking()
            .Where(value =>
                value.PlayerProfileId == session.PlayerProfileId &&
                value.FriendPlayerProfileId != session.PlayerProfileId)
            .Select(value => value.FriendPlayerProfileId)
            .ToListAsync(cancellationToken);
        var friendProfileIdSet = friendProfileIds.ToHashSet();

        var opponentRecords = await dbContext.Grid2RivalOpponents
            .AsNoTracking()
            .Where(value => value.PlayerProfileId == session.PlayerProfileId)
            .ToListAsync(cancellationToken);
        var recentOpponentByProfileId = opponentRecords
            .GroupBy(value => value.OpponentPlayerProfileId)
            .ToDictionary(
                value => value.Key,
                value => value
                    .OrderByDescending(record => record.LastSeenAt)
                    .First());

        var profiles = await dbContext.PlayerProfiles
            .AsNoTracking()
            .Where(value => value.Id != session.PlayerProfileId)
            .ToListAsync(cancellationToken);

        var candidates = profiles
            .Where(profile => IsGrid2RivalNameAllowed(profile.DisplayName))
            .Select(profile =>
            {
                var sessionDataUpdatedAt = sessionDataUpdatedByProfileId.TryGetValue(profile.Id, out var updatedAt)
                    ? updatedAt
                    : (DateTimeOffset?)null;
                var recentOpponent = recentOpponentByProfileId.GetValueOrDefault(profile.Id);

                return new Grid2RivalCandidate(
                    profile,
                    sessionDataUpdatedAt,
                    friendProfileIdSet.Contains(profile.Id),
                    recentOpponent?.LastSeenAt,
                    recentOpponent?.TimesMet ?? 0);
            })
            .ToArray();

        var selectedProfileIds = new HashSet<long>();
        var rivals = new List<Grid2RivalSnapshot>(capacity: 3);

        AddGrid2Rival(rivals, selectedProfileIds, SelectGrid2SocialRival(candidates, selectedProfileIds), type: 2);
        AddGrid2Rival(rivals, selectedProfileIds, SelectGrid2AutomaticRival(candidates, selectedProfileIds), type: 0);
        AddGrid2Rival(rivals, selectedProfileIds, SelectGrid2AutomaticRival(candidates, selectedProfileIds), type: 1);

        return rivals;
    }

    private static Grid2RivalSnapshot ToGrid2RivalSnapshot(Grid2RivalAssignmentRecord assignment)
    {
        var profile = assignment.RivalPlayerProfile
            ?? throw new InvalidOperationException("GRID 2 rival assignment profile is missing.");
        return new Grid2RivalSnapshot(
            ResolveGrid2SteamId(profile),
            profile.DisplayName.Trim(),
            profile.Id,
            assignment.Type);
    }

    public async Task SaveGrid2RivalSessionDataAsync(
        RaceNetSessionInfo session,
        byte[] sessionData,
        CancellationToken cancellationToken)
    {
        if (session.PlayerProfileId <= 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var record = await dbContext.Grid2RivalSessionData
            .FirstOrDefaultAsync(value => value.PlayerProfileId == session.PlayerProfileId, cancellationToken);
        var isMeaningful = IsGrid2RivalSessionDataMeaningful(sessionData);
        if (!isMeaningful)
        {
            logger.LogInformation(
                "GRID 2 blank rival session data ignored for {Player}: {Length} bytes nonZero={NonZeroBytes} preview={Preview}",
                session.DisplayName,
                sessionData.Length,
                CountNonZeroBytes(sessionData),
                FormatGrid2HexPreview(sessionData));
            return;
        }

        if (record is null)
        {
            dbContext.Grid2RivalSessionData.Add(new Grid2RivalSessionDataRecord
            {
                PlayerProfileId = session.PlayerProfileId,
                SessionData = sessionData.ToArray(),
                UpdatedAt = now
            });
        }
        else
        {
            record.SessionData = sessionData.ToArray();
            record.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "GRID 2 rival session data saved by {Player}: {Length} bytes nonZero={NonZeroBytes} preview={Preview}",
            session.DisplayName,
            sessionData.Length,
            CountNonZeroBytes(sessionData),
            FormatGrid2HexPreview(sessionData));
    }

    public async Task<IReadOnlyList<Grid2RivalSessionDataSnapshot>> GetGrid2RivalSessionDataAsync(
        RaceNetSessionInfo session,
        IReadOnlyCollection<long> egonetIds,
        CancellationToken cancellationToken)
    {
        var ids = egonetIds
            .Where(value => value > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var records = await dbContext.Grid2RivalSessionData
            .AsNoTracking()
            .Where(value => ids.Contains(value.PlayerProfileId))
            .ToListAsync(cancellationToken);
        var dataByProfileId = records
            .Where(value => IsGrid2RivalSessionDataMeaningful(value.SessionData))
            .ToDictionary(value => value.PlayerProfileId, value => value.SessionData.ToArray());

        return ids
            .Where(dataByProfileId.ContainsKey)
            .Select(value => new Grid2RivalSessionDataSnapshot(value, dataByProfileId[value]))
            .ToArray();
    }

    private async Task UpsertGrid2RivalOpponentAsync(
        (Grid2RaceParticipant Participant, PlayerProfile Profile) player,
        (Grid2RaceParticipant Participant, PlayerProfile Profile) opponent,
        Grid2MultiplayerEventSubmission submission,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.Grid2RivalOpponents.FirstOrDefaultAsync(
            value =>
                value.PlayerProfileId == player.Profile.Id &&
                value.OpponentPlayerProfileId == opponent.Profile.Id,
            cancellationToken);

        if (record is null)
        {
            record = new Grid2RivalOpponentRecord
            {
                PlayerProfileId = player.Profile.Id,
                OpponentPlayerProfileId = opponent.Profile.Id,
                FirstSeenAt = now
            };
            dbContext.Grid2RivalOpponents.Add(record);
        }
        else if (record.LastSeenAt < GetGrid2CurrentGlobalEventStartsAt(now))
        {
            record.FirstSeenAt = now;
            record.TimesMet = 0;
        }

        record.LastRaceNetEventId = submission.RaceNetId;
        record.LastSaveGameId = submission.SaveGameId;
        record.PlayerPosition = player.Participant.Position;
        record.OpponentPosition = opponent.Participant.Position;
        record.PlayerStatus = player.Participant.Status;
        record.OpponentStatus = opponent.Participant.Status;
        record.PlayerResult = player.Participant.Result;
        record.OpponentResult = opponent.Participant.Result;
        record.OpponentVehicleId = opponent.Participant.VehicleId;
        record.TimesMet = Math.Max(1, record.TimesMet + 1);
        record.LastSeenAt = now;
    }

    private static uint CalculateGrid2WeeklyProfileXp(
        IEnumerable<Grid2ProfileXpSnapshotRecord> records,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt)
    {
        var snapshots = records
            .Where(value => value.CapturedAt < expiresAt)
            .OrderBy(value => value.CapturedAt)
            .ToArray();
        if (snapshots.Length < 2)
        {
            return 0;
        }

        var xp = 0UL;
        for (var i = 1; i < snapshots.Length; i++)
        {
            var current = snapshots[i];
            if (current.CapturedAt < startsAt || current.CapturedAt >= expiresAt)
            {
                continue;
            }

            var delta = (long)current.XpTotal - snapshots[i - 1].XpTotal;
            if (delta <= 0)
            {
                continue;
            }

            xp += (ulong)delta;
            if (xp > uint.MaxValue)
            {
                return uint.MaxValue;
            }
        }

        return (uint)xp;
    }

    private static uint AddGrid2RivalXp(uint playerXp, uint rivalXp)
    {
        var totalXp = (ulong)playerXp + rivalXp;
        return totalXp > uint.MaxValue
            ? uint.MaxValue
            : (uint)totalXp;
    }

    private static uint SumGrid2RivalXp(IEnumerable<long> xpAmounts)
    {
        var totalXp = 0UL;
        foreach (var amount in xpAmounts)
        {
            if (amount <= 0)
            {
                continue;
            }

            totalXp += (ulong)amount;
            if (totalXp > uint.MaxValue)
            {
                return uint.MaxValue;
            }
        }

        return (uint)totalXp;
    }

    private static uint MaxGrid2RivalXp(uint first, uint second)
    {
        return first >= second ? first : second;
    }

    private static bool IsGrid2RivalNameAllowed(string displayName)
    {
        var name = displayName.Trim();
        return name.Length is >= 3 and <= 15 && name.All(value => char.IsLetterOrDigit(value) || value == '_');
    }

    private static Grid2RivalCandidate? SelectGrid2SocialRival(
        IReadOnlyCollection<Grid2RivalCandidate> candidates,
        IReadOnlySet<long> selectedProfileIds)
    {
        var eligibleCandidates = candidates
            .Where(value => !selectedProfileIds.Contains(value.Profile.Id))
            .ToArray();

        return OrderGrid2RivalCandidates(eligibleCandidates.Where(value => value.IsKnownFriend))
            .FirstOrDefault()
            ?? OrderGrid2RivalCandidates(eligibleCandidates.Where(value => value.IsRecentOpponent))
                .FirstOrDefault();
    }

    private static Grid2RivalCandidate? SelectGrid2AutomaticRival(
        IReadOnlyCollection<Grid2RivalCandidate> candidates,
        IReadOnlySet<long> selectedProfileIds)
    {
        return OrderGrid2RivalCandidates(candidates.Where(value =>
                value.HasGrid2Signal &&
                !selectedProfileIds.Contains(value.Profile.Id)))
            .FirstOrDefault();
    }

    private static IOrderedEnumerable<Grid2RivalCandidate> OrderGrid2RivalCandidates(
        IEnumerable<Grid2RivalCandidate> candidates)
    {
        return candidates
            .OrderByDescending(value => value.IsRecentOpponent)
            .ThenByDescending(value => value.RecentOpponentAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(value => value.OpponentRaceCount)
            .ThenByDescending(value => value.SessionDataUpdatedAt.HasValue)
            .ThenByDescending(value => value.SessionDataUpdatedAt ?? value.Profile.LastSeenAt)
            .ThenByDescending(value => value.Profile.LastSeenAt)
            .ThenBy(value => value.Profile.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddGrid2Rival(
        List<Grid2RivalSnapshot> rivals,
        HashSet<long> selectedProfileIds,
        Grid2RivalCandidate? candidate,
        int type)
    {
        if (candidate is null || !selectedProfileIds.Add(candidate.Profile.Id))
        {
            return;
        }

        rivals.Add(new Grid2RivalSnapshot(
            ResolveGrid2SteamId(candidate.Profile),
            candidate.Profile.DisplayName.Trim(),
            candidate.Profile.Id,
            type));
    }

    private static string FormatGrid2Rivals(IEnumerable<Grid2RivalSnapshot> rivals)
    {
        var descriptions = rivals
            .Select(value => $"{GetGrid2RivalTypeName(value.Type)}={value.Name}/{value.SteamId} egonet={value.EgonetId} totalXp={value.TotalXpWon} rivalXp={value.RivalXpWon}")
            .ToArray();

        return descriptions.Length == 0
            ? "<none>"
            : string.Join(", ", descriptions);
    }

    private static int GetGrid2RivalTypeSortOrder(int type)
    {
        return type switch
        {
            2 => 0,
            0 => 1,
            1 => 2,
            _ => 3
        };
    }

    private static string FormatGrid2Participant(Grid2RaceParticipant participant)
    {
        return $"{participant.Name}/{participant.SteamId} pos={FormatGrid2Nullable(participant.Position)} status={FormatGrid2Nullable(participant.Status)} result={FormatGrid2Nullable(participant.Result)} vehicle={FormatGrid2Nullable(participant.VehicleId)}";
    }

    private static int CountNonZeroBytes(byte[] bytes)
    {
        return bytes.Count(value => value != 0);
    }

    private static bool IsGrid2RivalSessionDataMeaningful(byte[] sessionData)
    {
        return CountNonZeroBytes(sessionData) > 1;
    }

    private static string FormatGrid2HexPreview(byte[] bytes)
    {
        return bytes.Length == 0
            ? "<empty>"
            : Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 16)));
    }

    private static string FormatGrid2Nullable<T>(T? value)
        where T : struct
    {
        return value.HasValue
            ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "<none>"
            : "<none>";
    }

    private static string GetGrid2RivalTypeName(int type)
    {
        return type switch
        {
            0 => "weekly",
            1 => "custom",
            2 => "social",
            _ => "unknown"
        };
    }

    private static int GetGrid2RivalSlotOrder(int type)
    {
        return type switch
        {
            2 => 0,
            0 => 1,
            1 => 2,
            _ => 3
        };
    }

    private sealed record Grid2RivalCandidate(
        PlayerProfile Profile,
        DateTimeOffset? SessionDataUpdatedAt,
        bool IsKnownFriend,
        DateTimeOffset? RecentOpponentAt,
        int OpponentRaceCount)
    {
        public bool IsRecentOpponent => RecentOpponentAt.HasValue;

        public bool HasGrid2Signal => IsRecentOpponent || SessionDataUpdatedAt.HasValue;
    }

    private async Task<Grid2GlobalEventRecord?> LoadGrid2GlobalEventByStatusAsync(
        string status,
        CancellationToken cancellationToken)
    {
        var events = await dbContext.Grid2GlobalEvents
            .AsNoTracking()
            .Include(value => value.Races)
            .Where(value => value.Status == status)
            .ToListAsync(cancellationToken);

        return events
            .OrderByDescending(value => value.RaceNetEventId)
            .FirstOrDefault();
    }

    private async Task<bool> HasGrid2GlobalRewardClaimAsync(
        long playerProfileId,
        long raceNetEventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Grid2GlobalRewardClaims
            .AsNoTracking()
            .AnyAsync(value =>
                value.PlayerProfileId == playerProfileId &&
                value.RaceNetEventId == raceNetEventId,
                cancellationToken);
    }

    private async Task<bool> WasGrid2PreviousEventAlreadyReturnedAsync(
        RaceNetSessionInfo session,
        Grid2GlobalEventRecord previousEvent,
        CancellationToken cancellationToken)
    {
        var sessionIds = await dbContext.RaceNetSessions
            .AsNoTracking()
            .Where(value => value.PlayerProfileId == session.PlayerProfileId)
            .Select(value => value.SessionId)
            .ToListAsync(cancellationToken);
        if (sessionIds.Count == 0)
        {
            return false;
        }

        return await dbContext.RaceNetCalls
            .AsNoTracking()
            .AnyAsync(value =>
                value.EgoNetFunction == Grid2GlobalPreviousEventFunction &&
                sessionIds.Contains(value.EgoNetSessionId) &&
                value.ResponseStatus >= 200 &&
                value.ResponseStatus < 300 &&
                value.Time >= previousEvent.ExpiresAt,
                cancellationToken);
    }

    private async Task<bool> TryClaimGrid2GlobalRewardAsync(
        RaceNetSessionInfo session,
        Grid2GlobalEventRecord previousEvent,
        string reason,
        CancellationToken cancellationToken)
    {
        dbContext.Grid2GlobalRewardClaims.Add(new Grid2GlobalRewardClaimRecord
        {
            PlayerProfileId = session.PlayerProfileId,
            RaceNetEventId = previousEvent.RaceNetEventId,
            ClaimedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            foreach (var entry in dbContext.ChangeTracker.Entries<Grid2GlobalRewardClaimRecord>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
            }

            logger.LogDebug(
                ex,
                "GRID 2 global reward claim skipped for {Player}: event={EventId}",
                session.DisplayName,
                previousEvent.RaceNetEventId);
            return false;
        }

        logger.LogInformation(
            "GRID 2 global reward claimed for {Player}: event={EventId} reason={Reason}",
            session.DisplayName,
            previousEvent.RaceNetEventId,
            reason);
        return true;
    }

    private async Task<Grid2GlobalEventSnapshot> ToGrid2GlobalEventSnapshotAsync(
        Grid2GlobalEventRecord eventRecord,
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken)
    {
        var races = eventRecord.Races
            .OrderBy(value => value.SortOrder)
            .Select(ToGrid2GlobalRaceSnapshot)
            .ToArray();
        var leaderboardProfileIds = await LoadGrid2GlobalLeaderboardProfileIdsAsync(
            eventRecord,
            session,
            cancellationToken);
        var leaderboardEntries = await LoadGrid2GlobalLeaderboardEntriesAsync(
            eventRecord.RaceNetEventId,
            races.Select(value => value.RaceNetRaceId).ToArray(),
            leaderboardProfileIds,
            cancellationToken);

        return new Grid2GlobalEventSnapshot(
            eventRecord.RaceNetEventId,
            eventRecord.StartsAt,
            eventRecord.ExpiresAt,
            races,
            leaderboardEntries);
    }

    private async Task<IReadOnlyCollection<long>> LoadGrid2GlobalLeaderboardProfileIdsAsync(
        Grid2GlobalEventRecord eventRecord,
        RaceNetSessionInfo? session,
        CancellationToken cancellationToken)
    {
        if (session is null || session.PlayerProfileId <= 0)
        {
            return [];
        }

        var profileIds = new HashSet<long> { session.PlayerProfileId };
        var rivals = await LoadGrid2RivalAssignmentsAsync(
            session,
            eventRecord.StartsAt,
            eventRecord.ExpiresAt,
            createIfMissing: eventRecord.Status is Grid2GlobalEventStatusActive or Grid2GlobalEventStatusPrevious,
            cancellationToken);
        foreach (var rival in rivals)
        {
            if (rival.EgonetId > 0)
            {
                profileIds.Add(rival.EgonetId);
            }
        }

        return profileIds;
    }

    private async Task<IReadOnlyList<Grid2GlobalLeaderboardEntry>> LoadGrid2GlobalLeaderboardEntriesAsync(
        long raceNetEventId,
        IReadOnlyCollection<long> raceNetRaceIds,
        IReadOnlyCollection<long> playerProfileIds,
        CancellationToken cancellationToken)
    {
        var raceIds = raceNetRaceIds.Distinct().ToArray();
        var profileIds = playerProfileIds
            .Where(value => value > 0)
            .Distinct()
            .ToArray();
        if (raceIds.Length == 0 || profileIds.Length == 0)
        {
            return [];
        }

        var scores = await dbContext.Grid2GlobalScores
            .AsNoTracking()
            .Include(value => value.PlayerProfile)
            .Where(value =>
                value.RaceNetEventId == raceNetEventId &&
                raceIds.Contains(value.RaceNetRaceId) &&
                profileIds.Contains(value.PlayerProfileId))
            .ToListAsync(cancellationToken);

        return scores
            .Where(value => value.PlayerProfile is not null)
            .Select(value => new Grid2GlobalLeaderboardEntry(
                value.RaceNetEventId,
                value.RaceNetRaceId,
                new RaceNetPrincipal(
                    ParseSteamExternalId(value.PlayerProfile!.ExternalId),
                    value.PlayerProfile.DisplayName),
                value.PlayerProfileId,
                value.Score,
                value.VehicleId,
                value.SubmittedAt))
            .ToArray();
    }

    private async Task EnsureGrid2GlobalEventRotationAsync(CancellationToken cancellationToken)
    {
        await Grid2GlobalEventLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var activeStartsAt = GetGrid2CurrentGlobalEventStartsAt(now);
            var activeExpiresAt = activeStartsAt.Add(Grid2GlobalEventLifetime);
            var previousStartsAt = activeStartsAt.Subtract(Grid2GlobalEventLifetime);
            var events = await dbContext.Grid2GlobalEvents
                .Include(value => value.Races)
                .ToListAsync(cancellationToken);

            if (events.Count == 0)
            {
                SeedInitialGrid2GlobalEvents(activeStartsAt);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var activeEvent = events
                .Where(value => value.Status == Grid2GlobalEventStatusActive)
                .OrderByDescending(value => value.RaceNetEventId)
                .FirstOrDefault();

            if (activeEvent is not null &&
                activeEvent.StartsAt == activeStartsAt &&
                activeEvent.ExpiresAt == activeExpiresAt)
            {
                return;
            }

            Grid2GlobalEventRecord? previousEvent = null;
            if (activeEvent is not null)
            {
                if (activeEvent.StartsAt == previousStartsAt && activeEvent.ExpiresAt == activeStartsAt)
                {
                    activeEvent.Status = Grid2GlobalEventStatusPrevious;
                    activeEvent.ClosedAt = activeEvent.ExpiresAt;
                    previousEvent = activeEvent;
                }
                else
                {
                    activeEvent.Status = Grid2GlobalEventStatusArchived;
                    activeEvent.ClosedAt ??= now;
                }
            }

            previousEvent ??= events
                .Where(value =>
                    value.Status == Grid2GlobalEventStatusPrevious &&
                    value.StartsAt == previousStartsAt &&
                    value.ExpiresAt == activeStartsAt)
                .OrderByDescending(value => value.RaceNetEventId)
                .FirstOrDefault();

            if (previousEvent is null)
            {
                var previousEventId = GetNextGrid2RaceNetEventId(events);
                previousEvent = CreateGrid2GlobalEvent(
                    previousEventId,
                    Grid2GlobalEventStatusPrevious,
                    previousStartsAt,
                    activeStartsAt,
                    CreateActiveGrid2RaceSeeds(previousEventId));
                dbContext.Grid2GlobalEvents.Add(previousEvent);
                events.Add(previousEvent);
            }

            ArchivePreviousGrid2GlobalEvents(events, previousEvent, now);

            var nextEventId = Math.Max(GetNextGrid2RaceNetEventId(events), previousEvent.RaceNetEventId + 1);
            dbContext.Grid2GlobalEvents.Add(CreateGrid2GlobalEvent(
                nextEventId,
                Grid2GlobalEventStatusActive,
                activeStartsAt,
                activeExpiresAt,
                CreateActiveGrid2RaceSeeds(nextEventId)));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Grid2GlobalEventLock.Release();
        }
    }

    private void SeedInitialGrid2GlobalEvents(DateTimeOffset activeStartsAt)
    {
        dbContext.Grid2GlobalEvents.Add(CreateGrid2GlobalEvent(
            697,
            Grid2GlobalEventStatusPrevious,
            activeStartsAt.Subtract(Grid2GlobalEventLifetime),
            activeStartsAt,
            CreateInitialPreviousGrid2RaceSeeds()));
        dbContext.Grid2GlobalEvents.Add(CreateGrid2GlobalEvent(
            698,
            Grid2GlobalEventStatusActive,
            activeStartsAt,
            activeStartsAt.Add(Grid2GlobalEventLifetime),
            CreateActiveGrid2RaceSeeds(698)));
    }

    private static void ArchivePreviousGrid2GlobalEvents(
        IEnumerable<Grid2GlobalEventRecord> events,
        Grid2GlobalEventRecord? newPreviousEvent,
        DateTimeOffset now)
    {
        foreach (var eventRecord in events.Where(value => value.Status == Grid2GlobalEventStatusPrevious))
        {
            if (newPreviousEvent is not null && eventRecord.Id == newPreviousEvent.Id)
            {
                continue;
            }

            eventRecord.Status = Grid2GlobalEventStatusArchived;
            eventRecord.ClosedAt ??= now;
        }
    }

    private static long GetNextGrid2RaceNetEventId(IEnumerable<Grid2GlobalEventRecord> events)
    {
        return Math.Max(698, events.Select(value => value.RaceNetEventId).DefaultIfEmpty(697).Max() + 1);
    }

    private static DateTimeOffset GetGrid2CurrentGlobalEventStartsAt(DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var startsAt = new DateTimeOffset(
            utcNow.Year,
            utcNow.Month,
            utcNow.Day,
            Grid2GlobalEventResetHourUtc,
            0,
            0,
            TimeSpan.Zero);
        var daysSinceReset = ((int)utcNow.DayOfWeek - (int)Grid2GlobalEventResetDay + 7) % 7;
        startsAt = startsAt.AddDays(-daysSinceReset);

        return utcNow < startsAt
            ? startsAt.Subtract(Grid2GlobalEventLifetime)
            : startsAt;
    }

    private static Grid2GlobalEventRecord CreateGrid2GlobalEvent(
        long raceNetEventId,
        string status,
        DateTimeOffset startsAt,
        DateTimeOffset expiresAt,
        IReadOnlyList<Grid2GlobalRaceSeed> races)
    {
        var eventRecord = new Grid2GlobalEventRecord
        {
            RaceNetEventId = raceNetEventId,
            Status = status,
            StartsAt = startsAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            ClosedAt = status == Grid2GlobalEventStatusActive ? null : expiresAt
        };

        foreach (var race in races)
        {
            eventRecord.Races.Add(new Grid2GlobalRaceRecord
            {
                RaceNetRaceId = race.RaceNetRaceId,
                SortOrder = race.SortOrder,
                HigherIsBetter = race.HigherIsBetter,
                LocationId = race.LocationId,
                TrackModelId = race.TrackModelId,
                TrackModelDlcId = race.TrackModelDlcId,
                ConditionsId = race.ConditionsId,
                RaceTypeId = race.RaceTypeId,
                RaceDuration = race.RaceDuration,
                VehicleTierId = race.VehicleTierId,
                VehicleClassId = race.VehicleClassId,
                VehicleId = race.VehicleId,
                VehicleDlcId = race.VehicleDlcId,
                SpecialRace = race.SpecialRace,
                GhostSlotId = race.GhostSlotId
            });
        }

        return eventRecord;
    }

    private static Grid2GlobalRaceSnapshot ToGrid2GlobalRaceSnapshot(Grid2GlobalRaceRecord race)
    {
        return new Grid2GlobalRaceSnapshot(
            race.RaceNetRaceId,
            race.HigherIsBetter,
            race.LocationId,
            race.TrackModelId,
            race.TrackModelDlcId,
            race.ConditionsId,
            race.RaceTypeId,
            race.RaceDuration,
            race.VehicleTierId,
            race.VehicleClassId,
            race.VehicleId,
            race.VehicleDlcId,
            race.SpecialRace,
            race.GhostSlotId,
            race.SortOrder);
    }

    private static IReadOnlyList<Grid2GlobalRaceSeed> CreateActiveGrid2RaceSeeds(long raceNetEventId)
    {
        var raceId = 6_282 + Math.Max(0, raceNetEventId - 698) * 9;
        return
        [
            new(raceId + 0, false, 31, 355, 0, 554, 24, 7, 3, -1, -1, 0, true, 1, 1),
            new(raceId + 1, true, 33, 408, 0, 361, 7, 79_102, 3, -1, -1, 0, false, 2, 2),
            new(raceId + 2, true, 27, 333, 0, 367, 17, 6, 5, -1, -1, 0, false, 3, 3),
            new(raceId + 3, true, 26, 326, 0, 517, 22, 7, 5, -1, -1, 0, false, 4, 4),
            new(raceId + 4, false, 32, 362, 0, 409, 24, 4, 3, -1, -1, 0, false, 5, 5),
            new(raceId + 5, true, 32, 359, 0, 509, 22, 7, 2, -1, -1, 0, false, 6, 6),
            new(raceId + 6, true, 31, 357, 0, 327, 17, 6, 2, -1, -1, 0, false, 7, 7),
            new(raceId + 7, false, 31, 357, 0, 327, 24, 5, 4, -1, -1, 0, false, 8, 8),
            new(raceId + 8, true, 33, 408, 0, 361, 7, 83_000, 4, -1, -1, 0, false, 9, 9)
        ];
    }

    private static IReadOnlyList<Grid2GlobalRaceSeed> CreateInitialPreviousGrid2RaceSeeds()
    {
        return
        [
            new(6_264, true, 25, 322, 0, 319, 22, 6, 4, -1, -1, 0, false, -1, 1),
            new(6_265, false, 31, 355, 0, 554, 24, 7, 3, -1, -1, 0, false, -1, 2),
            new(6_266, true, 32, 359, 0, 509, 22, 7, 2, -1, -1, 0, false, -1, 3)
        ];
    }

    private async Task EnsureGrid2GlobalTablesAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2GlobalEvents" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2GlobalEvents" PRIMARY KEY AUTOINCREMENT,
                    "RaceNetEventId" INTEGER NOT NULL,
                    "Status" TEXT NOT NULL,
                    "StartsAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "ClosedAt" TEXT NULL
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2GlobalEvents_RaceNetEventId"
                ON "Grid2GlobalEvents" ("RaceNetEventId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2GlobalEvents_Status"
                ON "Grid2GlobalEvents" ("Status");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2GlobalRaces" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2GlobalRaces" PRIMARY KEY AUTOINCREMENT,
                    "Grid2GlobalEventRecordId" INTEGER NOT NULL,
                    "RaceNetRaceId" INTEGER NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    "HigherIsBetter" INTEGER NOT NULL,
                    "LocationId" INTEGER NOT NULL,
                    "TrackModelId" INTEGER NOT NULL,
                    "TrackModelDlcId" INTEGER NOT NULL,
                    "ConditionsId" INTEGER NOT NULL,
                    "RaceTypeId" INTEGER NOT NULL,
                    "RaceDuration" INTEGER NOT NULL,
                    "VehicleTierId" INTEGER NOT NULL,
                    "VehicleClassId" INTEGER NOT NULL,
                    "VehicleId" INTEGER NOT NULL,
                    "VehicleDlcId" INTEGER NOT NULL,
                    "SpecialRace" INTEGER NOT NULL,
                    "GhostSlotId" INTEGER NOT NULL,
                    CONSTRAINT "FK_Grid2GlobalRaces_Grid2GlobalEvents_Grid2GlobalEventRecordId" FOREIGN KEY ("Grid2GlobalEventRecordId") REFERENCES "Grid2GlobalEvents" ("Id") ON DELETE CASCADE
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2GlobalRaces_Grid2GlobalEventRecordId_RaceNetRaceId"
                ON "Grid2GlobalRaces" ("Grid2GlobalEventRecordId", "RaceNetRaceId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2GlobalScores" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2GlobalScores" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "RaceNetEventId" INTEGER NOT NULL,
                    "RaceNetRaceId" INTEGER NOT NULL,
                    "GameId" INTEGER NOT NULL,
                    "Score" INTEGER NOT NULL,
                    "VehicleId" INTEGER NOT NULL,
                    "SubmittedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2GlobalScores_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2GlobalScores_RaceNetEventId_RaceNetRaceId"
                ON "Grid2GlobalScores" ("RaceNetEventId", "RaceNetRaceId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2GlobalScores_PlayerProfileId_RaceNetEventId_RaceNetRaceId"
                ON "Grid2GlobalScores" ("PlayerProfileId", "RaceNetEventId", "RaceNetRaceId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2GlobalRewardClaims" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2GlobalRewardClaims" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "RaceNetEventId" INTEGER NOT NULL,
                    "ClaimedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2GlobalRewardClaims_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2GlobalRewardClaims_PlayerProfileId_RaceNetEventId"
                ON "Grid2GlobalRewardClaims" ("PlayerProfileId", "RaceNetEventId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2GlobalRewardClaims_RaceNetEventId"
                ON "Grid2GlobalRewardClaims" ("RaceNetEventId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2ProfileXpSnapshots" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2ProfileXpSnapshots" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "SaveGameId" INTEGER NULL,
                    "XpTotal" INTEGER NOT NULL,
                    "XpLevel" INTEGER NOT NULL,
                    "CapturedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2ProfileXpSnapshots_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2ProfileXpSnapshots_PlayerProfileId_CapturedAt"
                ON "Grid2ProfileXpSnapshots" ("PlayerProfileId", "CapturedAt");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2WeeklyXpDeltas" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2WeeklyXpDeltas" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "WeekStartsAt" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Source" TEXT NOT NULL,
                    "ReferenceKey" TEXT NOT NULL,
                    "EarnedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2WeeklyXpDeltas_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2WeeklyXpDeltas_PlayerProfileId_WeekStartsAt"
                ON "Grid2WeeklyXpDeltas" ("PlayerProfileId", "WeekStartsAt");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2WeeklyXpDeltas_PlayerProfileId_Source_ReferenceKey"
                ON "Grid2WeeklyXpDeltas" ("PlayerProfileId", "Source", "ReferenceKey");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2RivalSessionData" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2RivalSessionData" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "SessionData" BLOB NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2RivalSessionData_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2RivalSessionData_PlayerProfileId"
                ON "Grid2RivalSessionData" ("PlayerProfileId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2RivalAssignments" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2RivalAssignments" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "RivalPlayerProfileId" INTEGER NOT NULL,
                    "Type" INTEGER NOT NULL,
                    "StartsAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2RivalAssignments_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Grid2RivalAssignments_PlayerProfiles_RivalPlayerProfileId" FOREIGN KEY ("RivalPlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type"
                ON "Grid2RivalAssignments" ("PlayerProfileId", "StartsAt", "Type");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2RivalAssignments_ExpiresAt"
                ON "Grid2RivalAssignments" ("ExpiresAt");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2RivalOpponents" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2RivalOpponents" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "OpponentPlayerProfileId" INTEGER NOT NULL,
                    "LastRaceNetEventId" INTEGER NULL,
                    "LastSaveGameId" INTEGER NULL,
                    "PlayerPosition" INTEGER NULL,
                    "OpponentPosition" INTEGER NULL,
                    "PlayerStatus" INTEGER NULL,
                    "OpponentStatus" INTEGER NULL,
                    "PlayerResult" INTEGER NULL,
                    "OpponentResult" INTEGER NULL,
                    "OpponentVehicleId" INTEGER NULL,
                    "TimesMet" INTEGER NOT NULL,
                    "FirstSeenAt" TEXT NOT NULL,
                    "LastSeenAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2RivalOpponents_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Grid2RivalOpponents_PlayerProfiles_OpponentPlayerProfileId" FOREIGN KEY ("OpponentPlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2RivalOpponents_PlayerProfileId_OpponentPlayerProfileId"
                ON "Grid2RivalOpponents" ("PlayerProfileId", "OpponentPlayerProfileId");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2RivalOpponents_LastSeenAt"
                ON "Grid2RivalOpponents" ("LastSeenAt");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "Grid2RivalAssignments" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Grid2RivalAssignments" PRIMARY KEY AUTOINCREMENT,
                    "PlayerProfileId" INTEGER NOT NULL,
                    "RivalPlayerProfileId" INTEGER NOT NULL,
                    "Type" INTEGER NOT NULL,
                    "StartsAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Grid2RivalAssignments_PlayerProfiles_PlayerProfileId" FOREIGN KEY ("PlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Grid2RivalAssignments_PlayerProfiles_RivalPlayerProfileId" FOREIGN KEY ("RivalPlayerProfileId") REFERENCES "PlayerProfiles" ("Id") ON DELETE RESTRICT
                );
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type"
                ON "Grid2RivalAssignments" ("PlayerProfileId", "StartsAt", "Type");
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Grid2RivalAssignments_ExpiresAt"
                ON "Grid2RivalAssignments" ("ExpiresAt");
                """,
                cancellationToken);
            return;
        }

        if (dbContext.Database.IsSqlServer())
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2GlobalEvents]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2GlobalEvents] (
                        [Id] bigint NOT NULL IDENTITY,
                        [RaceNetEventId] bigint NOT NULL,
                        [Status] nvarchar(32) NOT NULL,
                        [StartsAt] datetimeoffset NOT NULL,
                        [ExpiresAt] datetimeoffset NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [ClosedAt] datetimeoffset NULL,
                        CONSTRAINT [PK_Grid2GlobalEvents] PRIMARY KEY ([Id])
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalEvents_RaceNetEventId' AND object_id = OBJECT_ID(N'[Grid2GlobalEvents]'))
                    CREATE UNIQUE INDEX [IX_Grid2GlobalEvents_RaceNetEventId] ON [Grid2GlobalEvents] ([RaceNetEventId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalEvents_Status' AND object_id = OBJECT_ID(N'[Grid2GlobalEvents]'))
                    CREATE INDEX [IX_Grid2GlobalEvents_Status] ON [Grid2GlobalEvents] ([Status]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2GlobalRaces]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2GlobalRaces] (
                        [Id] bigint NOT NULL IDENTITY,
                        [Grid2GlobalEventRecordId] bigint NOT NULL,
                        [RaceNetRaceId] bigint NOT NULL,
                        [SortOrder] int NOT NULL,
                        [HigherIsBetter] bit NOT NULL,
                        [LocationId] int NOT NULL,
                        [TrackModelId] int NOT NULL,
                        [TrackModelDlcId] int NOT NULL,
                        [ConditionsId] int NOT NULL,
                        [RaceTypeId] int NOT NULL,
                        [RaceDuration] bigint NOT NULL,
                        [VehicleTierId] int NOT NULL,
                        [VehicleClassId] int NOT NULL,
                        [VehicleId] int NOT NULL,
                        [VehicleDlcId] int NOT NULL,
                        [SpecialRace] bit NOT NULL,
                        [GhostSlotId] bigint NOT NULL,
                        CONSTRAINT [PK_Grid2GlobalRaces] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2GlobalRaces_Grid2GlobalEvents_Grid2GlobalEventRecordId] FOREIGN KEY ([Grid2GlobalEventRecordId]) REFERENCES [Grid2GlobalEvents] ([Id]) ON DELETE CASCADE
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalRaces_Grid2GlobalEventRecordId_RaceNetRaceId' AND object_id = OBJECT_ID(N'[Grid2GlobalRaces]'))
                    CREATE UNIQUE INDEX [IX_Grid2GlobalRaces_Grid2GlobalEventRecordId_RaceNetRaceId] ON [Grid2GlobalRaces] ([Grid2GlobalEventRecordId], [RaceNetRaceId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2GlobalScores]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2GlobalScores] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [RaceNetEventId] bigint NOT NULL,
                        [RaceNetRaceId] bigint NOT NULL,
                        [GameId] smallint NOT NULL,
                        [Score] bigint NOT NULL,
                        [VehicleId] int NOT NULL,
                        [SubmittedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2GlobalScores] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2GlobalScores_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalScores_RaceNetEventId_RaceNetRaceId' AND object_id = OBJECT_ID(N'[Grid2GlobalScores]'))
                    CREATE INDEX [IX_Grid2GlobalScores_RaceNetEventId_RaceNetRaceId] ON [Grid2GlobalScores] ([RaceNetEventId], [RaceNetRaceId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalScores_PlayerProfileId_RaceNetEventId_RaceNetRaceId' AND object_id = OBJECT_ID(N'[Grid2GlobalScores]'))
                    CREATE INDEX [IX_Grid2GlobalScores_PlayerProfileId_RaceNetEventId_RaceNetRaceId] ON [Grid2GlobalScores] ([PlayerProfileId], [RaceNetEventId], [RaceNetRaceId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2GlobalRewardClaims]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2GlobalRewardClaims] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [RaceNetEventId] bigint NOT NULL,
                        [ClaimedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2GlobalRewardClaims] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2GlobalRewardClaims_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalRewardClaims_PlayerProfileId_RaceNetEventId' AND object_id = OBJECT_ID(N'[Grid2GlobalRewardClaims]'))
                    CREATE UNIQUE INDEX [IX_Grid2GlobalRewardClaims_PlayerProfileId_RaceNetEventId] ON [Grid2GlobalRewardClaims] ([PlayerProfileId], [RaceNetEventId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2GlobalRewardClaims_RaceNetEventId' AND object_id = OBJECT_ID(N'[Grid2GlobalRewardClaims]'))
                    CREATE INDEX [IX_Grid2GlobalRewardClaims_RaceNetEventId] ON [Grid2GlobalRewardClaims] ([RaceNetEventId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2ProfileXpSnapshots]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2ProfileXpSnapshots] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [SaveGameId] bigint NULL,
                        [XpTotal] int NOT NULL,
                        [XpLevel] int NOT NULL,
                        [CapturedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2ProfileXpSnapshots] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2ProfileXpSnapshots_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2ProfileXpSnapshots_PlayerProfileId_CapturedAt' AND object_id = OBJECT_ID(N'[Grid2ProfileXpSnapshots]'))
                    CREATE INDEX [IX_Grid2ProfileXpSnapshots_PlayerProfileId_CapturedAt] ON [Grid2ProfileXpSnapshots] ([PlayerProfileId], [CapturedAt]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2WeeklyXpDeltas]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2WeeklyXpDeltas] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [WeekStartsAt] datetimeoffset NOT NULL,
                        [Amount] bigint NOT NULL,
                        [Source] nvarchar(32) NOT NULL,
                        [ReferenceKey] nvarchar(128) NOT NULL,
                        [EarnedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2WeeklyXpDeltas] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2WeeklyXpDeltas_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2WeeklyXpDeltas_PlayerProfileId_WeekStartsAt' AND object_id = OBJECT_ID(N'[Grid2WeeklyXpDeltas]'))
                    CREATE INDEX [IX_Grid2WeeklyXpDeltas_PlayerProfileId_WeekStartsAt] ON [Grid2WeeklyXpDeltas] ([PlayerProfileId], [WeekStartsAt]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2WeeklyXpDeltas_PlayerProfileId_Source_ReferenceKey' AND object_id = OBJECT_ID(N'[Grid2WeeklyXpDeltas]'))
                    CREATE UNIQUE INDEX [IX_Grid2WeeklyXpDeltas_PlayerProfileId_Source_ReferenceKey] ON [Grid2WeeklyXpDeltas] ([PlayerProfileId], [Source], [ReferenceKey]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2RivalSessionData]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2RivalSessionData] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [SessionData] varbinary(max) NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2RivalSessionData] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2RivalSessionData_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalSessionData_PlayerProfileId' AND object_id = OBJECT_ID(N'[Grid2RivalSessionData]'))
                    CREATE UNIQUE INDEX [IX_Grid2RivalSessionData_PlayerProfileId] ON [Grid2RivalSessionData] ([PlayerProfileId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2RivalAssignments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2RivalAssignments] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [RivalPlayerProfileId] bigint NOT NULL,
                        [Type] int NOT NULL,
                        [StartsAt] datetimeoffset NOT NULL,
                        [ExpiresAt] datetimeoffset NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2RivalAssignments] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2RivalAssignments_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Grid2RivalAssignments_PlayerProfiles_RivalPlayerProfileId] FOREIGN KEY ([RivalPlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type' AND object_id = OBJECT_ID(N'[Grid2RivalAssignments]'))
                    CREATE UNIQUE INDEX [IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type] ON [Grid2RivalAssignments] ([PlayerProfileId], [StartsAt], [Type]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalAssignments_ExpiresAt' AND object_id = OBJECT_ID(N'[Grid2RivalAssignments]'))
                    CREATE INDEX [IX_Grid2RivalAssignments_ExpiresAt] ON [Grid2RivalAssignments] ([ExpiresAt]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2RivalOpponents]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2RivalOpponents] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [OpponentPlayerProfileId] bigint NOT NULL,
                        [LastRaceNetEventId] bigint NULL,
                        [LastSaveGameId] bigint NULL,
                        [PlayerPosition] int NULL,
                        [OpponentPosition] int NULL,
                        [PlayerStatus] int NULL,
                        [OpponentStatus] int NULL,
                        [PlayerResult] bigint NULL,
                        [OpponentResult] bigint NULL,
                        [OpponentVehicleId] int NULL,
                        [TimesMet] int NOT NULL,
                        [FirstSeenAt] datetimeoffset NOT NULL,
                        [LastSeenAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2RivalOpponents] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2RivalOpponents_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Grid2RivalOpponents_PlayerProfiles_OpponentPlayerProfileId] FOREIGN KEY ([OpponentPlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalOpponents_PlayerProfileId_OpponentPlayerProfileId' AND object_id = OBJECT_ID(N'[Grid2RivalOpponents]'))
                    CREATE UNIQUE INDEX [IX_Grid2RivalOpponents_PlayerProfileId_OpponentPlayerProfileId] ON [Grid2RivalOpponents] ([PlayerProfileId], [OpponentPlayerProfileId]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalOpponents_LastSeenAt' AND object_id = OBJECT_ID(N'[Grid2RivalOpponents]'))
                    CREATE INDEX [IX_Grid2RivalOpponents_LastSeenAt] ON [Grid2RivalOpponents] ([LastSeenAt]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'[Grid2RivalAssignments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [Grid2RivalAssignments] (
                        [Id] bigint NOT NULL IDENTITY,
                        [PlayerProfileId] bigint NOT NULL,
                        [RivalPlayerProfileId] bigint NOT NULL,
                        [Type] int NOT NULL,
                        [StartsAt] datetimeoffset NOT NULL,
                        [ExpiresAt] datetimeoffset NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_Grid2RivalAssignments] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Grid2RivalAssignments_PlayerProfiles_PlayerProfileId] FOREIGN KEY ([PlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Grid2RivalAssignments_PlayerProfiles_RivalPlayerProfileId] FOREIGN KEY ([RivalPlayerProfileId]) REFERENCES [PlayerProfiles] ([Id]) ON DELETE NO ACTION
                    );
                END
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type' AND object_id = OBJECT_ID(N'[Grid2RivalAssignments]'))
                    CREATE UNIQUE INDEX [IX_Grid2RivalAssignments_PlayerProfileId_StartsAt_Type] ON [Grid2RivalAssignments] ([PlayerProfileId], [StartsAt], [Type]);
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Grid2RivalAssignments_ExpiresAt' AND object_id = OBJECT_ID(N'[Grid2RivalAssignments]'))
                    CREATE INDEX [IX_Grid2RivalAssignments_ExpiresAt] ON [Grid2RivalAssignments] ([ExpiresAt]);
                """,
                cancellationToken);
        }
    }

    private sealed record Grid2GlobalRaceSeed(
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

    private async Task ReconcileOpenChallengesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeChallengeCutoff = now.Subtract(ChallengeLifetime);
        var openChallenges = await dbContext.Challenges
            .Include(value => value.Results)
            .Where(value => value.Status == ChallengeStatusOpen)
            .ToListAsync(cancellationToken);
        var challengesToClose = openChallenges
            .Where(value => value.CreatedAt <= activeChallengeCutoff || value.Results.Count > 0)
            .ToArray();

        if (challengesToClose.Length == 0)
        {
            return;
        }

        var completedCount = 0;
        var expiredCount = 0;
        foreach (var challenge in challengesToClose)
        {
            var latestResult = challenge.Results
                .OrderByDescending(value => value.SubmittedAt)
                .FirstOrDefault();

            if (latestResult is not null)
            {
                challenge.Status = ChallengeStatusCompleted;
                challenge.CompletedAt = latestResult.SubmittedAt;
                completedCount++;
                continue;
            }

            challenge.Status = ChallengeStatusExpired;
            challenge.CompletedAt = GetChallengeExpiresAt(challenge);
            expiredCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Reconciled open challenges: completed={CompletedCount} expired={ExpiredCount}",
            completedCount,
            expiredCount);
    }

    private async Task<PlayerProfile> FindOrCreateProfileAsync(
        string? loginName,
        string remoteAddress,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PlayerProfile? profile = null;

        if (!string.IsNullOrWhiteSpace(loginName))
        {
            profile = await dbContext.PlayerProfiles
                .FirstOrDefaultAsync(
                    value => value.DisplayName == loginName || value.ExternalId == BuildNameExternalId(loginName),
                    cancellationToken);
        }

        if (profile is null)
        {
            var externalId = string.IsNullOrWhiteSpace(loginName)
                ? $"remote:{remoteAddress}"
                : BuildNameExternalId(loginName);
            profile = await dbContext.PlayerProfiles
                .FirstOrDefaultAsync(value => value.ExternalId == externalId, cancellationToken);

            if (profile is null)
            {
                profile = new PlayerProfile
                {
                    ExternalId = externalId,
                    DisplayName = string.IsNullOrWhiteSpace(loginName) ? $"Player {remoteAddress}" : loginName,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };
                dbContext.PlayerProfiles.Add(profile);
            }
        }

        profile.DisplayName = string.IsNullOrWhiteSpace(loginName) ? profile.DisplayName : loginName;
        profile.LastSeenAt = now;
        return profile;
    }

    private async Task<PlayerProfile> FindOrCreateSteamProfileAsync(
        RaceNetPrincipal principal,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var externalId = BuildSteamExternalId(principal.SteamId);
        var profile = await dbContext.PlayerProfiles
            .FirstOrDefaultAsync(value => value.ExternalId == externalId, cancellationToken);

        profile ??= await dbContext.PlayerProfiles
            .FirstOrDefaultAsync(value => value.DisplayName == principal.Name, cancellationToken);

        if (profile is null)
        {
            profile = new PlayerProfile
            {
                ExternalId = externalId,
                DisplayName = principal.Name,
                FirstSeenAt = now,
                LastSeenAt = now
            };
            dbContext.PlayerProfiles.Add(profile);
        }
        else
        {
            profile.ExternalId = externalId;
            profile.DisplayName = principal.Name;
            profile.LastSeenAt = now;
        }

        return profile;
    }

    private async Task<long> GetNextChallengeIdAsync(CancellationToken cancellationToken)
    {
        var current = await GetHighestChallengeIdAsync(cancellationToken);
        return Math.Max(current, 10_000) + 1;
    }

    private static RaceNetSessionInfo ToSessionInfo(RaceNetSession session)
    {
        var profile = session.PlayerProfile ?? throw new InvalidOperationException("Session profile is missing.");
        return new RaceNetSessionInfo(
            session.SessionId,
            profile.Id,
            profile.ExternalId,
            profile.DisplayName);
    }

    private static ChallengeTallyEntry? ToTallyEntry(
        ChallengeRecord challenge,
        long playerProfileId)
    {
        if (challenge.TargetPlayerProfileId == playerProfileId)
        {
            var result = GetLatestPlayerResult(challenge, playerProfileId);
            if (result is null)
            {
                return null;
            }

            return new ChallengeTallyEntry(
                challenge.IssuerPlayerProfileId,
                challenge.IssuerPlayerProfile,
                challenge,
                result,
                result.BeatChallenge ? 1 : -1);
        }

        if (challenge.IssuerPlayerProfileId == playerProfileId &&
            challenge.TargetPlayerProfileId is long targetPlayerProfileId)
        {
            var result = GetLatestPlayerResult(challenge, targetPlayerProfileId);
            if (result is null)
            {
                return null;
            }

            return new ChallengeTallyEntry(
                targetPlayerProfileId,
                challenge.TargetPlayerProfile,
                challenge,
                result,
                result.BeatChallenge ? -1 : 1);
        }

        return null;
    }

    private static ChallengeResultRecord? GetLatestPlayerResult(
        ChallengeRecord challenge,
        long playerProfileId)
    {
        return challenge.Results
            .Where(value => value.PlayerProfileId == playerProfileId)
            .OrderByDescending(value => value.SubmittedAt)
            .FirstOrDefault();
    }

    private static RaceNetFriendChallenge ToFriendChallenge(
        ChallengeRecord challenge,
        long currentPlayerProfileId,
        long tally)
    {
        var issuer = challenge.IssuerPlayerProfile;
        var bestResult = challenge.ResultToBeat > 0 ? challenge.ResultToBeat : challenge.Score;
        var playerBest = challenge.Results
            .Where(value => value.PlayerProfileId == currentPlayerProfileId)
            .OrderByDescending(value => value.SubmittedAt)
            .Select(value => value.Score)
            .FirstOrDefault();

        return new RaceNetFriendChallenge(
            EgonetId: issuer?.Id ?? 0,
            SteamId: ReadSteamId(issuer),
            Name: issuer?.DisplayName ?? "RaceNet Friend",
            Presence: 1,
            ChallengeId: challenge.EgoNetChallengeId,
            RaceEventId: challenge.CareerEventId,
            VehicleId: challenge.VehicleId,
            BestResult: bestResult,
            YourBestResult: playerBest,
            Tally: tally,
            ExpiresAt: GetChallengeExpiresAt(challenge),
            GhostSlotId: checked((int)Math.Clamp(challenge.GhostSlotId, 0, int.MaxValue)));
    }

    private static RaceNetFriendChallenge ToTallyFriend(ChallengeTallyEntry entry, long tally)
    {
        var challenge = entry.Challenge;
        var profile = entry.FriendPlayerProfile;
        return new RaceNetFriendChallenge(
            EgonetId: profile?.Id ?? entry.FriendPlayerProfileId,
            SteamId: ReadSteamId(profile),
            Name: profile?.DisplayName ?? "RaceNet Friend",
            Presence: 1,
            ChallengeId: 0,
            RaceEventId: challenge.CareerEventId,
            VehicleId: challenge.VehicleId,
            BestResult: 0,
            YourBestResult: 0,
            Tally: tally,
            ExpiresAt: DateTimeOffset.UtcNow.Add(ChallengeLifetime),
            GhostSlotId: 0);
    }

    private sealed record ChallengeTallyEntry(
        long FriendPlayerProfileId,
        PlayerProfile? FriendPlayerProfile,
        ChallengeRecord Challenge,
        ChallengeResultRecord Result,
        long TallyDelta);

    private static RaceNetIssuedChallenge ToIssuedChallenge(
        ChallengeRecord challenge,
        PlayerProfile targetProfile,
        RaceNetPrincipal? target)
    {
        target ??= new RaceNetPrincipal(ParseSteamExternalId(targetProfile.ExternalId), targetProfile.DisplayName);

        return new RaceNetIssuedChallenge(
            challenge.EgoNetChallengeId,
            targetProfile.Id,
            target,
            new RaceNetChallengeDraft(
                challenge.CareerEventId,
                challenge.GridPosition,
                challenge.Difficulty,
                challenge.ResultToBeat,
                challenge.TimeBased,
                challenge.VehicleId,
                challenge.LiveryId,
                challenge.Strength,
                challenge.Power,
                challenge.Handling),
            GetChallengeExpiresAt(challenge),
            challenge.GhostSlotId);
    }

    private static DateTimeOffset GetChallengeExpiresAt(ChallengeRecord challenge)
    {
        return challenge.CreatedAt.Add(ChallengeLifetime);
    }

    private static bool IsExpired(ChallengeRecord challenge, DateTimeOffset now)
    {
        return GetChallengeExpiresAt(challenge) <= now;
    }

    private static bool IsDominated(bool timeBased, long result, long target)
    {
        return timeBased
            ? result > 0 && result <= target
            : result >= target;
    }

    private static string ReadSteamId(PlayerProfile? profile)
    {
        return profile is null
            ? "0"
            : ParseSteamExternalId(profile.ExternalId).ToString(CultureInfo.InvariantCulture);
    }

    private static ulong ParseSteamExternalId(string externalId)
    {
        const string prefix = "steam:";
        return externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(externalId[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId)
            ? steamId
            : 0;
    }


    private static ulong ResolveGrid2SteamId(PlayerProfile profile)
    {
        var steamId = ParseSteamExternalId(profile.ExternalId);
        return steamId > 0 ? steamId : BuildStableGrid2SteamId(profile.DisplayName);
    }

    private static ulong BuildStableGrid2SteamId(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = BitConverter.ToUInt64(hashBytes, 0);
        return 76_561_198_000_000_000UL + hash % 10_000_000_000UL;
    }

    private static string BuildSteamExternalId(ulong steamId)
    {
        return $"steam:{steamId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string BuildNameExternalId(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return $"name:{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()}";
    }

    private static string CreateSessionId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    private static bool IsWireCompatibleSessionId(string sessionId)
    {
        return sessionId.Length == 32 &&
            sessionId.All(value =>
                value is >= '0' and <= '9' or >= 'A' and <= 'F');
    }
}
