# DiRT 4 release notes

## Unreleased

- Fixed friend-filter requests borrowing another player's name and Steam ID. Career and Community leaderboards now use the player's matched identity across filter changes and restarts; legacy associations are rebuilt instead of trusting the first friend in the list.
- Added a deterministic expanded Community Event rotation using captured rally stages and circuit IDs verified in the local DiRT 4 catalogue.
- Temporarily removed the original full events from new-round selection, leaving 28 Daily Live, 24 Owners Club, two choices per Weekly slot and three Monthly choices. Already issued rounds remain unchanged; tier times and payouts remain provisional for player feedback.
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
- Pro Tour matchmaking can reach the host flow, but scoring, promotion and its achievements are not yet production-supported.
