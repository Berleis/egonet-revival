namespace RaceNetShowdown.Server.Data;

public static class RaceNetChallengeSeed
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromDays(7);

    private static readonly (string Name, int TimeMs, long Tally)[] Friends =
    [
        ("TheDebuter", 133320, 0),
        ("Rilleracer", 124690, 0),
        ("Daxterman20", 137870, 0),
        ("ericperez18", 114770, 0),
        ("rdelrahh85", 129540, 0)
    ];

    public static RaceNetChallengeSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var friends = Friends
            .Select((friend, index) => CreateFriend(index + 1, friend.Name, friend.TimeMs, friend.Tally, now))
            .ToArray();

        return new RaceNetChallengeSnapshot(
            HighChallengeId: friends.Max(value => value.ChallengeId),
            ChallengeCount: friends.Length,
            OverallTally: friends.Sum(value => value.Tally),
            BestResult: friends.Min(value => value.BestResult),
            Friends: friends);
    }

    public static RaceNetFriendChallenge CreateFriend(
        int index,
        string name,
        long bestResult,
        long tally,
        DateTimeOffset now)
    {
        var egonetId = 10_000L + index;
        var steamId = (76_561_198_000_000_000L + index).ToString();

        return new RaceNetFriendChallenge(
            EgonetId: egonetId,
            SteamId: steamId,
            Name: name,
            Presence: 1,
            ChallengeId: index,
            RaceEventId: 325,
            VehicleId: 152,
            BestResult: bestResult,
            YourBestResult: 0,
            Tally: tally,
            ExpiresAt: now.Add(ChallengeLifetime),
            GhostSlotId: index);
    }
}
