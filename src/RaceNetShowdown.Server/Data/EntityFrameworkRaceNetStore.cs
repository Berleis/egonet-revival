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
        await SeedDefaultChallengeAsync(cancellationToken);
    }

    public async Task<RaceNetSessionInfo> EnsureSessionAsync(HttpContext context, CancellationToken cancellationToken)
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

                return new RaceNetSessionInfo(
                    existingSession.SessionId,
                    existingSession.PlayerProfile.Id,
                    existingSession.PlayerProfile.ExternalId,
                    existingSession.PlayerProfile.DisplayName);
            }
        }

        var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var externalId = $"remote:{remoteAddress}";

        var profile = await dbContext.PlayerProfiles
            .FirstOrDefaultAsync(value => value.ExternalId == externalId, cancellationToken);

        if (profile is null)
        {
            profile = new PlayerProfile
            {
                ExternalId = externalId,
                DisplayName = $"Player {remoteAddress}",
                FirstSeenAt = now,
                LastSeenAt = now
            };

            dbContext.PlayerProfiles.Add(profile);
        }
        else
        {
            profile.LastSeenAt = now;
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

        logger.LogInformation("RaceNet profile/session created: {Profile} {Session}", profile.ExternalId, sessionId);

        return new RaceNetSessionInfo(sessionId, profile.Id, profile.ExternalId, profile.DisplayName);
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
            .OrderBy(value => value.EgoNetChallengeId)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (challenges.Count == 0)
        {
            return RaceNetChallengeSeed.CreateSnapshot();
        }

        var now = DateTimeOffset.UtcNow;
        var friends = challenges
            .Select((challenge, index) =>
            {
                var bestResult = challenge.LapTime is not null
                    ? (long)challenge.LapTime.Value.TotalMilliseconds
                    : challenge.Score;

                if (bestResult <= 0)
                {
                    bestResult = 120_000 + (index * 5_000);
                }

                return RaceNetChallengeSeed.CreateFriend(
                    index + 1,
                    challenge.IssuerPlayerProfile?.DisplayName ?? $"RaceNet Friend {index + 1}",
                    bestResult,
                    0,
                    now) with
                    {
                        ChallengeId = challenge.EgoNetChallengeId,
                        ExpiresAt = challenge.CreatedAt.AddDays(30)
                    };
            })
            .ToArray();

        return new RaceNetChallengeSnapshot(
            HighChallengeId: friends.Max(value => value.ChallengeId),
            ChallengeCount: friends.Length,
            OverallTally: friends.Sum(value => value.Tally),
            BestResult: friends.Min(value => value.BestResult),
            Friends: friends);
    }

    private async Task SeedDefaultChallengeAsync(CancellationToken cancellationToken)
    {
        var challengeCount = await dbContext.Challenges.CountAsync(cancellationToken);
        if (challengeCount >= 5)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var seed = RaceNetChallengeSeed.CreateSnapshot();
        foreach (var friend in seed.Friends.Skip(challengeCount))
        {
            var externalId = $"local:seed-friend-{friend.ChallengeId}";
            var seedProfile = await dbContext.PlayerProfiles
                .FirstOrDefaultAsync(value => value.ExternalId == externalId, cancellationToken);

            if (seedProfile is null)
            {
                seedProfile = new PlayerProfile
                {
                    ExternalId = externalId,
                    DisplayName = friend.Name,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };

                dbContext.PlayerProfiles.Add(seedProfile);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (await dbContext.Challenges.AnyAsync(
                value => value.EgoNetChallengeId == friend.ChallengeId,
                cancellationToken))
            {
                continue;
            }

            dbContext.Challenges.Add(new ChallengeRecord
            {
                EgoNetChallengeId = friend.ChallengeId,
                IssuerPlayerProfileId = seedProfile.Id,
                EventKey = $"seed-friend-challenge-{friend.ChallengeId}",
                VehicleKey = friend.VehicleId.ToString(),
                Score = friend.BestResult,
                LapTime = TimeSpan.FromMilliseconds(friend.BestResult),
                Status = "open",
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CreateSessionId()
    {
        return $"local-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
    }
}
