using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Dirt4EgoNetPayloads
{
    private const string HtmlContentType = "text/html";
    private const string EgoNetContentType = "application/egonet-stream";
    private static readonly SemaphoreSlim StateLock = new(1, 1);

    public static RaceNetResponse? TryBuild(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers,
        Dirt4DailyStore daily,
        Dirt4ProTour proTour)
    {
        return Build(functionName, body, session, headers, daily, proTour);
    }

    public static async Task<RaceNetResponse?> TryBuildAsync(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers,
        IRaceNetStore store,
        int dailyTestSeconds,
        Dirt4ProTour proTour,
        CancellationToken cancellationToken)
    {
        await StateLock.WaitAsync(cancellationToken);
        try
        {
            var originalState = await store.LoadDirt4CommunityStateAsync(cancellationToken);
            var daily = new Dirt4DailyStore(originalState, dailyTestSeconds);
            var response = Build(functionName, body, session, headers, daily, proTour);
            if (response is not null)
            {
                var updatedState = daily.Export();
                if (!string.Equals(originalState, updatedState, StringComparison.Ordinal))
                    await store.SaveDirt4CommunityStateAsync(updatedState, cancellationToken);
            }
            return response;
        }
        finally
        {
            StateLock.Release();
        }
    }

    private static RaceNetResponse? Build(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers,
        Dirt4DailyStore daily,
        Dirt4ProTour proTour)
    {
        var player = session?.PlayerExternalId ?? "local-player";
        var now = DateTimeOffset.UtcNow;
        var lobbyOwner = string.IsNullOrWhiteSpace(session?.SessionId) ? null : session.SessionId;
        if (functionName == "LoginService.Tick") proTour.Tick(lobbyOwner, now);

        return functionName switch
        {
            "LoginService.GetCurrentVersion" => Html(EgoNetBinary.Dictionary(EgoNetBinary.Ui32("Version", 1)), headers),
            "LoginService.Login" => Html(BuildLogin(body, session), headers),
            "LoginService.Tick" => Empty(headers),
            "RaceNet.GetContentMask" => Html(EgoNetBinary.Dictionary(EgoNetBinary.Si32("ContentMask", 0)), headers),
            "AsyncChallenge.GetEvents" => Html(Dirt4CommunityEvents.Build(now, daily, player,
                EgoNetRequestParser.ReadTopLevelIntegerVector(body, "EventIds"),
                EgoNetRequestParser.ReadTopLevelBoolean(body, "OmitScores") == true), headers),
            "AsyncChallenge.GetResults" => Html(Dirt4CommunityEvents.BuildResults(now, daily, player,
                EgoNetRequestParser.ReadTopLevelIntegerVector(body, "EventIds")), headers),
            "AsyncChallenge.StartStage" => RecordStart(body, daily, player, now, headers),
            "DataMining.DataEvent" or "DataMining.StatsEvent" => EmptyWithRaceNet(headers),
            "GhostCar.Upload" => RecordGhostUpload(body, daily, player, session?.DisplayName, now, headers),
            "LiveLadder.DownloadPrincipalData" => Html(BuildLiveLadder(now), headers),
            "LiveLadder.GetSessionList" => Html(proTour.GetSessionList(body, lobbyOwner, now), headers),
            "LiveLadder.SessionConfigDownload" => Html(Dirt4ProTour.SessionConfig(now), headers),
            "LiveLadder.SubmitSession" => Html(proTour.SubmitSession(body, lobbyOwner, now), headers),
            "LiveLadder.SubmitSessionScores" => Html(proTour.SubmitSessionScores(body, lobbyOwner, now), headers),
            "LiveLadder.SessionStart" => Html(proTour.SessionStart(body, lobbyOwner, now), headers),
            "LiveLadder.QuitSession" => Html(proTour.QuitSession(body, lobbyOwner, now), headers),
            "LiveLadder.PenalisePlayer" => Html(Dirt4ProTour.PenalisePlayer(body), headers),
            "Localisation.GetStrings" => Html(BuildLocalisation(body), headers),
            "Mailbox.GetPendingMessageCount" => Html(EgoNetBinary.Dictionary(EgoNetBinary.Si32("Count", 0)), headers),
            "RaceNetLeaderboard.GetFriendsEntries" => Html(
                Dirt4Leaderboard.Build(body, daily, player, friendsOnly: true, session?.DisplayName), headers),
            "RaceNetLeaderboard.GetLeaderboardEntries" => Html(
                Dirt4Leaderboard.Build(body, daily, player, friendsOnly: false, session?.DisplayName), headers),
            _ => null
        };
    }

    private static RaceNetResponse RecordStart(CapturedBody body, Dirt4DailyStore daily, string player,
        DateTimeOffset now, IReadOnlyDictionary<string, string> headers)
    {
        if (EgoNetRequestParser.ReadTopLevelInteger(body, "VehicleId") is > 0 and var vehicle &&
            EgoNetRequestParser.ReadTopLevelInteger(body, "LeaderboardId") is { } leaderboard)
            daily.Start(leaderboard, player, now, vehicle);
        return Empty(headers);
    }

    private static RaceNetResponse RecordGhostUpload(CapturedBody body,
        Dirt4DailyStore daily, string player, string? displayName, DateTimeOffset now,
        IReadOnlyDictionary<string, string> headers)
    {
        var leaderboardId = EgoNetRequestParser.ReadTopLevelInteger(body, "LeaderboardId");
        if (leaderboardId is { } id)
            daily.BindDisplayName(id, player, displayName);
        if (EgoNetRequestParser.ReadTopLevelInteger(body, "VehicleId") is > 0 and var vehicle &&
            leaderboardId is { } leaderboard &&
            EgoNetRequestParser.ReadTopLevelInteger(body, "EventTime") is { } time)
            daily.Finish(leaderboard, player, time, now, vehicle,
                checked((uint)(EgoNetRequestParser.ReadTopLevelInteger(body, "Nationality") ?? 0)));
        var predictedRank = leaderboardId is { } rankedLeaderboardId
            ? Dirt4Leaderboard.Rank(daily, rankedLeaderboardId, player) : 0;
        return Html(EgoNetBinary.Dictionary(EgoNetBinary.Si32("PredictedRanks", predictedRank)), headers,
            includeRaceNetHeader: true);
    }

    private static byte[] BuildLogin(CapturedBody body, RaceNetSessionInfo? session)
    {
        var name = EgoNetRequestParser.ReadTopLevelString(body, "Name") ?? session?.DisplayName ?? "EgoNetPlayer";
        var principalId = session?.PlayerProfileId > 0 ? session.PlayerProfileId : 752_828;
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("PrincipalId", principalId),
            EgoNetBinary.Dstr("Name", name),
            EgoNetBinary.Ui64("XUID", 0),
            EgoNetBinary.Ui64("SteamId", 76561198000000001),
            EgoNetBinary.Ui64("SubjectId", 0),
            EgoNetBinary.Ui64("OculusId", 0),
            EgoNetBinary.Si64("AccountRef", principalId),
            EgoNetBinary.Ui64("Flags", 0));
    }

    internal static byte[] BuildLiveLadder(DateTimeOffset now)
    {
        // Match the captured baseline. Telemetry is not proof of a completed Pro Tour session.
        var reset = DateTimeOffset.FromUnixTimeSeconds(Dirt4EventCalendar.Window(now, 1).End);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si32("Division", 3),
            EgoNetBinary.Si32("Tier", Dirt4ProTour.BaselineTier),
            EgoNetBinary.Si32("Points", 0),
            EgoNetBinary.Si32("PrevPoints", 0),
            EgoNetBinary.Si32("EventsDone", 0),
            EgoNetBinary.Si32("PromotionPoints", 7),
            EgoNetBinary.Si32("DemotionPoints", 0),
            EgoNetBinary.Ui32("CurrentVehClass", 74),
            EgoNetBinary.Tutc("VehClassReset", reset));
    }

    private static byte[] BuildLocalisation(CapturedBody body)
    {
        var requested = EgoNetRequestParser.ReadTopLevelStringVector(body, "StringIds");
        if (requested.Count == 0)
        {
            requested =
            [
                "lng_dirt_daily_live",
                "lng_dirt_daily_owners_club",
                "lng_dirt_weekly",
                "lng_dirt_weekly2",
                "lng_dirt_monthly"
            ];
        }

        var strings = requested
            .Select(id => EgoNetBinary.DictValue(
                EgoNetBinary.Dstr("StringId", id),
                EgoNetBinary.Dstr("Translation", id switch
                {
                    "lng_dirt_daily_live" => "Daily Live",
                    "lng_dirt_daily_owners_club" => "Daily Owners Club",
                    "lng_dirt_delta_daily" => "Delta Daily",
                    "lng_dirt_weekly_live" => "Weekly Live",
                    "lng_dirt_weekly_owners_club" => "Weekly Owners Club",
                    "lng_dirt_weekly" => "Weekly",
                    "lng_dirt_weekly2" => "Weekly 2",
                    "lng_dirt_monthly_live" => "Monthly Live",
                    _ => id
                })))
            .ToArray();

        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Strings", strings));
    }
    private static RaceNetResponse Html(byte[] body, IReadOnlyDictionary<string, string> headers, bool includeRaceNetHeader = false)
    {
        var responseHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        if (includeRaceNetHeader)
        {
            responseHeaders["X-EgoNet-RaceNet"] = "1";
        }

        return new RaceNetResponse(HtmlContentType, body, responseHeaders);
    }

    private static RaceNetResponse Empty(IReadOnlyDictionary<string, string> headers)
    {
        return new RaceNetResponse(EgoNetContentType, [], headers);
    }

    private static RaceNetResponse EmptyWithRaceNet(IReadOnlyDictionary<string, string> headers)
    {
        var responseHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
        {
            ["X-EgoNet-RaceNet"] = "1"
        };
        return new RaceNetResponse(EgoNetContentType, [], responseHeaders);
    }

}
