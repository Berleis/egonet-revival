using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Grid2PresenceResolverTests
{
    private const ulong OwnId = 76561198000000101;
    private const ulong FriendId = 76561198000000102;
    private static readonly RaceNetPrincipal Self = new(OwnId, "Driver");
    private static readonly RaceNetPrincipal Friend = new(FriendId, "Friend");

    [Fact]
    public void EstablishedSteamIdentitySurvivesRenameAndConflictingName()
    {
        var renamedSelf = Self with { Name = "New Driver" };
        var namesake = Friend with { Name = "Driver" };

        Assert.Equal(renamedSelf,
            Grid2PresenceResolver.ResolveSelf(Session($"steam:{OwnId}"), [namesake, renamedSelf]));
    }

    [Fact]
    public void MissingEstablishedSteamIdentityDoesNotAdoptNamesake()
    {
        var namesake = Friend with { Name = "Driver" };

        Assert.Equal(Self,
            Grid2PresenceResolver.ResolveSelf(Session($"steam:{OwnId}"), [namesake]));
        Assert.Equal(Self,
            Grid2PresenceResolver.ResolveSelf(Session($"steam:{OwnId}"), []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UniqueNameResolvesIndependentlyOfVectorPosition(bool selfFirst)
    {
        RaceNetPrincipal[] presences = selfFirst ? [Self, Friend] : [Friend, Self];

        Assert.Equal(Self, Grid2PresenceResolver.ResolveSelf(Session(), presences));
    }

    [Fact]
    public void NameMatchingTrimsWhitespaceAndIgnoresCase()
    {
        var presence = Self with { Name = " dRiVeR " };

        Assert.Equal(presence,
            Grid2PresenceResolver.ResolveSelf(Session(name: " DRIVER "), [presence, Friend]));
    }

    [Fact]
    public void DifferentSteamIdsWithTheSameNameAreAmbiguous()
    {
        var namesake = Friend with { Name = " DRIVER " };

        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(), [Self, namesake]));
        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(), [namesake, Self]));
    }

    [Fact]
    public void DuplicatePresencesForOneSteamIdRemainUnambiguous()
    {
        Assert.Equal(Self, Grid2PresenceResolver.ResolveSelf(Session(),
            [Self, Self with { Name = " DRIVER " }, Friend]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptySessionNameDoesNotSelectAnArbitraryPresence(string name)
    {
        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(name: name),
            [Friend, Self, new RaceNetPrincipal(OwnId, name)]));
    }

    [Fact]
    public void MissingMatchingNameOrPositiveSteamIdDoesNotSelectACandidate()
    {
        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(), []));
        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(), [Friend]));
        Assert.Null(Grid2PresenceResolver.ResolveSelf(Session(),
            [new RaceNetPrincipal(0, "Driver"), new RaceNetPrincipal(OwnId, "")]));
    }

    [Theory]
    [InlineData("steam:76561198000000101", OwnId)]
    [InlineData("STEAM:76561198000000101", OwnId)]
    [InlineData("steam:0", 0UL)]
    [InlineData("name:driver", 0UL)]
    [InlineData("steam:bad", 0UL)]
    [InlineData("steam:-1", 0UL)]
    [InlineData("steam:18446744073709551616", 0UL)]
    public void KnownSteamIdRequiresAValidPositiveSteamExternalId(string externalId, ulong expected)
    {
        Assert.Equal(expected, Grid2PresenceResolver.ResolveKnownSteamId(Session(externalId)));
    }

    private static RaceNetSessionInfo Session(string externalId = "name:driver", string name = "Driver")
        => new("session", 123, externalId, name);
}
