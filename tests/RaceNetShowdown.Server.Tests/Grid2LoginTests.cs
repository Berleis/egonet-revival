using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Grid2LoginTests
{
    [Fact]
    public void Dirt4ReturnsCanonicalSteamIdentityInsteadOfSharedPlaceholder()
    {
        const ulong id = 76561198000000101;
        var bytes = EgoNetBinary.Dictionary(EgoNetBinary.Dstr("Name", "Player"));
        var response = Dirt4EgoNetPayloads.TryBuild("LoginService.Login",
            new(bytes.Length, "", "", false, bytes, bytes),
            new("session", 42, $"steam:{id}", "Player"), new Dictionary<string, string>(),
            new Dirt4DailyStore(), new Dirt4ProTour());
        Assert.Contains($"SteamId: ui64 value={id}", EgoNetBinaryFormatter.Format(response!.BodyBytes));
    }
    [Theory]
    [InlineData("LoginService.Login")]
    [InlineData("RaceNet.SignIn")]
    [InlineData("RaceNet.CreateAccount")]
    public void KnownSteamIdentityIsReturnedInsteadOfANameDerivedId(string function)
    {
        const long steamId = 76561198292921714;
        var bytes = EgoNetBinary.Dictionary(EgoNetBinary.Dstr("Name", "Renamed Player!"));
        var response = Grid2EgoNetPayloads.TryBuild(function,
            new CapturedBody(bytes.Length, "", "", false, bytes, bytes),
            new RaceNetSessionInfo("test", 42, $"steam:{steamId}", "Old Name"),
            new Dictionary<string, string>());

        Assert.NotNull(response);
        var result = new CapturedBody(response.BodyBytes.Length, "", "", false, response.BodyBytes, response.BodyBytes);
        Assert.Equal(steamId, EgoNetRequestParser.ReadTopLevelInteger(result, "SteamId"));
        Assert.Equal(42, EgoNetRequestParser.ReadTopLevelInteger(result, "PrincipalId"));
        Assert.Equal("Renamed Player!", EgoNetRequestParser.ReadTopLevelString(result, "Name"));
    }
}
