using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading;
using RaceNetShowdown.Server;
using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

public sealed class RaceNetResponder
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string HtmlContentType = "text/html";
    private const string XmlContentType = "text/xml; charset=utf-8";
    private const string EgoNetContentType = "application/egonet-stream";
    private const long CapturedFriendChallengeId = 237_570;
    private const long CapturedFriendResultToBeat = 1_055;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, IReadOnlyList<RaceNetPrincipal>> _principalsBySession = new();
    private readonly ConcurrentDictionary<string, List<RaceNetIssuedChallenge>> _issuedChallengesBySession = new();
    private readonly ConcurrentDictionary<string, ChallengeSessionResult> _challengeResultsBySession = new();
    private readonly ConcurrentDictionary<long, byte[]> _ghostDataBySlot = new();
    private byte[]? _lastUploadedGhostData;
    private long _nextIssuedChallengeId = 10_000;

    public RaceNetResponder(RaceNetOptions options)
    {
        Options = options;
    }

    private RaceNetOptions Options { get; }

    public RaceNetResponse BuildResponse(
        HttpRequest request,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot? challengeSnapshot)
    {
        var path = request.Path.Value?.ToLowerInvariant() ?? "/";
        var egoNetFunction = request.Headers["X-EgoNet-Function"].ToString();

        if (!string.IsNullOrWhiteSpace(egoNetFunction))
        {
            return BuildEgoNetResponse(egoNetFunction, body, session, challengeSnapshot);
        }

        if (path is "/" or "/health")
        {
            return Json(new
            {
                ok = true,
                service = "egonet-revival",
                game = "dirt-showdown",
                time = DateTimeOffset.Now
            });
        }

        if (path.Contains("loginservice") || path.Contains("/login"))
        {
            return Json(new
            {
                status = "ok",
                authenticated = true,
                token = "local-dev-token",
                profile_id = "local-profile",
                persona_id = "local-persona",
                steam_id = "local-steam"
            });
        }

        if (path.Contains("getcontentmask"))
        {
            return Json(new
            {
                status = "ok",
                content_mask = 0,
                unlocks = Array.Empty<object>()
            });
        }

        if (path.Contains("getnewsfeed") || path.Contains("news"))
        {
            return Json(new
            {
                status = "ok",
                items = Array.Empty<object>(),
                news = Array.Empty<object>()
            });
        }

        if (path.Contains("leaderboard") || path.Contains("leaderboards"))
        {
            return Json(new
            {
                status = "ok",
                leaderboard = Array.Empty<object>(),
                entries = Array.Empty<object>(),
                player_rank = 1
            });
        }

        if (path.Contains("challenge"))
        {
            return Json(new
            {
                status = "ok",
                challenges = Array.Empty<object>(),
                completed = Array.Empty<object>(),
                issued = Array.Empty<object>(),
                highest_id = 0
            });
        }

        if (path.Contains("raceevent") || path.Contains("posteventbest") || path.Contains("/rp6/"))
        {
            return Json(new
            {
                status = "ok",
                events = Array.Empty<object>(),
                @event = (object?)null,
                accepted = true
            });
        }

        if (body.Preview.TrimStart().StartsWith('<'))
        {
            return Xml("response", "<status>ok</status>");
        }

        return Json(new
        {
            status = "ok",
            message = "local RaceNet fallback response"
        });
    }

    private RaceNetResponse BuildEgoNetResponse(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot? challengeSnapshot)
    {
        var normalized = functionName.Trim();

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-EgoNet-Result"] = "0",
            ["X-EgoNet-SessionID"] = session?.SessionId ?? "local-racenet-session"
        };

        var snapshot = challengeSnapshot ?? RaceNetChallengeSeed.CreateSnapshot();

        return normalized switch
        {
            "LoginService.Login" => EgoNet(headers),
            "LoginService.Tick" => EgoNet(headers),
            "RaceNet.GetContentMask" => EgoNet(headers),
            "RaceNet.GetNewsFeed" => EgoNet(headers),
            "RaceNet.LinkAccount" => EgoNet(headers),
            "RaceNetRP6.GetRaceEvent" => EgoNet(headers),
            "RaceNetRP6.PostEventBest" => EgoNet(headers),
            "AsynchronousChallengeService.AcceptChallenge" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "AsynchronousChallengeService.DownloadGhost" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "AsynchronousChallengeService.GetCompletedIssuedChallenges" => ChallengeEgoNet(headers, normalized, body, session, snapshot),
            "AsynchronousChallengeService.GetFriendChallenges" => ChallengeEgoNet(headers, normalized, body, session, snapshot),
            "AsynchronousChallengeService.GetFriendsOverview" => ChallengeEgoNet(headers, normalized, body, session, snapshot),
            "AsynchronousChallengeService.GetHighestID" => ChallengeEgoNet(headers, normalized, body, session, snapshot),
            "AsynchronousChallengeService.IssueChallenge" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "AsynchronousChallengeService.SubmitChallengeResult" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "AsynchronousChallengeService.SubmitPersonalRecord" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "AsynchronousChallengeService.UploadGhost" => ChallengeLifecycleEgoNet(headers, normalized, body, session),
            "LanguageService.FetchLanguageData" => EgoNet(headers),
            "StatisticsService.SubmitChatUsage" => EgoNet(headers),
            "StatisticsService.SubmitConnectionAttempts" => EgoNet(headers),
            "StatisticsService.SubmitEndOfEvent" => EgoNet(headers),
            "StatisticsService.SubmitHostMigration" => EgoNet(headers),
            "StatisticsService.SubmitNetworkRaceStatistics" => EgoNet(headers),
            "StatisticsService.SubmitPcHardware" => EgoNet(headers),
            "StatisticsService.SubmitProfileLoaded" => EgoNet(headers),
            "StatisticsService.SubmitRaceEnded" => EgoNet(headers),
            "StatisticsService.SubmitRaceStarted" => EgoNet(headers),
            "StatisticsService.SubmitVideoUploaded" => EgoNet(headers),
            _ => EgoNet(headers)
        };
    }

    private static RaceNetResponse Json(object value)
    {
        return new RaceNetResponse(JsonContentType, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static RaceNetResponse Xml(string rootName, string innerXml)
    {
        return new RaceNetResponse(
            XmlContentType,
            $"""<?xml version="1.0" encoding="utf-8"?><{rootName}>{innerXml}</{rootName}>""");
    }

    private static RaceNetResponse EgoNet(IReadOnlyDictionary<string, string> headers)
    {
        return new RaceNetResponse(EgoNetContentType, string.Empty, headers);
    }

    private RaceNetResponse ChallengeEgoNet(
        IReadOnlyDictionary<string, string> headers,
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot snapshot)
    {
        var sessionKey = session?.SessionId ?? "local-racenet-session";
        var requestContext = EgoNetRequestParser.ReadChallengeContext(body);
        var parsedPrincipals = requestContext.Principals;
        if (parsedPrincipals.Count > 0)
        {
            _principalsBySession[sessionKey] = parsedPrincipals;
        }

        var principals = _principalsBySession.TryGetValue(sessionKey, out var storedPrincipals)
            ? storedPrincipals
            : parsedPrincipals;

        var issuedChallenges = GetIssuedChallenges(sessionKey);
        snapshot = ApplySessionResult(sessionKey, snapshot);
        var responseBody = EgoNetChallengePayloads.Build(
            functionName,
            snapshot,
            Options.ChallengePayloadFormat,
            principals,
            issuedChallenges,
            requestContext.Presence,
            requestContext.RaceEventId,
            requestContext.VehicleId);

        return new RaceNetResponse(EgoNetContentType, responseBody, headers);
    }

    private RaceNetResponse ChallengeLifecycleEgoNet(
        IReadOnlyDictionary<string, string> headers,
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session)
    {
        var sessionKey = session?.SessionId ?? "local-racenet-session";
        var requestContext = EgoNetRequestParser.ReadChallengeContext(body);
        if (requestContext.Principals.Count > 0)
        {
            _principalsBySession[sessionKey] = requestContext.Principals;
        }

        if (functionName == "AsynchronousChallengeService.IssueChallenge" &&
            requestContext.Presence is not null &&
            requestContext.ChallengeData is not null)
        {
            var challengeId = Interlocked.Increment(ref _nextIssuedChallengeId);
            var issuedChallenge = new RaceNetIssuedChallenge(
                challengeId,
                ResolveEgonetId(sessionKey, requestContext.Presence),
                requestContext.Presence,
                requestContext.ChallengeData,
                DateTimeOffset.UtcNow.AddDays(30),
                challengeId);
            StoreIssuedChallenge(sessionKey, issuedChallenge);
            var responseBody = EgoNetBinary.Dictionary(
                EgoNetBinary.Si64("GhostSlotID", issuedChallenge.GhostSlotId));
            return new RaceNetResponse(EgoNetContentType, responseBody, headers);
        }

        if (functionName == "AsynchronousChallengeService.UploadGhost" &&
            requestContext.GhostSlotId is not null &&
            requestContext.GhostData is not null)
        {
            StoreGhostData(requestContext.GhostSlotId.Value, requestContext.GhostData);
        }

        if (functionName == "AsynchronousChallengeService.DownloadGhost" &&
            requestContext.GhostSlotId is not null &&
            TryGetGhostData(requestContext.GhostSlotId.Value, out var ghostData))
        {
            var responseBody = BuildGhostDownloadResponse(requestContext.GhostSlotId.Value, ghostData);
            var responseHeaders = headers
                .Concat([new KeyValuePair<string, string>("Cache-Control", "private, s-maxage=0")])
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} ghost-download: slot={requestContext.GhostSlotId.Value} bytes={ghostData.Length} format={Options.GhostDownloadPayloadFormat}");

            return new RaceNetResponse(HtmlContentType, responseBody, responseHeaders);
        }

        if (functionName == "AsynchronousChallengeService.SubmitChallengeResult" &&
            requestContext.ChallengeResult is not null)
        {
            StoreChallengeResult(sessionKey, requestContext.ChallengeResult);
        }

        var presence = requestContext.Presence is null
            ? "presence=<none>"
            : $"presence={requestContext.Presence.Name}/{requestContext.Presence.SteamId}";
        var raceEvent = requestContext.RaceEventId?.ToString() ?? "<none>";
        var vehicle = requestContext.VehicleId?.ToString() ?? "<none>";
        var ghostSlot = requestContext.GhostSlotId?.ToString() ?? "<none>";

        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss} challenge-flow: {functionName} request={body.BodyBytes.Length} bytes {presence} raceEvent={raceEvent} vehicle={vehicle} ghostSlot={ghostSlot}");

        return EgoNet(headers);
    }

    private void StoreGhostData(long ghostSlotId, byte[] ghostData)
    {
        var copy = ghostData.ToArray();
        _ghostDataBySlot[ghostSlotId] = copy;
        Volatile.Write(ref _lastUploadedGhostData, copy);

        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss} ghost-upload: slot={ghostSlotId} bytes={copy.Length}");
    }

    private bool TryGetGhostData(long ghostSlotId, out byte[] ghostData)
    {
        if (_ghostDataBySlot.TryGetValue(ghostSlotId, out ghostData!))
        {
            return true;
        }

        var fallback = Volatile.Read(ref _lastUploadedGhostData);
        if (fallback is not null)
        {
            ghostData = fallback;
            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} ghost-download: slot={ghostSlotId} using latest uploaded ghost fallback");
            return true;
        }

        ghostData = [];
        return false;
    }

    private byte[] BuildGhostDownloadResponse(long ghostSlotId, byte[] ghostData)
    {
        return Options.GhostDownloadPayloadFormat.Trim() switch
        {
            "GhostDataOnly" => EgoNetBinary.Dictionary(
                EgoNetBinary.Blob("GhostData", ghostData)),
            "Dictionary" => EgoNetBinary.Dictionary(
                EgoNetBinary.Si64("SlotId", ghostSlotId),
                EgoNetBinary.Blob("GhostData", ghostData)),
            "RawBlob" => ghostData,
            _ => EgoNetBinary.BlobValue(ghostData)
        };
    }

    private long ResolveEgonetId(string sessionKey, RaceNetPrincipal presence)
    {
        if (_principalsBySession.TryGetValue(sessionKey, out var principals))
        {
            for (var i = 0; i < principals.Count; i++)
            {
                if (principals[i].SteamId == presence.SteamId)
                {
                    return 10_000L + i + 1;
                }
            }
        }

        return 10_000L + Math.Abs((long)(presence.SteamId % 100_000));
    }

    private void StoreIssuedChallenge(string sessionKey, RaceNetIssuedChallenge issuedChallenge)
    {
        var issuedChallenges = _issuedChallengesBySession.GetOrAdd(sessionKey, _ => []);
        lock (issuedChallenges)
        {
            issuedChallenges.RemoveAll(value =>
                value.ChallengeId == issuedChallenge.ChallengeId ||
                value.GhostSlotId == issuedChallenge.GhostSlotId);
            issuedChallenges.Add(issuedChallenge);
        }

        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss} challenge-flow: mirrored challenge {issuedChallenge.ChallengeId} for {issuedChallenge.Presence.Name} result={issuedChallenge.ChallengeData.ResultToBeat}");
    }

    private void StoreChallengeResult(string sessionKey, EgoNetSubmittedChallengeResult result)
    {
        var resultToBeat = FindResultToBeat(sessionKey, result.ChallengeId) ?? CapturedFriendResultToBeat;
        var dominated = result.Result >= resultToBeat;

        _challengeResultsBySession[sessionKey] = new ChallengeSessionResult(
            result.ChallengeId,
            result.Result,
            result.Attempts,
            dominated);

        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss} challenge-flow: result challenge={result.ChallengeId} result={result.Result} target={resultToBeat} attempts={result.Attempts} dominated={dominated}");
    }

    private long? FindResultToBeat(string sessionKey, long challengeId)
    {
        var catalogResult = EgoNetChallengePayloads.TryGetCatalogResultToBeat(challengeId);
        if (catalogResult is not null)
        {
            return catalogResult.Value;
        }

        if (!_issuedChallengesBySession.TryGetValue(sessionKey, out var issuedChallenges))
        {
            return null;
        }

        lock (issuedChallenges)
        {
            return issuedChallenges
                .FirstOrDefault(value => value.ChallengeId == challengeId)
                ?.ChallengeData
                .ResultToBeat;
        }
    }

    private RaceNetChallengeSnapshot ApplySessionResult(string sessionKey, RaceNetChallengeSnapshot snapshot)
    {
        if (!_challengeResultsBySession.TryGetValue(sessionKey, out var result) || !result.Dominated)
        {
            return snapshot;
        }

        var friends = snapshot.Friends
            .Select((friend, index) => index == 0
                ? friend with
                {
                    ChallengeId = Math.Max(friend.ChallengeId, result.ChallengeId),
                    BestResult = Math.Max(friend.BestResult, result.Result),
                    YourBestResult = Math.Max(friend.YourBestResult, result.Result),
                    Tally = Math.Max(friend.Tally, 1)
                }
                : friend)
            .ToArray();

        return snapshot with
        {
            HighChallengeId = Math.Max(snapshot.HighChallengeId, result.ChallengeId),
            ChallengeCount = Math.Max(snapshot.ChallengeCount, 1),
            OverallTally = Math.Max(snapshot.OverallTally, 1),
            BestResult = Math.Max(snapshot.BestResult, result.Result),
            Friends = friends
        };
    }

    private IReadOnlyList<RaceNetIssuedChallenge> GetIssuedChallenges(string sessionKey)
    {
        if (!_issuedChallengesBySession.TryGetValue(sessionKey, out var issuedChallenges))
        {
            return [];
        }

        lock (issuedChallenges)
        {
            return issuedChallenges.ToArray();
        }
    }

    private sealed record ChallengeSessionResult(
        long ChallengeId,
        long Result,
        int Attempts,
        bool Dominated);
}
