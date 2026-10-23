using System.Buffers.Binary;
using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Grid2RequestParserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsCapturedGlobalEventPresenceVectorWithoutInferringSelf(bool previousEvent)
    {
        // These are the original GetEvent / GetPreviousEvent request field names and wire types.
        var fields = new List<EgoNetField> { EgoNetBinary.Si16("GameId", 4) };
        if (previousEvent) fields.Add(EgoNetBinary.Si64("RaceNetEventId", 698));
        fields.Add(EgoNetBinary.Vector("Presence",
            Principal(76561197981344778, "Atomizer_Zero"),
            Principal(76561198152241364, "Island Yoshi"),
            Principal(76561198145790184, "Pasadena O'Possum"),
            Principal(76561198200000000, "👽N0~SK!LL®")));
        if (previousEvent) fields.Add(EgoNetBinary.Si64("SaveGameId", 1424543569));
        var body = Body(EgoNetBinary.Dictionary(fields.ToArray()));

        var result = EgoNetRequestParser.ReadGrid2Presences(body);

        Assert.Equal(new[] { "Atomizer_Zero", "Island Yoshi", "Pasadena O'Possum", "👽N0~SK!LL®" },
            result.Select(value => value.Name));
        Assert.Equal(76561198145790184UL, result[2].SteamId);
        Assert.Null(EgoNetRequestParser.ReadChallengeContext(body).Presence);
        Assert.Empty(EgoNetRequestParser.ReadPrincipals(body));
    }

    [Fact]
    public void SkipsUnknownFieldsBeforeAfterAndInsidePresenceEntries()
    {
        var bytes = EgoNetBinary.Dictionary(
            EgoNetBinary.Dict("Metadata", EgoNetBinary.Vector("Flags",
                EgoNetBinary.DictValue(EgoNetBinary.Bool("Enabled", true)))),
            EgoNetBinary.Vector("Presence", EgoNetBinary.DictValue(
                EgoNetBinary.Si64("EgonetId", 123),
                EgoNetBinary.Ui64("SteamId", 76561198145790184),
                EgoNetBinary.Dict("Extra", EgoNetBinary.Dstr("Value", "ignored")),
                EgoNetBinary.Dstr("Name", "A Friend!"))),
            EgoNetBinary.Blob("ExtraData", [1, 2, 3]));

        var result = EgoNetRequestParser.ReadGrid2Presences(Body(bytes));

        Assert.Equal(new RaceNetPrincipal(76561198145790184, "A Friend!"), Assert.Single(result));
    }

    [Fact]
    public void OmitsZeroAndConflictingIdsAndCollapsesExactDuplicates()
    {
        var body = Body(EgoNetBinary.Dictionary(EgoNetBinary.Vector("Presence",
            Principal(0, "Unknown"),
            Principal(1, "Friend"), Principal(1, "Friend"),
            Principal(2, "First Name"), Principal(2, "Other Name"),
            Principal(3, "Another Friend"))));

        var result = EgoNetRequestParser.ReadGrid2Presences(body);

        Assert.Equal(new[] { new RaceNetPrincipal(1, "Friend"), new RaceNetPrincipal(3, "Another Friend") }, result);
    }

    [Fact]
    public void RejectsEveryTruncatedPrefixIncludingAnIncompleteLastName()
    {
        var bytes = EgoNetBinary.Dictionary(EgoNetBinary.Vector("Presence", Principal(1, "Long Friend Name!")));

        for (var length = 0; length < bytes.Length; length++)
            Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body(bytes[..length])));

        Assert.Single(EgoNetRequestParser.ReadGrid2Presences(Body(bytes)));
    }

    [Fact]
    public void RejectsMalformedCountsAndTrailingData()
    {
        var valid = EgoNetBinary.Dictionary(EgoNetBinary.Vector("Presence", Principal(1, "Friend")));
        var invalidRoot = valid.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(invalidRoot.AsSpan(4), -1);
        var invalidVector = valid.ToArray();
        var vectorTag = invalidVector.AsSpan().IndexOf("vvtr"u8);
        BinaryPrimitives.WriteInt32LittleEndian(invalidVector.AsSpan(vectorTag + 4), -1);

        Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body(invalidRoot)));
        Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body(invalidVector)));
        Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body([.. valid, 0])));
    }

    [Fact]
    public void RejectsExcessiveNestingInUnknownFields()
    {
        var nested = EgoNetBinary.Bool("Leaf", true);
        for (var i = 0; i < 70; i++) nested = EgoNetBinary.Dict("Nested", nested);
        var bytes = EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Presence", Principal(1, "Friend")), nested);

        Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body(bytes)));
    }

    [Fact]
    public void RejectsIncompleteEntriesWrongShapesAndRepeatedPresenceFields()
    {
        var invalidBodies = new[]
        {
            EgoNetBinary.Dictionary(EgoNetBinary.Vector("Presence",
                Principal(1, "Friend"), EgoNetBinary.DictValue(EgoNetBinary.Ui64("SteamId", 2)))),
            EgoNetBinary.Dictionary(EgoNetBinary.Dict("Presence",
                EgoNetBinary.Ui64("SteamId", 1), EgoNetBinary.Dstr("Name", "Friend"))),
            EgoNetBinary.Dictionary(
                EgoNetBinary.Vector("Presence", Principal(1, "Friend")),
                EgoNetBinary.Vector("Presence", Principal(2, "Other")))
        };

        foreach (var bytes in invalidBodies)
            Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(Body(bytes)));
    }

    [Fact]
    public void PreservesShowdownPrincipalsAndSinglePresenceSemantics()
    {
        var body = Body(EgoNetBinary.Dictionary(
            EgoNetBinary.Vector("Principals", Principal(1, "Friend")),
            EgoNetBinary.Dict("Presence", EgoNetBinary.Ui64("SteamId", 2), EgoNetBinary.Dstr("Name", "Caller"))));

        Assert.Equal(new RaceNetPrincipal(1, "Friend"), Assert.Single(EgoNetRequestParser.ReadPrincipals(body)));
        Assert.Equal(new RaceNetPrincipal(2, "Caller"), EgoNetRequestParser.ReadChallengeContext(body).Presence);
        Assert.Empty(EgoNetRequestParser.ReadGrid2Presences(body));
    }

    private static Action<BinaryWriter> Principal(ulong steamId, string name) => EgoNetBinary.DictValue(
        EgoNetBinary.Ui64("SteamId", steamId), EgoNetBinary.Dstr("Name", name));

    private static CapturedBody Body(byte[] bytes) => new(bytes.Length, "", "", false, bytes, bytes);
}
