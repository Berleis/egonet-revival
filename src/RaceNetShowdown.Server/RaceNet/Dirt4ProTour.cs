using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Dirt4ProTour
{
    internal static byte[] GetSessionList(CapturedBody body) => EgoNetBinary.Dictionary(
        EgoNetBinary.Ui32("SessionLocation", Unsigned(body, "SessionLocation")),
        EgoNetBinary.Ui32("SessionRep", Unsigned(body, "SessionRep")),
        EgoNetBinary.Bool("IsAltHandling", AltHandling(body)),
        EgoNetBinary.Vector("SessionList"));

    internal static byte[] SessionConfig(DateTimeOffset now) => Dirt4CommunityEvents.BuildProTourConfig(now);

    internal static byte[] SubmitSession(CapturedBody body)
    {
        var data = SessionData(body);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Ui32("SessionRep", Unsigned(body, "SessionRep")),
            EgoNetBinary.Ui32("SessionLocation", Unsigned(body, "SessionLocation")),
            EgoNetBinary.Bool("IsAltHandling", AltHandling(body)));
    }

    internal static byte[] SubmitSessionScores(CapturedBody body)
    {
        var data = SessionData(body);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Vector("SessionScores"),
            EgoNetBinary.Bool("IsHost", EgoNetRequestParser.ReadTopLevelBoolean(body, "IsHost") ?? true));
    }

    internal static byte[] SessionStart(CapturedBody body)
    {
        var data = SessionData(body);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)),
            EgoNetBinary.Ui32("SessionPlayers", Unsigned(body, "SessionPlayers")));
    }

    internal static byte[] QuitSession(CapturedBody body)
    {
        var data = SessionData(body);
        return EgoNetBinary.Dictionary(
            EgoNetBinary.Blob("SessionData", data),
            EgoNetBinary.Ui32("SessionDataLen", checked((uint)data.Length)));
    }

    internal static byte[] PenalisePlayer(CapturedBody body) => EgoNetBinary.Dictionary(
        EgoNetBinary.Bool("IsAltHandling", AltHandling(body)));

    private static byte[] SessionData(CapturedBody body) =>
        EgoNetRequestParser.ReadTopLevelBlob(body, "SessionData") ?? [];

    private static bool AltHandling(CapturedBody body) =>
        EgoNetRequestParser.ReadTopLevelBoolean(body, "IsAltHandling") ??
        EgoNetRequestParser.ReadTopLevelBoolean(body, "isAltHandling") ?? false;

    private static uint Unsigned(CapturedBody body, string field) =>
        checked((uint)Math.Clamp(EgoNetRequestParser.ReadTopLevelInteger(body, field) ?? 0, 0, uint.MaxValue));
}
