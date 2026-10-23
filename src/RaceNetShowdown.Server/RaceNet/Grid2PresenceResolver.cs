using System.Globalization;
using RaceNetShowdown.Server.Data;

namespace RaceNetShowdown.Server.RaceNet;

internal static class Grid2PresenceResolver
{
    public static RaceNetPrincipal? ResolveSelf(
        RaceNetSessionInfo session,
        IReadOnlyList<RaceNetPrincipal> presences)
    {
        var knownSteamId = ResolveKnownSteamId(session);
        if (knownSteamId > 0)
        {
            return presences.FirstOrDefault(value => value.SteamId == knownSteamId)
                ?? new RaceNetPrincipal(knownSteamId, session.DisplayName);
        }

        var name = session.DisplayName.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        RaceNetPrincipal? candidate = null;
        foreach (var presence in presences)
        {
            if (presence.SteamId == 0 ||
                !string.Equals(presence.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (candidate is not null && candidate.SteamId != presence.SteamId)
            {
                return null;
            }

            candidate ??= presence;
        }

        return candidate;
    }

    public static ulong ResolveKnownSteamId(RaceNetSessionInfo session)
    {
        const string prefix = "steam:";
        return session.PlayerExternalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(session.PlayerExternalId[prefix.Length..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var steamId)
            ? steamId
            : 0;
    }
}
