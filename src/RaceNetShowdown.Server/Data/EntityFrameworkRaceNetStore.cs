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
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromDays(7);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
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
