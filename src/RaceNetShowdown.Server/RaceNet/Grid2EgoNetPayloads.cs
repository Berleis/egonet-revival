using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Grid2EgoNetPayloads
{
    private const string HtmlContentType = "text/html";
    private const string EgoNetContentType = "application/egonet-stream";

    public static RaceNetResponse? TryBuild(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers)
    {
        return TryBuildCommon(functionName, body, session, headers);
    }
    public static async Task<RaceNetResponse?> TryBuildAsync(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers,
        IRaceNetStore store,
        CancellationToken cancellationToken)
    {
        if (functionName == "DataMining.EndEvent")
        {
            var multiplayerEvent = EgoNetRequestParser.ReadGrid2MultiplayerEventSubmission(body);
            if (multiplayerEvent is not null && session is not null)
            {
                await store.SaveGrid2MultiplayerEventAsync(session, multiplayerEvent, cancellationToken);
            }

            return Empty(headers);
        }

        if (functionName == "RaceNetGlobalDomination.PostScore")
        {
            var submission = EgoNetRequestParser.ReadGrid2GlobalScoreSubmission(body);
            if (submission is not null && session is not null)
            {
                await store.SaveGrid2GlobalScoreAsync(session, submission, cancellationToken);
            }

            return Empty(headers);
        }

        if (functionName == "RaceNetGlobalDomination.GetEvent")
        {
            var activeEvent = await store.GetGrid2CurrentGlobalEventAsync(session, cancellationToken);
            return Html(BuildCurrentGlobalDomination(activeEvent), headers);
        }

        if (functionName == "RaceNetGlobalDomination.GetPreviousEvent")
        {
            var previousEvent = await store.GetGrid2PreviousGlobalEventAsync(session, cancellationToken);
            return Html(BuildPreviousGlobalDomination(previousEvent), headers);
        }

        if (functionName == "RaceNetRivals.GetRivals")
        {
            var now = DateTimeOffset.UtcNow;
            var rivals = session is null
                ? new Grid2RivalsSnapshot(now, now.AddDays(7), [])
                : await store.GetGrid2RivalsAsync(session, cancellationToken);
            return Html(BuildRivals(rivals), headers);
        }

        if (functionName == "Rivals.GetRivalsSessionData")
        {
            var rivalIds = EgoNetRequestParser.ReadTopLevelIntegerVector(body, "RivalsEgonetIds");
            var sessionData = session is null
                ? Array.Empty<Grid2RivalSessionDataSnapshot>()
                : await store.GetGrid2RivalSessionDataAsync(session, rivalIds, cancellationToken);
            return Html(BuildRivalsSessionData(sessionData), headers);
        }

        if (functionName == "Rivals.UpdateRivalsSessionData")
        {
            var sessionData = EgoNetRequestParser.ReadGrid2RivalSessionDataUpload(body);
            if (session is not null && sessionData is not null)
            {
                await store.SaveGrid2RivalSessionDataAsync(session, sessionData, cancellationToken);
            }

            return Empty(headers);
        }

        return TryBuildCommon(functionName, body, session, headers);
    }

    private static RaceNetResponse? TryBuildCommon(
        string functionName,
        CapturedBody body,
        RaceNetSessionInfo? session,
        IReadOnlyDictionary<string, string> headers)
    {
        return functionName switch
        {
            "LoginService.Login" => Html(BuildLogin(body, session), headers),
            "LoginService.Tick" => Empty(headers),
            "GameData.Get" => Html(BuildGameData(), headers),
            "LanguageService.FetchLanguageData" => Html(BuildLanguageData(), headers),
            "OnlineSkill.GetOnlineSkill" => Html(BuildOnlineSkill(), headers),
            "OnlineSkill.UpdateOnlineSkill" => Empty(headers),
            "RaceNet.CheckAccount" => Html(BuildCheckAccount(), headers),
            "RaceNet.CheckAccountLinked" => Html(BuildCheckAccountLinked(), headers),
            "RaceNet.CreateAccount" => Html(BuildAccountProfile(body, session), headers),
            "RaceNet.GetContentMask" => Html(EgoNetBinary.Dictionary(), headers),
            "RaceNet.GetNewsFeed" => Html(BuildNewsFeed(), headers),
            "RaceNet.GetTermsAndConditions" => Html(BuildTermsAndConditions(), headers),
            "RaceNet.SignIn" => Html(BuildAccountProfile(body, session), headers),
            "RaceNet.UnlinkAccount" => Empty(headers),
            "RaceNet.ValidateDateOfBirth" => Html(BuildValidation(), headers),
            "RaceNet.ValidateEmail" => Html(BuildValidation(), headers),
            "RaceNet.ValidatePassword" => Html(BuildValidation(), headers),
            "RaceNet.ValidateSocialLinks" => Html(BuildValidation(), headers),
            "RaceNet.ValidateUsername" => Html(BuildValidation(), headers),
            "RaceNetGlobalDomination.GetEvent" => Html(BuildCurrentGlobalDomination(null), headers),
            "RaceNetGlobalDomination.GetPreviousEvent" => Html(BuildPreviousGlobalDomination(null), headers),
            "RaceNetGlobalDomination.PostScore" => Empty(headers),
            "RaceNetRivals.BlockRival" => Empty(headers),
            "RaceNetRivals.GetRivals" => Html(BuildRivals(new Grid2RivalsSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [])), headers),
            "RaceNetRivals.PostRivalResults" => Empty(headers),
            "Rivals.GetRivalsSessionData" => Html(BuildRivalsSessionData(Array.Empty<Grid2RivalSessionDataSnapshot>()), headers),
            "Rivals.UpdateRivalsSessionData" => Empty(headers),
            "DataMiningHelper.GetRaceId" => Html(BuildRaceId(), headers),
            "DataMining.EndEvent" => Empty(headers),
            "DataMining.EndRace" => Empty(headers),
            "DataMining.PcHardware" => Empty(headers),
            "DataMining.Profile" => Empty(headers),
            "DataMining.StartEvent" => Empty(headers),
            "GhostCar.Download" => Html(BuildGhostDownload(), headers),
            "GhostCar.Upload" => Empty(headers),
            "Statistics.SubmitRaceEndedHeatMapData" => Empty(headers),
            "Texture.FetchTexture" => Empty(headers),
            "Video.EnterQueue" => Empty(headers),
            "Video.PollQueue" => Empty(headers),
            "Video.Upload" => Empty(headers),
            "VipPassTrial.ProceedWithTrial" => Empty(headers),
            _ => null
        };
    }

    private static RaceNetResponse Html(byte[] body, IReadOnlyDictionary<string, string> headers)
    {
        return new RaceNetResponse(HtmlContentType, body, headers);
    }

    private static RaceNetResponse Empty(IReadOnlyDictionary<string, string> headers)
    {
        return new RaceNetResponse(EgoNetContentType, [], headers);
    }

    private static byte[] BuildLogin(CapturedBody body, RaceNetSessionInfo? session)
    {
        var name = ReadAccountName(body, session);
        var steamId = BuildStableSteamId(name);
        var principalId = session?.PlayerProfileId > 0
            ? session.PlayerProfileId
            : 752_828;

        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("PrincipalId", principalId),
            EgoNetBinary.Dstr("Name", name),
            EgoNetBinary.Ui64("XUID", 0),
            EgoNetBinary.Ui64("SteamId", steamId),
            EgoNetBinary.Ui64("SubjectId", 0));
    }

    private static byte[] BuildAccountProfile(CapturedBody body, RaceNetSessionInfo? session)
    {
        var name = ReadAccountName(body, session);
        var steamId = BuildStableSteamId(name);
        var principalId = session?.PlayerProfileId > 0
            ? session.PlayerProfileId
            : 752_828;

        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("PrincipalId", principalId),
            EgoNetBinary.Dstr("Name", name),
            EgoNetBinary.Ui64("XUID", 0),
            EgoNetBinary.Ui64("SteamId", steamId),
            EgoNetBinary.Ui64("SubjectId", 0));
    }

    private static string ReadAccountName(CapturedBody body, RaceNetSessionInfo? session)
    {
        return EgoNetRequestParser.ReadTopLevelString(body, "Name") ??
            EgoNetRequestParser.ReadTopLevelString(body, "Username") ??
            session?.DisplayName ??
            "GRID 2 Player";
    }

    private static byte[] BuildGameData()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("DbPatch", [0]),
            EgoNetBinary.Si32("DbPatchSize", 0),
            EgoNetBinary.Vector("DlcPreview"));
    }

    private static byte[] BuildLanguageData()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Translations"),
            EgoNetBinary.Si32("Version", 1));
    }

    private static byte[] BuildOnlineSkill()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Dict(
                "SkillRating",
                EgoNetBinary.Fp64("Rating", 0),
                EgoNetBinary.Bool("IsValid", false)));
    }

    private static byte[] BuildCheckAccount()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Bool("Exists", false),
            EgoNetBinary.Bool("PasswordExists", false));
    }

    private static byte[] BuildCheckAccountLinked()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Bool("IsLinked", true));
    }

    private static byte[] BuildNewsFeed()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("News"));
    }

    private static byte[] BuildTermsAndConditions()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Dstr("TermsConditions", string.Empty),
            EgoNetBinary.Dstr("PasswordText", string.Empty),
            EgoNetBinary.Dstr("UsernameText", string.Empty));
    }

    private static byte[] BuildValidation()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Bool("IsValid", true),
            EgoNetBinary.Bool("Valid", true),
            EgoNetBinary.Bool("Available", true),
            EgoNetBinary.Bool("IsAvailable", true),
            EgoNetBinary.Bool("Exists", false),
            EgoNetBinary.Dstr("Error", string.Empty),
            EgoNetBinary.Dstr("Message", string.Empty));
    }

    private static byte[] BuildCurrentGlobalDomination(Grid2GlobalEventSnapshot? globalEvent)
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("RaceNetId", globalEvent?.RaceNetEventId ?? 0),
            EgoNetBinary.Tutc("Expires", globalEvent?.ExpiresAt ?? DateTimeOffset.UtcNow),
            EgoNetBinary.Vector(
                "Races",
                globalEvent?.Races
                    .Select(race => BuildGlobalRace(race, globalEvent.LeaderboardEntries))
                    .ToArray() ?? []));
    }

    private static byte[] BuildPreviousGlobalDomination(Grid2GlobalEventSnapshot? globalEvent)
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector(
                "Races",
                globalEvent?.Races
                    .Select(race => BuildGlobalRace(race, globalEvent.LeaderboardEntries))
                    .ToArray() ?? []));
    }

    private static Action<BinaryWriter> BuildGlobalRace(
        Grid2GlobalRaceSnapshot race,
        IReadOnlyList<Grid2GlobalLeaderboardEntry> leaderboardEntries)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si64("RaceNetId", race.RaceNetRaceId),
            EgoNetBinary.Bool("HigherIsBetter", race.HigherIsBetter),
            EgoNetBinary.Si32("LocationId", race.LocationId),
            EgoNetBinary.Si32("TrackModelId", race.TrackModelId),
            EgoNetBinary.Si32("TrackModelDlcId", race.TrackModelDlcId),
            EgoNetBinary.Si32("ConditionsId", race.ConditionsId),
            EgoNetBinary.Si32("RaceTypeId", race.RaceTypeId),
            EgoNetBinary.Si64("RaceDuration", race.RaceDuration),
            EgoNetBinary.Si32("VehicleTierId", race.VehicleTierId),
            EgoNetBinary.Si32("VehicleClassId", race.VehicleClassId),
            EgoNetBinary.Si32("VehicleId", race.VehicleId),
            EgoNetBinary.Si32("VehicleDlcId", race.VehicleDlcId),
            EgoNetBinary.Bool("SpecialRace", race.SpecialRace),
            EgoNetBinary.Si64("GhostSlotId", race.GhostSlotId),
            EgoNetBinary.Vector(
                "Leaderboard",
                BuildGlobalLeaderboard(race, leaderboardEntries)));
    }

    private static Action<BinaryWriter>[] BuildGlobalLeaderboard(
        Grid2GlobalRaceSnapshot race,
        IReadOnlyList<Grid2GlobalLeaderboardEntry> leaderboardEntries)
    {
        var rows = leaderboardEntries
            .Where(value => value.RaceNetRaceId == race.RaceNetRaceId && value.PersonalBest >= 0)
            .GroupBy(BuildGlobalPlayerKey)
            .Select(group => SelectBestGlobalEntry(race, group))
            .Select(value => new Grid2LeaderboardRow(
                ResolveSteamId(value.Presence),
                value.Presence.Name,
                value.EgonetId,
                value.PersonalBest,
                value.VehicleId,
                value.SubmittedAt))
            .ToList();


        var orderedRows = race.HigherIsBetter
            ? rows.OrderByDescending(value => value.PersonalBest).ThenBy(value => value.SubmittedAt)
            : rows.OrderBy(value => value.PersonalBest).ThenBy(value => value.SubmittedAt);

        return orderedRows
            .Take(10)
            .Select(BuildGlobalLeaderboardRow)
            .ToArray();
    }

    private static Grid2GlobalLeaderboardEntry SelectBestGlobalEntry(
        Grid2GlobalRaceSnapshot race,
        IEnumerable<Grid2GlobalLeaderboardEntry> entries)
    {
        return race.HigherIsBetter
            ? entries.OrderByDescending(value => value.PersonalBest).ThenBy(value => value.SubmittedAt).First()
            : entries.OrderBy(value => value.PersonalBest).ThenBy(value => value.SubmittedAt).First();
    }

    private static string BuildGlobalPlayerKey(Grid2GlobalLeaderboardEntry entry)
    {
        return entry.Presence.SteamId > 0
            ? $"steam:{entry.Presence.SteamId}"
            : $"name:{entry.Presence.Name.Trim().ToLowerInvariant()}";
    }

    private static Action<BinaryWriter> BuildGlobalLeaderboardRow(Grid2LeaderboardRow row)
    {
        return EgoNetBinary.DictValue(
            BuildPresence("Presence", row.SteamId, row.Name, row.EgonetId),
            EgoNetBinary.Si64("PersonalBest", row.PersonalBest),
            EgoNetBinary.Si32("VehicleId", row.VehicleId));
    }

    private static byte[] BuildRivals(Grid2RivalsSnapshot rivals)
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector(
                "RivalsList",
                rivals.Rivals.Select(BuildRival).ToArray()),
            EgoNetBinary.Tutc("NextRivalAlloc", rivals.ExpiresAt));
    }

    private static Action<BinaryWriter> BuildRival(Grid2RivalSnapshot rival)
    {
        return EgoNetBinary.DictValue(
            BuildPresence("Presence", rival.SteamId, rival.Name, rival.EgonetId),
            EgoNetBinary.Si64("PlatformId", checked((long)rival.SteamId)),
            EgoNetBinary.Si32("Type", rival.Type),
            EgoNetBinary.Bool("CanSeePresence", true),
            EgoNetBinary.Ui32("TotalXPWon", 0),
            EgoNetBinary.Ui32("RivalXPWon", 0));
    }


    private static EgoNetField BuildPresence(string name, ulong steamId, string displayName, long egonetId)
    {
        return EgoNetBinary.Dict(
            name,
            EgoNetBinary.Ui64("SteamId", steamId),
            EgoNetBinary.Dstr("Name", displayName),
            EgoNetBinary.Si64("EgonetId", egonetId));
    }

    private static byte[] BuildRivalsSessionData(IReadOnlyList<Grid2RivalSessionDataSnapshot> sessionData)
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector(
                "Results",
                sessionData.Select(BuildRivalsSessionDataResult).ToArray()));
    }

    private static Action<BinaryWriter> BuildRivalsSessionDataResult(Grid2RivalSessionDataSnapshot sessionData)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si64("EgonetId", sessionData.EgonetId),
            EgoNetBinary.Blob("SessionData", sessionData.SessionData));
    }

    private static byte[] BuildRaceId()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("RaceId", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }

    private static byte[] BuildGhostDownload()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("GhostData", []));
    }

    private static ulong ResolveSteamId(RaceNetPrincipal presence)
    {
        return presence.SteamId > 0 ? presence.SteamId : BuildStableSteamId(presence.Name);
    }

    private static ulong BuildStableSteamId(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = BitConverter.ToUInt64(hashBytes, 0);
        return 76_561_198_000_000_000UL + hash % 10_000_000_000UL;
    }

    private sealed record Grid2LeaderboardRow(
        ulong SteamId,
        string Name,
        long EgonetId,
        long PersonalBest,
        int VehicleId,
        DateTimeOffset SubmittedAt);
}
