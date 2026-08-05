using System.Text.Json;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RaceNetResponder(RaceNetOptions options)
    {
        Options = options;
    }

    private RaceNetOptions Options { get; }

    public async Task<RaceNetResponse> BuildResponseAsync(
        HttpRequest request,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot? challengeSnapshot,
        IRaceNetStore store,
        CancellationToken cancellationToken)
    {
        var path = request.Path.Value?.ToLowerInvariant() ?? "/";
        var egoNetFunction = request.Headers["X-EgoNet-Function"].ToString();

        if (!string.IsNullOrWhiteSpace(egoNetFunction))
        {
            return await BuildEgoNetResponseAsync(
                egoNetFunction,
                body,
                session,
                challengeSnapshot,
                store,
                cancellationToken);
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

    private async Task<RaceNetResponse> BuildEgoNetResponseAsync(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot? challengeSnapshot,
        IRaceNetStore store,
        CancellationToken cancellationToken)
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
            "AsynchronousChallengeService.AcceptChallenge" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
            "AsynchronousChallengeService.DownloadGhost" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
            "AsynchronousChallengeService.GetCompletedIssuedChallenges" => await ChallengeEgoNetAsync(headers, normalized, body, session, snapshot, store, cancellationToken),
            "AsynchronousChallengeService.GetFriendChallenges" => await ChallengeEgoNetAsync(headers, normalized, body, session, snapshot, store, cancellationToken),
            "AsynchronousChallengeService.GetFriendsOverview" => await ChallengeEgoNetAsync(headers, normalized, body, session, snapshot, store, cancellationToken),
            "AsynchronousChallengeService.GetHighestID" => await ChallengeEgoNetAsync(headers, normalized, body, session, snapshot, store, cancellationToken),
            "AsynchronousChallengeService.IssueChallenge" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
            "AsynchronousChallengeService.SubmitChallengeResult" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
            "AsynchronousChallengeService.SubmitPersonalRecord" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
            "AsynchronousChallengeService.UploadGhost" => await ChallengeLifecycleEgoNetAsync(headers, normalized, body, session, store, cancellationToken),
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

    private async Task<RaceNetResponse> ChallengeEgoNetAsync(
        IReadOnlyDictionary<string, string> headers,
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        RaceNetChallengeSnapshot snapshot,
        IRaceNetStore store,
        CancellationToken cancellationToken)
    {
        session ??= new RaceNetSessionInfo("local-racenet-session", 0, "local-player", "Local Player");
        var requestContext = EgoNetRequestParser.ReadChallengeContext(body);
        var parsedPrincipals = requestContext.Principals;
        if (parsedPrincipals.Count > 0)
        {
            await store.SavePrincipalsAsync(session, parsedPrincipals, cancellationToken);
        }

        var principals = parsedPrincipals.Count > 0
            ? parsedPrincipals
            : await store.GetPrincipalsAsync(session, cancellationToken);

        var issuedChallenges = await store.GetIssuedChallengesAsync(
            session,
            requestContext.Presence,
            cancellationToken);
        var responseBody = EgoNetChallengePayloads.Build(
            functionName,
            snapshot,
            Options.ChallengePayloadFormat,
            principals,
            issuedChallenges,
            requestContext.Presence,
            requestContext.RaceEventId,
            requestContext.VehicleId);

        var presence = requestContext.Presence is null
            ? "presence=<none>"
            : $"presence={requestContext.Presence.Name}/{requestContext.Presence.SteamId}";
        var raceEvent = requestContext.RaceEventId?.ToString() ?? "<none>";
        var vehicle = requestContext.VehicleId?.ToString() ?? "<none>";

        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss} challenge-query: {functionName} request={body.BodyBytes.Length} bytes response={responseBody.Length} bytes parsedPrincipals={parsedPrincipals.Count} savedPrincipals={principals.Count} issued={issuedChallenges.Count} snapshotFriends={snapshot.Friends.Count} snapshotChallenges={snapshot.ChallengeCount} highId={snapshot.HighChallengeId} {presence} raceEvent={raceEvent} vehicle={vehicle}");

        return new RaceNetResponse(EgoNetContentType, responseBody, headers);
    }

    private async Task<RaceNetResponse> ChallengeLifecycleEgoNetAsync(
        IReadOnlyDictionary<string, string> headers,
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IRaceNetStore store,
        CancellationToken cancellationToken)
    {
        session ??= new RaceNetSessionInfo("local-racenet-session", 0, "local-player", "Local Player");
        var requestContext = EgoNetRequestParser.ReadChallengeContext(body);
        if (requestContext.Principals.Count > 0)
        {
            await store.SavePrincipalsAsync(session, requestContext.Principals, cancellationToken);
        }

        if (functionName == "AsynchronousChallengeService.IssueChallenge" &&
            requestContext.Presence is not null &&
            requestContext.ChallengeData is not null)
        {
            var issuedChallenge = await store.IssueChallengeAsync(
                session,
                requestContext.Presence,
                requestContext.ChallengeData,
                cancellationToken);
            var responseBody = EgoNetBinary.Dictionary(
                EgoNetBinary.Si64("GhostSlotID", issuedChallenge.GhostSlotId));
            return new RaceNetResponse(EgoNetContentType, responseBody, headers);
        }

        if (functionName == "AsynchronousChallengeService.IssueChallenge")
        {
            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} challenge-flow: IssueChallenge payload incomplete presence={requestContext.Presence is not null} challengeData={requestContext.ChallengeData is not null}");
        }

        if (functionName == "AsynchronousChallengeService.UploadGhost" &&
            requestContext.GhostSlotId is not null &&
            requestContext.GhostData is not null)
        {
            await store.SaveGhostDataAsync(
                session,
                requestContext.GhostSlotId.Value,
                requestContext.GhostData,
                cancellationToken);

            Console.WriteLine(
                $"{DateTime.Now:HH:mm:ss} ghost-upload: slot={requestContext.GhostSlotId.Value} bytes={requestContext.GhostData.Length}");
        }

        if (functionName == "AsynchronousChallengeService.DownloadGhost" &&
            requestContext.GhostSlotId is not null)
        {
            var ghostData = await store.GetGhostDataAsync(requestContext.GhostSlotId.Value, cancellationToken);
            if (ghostData is not null)
            {
                var responseBody = BuildGhostDownloadResponse(requestContext.GhostSlotId.Value, ghostData);
                var responseHeaders = headers
                    .Concat([new KeyValuePair<string, string>("Cache-Control", "private, s-maxage=0")])
                    .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);

                Console.WriteLine(
                    $"{DateTime.Now:HH:mm:ss} ghost-download: slot={requestContext.GhostSlotId.Value} bytes={ghostData.Length} format={Options.GhostDownloadPayloadFormat}");

                return new RaceNetResponse(HtmlContentType, responseBody, responseHeaders);
            }
        }

        if (functionName == "AsynchronousChallengeService.SubmitChallengeResult" &&
            requestContext.ChallengeResult is not null)
        {
            await store.SaveChallengeResultAsync(session, requestContext.ChallengeResult, cancellationToken);
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
}
