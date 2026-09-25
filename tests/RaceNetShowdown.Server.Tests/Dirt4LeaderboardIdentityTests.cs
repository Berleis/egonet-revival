using System.Text.Json.Nodes;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4LeaderboardIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private const string Player = "remote:test-driver";
    private const ulong OwnId = 76561198000000101;
    private const ulong FriendId = 76561198000000102;
    private static readonly Dirt4LeaderboardPresence Self = new(false, 0, 0, OwnId, "DxNitro_");
    private static readonly Dirt4LeaderboardPresence Friend = new(false, 0, 0, FriendId, "Maxam07");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FriendsGlobalFriendsKeepsOwnNameAndSteamIdAcrossRestart(bool community)
    {
        var (store, leaderboard) = Create(community);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var friends = Show(store, leaderboard, true, [Friend, Self]);
            AssertOwnEntry(friends);
            AssertOwnEntry(Show(store, leaderboard, false, []));
            AssertOwnEntry(Show(store, leaderboard, true, [Self, Friend]));
            store = new Dirt4DailyStore(store.Export());
        }
        Assert.Equal(100_000, Assert.Single(store.Leaderboard(leaderboard, Player, true)!.Entries).PersonalBest);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void LegacyBorrowedIdentityIsClearedOnGlobalAndCorrectlyRebound(bool community, bool nameAlreadyRepaired)
    {
        var (store, leaderboard) = Create(community);
        store.BindPresence(leaderboard, Player, Friend);
        // Reproduce state written before player-bound identity metadata existed.
        var state = JsonNode.Parse(store.Export())!;
        state["Presences"]![Player]!.AsObject().Remove("BoundToPlayer");
        if (nameAlreadyRepaired) state["Presences"]![Player]!["Name"] = Self.Name;
        store = new Dirt4DailyStore(state.ToJsonString());
        var global = Show(store, leaderboard, false, []);
        Assert.Contains("DxNitro_", global);
        Assert.Contains("NetworkId: ui64 value=0", global);
        Assert.DoesNotContain("Maxam07", global);
        Assert.DoesNotContain(FriendId.ToString(), global);
        if (community)
        {
            var repaired = store.Find(30_000_000)!.Runs[Player];
            Assert.Equal("DxNitro_", repaired.DisplayName);
            Assert.Equal(0UL, repaired.NetworkId);
            Assert.Equal(100_000, repaired.TimeMs);
        }
        AssertOwnEntry(Show(store, leaderboard, true, [Friend, Self]));
        AssertOwnEntry(Show(new Dirt4DailyStore(store.Export()), leaderboard, false, []));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingSelfInFriendsDoesNotBorrowAFriendOrDropTheKnownIdentity(bool community)
    {
        var (store, leaderboard) = Create(community);
        AssertOwnEntry(Show(store, leaderboard, true, [Friend, Self]));
        Assert.Contains("Entries: vvtr count=0", Show(store, leaderboard, true, [Friend]));
        AssertOwnEntry(Show(store, leaderboard, false, []));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AmbiguousNamesDoNotSelectAnArbitrarySteamAccount(bool community)
    {
        var (store, leaderboard) = Create(community);
        var namesake = Friend with { Name = Self.Name };
        Show(store, leaderboard, true, [namesake, Self]);
        var global = Show(store, leaderboard, false, []);
        Assert.Contains("DxNitro_", global);
        Assert.Contains("NetworkId: ui64 value=0", global);
        Assert.DoesNotContain(FriendId.ToString(), global);
        Assert.DoesNotContain(OwnId.ToString(), global);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingSessionNameDoesNotAdoptTheFirstFriend(bool community)
    {
        var (store, leaderboard) = Create(community);
        Show(store, leaderboard, true, [Friend, Self], displayName: null);
        var global = Show(store, leaderboard, false, [], displayName: null);
        Assert.DoesNotContain("Maxam07", global);
        Assert.DoesNotContain(FriendId.ToString(), global);
        Assert.DoesNotContain(Player, global);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SteamSessionIdentityTakesPriorityOverDisplayName(bool community)
    {
        var player = $"steam:{OwnId}";
        var (store, leaderboard) = Create(community, player);
        var namesake = Friend with { Name = Self.Name };
        var renamedSelf = Self with { Name = "OldName" };
        var friends = Show(store, leaderboard, true, [namesake, renamedSelf], player);
        AssertOwnEntry(friends);
        AssertOwnEntry(Show(new Dirt4DailyStore(store.Export()), leaderboard, false, [], player));
    }

    [Fact]
    public void FriendsFilterDoesNotIncludeADifferentSteamAccountWithTheSameName()
    {
        var (store, leaderboard) = Create(false);
        Assert.True(store.Finish(leaderboard, "other", 90_000, Now, 504));
        store.BindPresence(leaderboard, "other", Friend with { Name = Self.Name });
        var friends = Show(store, leaderboard, true, [Self]);
        Assert.Contains("Entries: vvtr count=1", friends);
        AssertOwnEntry(friends);
        var global = Show(store, leaderboard, false, []);
        Assert.Contains("Entries: vvtr count=2", global);
        Assert.Contains($"NetworkId: ui64 value={FriendId}", global);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DifferentPlayersKeepSeparateResultsAndIdentities(bool community)
    {
        var (store, leaderboard) = Create(community);
        const string friend = "remote:test-friend";
        if (community) Dirt4RewardTests.Finish(store, store.Find(30_000_000)!, Now, friend);
        else Assert.True(store.Finish(leaderboard, friend, 90_000, Now, 504));
        Show(store, leaderboard, true, [Friend, Self]);
        Show(store, leaderboard, true, [Self, Friend], friend, Friend.Name);
        store = new Dirt4DailyStore(store.Export());
        var entries = store.Leaderboard(leaderboard, Player, true)!.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(Self.Name, Assert.Single(entries, e => e.Presence.NetworkId == OwnId).Presence.Name);
        Assert.Equal(Friend.Name, Assert.Single(entries, e => e.Presence.NetworkId == FriendId).Presence.Name);
        Assert.Contains("Entries: vvtr count=1", Show(store, leaderboard, true, [Self]));
        Assert.Contains("Entries: vvtr count=2", Show(store, leaderboard, false, []));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SessionRenameRetainsPreviouslyMatchedSteamIdentity(bool community)
    {
        var (store, leaderboard) = Create(community);
        AssertOwnEntry(Show(store, leaderboard, true, [Friend, Self]));
        var global = Show(store, leaderboard, false, [], displayName: "RenamedDriver");
        Assert.Contains("RenamedDriver", global);
        Assert.Contains($"NetworkId: ui64 value={OwnId}", global);
        Assert.DoesNotContain(FriendId.ToString(), global);
        store = new Dirt4DailyStore(store.Export());
        var friends = Show(store, leaderboard, true, [Friend, Self], displayName: "RenamedDriver");
        Assert.Contains("RenamedDriver", friends);
        Assert.Contains($"NetworkId: ui64 value={OwnId}", friends);
    }

    private static (Dirt4DailyStore Store, long Leaderboard) Create(bool community, string player = Player)
    {
        if (!community)
        {
            var store = new Dirt4DailyStore();
            const long leaderboard = 12345;
            Assert.True(store.Finish(leaderboard, player, 100_000, Now, 504));
            return (store, leaderboard);
        }
        var (daily, round) = Dirt4RewardTests.CreateRound(Now);
        Dirt4RewardTests.Finish(daily, round, Now, player);
        return (daily, round.LeaderboardId);
    }

    private static string Show(Dirt4DailyStore store, long leaderboard, bool friends,
        Dirt4LeaderboardPresence[] presences, string player = Player, string? displayName = "DxNitro_")
    {
        var bytes = EgoNetBinary.Dictionary(
            EgoNetBinary.Si64("LeaderboardId", leaderboard),
            EgoNetBinary.Bool("SortCumulative", true),
            EgoNetBinary.Vector("Presences", presences.Select(p => EgoNetBinary.DictValue(
                EgoNetBinary.Bool("IsCrossPlatform", p.IsCrossPlatform),
                EgoNetBinary.Si64("EgonetId", p.EgonetId),
                EgoNetBinary.Si64("AccountRef", p.AccountRef),
                EgoNetBinary.Ui64("NetworkId", p.NetworkId),
                EgoNetBinary.Dstr("Name", p.Name))).ToArray()));
        var body = new CapturedBody(bytes.Length, "", "", false, bytes, bytes);
        var response = Dirt4Leaderboard.Build(body, store, player, friends, displayName);
        var text = EgoNetBinaryFormatter.Format(response);
        Assert.DoesNotContain("parse-stopped", text);
        Assert.DoesNotContain("trailing-bytes", text);
        Assert.DoesNotContain("BoundToPlayer", text);
        return text;
    }

    private static void AssertOwnEntry(string text)
    {
        Assert.Contains("DxNitro_", text);
        Assert.Contains($"NetworkId: ui64 value={OwnId}", text);
        Assert.DoesNotContain("Maxam07", text);
        Assert.DoesNotContain(FriendId.ToString(), text);
    }
}
