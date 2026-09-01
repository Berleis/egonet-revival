using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using RaceNetShowdown.Server.Data;

namespace RaceNetShowdown.Server.RaceNet;

public static class EgoNetChallengePayloads
{
    private const int MaxFriendsReturnedToShowdown = 64;
    private const long CapturedFriendChallengeId = 237_570;
    private const long CapturedFriendGhostSlotId = 153_970;
    private const long CapturedCatalogChallengeIdBase = 238_000;
    private const long CapturedCatalogGhostSlotIdBase = 154_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly RaceNetChallengeDraft[] CapturedChallengeCatalog =
    [
        new(CareerEventId: 1005, GridPosition: 7, Difficulty: 1, ResultToBeat: 108235, TimeBased: true, VehicleId: 223, LiveryId: 1146, Strength: 7, Power: 8, Handling: 6),
        new(CareerEventId: 1005, GridPosition: 7, Difficulty: 1, ResultToBeat: 119431, TimeBased: true, VehicleId: 225, LiveryId: 1173, Strength: 3, Power: 10, Handling: 8),
        new(CareerEventId: 1009, GridPosition: 0, Difficulty: 0, ResultToBeat: 16065, TimeBased: false, VehicleId: 240, LiveryId: 1318, Strength: 9, Power: 8, Handling: 4),
        new(CareerEventId: 1009, GridPosition: 0, Difficulty: 1, ResultToBeat: 18955, TimeBased: false, VehicleId: 217, LiveryId: 1275, Strength: 7, Power: 8, Handling: 6),
        new(CareerEventId: 1010, GridPosition: 1, Difficulty: 1, ResultToBeat: 21700, TimeBased: false, VehicleId: 217, LiveryId: 1275, Strength: 7, Power: 8, Handling: 6),
        new(CareerEventId: 1013, GridPosition: 1, Difficulty: 1, ResultToBeat: 73780, TimeBased: true, VehicleId: 196, LiveryId: 1327, Strength: 8, Power: 7, Handling: 9),
        new(CareerEventId: 1015, GridPosition: 8, Difficulty: 1, ResultToBeat: 102018, TimeBased: true, VehicleId: 217, LiveryId: 1275, Strength: 7, Power: 8, Handling: 6),
        new(CareerEventId: 1017, GridPosition: 6, Difficulty: 1, ResultToBeat: 100697, TimeBased: true, VehicleId: 225, LiveryId: 1173, Strength: 3, Power: 10, Handling: 8),
        new(CareerEventId: 1029, GridPosition: 0, Difficulty: 1, ResultToBeat: 82253, TimeBased: true, VehicleId: 196, LiveryId: 1327, Strength: 8, Power: 7, Handling: 9),
        new(CareerEventId: 1038, GridPosition: 0, Difficulty: 1, ResultToBeat: 305000, TimeBased: false, VehicleId: 196, LiveryId: 1327, Strength: 8, Power: 7, Handling: 9),
        new(CareerEventId: 1366, GridPosition: 5, Difficulty: 1, ResultToBeat: 116710, TimeBased: true, VehicleId: 217, LiveryId: 1275, Strength: 7, Power: 8, Handling: 6),
        new(CareerEventId: 1370, GridPosition: 4, Difficulty: 1, ResultToBeat: 104616, TimeBased: true, VehicleId: 225, LiveryId: 1173, Strength: 3, Power: 10, Handling: 8),
        new(CareerEventId: 1415, GridPosition: 0, Difficulty: 1, ResultToBeat: 61941, TimeBased: true, VehicleId: 196, LiveryId: 1327, Strength: 8, Power: 7, Handling: 9)
    ];

    public static long? TryGetCatalogResultToBeat(long challengeId)
    {
        if (challengeId == CapturedFriendChallengeId)
        {
            return 1055;
        }

        var index = challengeId - CapturedCatalogChallengeIdBase;
        return index >= 0 && index < CapturedChallengeCatalog.Length
            ? CapturedChallengeCatalog[index].ResultToBeat
            : null;
    }

    public static byte[] Build(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        string format,
        IReadOnlyList<RaceNetPrincipal> principals,
        IReadOnlyList<RaceNetIssuedChallenge> issuedChallenges,
        RaceNetPrincipal? selectedPresence = null,
        int? requestedRaceEventId = null,
        int? requestedVehicleId = null)
    {
        var normalizedFormat = string.IsNullOrWhiteSpace(format)
            ? "BinaryDictionary"
            : format.Trim();

        byte[] Binary(FriendsOverviewPayloadMode mode) => BuildBinaryDictionary(
            functionName,
            snapshot,
            principals,
            issuedChallenges,
            selectedPresence,
            requestedRaceEventId,
            requestedVehicleId,
            mode);

        return normalizedFormat.ToLowerInvariant() switch
        {
            "empty" => [],
            "json" => Encoding.UTF8.GetBytes(BuildJson(functionName, snapshot, principals)),
            "keyvalue" => Encoding.UTF8.GetBytes(BuildKeyValue(functionName, snapshot, principals, false)),
            "nulkeyvalue" => Encoding.UTF8.GetBytes(BuildKeyValue(functionName, snapshot, principals, true)),
            "xmlattributes" => Encoding.UTF8.GetBytes(BuildXmlAttributes(functionName, snapshot, principals)),
            "xmlelements" => Encoding.UTF8.GetBytes(BuildXmlElements(functionName, snapshot, principals)),
            "binaryoverviewresponseonly" => Binary(FriendsOverviewPayloadMode.ResponseOnlyBool),
            "binaryoverviewresponseonlyusedsi32" => Binary(FriendsOverviewPayloadMode.ResponseOnlySi32),
            "binaryoverviewcountsonly" => Binary(FriendsOverviewPayloadMode.CountsOnly),
            "binaryoverviewcountsactiveonly" => Binary(FriendsOverviewPayloadMode.CountsActiveOnly),
            "binaryoverviewcountsrootactiveusedsi32" => Binary(FriendsOverviewPayloadMode.CountsRootActiveSi32),
            "binaryoverviewvectoractiveonly" => Binary(FriendsOverviewPayloadMode.VectorActiveOnly),
            "binaryoverviewfullechoactiveusedsi32" => Binary(FriendsOverviewPayloadMode.FullEchoActiveSi32),
            "binaryoverviewrequestorderactiveusedsi32" => Binary(FriendsOverviewPayloadMode.RequestOrderActiveSi32),
            "binaryoverviewwithresultsactiveusedsi32" => Binary(FriendsOverviewPayloadMode.WithResultsActiveSi32),
            "binaryoverviewrequestorderwithresultsactiveusedsi32" => Binary(FriendsOverviewPayloadMode.RequestOrderWithResultsActiveSi32),
            "binaryoverviewflatchallengeactiveusedsi32" => Binary(FriendsOverviewPayloadMode.FlatChallengeActiveSi32),
            "binaryoverviewnestedchallengeactiveusedsi32" => Binary(FriendsOverviewPayloadMode.NestedChallengeActiveSi32),
            "binaryoverviewcapturedsummary" => Binary(FriendsOverviewPayloadMode.CapturedSummary),
            "binaryoverviewresponseonlynoused" => Binary(FriendsOverviewPayloadMode.ResponseOnlyNoUsed),
            "binaryoverviewemptyfriends" => Binary(FriendsOverviewPayloadMode.EmptyFriends),
            "binaryoverviewrequestorder" => Binary(FriendsOverviewPayloadMode.RequestOrder),
            _ => Binary(FriendsOverviewPayloadMode.FullEcho)
        };
    }

    private static byte[] BuildBinaryDictionary(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals,
        IReadOnlyList<RaceNetIssuedChallenge> issuedChallenges,
        RaceNetPrincipal? selectedPresence,
        int? requestedRaceEventId,
        int? requestedVehicleId,
        FriendsOverviewPayloadMode overviewPayloadMode)
    {
        var friends = MergePrincipals(snapshot, principals);
        var fallbackRaceEventId = friends.FirstOrDefault()?.RaceEventId ?? 1;
        var fallbackVehicleId = friends.FirstOrDefault()?.VehicleId ?? 1;
        var responseRaceEventId = requestedRaceEventId ?? fallbackRaceEventId;
        var responseVehicleId = requestedVehicleId ?? fallbackVehicleId;
        var challengeRaceEventId = responseRaceEventId > 0
            ? responseRaceEventId
            : friends.FirstOrDefault()?.RaceEventId ?? -1;
        var challengeVehicleId = responseVehicleId > 0
            ? responseVehicleId
            : friends.FirstOrDefault()?.VehicleId ?? -1;
        var activeChallengeCount = GetActiveChallengeCount(snapshot, friends);
        var activeFriends = friends.Take(activeChallengeCount).ToArray();
        var highChallengeId = activeChallengeCount > 0
            ? activeFriends.Max(value => value.ChallengeId)
            : 0;
        var bestResult = activeChallengeCount > 0
            ? activeFriends.Min(value => value.BestResult)
            : 0;
        var tally = snapshot.OverallTally;

        return functionName switch
        {
            "AsynchronousChallengeService.GetHighestID" => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("Presence", 1),
                EgoNetBinary.Si32("Tally", checked((int)tally)),
                EgoNetBinary.Si32("ChallengeCount", activeChallengeCount),
                EgoNetBinary.Ui64("HighChallengeID", checked((ulong)highChallengeId)),
                EgoNetBinary.Ui64("BestResult", checked((ulong)bestResult))),

            "AsynchronousChallengeService.GetCompletedIssuedChallenges" => BuildCompletedIssuedChallengesPayload(),

            "AsynchronousChallengeService.GetFriendChallenges" => BuildFriendChallengesPayload(
                activeFriends,
                issuedChallenges,
                selectedPresence),

            _ => BuildFriendsOverviewPayload(
                friends,
                responseRaceEventId,
                responseVehicleId,
                challengeRaceEventId,
                challengeVehicleId,
                overviewPayloadMode,
                activeChallengeCount,
                Math.Min(issuedChallenges.Count, 20))
        };
    }

    private static byte[] BuildFriendsOverviewPayload(
        IReadOnlyList<RaceNetFriendChallenge> friends,
        int responseRaceEventId,
        int responseVehicleId,
        int challengeRaceEventId,
        int challengeVehicleId,
        FriendsOverviewPayloadMode mode,
        int activeChallengeCount,
        int usedCount)
    {
        var overviewFriends = mode switch
        {
            FriendsOverviewPayloadMode.EmptyFriends => [],
            FriendsOverviewPayloadMode.NestedChallengeActiveSi32 => friends.Take(activeChallengeCount).ToArray(),
            _ => friends
        };
        var friendsData = EgoNetBinary.Vector(
            "FriendsData",
            overviewFriends
                .Select((friend, index) => ToFriendOverviewValue(
                    friend,
                    challengeRaceEventId,
                    challengeVehicleId,
                    mode,
                    index < activeChallengeCount))
                .ToArray());

        return mode switch
        {
            FriendsOverviewPayloadMode.CountsActiveOnly => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.CountsOnly => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.CountsRootActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.VectorActiveOnly => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.ResponseOnlyBool => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Bool("Used", true)),

            FriendsOverviewPayloadMode.ResponseOnlySi32 => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.ResponseOnlyNoUsed => EgoNetBinary.Dictionary(friendsData),

            FriendsOverviewPayloadMode.EmptyFriends => EgoNetBinary.Dictionary(
                friendsData,
                EgoNetBinary.Bool("Used", true)),

            FriendsOverviewPayloadMode.RequestOrder => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                EgoNetBinary.Vector("Principals", friends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                friendsData,
                EgoNetBinary.Bool("Used", true)),

            FriendsOverviewPayloadMode.RequestOrderActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.WithResultsActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.NestedChallengeActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", challengeRaceEventId),
                EgoNetBinary.Si32("VehicleID", challengeVehicleId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.CapturedSummary => EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("FriendsData", overviewFriends
                    .Select((friend, index) => ToCapturedOverviewFriendValue(friend, index < activeChallengeCount))
                    .ToArray()),
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.RequestOrderWithResultsActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.FlatChallengeActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            FriendsOverviewPayloadMode.FullEchoActiveSi32 => EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("Principals", overviewFriends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                friendsData,
                EgoNetBinary.Si32("Used", usedCount)),

            _ => EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("Principals", friends.Select(ToPrincipalValue).ToArray()),
                EgoNetBinary.Si32("RaceEventID", responseRaceEventId),
                EgoNetBinary.Si32("VehicleID", responseVehicleId),
                friendsData,
                EgoNetBinary.Bool("Used", true))
        };
    }

    private static byte[] BuildFriendChallengesPayload(
        IReadOnlyList<RaceNetFriendChallenge> activeFriends,
        IReadOnlyList<RaceNetIssuedChallenge> issuedChallenges,
        RaceNetPrincipal? selectedPresence)
    {
        var issued = SelectIssuedChallenges(issuedChallenges, selectedPresence);
        var challenges = issued.Count > 0
            ? issued
            : SelectFriend(activeFriends, selectedPresence) is { } selectedFriend
                ? [ToCatalogChallenge(selectedFriend)]
                : [];
        var challengeEntries = challenges
            .Select(ToFriendChallengeListValue)
            .ToArray();

        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Challenges", challengeEntries));
    }

    private static byte[] BuildCompletedIssuedChallengesPayload()
    {
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Dict("Overview",
                EgoNetBinary.Si32("Wins", 0),
                EgoNetBinary.Si32("Losses", 0),
                EgoNetBinary.Si32("OverallTally", 0)),
            EgoNetBinary.Vector("Completed"));
    }

    private static IReadOnlyList<RaceNetIssuedChallenge> SelectIssuedChallenges(
        IReadOnlyList<RaceNetIssuedChallenge> issuedChallenges,
        RaceNetPrincipal? selectedPresence)
    {
        if (issuedChallenges.Count == 0)
        {
            return [];
        }

        if (selectedPresence is null)
        {
            return issuedChallenges;
        }

        var selected = issuedChallenges
            .Where(value =>
                value.Presence.SteamId == selectedPresence.SteamId ||
                string.Equals(value.Presence.Name, selectedPresence.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.ChallengeId)
            .Take(20)
            .ToArray();

        return selected.Length == 0
            ? []
            : selected;
    }

    private static RaceNetIssuedChallenge ToCatalogChallenge(RaceNetFriendChallenge friend)
    {
        var catalogIndex = GetCatalogIndex(friend);
        var challengeData = CapturedChallengeCatalog[catalogIndex];

        return new RaceNetIssuedChallenge(
            CapturedCatalogChallengeIdBase + catalogIndex,
            friend.EgonetId,
            new RaceNetPrincipal(ReadSteamId(friend), friend.Name),
            challengeData,
            DateTimeOffset.UtcNow.AddDays(7),
            CapturedCatalogGhostSlotIdBase + catalogIndex);
    }

    private static int GetCatalogIndex(RaceNetFriendChallenge friend)
    {
        var steamId = ReadSteamId(friend);
        return checked((int)(steamId % (ulong)CapturedChallengeCatalog.Length));
    }

    private static RaceNetFriendChallenge? SelectFriend(
        IReadOnlyList<RaceNetFriendChallenge> activeFriends,
        RaceNetPrincipal? selectedPresence)
    {
        if (activeFriends.Count == 0)
        {
            return null;
        }

        if (selectedPresence is null)
        {
            return activeFriends[0];
        }

        var steamId = selectedPresence.SteamId.ToString(CultureInfo.InvariantCulture);
        return activeFriends.FirstOrDefault(friend => friend.SteamId == steamId)
            ?? activeFriends.FirstOrDefault(friend => string.Equals(friend.Name, selectedPresence.Name, StringComparison.OrdinalIgnoreCase))
            ?? activeFriends[0];
    }

    private static string BuildJson(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals)
    {
        return JsonSerializer.Serialize(ToContract(functionName, snapshot, principals), JsonOptions);
    }

    private static string BuildKeyValue(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals,
        bool nulSeparated)
    {
        var friends = MergePrincipals(snapshot, principals);
        var separator = nulSeparated ? "\0" : "\n";
        var builder = new StringBuilder();

        Append("Function", functionName);
        Append("HighChallengeID", snapshot.HighChallengeId);
        Append("ChallengeCount", snapshot.ChallengeCount);
        Append("NumberOfFriends", friends.Count);
        Append("OverallTally", snapshot.OverallTally);
        Append("BestResult", snapshot.BestResult);

        for (var i = 0; i < friends.Count; i++)
        {
            var friend = friends[i];
            var prefix = $"FriendsData.{i}.";
            Append(prefix + "EgonetId", friend.EgonetId);
            Append(prefix + "SteamId", friend.SteamId);
            Append(prefix + "Name", friend.Name);
            Append(prefix + "Presence", friend.Presence);
            Append(prefix + "Challenges", 1);
            Append(prefix + "ChallengeData.ChallengeID", friend.ChallengeId);
            Append(prefix + "ChallengeData.RaceEventID", friend.RaceEventId);
            Append(prefix + "ChallengeData.VehicleID", friend.VehicleId);
            Append(prefix + "ChallengeData.OwnerId", friend.EgonetId);
            Append(prefix + "ChallengeData.ExpiresAt", ToUnixSeconds(friend.ExpiresAt));
            Append(prefix + "ChallengeData.GhostSlotID", friend.GhostSlotId);
            Append(prefix + "ChallengeData.Results.BestResult", friend.BestResult);
            Append(prefix + "ChallengeData.Results.YourBestResult", friend.YourBestResult);
            Append(prefix + "ChallengeData.Results.Tally", friend.Tally);
        }

        if (nulSeparated)
        {
            builder.Append('\0');
        }

        return builder.ToString();

        void Append(string key, object value)
        {
            builder.Append(key);
            builder.Append('=');
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            builder.Append(separator);
        }
    }

    private static string BuildXmlElements(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals)
    {
        var friends = MergePrincipals(snapshot, principals);
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="utf-8"?>""");
        builder.Append("<EgoNetResponse>");
        Element(builder, "Function", functionName);
        Element(builder, "HighChallengeID", snapshot.HighChallengeId);
        Element(builder, "ChallengeCount", snapshot.ChallengeCount);
        Element(builder, "NumberOfFriends", friends.Count);
        Element(builder, "OverallTally", snapshot.OverallTally);
        Element(builder, "BestResult", snapshot.BestResult);
        builder.Append("<FriendsData>");
        foreach (var friend in friends)
        {
            AppendFriendElements(builder, friend);
        }
        builder.Append("</FriendsData>");
        builder.Append("<ChallengeData>");
        foreach (var friend in friends)
        {
            AppendChallengeElements(builder, friend);
        }
        builder.Append("</ChallengeData>");
        builder.Append("</EgoNetResponse>");
        return builder.ToString();
    }

    private static string BuildXmlAttributes(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals)
    {
        var friends = MergePrincipals(snapshot, principals);
        var builder = new StringBuilder();
        builder.Append("""<?xml version="1.0" encoding="utf-8"?>""");
        builder.Append("<EgoNetResponse");
        Attribute(builder, "Function", functionName);
        Attribute(builder, "HighChallengeID", snapshot.HighChallengeId);
        Attribute(builder, "ChallengeCount", snapshot.ChallengeCount);
        Attribute(builder, "NumberOfFriends", friends.Count);
        Attribute(builder, "OverallTally", snapshot.OverallTally);
        Attribute(builder, "BestResult", snapshot.BestResult);
        builder.Append('>');
        builder.Append("<FriendsData>");
        foreach (var friend in friends)
        {
            builder.Append("<PlayerData");
            Attribute(builder, "EgonetId", friend.EgonetId);
            Attribute(builder, "SteamId", friend.SteamId);
            Attribute(builder, "Name", friend.Name);
            Attribute(builder, "Presence", friend.Presence);
            Attribute(builder, "Challenges", 1);
            Attribute(builder, "Tally", friend.Tally);
            builder.Append('>');
            AppendChallengeAttributes(builder, friend);
            builder.Append("</PlayerData>");
        }
        builder.Append("</FriendsData>");
        builder.Append("</EgoNetResponse>");
        return builder.ToString();
    }

    private static object ToContract(
        string functionName,
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals)
    {
        var friends = MergePrincipals(snapshot, principals);
        return new
        {
            Function = functionName,
            snapshot.HighChallengeId,
            ChallengeCount = friends.Count,
            NumberOfFriends = friends.Count,
            snapshot.OverallTally,
            snapshot.BestResult,
            FriendsData = friends.Select(friend => new
            {
                PlayerData = new
                {
                    friend.EgonetId,
                    friend.SteamId,
                    friend.Name,
                    friend.Presence,
                    Challenges = 1,
                    friend.Tally
                },
                ChallengeData = new
                {
                    ChallengeID = friend.ChallengeId,
                    RaceEventID = friend.RaceEventId,
                    VehicleID = friend.VehicleId,
                    OwnerId = friend.EgonetId,
                    ExpiresAt = ToUnixSeconds(friend.ExpiresAt),
                    GhostSlotID = friend.GhostSlotId,
                    Results = new
                    {
                        friend.BestResult,
                        friend.YourBestResult,
                        friend.Tally
                    }
                }
            })
        };
    }

    private static void AppendFriendElements(StringBuilder builder, RaceNetFriendChallenge friend)
    {
        builder.Append("<PlayerData>");
        Element(builder, "EgonetId", friend.EgonetId);
        Element(builder, "SteamId", friend.SteamId);
        Element(builder, "Name", friend.Name);
        Element(builder, "Presence", friend.Presence);
        Element(builder, "Challenges", 1);
        Element(builder, "Tally", friend.Tally);
        AppendChallengeElements(builder, friend);
        builder.Append("</PlayerData>");
    }

    private static void AppendChallengeElements(StringBuilder builder, RaceNetFriendChallenge friend)
    {
        builder.Append("<ChallengeData>");
        Element(builder, "Id", friend.ChallengeId);
        Element(builder, "ChallengeID", friend.ChallengeId);
        Element(builder, "RaceEventID", friend.RaceEventId);
        Element(builder, "VehicleID", friend.VehicleId);
        Element(builder, "OwnerId", friend.EgonetId);
        Element(builder, "ExpiresAt", ToUnixSeconds(friend.ExpiresAt));
        Element(builder, "GhostSlotID", friend.GhostSlotId);
        Element(builder, "Metadata", string.Empty);
        builder.Append("<Results>");
        Element(builder, "OwnerId", friend.EgonetId);
        Element(builder, "BestResult", friend.BestResult);
        Element(builder, "YourBestResult", friend.YourBestResult);
        Element(builder, "Tally", friend.Tally);
        builder.Append("</Results>");
        builder.Append("</ChallengeData>");
    }

    private static void AppendChallengeAttributes(StringBuilder builder, RaceNetFriendChallenge friend)
    {
        builder.Append("<ChallengeData");
        Attribute(builder, "Id", friend.ChallengeId);
        Attribute(builder, "ChallengeID", friend.ChallengeId);
        Attribute(builder, "RaceEventID", friend.RaceEventId);
        Attribute(builder, "VehicleID", friend.VehicleId);
        Attribute(builder, "OwnerId", friend.EgonetId);
        Attribute(builder, "ExpiresAt", ToUnixSeconds(friend.ExpiresAt));
        Attribute(builder, "GhostSlotID", friend.GhostSlotId);
        Attribute(builder, "Metadata", string.Empty);
        builder.Append("><Results");
        Attribute(builder, "OwnerId", friend.EgonetId);
        Attribute(builder, "BestResult", friend.BestResult);
        Attribute(builder, "YourBestResult", friend.YourBestResult);
        Attribute(builder, "Tally", friend.Tally);
        builder.Append(" /></ChallengeData>");
    }

    private static void Element(StringBuilder builder, string name, object value)
    {
        builder.Append('<');
        builder.Append(name);
        builder.Append('>');
        builder.Append(SecurityElement.Escape(Convert.ToString(value, CultureInfo.InvariantCulture)));
        builder.Append("</");
        builder.Append(name);
        builder.Append('>');
    }

    private static void Attribute(StringBuilder builder, string name, object value)
    {
        builder.Append(' ');
        builder.Append(name);
        builder.Append("=\"");
        builder.Append(SecurityElement.Escape(Convert.ToString(value, CultureInfo.InvariantCulture)));
        builder.Append('"');
    }

    private static long ToUnixSeconds(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }

    private static IReadOnlyList<RaceNetFriendChallenge> MergePrincipals(
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetPrincipal> principals)
    {
        if (principals.Count == 0)
        {
            return snapshot.Friends;
        }

        var count = Math.Min(principals.Count, MaxFriendsReturnedToShowdown);
        var merged = new List<RaceNetFriendChallenge>(count);
        var friendsBySteamId = snapshot.Friends
            .Where(value => !string.IsNullOrWhiteSpace(value.SteamId))
            .GroupBy(value => value.SteamId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.First(), StringComparer.OrdinalIgnoreCase);
        var friendsByName = snapshot.Friends
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value.Key, value => value.First(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var principal = principals[i];
            var steamId = principal.SteamId.ToString(CultureInfo.InvariantCulture);
            var friend = friendsBySteamId.TryGetValue(steamId, out var steamMatch)
                ? steamMatch
                : friendsByName.TryGetValue(principal.Name, out var nameMatch)
                    ? nameMatch
                    : new RaceNetFriendChallenge(
                        EgonetId: 10_000L + i + 1,
                        SteamId: steamId,
                        Name: principal.Name,
                        Presence: 1,
                        ChallengeId: 0,
                        RaceEventId: 1,
                        VehicleId: 1,
                        BestResult: 0,
                        YourBestResult: 0,
                        Tally: 0,
                        ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
                        GhostSlotId: 0);

            merged.Add(friend with
            {
                EgonetId = friend.EgonetId > 0 ? friend.EgonetId : 10_000L + i + 1,
                SteamId = steamId,
                Name = principal.Name
            });
        }

        return merged
            .OrderByDescending(value => value.ChallengeId > 0)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Action<BinaryWriter> ToFriendOverviewValue(
        RaceNetFriendChallenge friend,
        int raceEventId,
        int vehicleId,
        FriendsOverviewPayloadMode mode,
        bool hasChallenge)
    {
        if (mode is FriendsOverviewPayloadMode.CountsOnly or FriendsOverviewPayloadMode.CountsActiveOnly or FriendsOverviewPayloadMode.CountsRootActiveSi32)
        {
            return EgoNetBinary.DictValue(
                EgoNetBinary.Si32("Presence", friend.Presence),
                EgoNetBinary.Si32("Challenges", hasChallenge || mode == FriendsOverviewPayloadMode.CountsOnly ? 1 : 0));
        }

        var challenges = hasChallenge || mode is not (
            FriendsOverviewPayloadMode.VectorActiveOnly or
            FriendsOverviewPayloadMode.FullEchoActiveSi32 or
            FriendsOverviewPayloadMode.RequestOrderActiveSi32 or
            FriendsOverviewPayloadMode.WithResultsActiveSi32 or
            FriendsOverviewPayloadMode.RequestOrderWithResultsActiveSi32 or
            FriendsOverviewPayloadMode.FlatChallengeActiveSi32 or
            FriendsOverviewPayloadMode.NestedChallengeActiveSi32)
            ? [ToFriendChallengeValue(friend, raceEventId, vehicleId, mode)]
            : Array.Empty<Action<BinaryWriter>>();

        return EgoNetBinary.DictValue(
            EgoNetBinary.Si32("Presence", friend.Presence),
            EgoNetBinary.Vector("Challenges", challenges));
    }

    private static Action<BinaryWriter> ToCapturedOverviewFriendValue(
        RaceNetFriendChallenge friend,
        bool hasChallenge)
    {
        var challengeCount = hasChallenge ? 1 : 0;
        var highChallengeId = hasChallenge ? friend.ChallengeId : 0;
        var bestResult = hasChallenge ? friend.BestResult : 0;
        var tally = friend.Tally;

        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("Presence",
                EgoNetBinary.Ui64("SteamId", ReadSteamId(friend)),
                EgoNetBinary.Dstr("Name", friend.Name),
                EgoNetBinary.Si64("EgonetId", friend.EgonetId)),
            EgoNetBinary.Si32("Tally", checked((int)tally)),
            EgoNetBinary.Si32("ChallengeCount", challengeCount),
            EgoNetBinary.Si64("HighChallengeID", highChallengeId),
            EgoNetBinary.Si64("BestResult", bestResult));
    }

    private static Action<BinaryWriter> ToFriendChallengeValue(
        RaceNetFriendChallenge friend,
        int raceEventId,
        int vehicleId,
        FriendsOverviewPayloadMode mode)
    {
        if (mode == FriendsOverviewPayloadMode.FlatChallengeActiveSi32)
        {
            return ToChallengeValue(friend, raceEventId, vehicleId);
        }

        if (mode == FriendsOverviewPayloadMode.NestedChallengeActiveSi32)
        {
            var fields = ToChallengeEnvelopeFields(friend)
                .Concat(
                [
                    EgoNetBinary.Si32("RaceEventID", raceEventId),
                    EgoNetBinary.Si32("VehicleID", vehicleId),
                    EgoNetBinary.Vector("Results", ToResultValue(friend))
                ])
                .ToArray();

            return EgoNetBinary.DictValue(
                EgoNetBinary.Dict("ChallengeData", fields),
                EgoNetBinary.Si32("Presence", friend.Presence),
                EgoNetBinary.Ui64("GhostSlotID", checked((ulong)friend.GhostSlotId)));
        }

        var challengeFields = mode is FriendsOverviewPayloadMode.WithResultsActiveSi32 or FriendsOverviewPayloadMode.RequestOrderWithResultsActiveSi32
            ? ToChallengeEnvelopeFields(friend)
                .Concat(
                [
                    EgoNetBinary.Si32("RaceEventID", raceEventId),
                    EgoNetBinary.Si32("VehicleID", vehicleId),
                    EgoNetBinary.Vector("Results", ToResultValue(friend))
                ])
                .ToArray()
            : ToChallengeEnvelopeFields(friend);

        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("ChallengeData", challengeFields),
            EgoNetBinary.Si32("Presence", friend.Presence),
            EgoNetBinary.Ui64("GhostSlotID", checked((ulong)friend.GhostSlotId)));
    }

    private static Action<BinaryWriter> ToChallengeEnvelopeValue(
        RaceNetFriendChallenge friend,
        int raceEventId,
        int vehicleId)
    {
        var fields = ToChallengeEnvelopeFields(friend)
            .Concat(
            [
                EgoNetBinary.Si32("RaceEventID", raceEventId),
                EgoNetBinary.Si32("VehicleID", vehicleId),
                EgoNetBinary.Vector("Results", ToResultValue(friend))
            ])
            .ToArray();

        return EgoNetBinary.DictValue(fields);
    }

    private static Action<BinaryWriter> ToCapturedChallengeEnvelopeValue(RaceNetFriendChallenge friend)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("ChallengeData",
                EgoNetBinary.Si64("Id", friend.ChallengeId),
                EgoNetBinary.Si64("OwnerId", friend.EgonetId),
                EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
                EgoNetBinary.Blob("Metadata", [])),
            EgoNetBinary.Si64("ChallengeID", friend.ChallengeId),
            EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
            EgoNetBinary.Si64("GhostSlotID", friend.GhostSlotId));
    }

    private static Action<BinaryWriter> ToFriendChallengeScreenEnvelopeValue(RaceNetFriendChallenge friend)
    {
        var challengeData = new RaceNetChallengeDraft(
            CareerEventId: 1005,
            GridPosition: 7,
            Difficulty: 1,
            ResultToBeat: 108235,
            TimeBased: true,
            VehicleId: 223,
            LiveryId: 1146,
            Strength: 7,
            Power: 8,
            Handling: 6);
        var issuedChallenge = new RaceNetIssuedChallenge(
            friend.ChallengeId,
            friend.EgonetId,
            new RaceNetPrincipal(ReadSteamId(friend), friend.Name),
            challengeData,
            friend.ExpiresAt,
            friend.GhostSlotId);

        return ToIssuedChallengeEnvelopeValue(issuedChallenge);
    }

    private static Action<BinaryWriter> ToIssuedChallengeEnvelopeValue(RaceNetIssuedChallenge challenge)
    {
        var data = challenge.ChallengeData;

        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("ChallengeData",
                EgoNetBinary.Si32("CareerEventID", data.CareerEventId),
                EgoNetBinary.Si32("GridPosition", data.GridPosition),
                EgoNetBinary.Si32("Difficulty", data.Difficulty),
                EgoNetBinary.Si64("ResultToBeat", data.ResultToBeat),
                EgoNetBinary.Bool("TimeBased", data.TimeBased),
                EgoNetBinary.Si32("VehicleID", data.VehicleId),
                EgoNetBinary.Si32("LiveryID", data.LiveryId),
                EgoNetBinary.Si32("Strength", data.Strength),
                EgoNetBinary.Si32("Power", data.Power),
                EgoNetBinary.Si32("Handling", data.Handling),
                EgoNetBinary.Si64("ChallengeID", challenge.ChallengeId),
                EgoNetBinary.Si64("Id", challenge.ChallengeId),
                EgoNetBinary.Si64("OwnerId", challenge.EgonetId),
                EgoNetBinary.Tutc("ExpiresAt", challenge.ExpiresAt),
                EgoNetBinary.Blob("Metadata", [])),
            EgoNetBinary.Dict("Presence",
                EgoNetBinary.Ui64("SteamId", challenge.Presence.SteamId),
                EgoNetBinary.Dstr("Name", challenge.Presence.Name),
                EgoNetBinary.Si64("EgonetId", challenge.EgonetId)),
            EgoNetBinary.Si64("GhostSlotID", challenge.GhostSlotId),
            EgoNetBinary.Vector("Results", ToChallengeResultValue(challenge, challenge.Presence.Name)));
    }

    private static Action<BinaryWriter> ToChallengeEnvelopeValueSi32(
        RaceNetFriendChallenge friend,
        int raceEventId,
        int vehicleId)
    {
        var fields = ToChallengeEnvelopeFieldsSi32(friend)
            .Concat(
            [
                EgoNetBinary.Si32("RaceEventID", raceEventId),
                EgoNetBinary.Si32("VehicleID", vehicleId),
                EgoNetBinary.Vector("Results", ToResultValue(friend))
            ])
            .ToArray();

        return EgoNetBinary.DictValue(fields);
    }

    private static EgoNetField[] ToChallengeEnvelopeFields(RaceNetFriendChallenge friend)
    {
        return
        [
            EgoNetBinary.Dict("ChallengeData",
                EgoNetBinary.Ui64("Id", checked((ulong)friend.ChallengeId)),
                EgoNetBinary.Ui64("OwnerId", ReadSteamId(friend)),
                EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
                EgoNetBinary.Blob("Metadata", [])),
            EgoNetBinary.Ui64("ChallengeID", checked((ulong)friend.ChallengeId)),
            EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
            EgoNetBinary.Ui64("GhostSlotID", checked((ulong)friend.GhostSlotId))
        ];
    }

    private static EgoNetField[] ToChallengeEnvelopeFieldsSi32(RaceNetFriendChallenge friend)
    {
        return
        [
            EgoNetBinary.Dict("ChallengeData",
                EgoNetBinary.Si32("Id", checked((int)friend.ChallengeId)),
                EgoNetBinary.Ui64("OwnerId", ReadSteamId(friend)),
                EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
                EgoNetBinary.Dstr("Metadata", string.Empty)),
            EgoNetBinary.Si32("ChallengeID", checked((int)friend.ChallengeId)),
            EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
            EgoNetBinary.Si32("GhostSlotID", friend.GhostSlotId)
        ];
    }

    private static EgoNetField[] ToChallengeOverviewFields(
        int challengeCount,
        long tally,
        long highChallengeId,
        long bestResult)
    {
        return
        [
            EgoNetBinary.Si32("Presence", 1),
            EgoNetBinary.Si32("Tally", checked((int)tally)),
            EgoNetBinary.Si32("ChallengeCount", challengeCount),
            EgoNetBinary.Ui64("HighChallengeID", checked((ulong)Math.Max(highChallengeId, 0))),
            EgoNetBinary.Ui64("BestResult", checked((ulong)Math.Max(bestResult, 0)))
        ];
    }

    private static Action<BinaryWriter> ToPrincipalValue(RaceNetFriendChallenge friend)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Ui64("SteamId", ReadSteamId(friend)),
            EgoNetBinary.Dstr("Name", friend.Name));
    }

    private static Action<BinaryWriter> ToFriendValue(RaceNetFriendChallenge friend)
    {
        var ownerId = ReadSteamId(friend);
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si32("Used", 1),
            EgoNetBinary.Ui64("EgonetId", ownerId),
            EgoNetBinary.Ui64("SteamId", ownerId),
            EgoNetBinary.Ui64("OwnerId", ownerId),
            EgoNetBinary.Dstr("Name", friend.Name),
            EgoNetBinary.Si32("Presence", friend.Presence),
            EgoNetBinary.Si32("Tally", checked((int)friend.Tally)),
            EgoNetBinary.Vector("Challenges", ToChallengeValue(friend)));
    }

    private static Action<BinaryWriter> ToChallengeValue(
        RaceNetFriendChallenge friend,
        int? raceEventId = null,
        int? vehicleId = null)
    {
        var ownerId = ReadSteamId(friend);
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si32("Id", checked((int)friend.ChallengeId)),
            EgoNetBinary.Si32("ChallengeID", checked((int)friend.ChallengeId)),
            EgoNetBinary.Si32("RaceEventID", raceEventId ?? friend.RaceEventId),
            EgoNetBinary.Si32("VehicleID", vehicleId ?? friend.VehicleId),
            EgoNetBinary.Ui64("OwnerId", ownerId),
            EgoNetBinary.Tutc("ExpiresAt", friend.ExpiresAt),
            EgoNetBinary.Si32("GhostSlotID", friend.GhostSlotId),
            EgoNetBinary.Dstr("Metadata", string.Empty),
            EgoNetBinary.Si32("BestResult", checked((int)friend.BestResult)),
            EgoNetBinary.Si32("YourBestResult", checked((int)friend.YourBestResult)),
            EgoNetBinary.Si32("Tally", checked((int)friend.Tally)),
            EgoNetBinary.Vector("Results", ToResultValue(friend)));
    }

    private static Action<BinaryWriter> ToResultValue(RaceNetFriendChallenge friend)
    {
        return EgoNetBinary.DictValue(
            EgoNetBinary.Si32("Presence", friend.Presence),
            EgoNetBinary.Si32("Tally", checked((int)friend.Tally)),
            EgoNetBinary.Si32("ChallengeCount", 1),
            EgoNetBinary.Ui64("HighChallengeID", checked((ulong)friend.ChallengeId)),
            EgoNetBinary.Ui64("BestResult", checked((ulong)friend.BestResult)));
    }

    private static Action<BinaryWriter> ToChallengeResultValue(
        RaceNetIssuedChallenge challenge,
        string winnerName)
    {
        var actualResult = challenge.ChallengeData.ResultToBeat > int.MaxValue
            ? int.MaxValue
            : checked((int)challenge.ChallengeData.ResultToBeat);

        return EgoNetBinary.DictValue(
            EgoNetBinary.Si32("Presence", 1),
            EgoNetBinary.Si32("Tally", 1),
            EgoNetBinary.Si32("ChallengeCount", 1),
            EgoNetBinary.Si64("HighChallengeID", Math.Max(challenge.ChallengeId, 0)),
            EgoNetBinary.Si64("BestResult", Math.Max(challenge.ChallengeData.ResultToBeat, 0)),
            EgoNetBinary.Dstr("WinnerLocalisedDisplayName", winnerName),
            EgoNetBinary.Si32("CumulativeTally", 1),
            EgoNetBinary.Dstr("TrackName", "BAJA, CALIFORNIA"),
            EgoNetBinary.Dstr("DisciplineLogoUrl", "./assets/placeholder_32.png"),
            EgoNetBinary.Si64("ActualResult", actualResult),
            EgoNetBinary.Dstr("OtherPlayerLocalisedDisplayName", challenge.Presence.Name),
            EgoNetBinary.Dstr("OtherPlayerLogo", "./Content/avatars/48x48/logged_out_icon.png"),
            EgoNetBinary.Si32("OtherPlayerLevel", 48),
            EgoNetBinary.Si32("OtherPlayerXP", 150));
    }

    private static Action<BinaryWriter> ToFriendChallengeListValue(RaceNetIssuedChallenge challenge)
    {
        var data = challenge.ChallengeData;

        return EgoNetBinary.DictValue(
            EgoNetBinary.Dict("ChallengeData",
                EgoNetBinary.Si32("CareerEventID", data.CareerEventId),
                EgoNetBinary.Si32("GridPosition", data.GridPosition),
                EgoNetBinary.Si32("Difficulty", data.Difficulty),
                EgoNetBinary.Si64("ResultToBeat", data.ResultToBeat),
                EgoNetBinary.Bool("TimeBased", data.TimeBased),
                EgoNetBinary.Si32("VehicleID", data.VehicleId),
                EgoNetBinary.Si32("LiveryID", data.LiveryId),
                EgoNetBinary.Si32("Strength", data.Strength),
                EgoNetBinary.Si32("Power", data.Power),
                EgoNetBinary.Si32("Handling", data.Handling)),
            EgoNetBinary.Si64("ChallengeID", challenge.ChallengeId),
            EgoNetBinary.Tutc("ExpiresAt", challenge.ExpiresAt),
            EgoNetBinary.Si64("GhostSlotID", challenge.GhostSlotId));
    }

    private static ulong ReadSteamId(RaceNetFriendChallenge friend)
    {
        return ulong.TryParse(friend.SteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId)
            ? steamId
            : checked((ulong)friend.EgonetId);
    }

    private static int GetActiveChallengeCount(
        RaceNetChallengeSnapshot snapshot,
        IReadOnlyList<RaceNetFriendChallenge> friends)
    {
        if (friends.Count == 0)
        {
            return 0;
        }

        var requestedChallengeCount = snapshot.ChallengeCount;
        return Math.Min(friends.Count, Math.Min(requestedChallengeCount, 20));
    }

    private enum FriendsOverviewPayloadMode
    {
        FullEcho,
        ResponseOnlyBool,
        ResponseOnlySi32,
        ResponseOnlyNoUsed,
        CountsOnly,
        CountsActiveOnly,
        CountsRootActiveSi32,
        VectorActiveOnly,
        FullEchoActiveSi32,
        RequestOrderActiveSi32,
        WithResultsActiveSi32,
        RequestOrderWithResultsActiveSi32,
        FlatChallengeActiveSi32,
        NestedChallengeActiveSi32,
        CapturedSummary,
        EmptyFriends,
        RequestOrder
    }
}
