using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Dirt4Leaderboard
{
    internal static byte[] Build(CapturedBody body, Dirt4DailyStore store, string player, bool friendsOnly,
        string? displayName = null)
    {
        var leaderboardId = EgoNetRequestParser.ReadTopLevelInteger(body, "LeaderboardId") ?? 0;
        var cumulative = EgoNetRequestParser.ReadTopLevelBoolean(body, "SortCumulative") == true;
        IReadOnlyList<Dirt4LeaderboardPresence>? presences = friendsOnly
            ? EgoNetRequestParser.ReadLeaderboardPresences(body)
            : null;
        store.BindDisplayName(leaderboardId, player, displayName);
        if (friendsOnly && presences!.Count > 0)
            store.BindPresence(leaderboardId, player, presences[0]);
        var snapshot = store.Leaderboard(leaderboardId, player, cumulative, presences)
            ?? new Dirt4LeaderboardSnapshot([], 0);
        var entries = snapshot.Entries;
        if (!friendsOnly)
        {
            var startRank = Math.Max(1, (int)(EgoNetRequestParser.ReadTopLevelInteger(body, "StartRank") ?? 1));
            var limit = Math.Clamp((int)(EgoNetRequestParser.ReadTopLevelInteger(body, "Limit") ?? 100), 1, 100);
            entries = entries.Where(entry => entry.Rank >= startRank).Take(limit).ToArray();
        }
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Entries", entries.Select(BuildEntry).ToArray()),
            EgoNetBinary.Si32("TotalEntries", snapshot.Entries.Count),
            EgoNetBinary.Si32("PlayerRank", snapshot.PlayerRank));
    }

    internal static int Rank(Dirt4DailyStore store, long leaderboardId, string player)
        => store.Leaderboard(leaderboardId, player, cumulative: false)?.PlayerRank ?? 0;

    private static Action<BinaryWriter> BuildEntry(Dirt4LeaderboardEntry entry) => EgoNetBinary.DictValue(
        EgoNetBinary.Dict("Presence",
            EgoNetBinary.Bool("IsCrossPlatform", entry.Presence.IsCrossPlatform),
            EgoNetBinary.Si64("EgonetId", entry.Presence.EgonetId),
            EgoNetBinary.Si64("AccountRef", entry.Presence.AccountRef),
            EgoNetBinary.Ui64("NetworkId", entry.Presence.NetworkId),
            EgoNetBinary.Dstr("Name", entry.Presence.Name)),
        EgoNetBinary.Si64("PersonalBest", entry.PersonalBest),
        EgoNetBinary.Si64("TimeDiff", entry.TimeDiff),
        EgoNetBinary.Si64("CumulativeBest", entry.CumulativeBest),
        EgoNetBinary.Si64("CumulativeDiff", entry.CumulativeDiff),
        EgoNetBinary.Si32("Rank", entry.Rank),
        EgoNetBinary.Ui32("VehicleId", entry.VehicleId),
        EgoNetBinary.Bool("IsFounder", false),
        EgoNetBinary.Bool("IsVIP", false),
        EgoNetBinary.Ui32("Nationality", entry.Nationality));
}
