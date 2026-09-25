using Microsoft.AspNetCore.Http;
using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4ProTourTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 21, 0, 0, TimeSpan.Zero);
    private static readonly byte[] LobbyData = [1, 2, 3, 4, 5, 6, 7, 8];

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AdvertisedRoomIsVisibleToOtherPlayersWithTheClientSchema(bool gamer)
    {
        var tour = new Dirt4ProTour();
        var submit = tour.SubmitSession(Advertisement(gamer), "host", Now);
        Assert.Equal(LobbyData, EgoNetRequestParser.ReadTopLevelBlob(Body(submit), "SessionData"));
        foreach (var guest in new[] { "guest-one", "guest-two", "guest-three" })
        {
            var result = tour.GetSessionList(Search(gamer), guest, Now);
            Assert.Equal(EgoNetBinary.Dictionary(
                EgoNetBinary.Ui32("SessionLocation", 44), EgoNetBinary.Ui32("SessionRep", 100),
                EgoNetBinary.Bool("IsAltHandling", gamer), EgoNetBinary.Vector("SessionList",
                    EgoNetBinary.DictValue(EgoNetBinary.Si32("SessionDataLen", 8),
                        EgoNetBinary.Si32("SessionLocation", 44), EgoNetBinary.Si32("HostReputation", 100),
                        EgoNetBinary.Si32("SessionPlayers", 1), EgoNetBinary.Si32("SessionTier", 7),
                        EgoNetBinary.Blob("SessionData", LobbyData), EgoNetBinary.Bool("isAltHandling", gamer)))), result);
            Assert.DoesNotContain("parse-stopped", Format(result));
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(20)]
    public void NativeReaderRequiresSignedRoomFieldsButUnsignedSearchEnvelope(int rooms)
    {
        var tour = new Dirt4ProTour();
        for (var i = 0; i < rooms; i++)
            tour.SubmitSession(Advertisement(data: BitConverter.GetBytes((long)i + 1)), "host-" + i, Now);
        var result = Format(tour.GetSessionList(Search(), "guest", Now));
        var split = result.IndexOf("SessionList: vvtr", StringComparison.Ordinal);
        Assert.True(split >= 0);
        var envelope = result[..split];
        var entries = result[split..];
        Assert.Contains("SessionLocation: ui32 value=44", envelope);
        Assert.Contains("SessionRep: ui32 value=100", envelope);
        Assert.Contains($"SessionList: vvtr count={rooms}", entries);
        foreach (var field in new[] { "SessionDataLen", "SessionLocation", "HostReputation", "SessionPlayers", "SessionTier" })
        {
            Assert.Equal(rooms, entries.Split(field + ": si32 value=", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(field + ": ui32", entries);
        }
    }

    [Fact]
    public void GamerAndSimulationNeverShareRoomsButRegionsAndReputationCan()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(true), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(false), "guest", Now));
        AssertOne(tour.GetSessionList(Search(true, uint.MaxValue, uint.MaxValue), "guest", Now));
    }

    [Fact]
    public void RepeatedAdvertisementUpdatesTheRoomInsteadOfDuplicatingIt()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        tour.SubmitSession(Advertisement(location: 55, reputation: 200), "host", Now.AddMinutes(1));
        var result = tour.GetSessionList(Search(), "guest", Now.AddMinutes(1));
        AssertOne(result);
        Assert.Contains("HostReputation: si32 value=200", Format(result));
        Assert.Contains("SessionLocation: si32 value=55", Format(result));
    }

    [Fact]
    public void AnotherClientCannotTakeOwnershipOfAnAdvertisedRoom()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        tour.SubmitSession(Advertisement(), "guest", Now);
        tour.QuitSession(Connection(), "guest", Now);
        AssertOne(tour.GetSessionList(Search(), "viewer", Now));
        tour.QuitSession(Connection(), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), "viewer", Now));
    }

    [Theory]
    [InlineData("quit")] [InlineData("start")] [InlineData("scores")]
    public void OnlyTheOwnerCanRemoveTheMatchingAdvertisement(string action)
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        Close(tour, action, "guest", Connection());
        Close(tour, action, "host", Connection([9, 8, 7, 6]));
        AssertOne(tour.GetSessionList(Search(), "viewer", Now));
        Close(tour, action, "host", Connection());
        AssertEmpty(tour.GetSessionList(Search(), "viewer", Now));
        Close(tour, action, "host", Connection());
    }

    [Fact]
    public void ReplacementRoomSurvivesDelayedQuitForTheOldRoom()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        tour.SubmitSession(Advertisement(data: [9, 8, 7, 6]), "host", Now);
        tour.QuitSession(Connection(), "host", Now);
        AssertOne(tour.GetSessionList(Search(), "guest", Now));
        tour.QuitSession(Connection([9, 8, 7, 6]), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), "guest", Now));
    }

    [Fact]
    public void HostStartingAnotherSearchAbandonsItsOldRoom()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), "host", Now));
        AssertEmpty(tour.GetSessionList(Search(), "guest", Now));
    }

    [Fact]
    public void OnlyTheHostsTicksKeepTheRoomAliveAndExpiredRoomsCannotRevive()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        for (var minute = 1; minute <= 12; minute++)
        {
            tour.Tick("host", Now.AddMinutes(minute));
            AssertOne(tour.GetSessionList(Search(), "guest", Now.AddMinutes(minute)));
        }
        var expiredAt = Now.AddMinutes(12) + Dirt4ProTour.SessionTimeout;
        tour.Tick("guest", expiredAt.AddSeconds(-1));
        tour.Tick("host", expiredAt);
        AssertEmpty(tour.GetSessionList(Search(), "guest", expiredAt));
    }

    [Fact]
    public void RoomsAreEphemeralAndNeverRestoredAfterServerRestart()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), "host", Now);
        AssertOne(tour.GetSessionList(Search(), "guest", Now));
        AssertEmpty(new Dirt4ProTour().GetSessionList(Search(), "guest", Now));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(8, 7)] [InlineData(8, -1)] [InlineData(1025, 1025)]
    public void InvalidConnectionDataIsNeverAdvertised(int size, int declaredLength)
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(data: new byte[size], declaredLength: declaredLength), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), "guest", Now));
    }

    [Fact]
    public void MissingIdentityOrFiltersCannotAdvertiseOrReadRooms()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(), null, Now);
        AssertEmpty(tour.GetSessionList(Search(), "guest", Now));
        tour.SubmitSession(Connection(), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), "guest", Now));
        tour.SubmitSession(Advertisement(), "host", Now);
        AssertEmpty(tour.GetSessionList(Search(), null, Now));
        AssertEmpty(tour.GetSessionList(Body(EgoNetBinary.Dictionary()), "guest", Now));
    }

    [Fact]
    public void LowercaseHandlingFlagIsAccepted()
    {
        var tour = new Dirt4ProTour();
        tour.SubmitSession(Advertisement(lowercase: true), "host", Now);
        AssertOne(tour.GetSessionList(Search(), "guest", Now));
    }

    [Fact]
    public async Task ConcurrentAdvertisementsAreDeduplicatedAndSearchIsBounded()
    {
        var tour = new Dirt4ProTour();
        await Task.WhenAll(Enumerable.Range(1, 32).Select(index => Task.Run(() =>
        {
            var body = Advertisement(data: BitConverter.GetBytes((long)index));
            tour.SubmitSession(body, "host-" + index, Now);
            tour.SubmitSession(body, "host-" + index, Now);
        })));
        Assert.Contains($"SessionList: vvtr count={Dirt4ProTour.MaxSearchResults}", Format(tour.GetSessionList(Search(), "guest", Now)));
        for (var index = 2; index <= 32; index++)
            tour.QuitSession(Connection(BitConverter.GetBytes((long)index)), "host-" + index, Now);
        AssertOne(tour.GetSessionList(Search(), "guest", Now));
    }

    [Fact]
    public void ResponderSharesRegistryAcrossRequestsButNotAcrossServers()
    {
        var options = new RaceNetOptions();
        var server = new RaceNetResponder(options);
        Call(server, "LiveLadder.SubmitSession", Advertisement(), "host");
        AssertOne(Call(server, "LiveLadder.GetSessionList", Search(), "guest"));
        AssertEmpty(Call(new RaceNetResponder(options), "LiveLadder.GetSessionList", Search(), "guest"));
        Call(server, "LiveLadder.QuitSession", Connection(), "guest");
        AssertOne(Call(server, "LiveLadder.GetSessionList", Search(), "guest"));
        Call(server, "LiveLadder.SessionStart", Connection(), "host");
        AssertEmpty(Call(server, "LiveLadder.GetSessionList", Search(), "guest"));
    }

    internal static CapturedBody Search(bool gamer = true, uint location = 44, uint reputation = 100) =>
        Body(EgoNetBinary.Dictionary(EgoNetBinary.Ui32("SessionLocation", location),
            EgoNetBinary.Ui32("SessionRep", reputation), EgoNetBinary.Bool("IsAltHandling", gamer)));

    internal static CapturedBody Advertisement(bool gamer = true, uint location = 44, uint reputation = 100,
        byte[]? data = null, int? declaredLength = null, bool lowercase = false)
    {
        data ??= LobbyData;
        return Body(EgoNetBinary.Dictionary(EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Si32("SessionDataLen", declaredLength ?? data.Length),
            EgoNetBinary.Ui32("SessionRep", reputation), EgoNetBinary.Ui32("SessionLocation", location),
            EgoNetBinary.Bool(lowercase ? "isAltHandling" : "IsAltHandling", gamer)));
    }

    internal static CapturedBody Connection(byte[]? data = null) => Body(EgoNetBinary.Dictionary(
        EgoNetBinary.Blob("SessionData", data ?? LobbyData),
        EgoNetBinary.Ui32("SessionDataLen", checked((uint)(data ?? LobbyData).Length)),
        EgoNetBinary.Ui32("SessionPlayers", 4)));

    private static void Close(Dirt4ProTour tour, string action, string owner, CapturedBody body)
    {
        var response = action switch
        {
            "quit" => tour.QuitSession(body, owner, Now),
            "start" => tour.SessionStart(body, owner, Now),
            _ => tour.SubmitSessionScores(body, owner, Now)
        };
        Assert.DoesNotContain("parse-stopped", Format(response));
    }

    private static byte[] Call(RaceNetResponder responder, string function, CapturedBody body, string owner)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = "/dirt4";
        request.Headers["X-EgoNet-Function"] = function;
        var session = new RaceNetSessionInfo(owner, 1, "player-" + owner, owner);
        return responder.BuildLocalResponse(request, body, session, null).BodyBytes;
    }

    internal static CapturedBody Body(byte[] bytes) => new(bytes.Length, "", "", false, bytes, bytes);
    internal static string Format(byte[] bytes) => EgoNetBinaryFormatter.Format(bytes);
    internal static void AssertEmpty(byte[] result) => Assert.Contains("SessionList: vvtr count=0", Format(result));
    internal static void AssertOne(byte[] result) => Assert.Contains("SessionList: vvtr count=1", Format(result));
}
