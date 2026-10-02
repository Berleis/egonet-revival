# DiRT 4

This package restores discontinued RaceNet functionality for DiRT 4 on Steam/PC.

## Player Install

Download the GUI installer from the latest `dirt-4-v...` release:

https://github.com/Berleis/egonet-revival/releases?q=dirt-4-v&expanded=true

The recommended file is:

```text
EgoNet Revival - DiRT 4 Installer.exe
```

Run it as Administrator with DiRT 4 closed. Choose the game folder if it is not detected automatically, then click `Install Mod`.

The installer updates the Windows hosts file, installs the public server certificate, patches `dirt4.exe`, flushes DNS, and tests the public server connection. It creates `dirt4.exe.racenet-original.bak` before the first executable change.

The release also includes `install-dirt-4-mod.cmd` as a command-line fallback. If the game is installed outside the default Steam folder and you use the `.cmd` fallback, pass the game folder manually:

```powershell
.\install-dirt-4-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\DiRT 4"
```

## Included Assets

- `EgoNet Revival - DiRT 4 Installer.exe`: recommended GUI installer for players.
- `install-dirt-4-mod.cmd`: command-line fallback installer.
- `*.sha256`: checksums for installer assets.
- `README.md` and `RELEASE_NOTES.md`: package documentation.

The GUI installer project lives in `installer`. Operational developer scripts live in `tools/dirt-4`.

## Current Status

- RaceNet login and Community Events work through the replacement server.
- Two Daily, two Weekly and one Monthly slot follow calendar-based rotations.
- Event attempts, stage times, leaderboards, results and rewards persist across server restarts.
- Daily completion, expiration, rewards and the next rotation were validated in-game.
- Weekly and Monthly events use the same persisted multi-stage flow.
- Pro Tour remains experimental, but real four-player matchmaking and score submission have been validated in-game. Completed host sessions award persistent rank points, promote Tier 7 to Tier 6 at 7, promote Tier 6 to Tier 5 at 12, and demote Tier 6 at -16. Progression above Tier 5 still needs real captures.
- Jam Session uses Steam multiplayer independently of the EgoNet server.

DiRT 4 support is published as public testing. The active development catalogue contains 84 Daily Live choices, 72 Owners Club choices, 100 Delta Daily choices, 36 choices per Weekly slot and 36 Monthly choices. These are 364 route/vehicle/weather configurations. Original full events remain reference-only.

Delta Daily replaces the Owners Club on 12 dates of its 72-day cycle, keeping five active event slots. The 100 mapped career stage/vehicle combinations are consumed across successive cycles; every entry appears within nine 72-day cycles.

All 100 production mappings include an exact `TrackgenName` tuple validated in-game. Every production Delta Daily is a single-stage event.

Delta Standings uses two tiers: at or below the target is Tier 1; above it is Tier 2. Event Details still displays four prize ranges, as the original game does. At round opening the server reads the mapped career leaderboard, filters it to the event vehicle, orders one best result per player, takes at most the three fastest results and freezes their rounded mean with its source and sample count. With fewer than three players it averages the available results. Imported times are used only as a fallback if a mapped board is unexpectedly empty. One hundred of 108 production boards were matched uniquely through the profile's stage history; the eight ambiguous or missing matches are excluded. Older snapshots without a frozen target retain their previous policy. Rewards use the minimum credit value of the earned tier after expiration. Public rounds never include synthetic benchmark players or the 10-second development target.

Rally events rotate through H1 FWD, H2 FWD, H2 RWD, H3 RWD, R2, R5, Group A, Group B RWD, Group B 4WD, NR4/R4, Up to 2000cc and F2 Kit Car. Rallycross Daily events include Supercars, Super1600 and RX Lites. Selection is calendar-based, not random: the Weekly slots have different classes, routes and weather profiles, and at least one of the two Dailies has clear daytime conditions each day. This applies to newly issued rounds; saved rounds keep their original conditions until expiry.

Weather presets and vehicle IDs come from the installed DiRT 4 catalogue. Non-Delta tier times and payouts still use provisional capture-derived estimates. New combinations require in-game validation and feedback-driven tuning; automated checks do not prove gameplay or achievement compatibility.

Community Event state uses the same configured database as the other server profiles. Each new round stores its complete event definition, so catalog updates and restarts cannot change an issued event. Existing rounds and pending rewards retain their original definitions.

## Pro Tour Testing

The server keeps waiting-room advertisements in memory, separated by Gamer/Simulation handling. Searches prefer matching location and nearby reputation without blocking players in other regions. Advertisements disappear when the host starts or quits, starts another search, or stops sending the existing login heartbeat for five minutes. A server restart clears waiting rooms, not saved Community Events or leaderboards.

Pro Tour uses a deterministic daily rotation that resets at 10:00 UTC. Each ranked event contains two captured Your Stage routes and one rally vehicle class. Twelve two-stage route pairs are crossed with all 12 rally classes over a 144-day cycle, while time of day and clear, cloudy or rain conditions also vary. Every server instance returns the same configuration for the entire daily window. Player points and tiers are never changed by rotation selection.

After a server update, cancel the old searches. For the first multiplayer test, let one player start searching, wait about ten seconds, then have the others search using the same handling mode. This checks discovery of an already advertised room before testing simultaneous searches. Steam still handles joining and live lobby membership; the server does not synthesize players or award progress from a search alone.

A completed host session is accepted only after the matching advertised room started with 4-8 players. Players are ranked by `ScoreMS`; the recovered table is derived from the number that started, from `+3, +1, -1, -3` for four racers through `+7, +5, +3, +1, -1, -3, -5, -7` for eight. Fewer classified scores may be accepted after a disconnect without shrinking the survivors' table, but the disconnected player's penalty still awaits a real capture. Processed session data is retained for replay protection. Progress, event count, threshold crossing and remainder points persist in the configured database.

Set `RaceNet:CaptureDirt4ProTourScores` to `true` during the remaining validation. Despite the legacy option name, bounded raw captures are written for `SessionStart`, `SubmitSessionScores`, `QuitSession`, and `PenalisePlayer`, with the call kind in each filename. Retire representation, disconnect identity/penalty, and progression above Tier 5 remain unverified.

## Release Tags

DiRT 4 releases use this tag format:

```text
dirt-4-v0.1.0
```

Creating a tag with that prefix publishes the DiRT 4 installer as a prerelease GitHub asset.
