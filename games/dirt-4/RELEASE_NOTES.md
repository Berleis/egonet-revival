# DiRT 4 release notes

## Unreleased

- Fixed Pro Tour session-list entries using unsigned integer tags. The game's per-room reader requires signed `si32` for all five numeric fields, while the top-level search fields remain `ui32`.
- Fixed Pro Tour returning an empty session list even after hosts advertised rooms. Waiting rooms are now shared between requests, separated by handling mode, and serialized using the client reader's fields and 20-entry limit.
- Added host-owned removal on start/quit, duplicate protection, heartbeat expiry and matchmaking diagnostics without session tokens or raw connection blobs. Waiting rooms are intentionally not restored after a server restart.
- Validated matchmaking, Steam lobby joining and score submission with four real players. The captured `SessionScores` schema is now parsed and ranked by `ScoreMS`.
- Added persistent Pro Tour points, event counts and duplicate-session protection. Recovered progression promotes Tier 7 to Tier 6 at `7`, Tier 6 to Tier 5 at `12`, demotes Tier 6 at `-16`, and carries points beyond promotion thresholds.
- Added the complete 4-8 player scoring matrix. Awards use the number recorded at `SessionStart`, so classified survivors retain the original table if a later disconnect removes a submitted score.
- Expanded bounded Pro Tour captures to label `SessionStart`, `SubmitSessionScores`, `QuitSession`, and `PenalisePlayer`; retire representation and disconnect penalties still require real validation.
- Restored a daily Pro Tour event rotation at 10:00 UTC. Ranked events now use two captured Your Stage routes, rotate all 12 rally classes across 12 route pairs over 144 days, and vary valid time-of-day and weather presets without modifying player progression.
- Added unit and four-client HTTP/SQLite restart tests covering host ownership, score parsing, rank order, duplicate rejection, promotion and persistence.
- Fixed friend-filter requests borrowing another player's name and Steam ID. Career and Community leaderboards now use the player's matched identity across filter changes and restarts; legacy associations are rebuilt instead of trusting the first friend in the list.
- Added a deterministic expanded Community Event rotation using captured rally stages and circuit IDs verified in the local DiRT 4 catalogue.
- Expanded the active rotation to 84 Daily Live, 72 Owners Club, 36 choices per Weekly slot and 36 Monthly choices, across 12 rally classes and three rallycross classes. These reuse existing routes with different vehicles and conditions, not 264 different roads.
- Added clear daylight, cloudy and rain profiles using DiRT 4 preset IDs. The two Weekly slots use different classes and conditions; each calendar day offers a clear daytime Daily or Owners Club event.
- Kept original full events out of new-round selection and preserved already issued rounds. Class/weather changes still use provisional capture-derived tier times and payouts for player-feedback tuning.
- Added full per-round event snapshots, preserving issued events, attempts, leaderboards and rewards across catalog updates.
- Preserved the original daily / Monday / first-of-month 10:00 UTC reset boundaries.
- Added automated catalog, calendar, legacy migration, reward, leaderboard and HTTP/SQLite restart tests.
- New combinations, especially cross-country Monthly events, still require in-game validation. Reference tier times for recomposed events are estimates, not recovered original RaceNet rankings.

## 0.1.0-public-testing

- Added the DiRT 4 RaceNet profile and captured `/RP15/STEAM/1.0/` endpoint dispatch.
- Added Community Events, Live Ladder, leaderboard, ghost upload, login, localisation, mailbox, and data-mining acknowledgements.
- Added the administrator installer for hosts, root certificate and `dirt4.exe` patching.
- Persisted Community Event rounds, attempts, results and rewards in the shared server database.
- Published Community Events for public testing with calendar-based Daily, Weekly and Monthly rotations.
- Kept Pro Tour experimental while multiplayer scoring and promotion still require multi-player validation.
- Documented Jam Session as Steam multiplayer functionality outside the EgoNet server.

Known limitations:

- Community Event templates currently repeat the captured original events at each calendar rotation.
- Pro Tour matchmaking and progression through Tier 5 are implemented. Retire/disconnect behavior and progression above Tier 5 still need real multiplayer validation.
