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

            if (existingSession?.PlayerProfile is not null)
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
        var profile = await FindOrCreateProfileAsync(loginName, remoteAddress, now, cancellationToken);

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

        logger.LogInformation("RaceNet profile/session created: {Profile} {Session}", profile.ExternalId, sessionId);

        return ToSessionInfo(session);
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
        var challenges = await dbContext.Challenges
            .Include(value => value.IssuerPlayerProfile)
            .Include(value => value.Results)
            .Where(value =>
                value.TargetPlayerProfileId == session.PlayerProfileId &&
                value.Status == "open")
            .OrderByDescending(value => value.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var friends = challenges
            .Select(challenge => ToFriendChallenge(challenge, session.PlayerProfileId))
            .ToArray();
        var challengedFriendCount = friends
            .Select(value => string.IsNullOrWhiteSpace(value.SteamId) ? value.Name : value.SteamId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new RaceNetChallengeSnapshot(
            HighChallengeId: friends.Length == 0 ? 0 : friends.Max(value => value.ChallengeId),
            ChallengeCount: challengedFriendCount,
            OverallTally: friends.Sum(value => value.Tally),
            BestResult: friends.Length == 0 ? 0 : friends.Min(value => value.BestResult),
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
        var challengedFriendIds = await dbContext.Challenges
            .Where(value =>
                value.TargetPlayerProfileId == session.PlayerProfileId &&
                value.Status == "open")
            .Select(value => value.IssuerPlayerProfileId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
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
            Status = "open",
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
        var query = dbContext.Challenges
            .Include(value => value.IssuerPlayerProfile)
            .Include(value => value.TargetPlayerProfile)
            .Where(value =>
                (value.IssuerPlayerProfileId == session.PlayerProfileId ||
                 value.TargetPlayerProfileId == session.PlayerProfileId) &&
                value.Status == "open");

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

        var challenges = await query
            .OrderByDescending(value => value.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        return challenges
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

        ghost ??= await dbContext.Ghosts
            .AsNoTracking()
            .OrderByDescending(value => value.UploadedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return ghost?.Data.ToArray();
    }

    public async Task SaveChallengeResultAsync(
        RaceNetSessionInfo session,
        EgoNetSubmittedChallengeResult result,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.Challenges
            .Include(value => value.Results)
            .FirstOrDefaultAsync(value => value.EgoNetChallengeId == result.ChallengeId, cancellationToken);

        if (challenge is null)
        {
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
            SubmittedAt = DateTimeOffset.UtcNow,
            RawPayloadHex = string.Empty
        });

        if (dominated)
        {
            challenge.CompletedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static RaceNetFriendChallenge ToFriendChallenge(
        ChallengeRecord challenge,
        long currentPlayerProfileId)
    {
        var issuer = challenge.IssuerPlayerProfile;
        var bestResult = challenge.ResultToBeat > 0 ? challenge.ResultToBeat : challenge.Score;
        var playerBest = challenge.Results
            .Where(value => value.PlayerProfileId == currentPlayerProfileId)
            .OrderByDescending(value => value.SubmittedAt)
            .Select(value => value.Score)
            .FirstOrDefault();
        var tally = challenge.Results
            .Where(value => value.PlayerProfileId == currentPlayerProfileId && value.BeatChallenge)
            .LongCount();

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
            ExpiresAt: challenge.CreatedAt.AddDays(30),
            GhostSlotId: checked((int)Math.Clamp(challenge.GhostSlotId, 0, int.MaxValue)));
    }

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
            challenge.CreatedAt.AddDays(30),
            challenge.GhostSlotId);
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
        return $"local-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
    }
}
