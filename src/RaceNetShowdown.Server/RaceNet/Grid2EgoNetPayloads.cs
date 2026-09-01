using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Grid2EgoNetPayloads
{
    private const string HtmlContentType = "text/html";
    private const string EgoNetContentType = "application/egonet-stream";

    private static readonly Grid2Presence[] RivalPresences =
    [
        new(76_561_198_081_105_540, "Heatray", 2_536_759, 2),
        new(76_561_197_998_609_659, "MMMMMMMMM", 551_744, 0),
        new(76_561_198_195_857_139, "sami_1_3_5", 2_473_109, 1)
    ];

    private static readonly Grid2GlobalRace[] CurrentGlobalRaces =
    [
        new(6_282, false, 31, 355, 0, 554, 24, 7, 3, -1, -1, 0, true, 1, 77_215_000, 65_763),
        new(6_283, true, 33, 408, 0, 361, 7, 79_102, 3, -1, -1, 0, false, 2, 662_890_000, 367),
        new(6_284, true, 27, 333, 0, 367, 17, 6, 5, -1, -1, 0, false, 3, 88_100_000, 131_331),
        new(6_285, true, 26, 326, 0, 517, 22, 7, 5, -1, -1, 0, false, 4, 12_498_828, 390),
        new(6_286, false, 32, 362, 0, 409, 24, 4, 3, -1, -1, 0, false, 5, 77_739_000, 65_763),
        new(6_287, true, 32, 359, 0, 509, 22, 7, 2, -1, -1, 0, false, 6, 14_250_000, 294),
        new(6_288, true, 31, 357, 0, 327, 17, 6, 2, -1, -1, 0, false, 7, 91_400_000, 131_331),
        new(6_289, false, 31, 357, 0, 327, 24, 5, 4, -1, -1, 0, false, 8, 82_020_000, 367),
        new(6_290, true, 33, 408, 0, 361, 7, 83_000, 4, -1, -1, 0, false, 9, 280_000_000, 390)
    ];

    private static readonly Grid2GlobalRace[] PreviousGlobalRaces =
    [
        new(6_264, true, 25, 322, 0, 319, 22, 6, 4, -1, -1, 0, false, -1, 10_000_000, 390),
        new(6_265, false, 31, 355, 0, 554, 24, 7, 3, -1, -1, 0, false, -1, 78_000_000, 65_763),
        new(6_266, true, 32, 359, 0, 509, 22, 7, 2, -1, -1, 0, false, -1, 12_000_000, 294)
    ];

    public static RaceNetResponse? TryBuild(
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
            "RaceNetGlobalDomination.GetEvent" => Html(BuildCurrentGlobalDomination(), headers),
            "RaceNetGlobalDomination.GetPreviousEvent" => Html(BuildPreviousGlobalDomination(), headers),
            "RaceNetGlobalDomination.PostScore" => Empty(headers),
            "RaceNetRivals.BlockRival" => Empty(headers),
            "RaceNetRivals.GetRivals" => Html(BuildRivals(), headers),
            "RaceNetRivals.PostRivalResults" => Empty(headers),
            "Rivals.GetRivalsSessionData" => Html(BuildRivalsSessionData(), headers),
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

    private static byte[] BuildCurrentGlobalDomination()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("RaceNetId", 698),
            EgoNetBinary.Tutc("Expires", DateTimeOffset.UtcNow.AddDays(7)),
            EgoNetBinary.Vector(
                "Races",
                CurrentGlobalRaces.Select(BuildGlobalRace).ToArray()));
    }

    private static byte[] BuildPreviousGlobalDomination()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector(
                "Races",
                PreviousGlobalRaces.Select(BuildGlobalRace).ToArray()));
    }

    private static Action<BinaryWriter> BuildGlobalRace(Grid2GlobalRace race)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si64("RaceNetId", race.RaceNetId),
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
                BuildGlobalLeaderboard(race)));
    }

    private static Action<BinaryWriter>[] BuildGlobalLeaderboard(Grid2GlobalRace race)
    {
        if (race.PersonalBest <= 0)
        {
            return [];
        }

        return
        [
            EgoNetBinary.DictValue(
                BuildPresence("Presence", RivalPresences[0]),
                EgoNetBinary.Si64("PersonalBest", race.PersonalBest),
                EgoNetBinary.Si32("VehicleId", race.BestVehicleId))
        ];
    }

    private static byte[] BuildRivals()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector(
                "RivalsList",
                RivalPresences.Select(BuildRival).ToArray()),
            EgoNetBinary.Tutc("NextRivalAlloc", DateTimeOffset.UtcNow.AddDays(7)));
    }

    private static Action<BinaryWriter> BuildRival(Grid2Presence rival)
    {
        return EgoNetBinary.DictValue(
            BuildPresence("Presence", rival),
            EgoNetBinary.Si64("PlatformId", checked((long)rival.SteamId)),
            EgoNetBinary.Si32("Type", rival.Type),
            EgoNetBinary.Bool("CanSeePresence", true),
            EgoNetBinary.Ui32("TotalXPWon", 0),
            EgoNetBinary.Ui32("RivalXPWon", 0));
    }

    private static EgoNetField BuildPresence(string name, Grid2Presence presence)
    {
        return EgoNetBinary.Dict(
            name,
            EgoNetBinary.Ui64("SteamId", presence.SteamId),
            EgoNetBinary.Dstr("Name", presence.Name),
            EgoNetBinary.Si64("EgonetId", presence.EgonetId));
    }

    private static byte[] BuildRivalsSessionData()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Results"));
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

    private static ulong BuildStableSteamId(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = BitConverter.ToUInt64(hashBytes, 0);
        return 76_561_198_000_000_000UL + hash % 10_000_000_000UL;
    }

    private sealed record Grid2Presence(
        ulong SteamId,
        string Name,
        long EgonetId,
        int Type);

    private sealed record Grid2GlobalRace(
        long RaceNetId,
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
        long PersonalBest,
        int BestVehicleId);
}
