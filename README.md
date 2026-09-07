# EgoNet Revival

Open-source ASP.NET Core replacement server for discontinued Codemasters EgoNet/RaceNet services.

EgoNet Revival currently restores the DiRT Showdown RaceNet Challenge flow and is expanding GRID 2 RaceNet support on Steam/PC. The long-term goal is to keep a shared foundation for Codemasters games affected by discontinued EgoNet/RaceNet services.

Translations: [Portuguese (Brazil)](docs/README.pt-BR.md) | [Spanish](docs/README.es.md)

## Current Status

| Game | Platform | Status |
| --- | --- | --- |
| DiRT Showdown | Steam / PC | Public testing, Challenge flow functional |
| GRID 2 | Steam / PC | Early public testing, Global Challenge and weekly Rivals functional |
| F1 2018 | Steam / PC | Local memory activator for expired Events |

## Game Packages and Releases

This repository is a shared home for multiple EgoNet/RaceNet revival projects. Each game has its own package folder, installer, release notes, and release tag prefix.

| Game | Package | Release tag prefix | Player asset |
| --- | --- | --- | --- |
| DiRT Showdown | [`games/dirt-showdown`](games/dirt-showdown) | `dirt-showdown-v` | `EgoNet Revival - DiRT Showdown Installer.exe` |
| GRID 2 | [`games/grid-2`](games/grid-2) | `grid-2-v` | `EgoNet Revival - GRID 2 Installer.exe` |
| F1 2018 | [`games/f1-2018`](games/f1-2018) | `f1-2018-v` | `EgoNet Revival - F1 2018 Event Activator.exe` |

Game-specific GitHub Releases are created from tags. For example:

```sh
git tag dirt-showdown-v0.1.0
git push origin dirt-showdown-v0.1.0
```

That publishes the matching game package assets, checksums, README, and release notes without mixing them with other game packages.

Development helper scripts are grouped by game under [`tools`](tools). Player-facing release assets stay under [`games`](games) and GitHub Releases.

Currently working for DiRT Showdown:

- EgoNet/RaceNet login and session tick.
- Friends and Challenges overview.
- View Challenges.
- IssueChallenge after finishing an event.
- AcceptChallenge.
- UploadGhost and DownloadGhost.
- SubmitChallengeResult / SubmitPersonalRecord.
- Persistent SQLite storage for players, friends, challenges, ghosts, and results.
- Multiple challenges for the same friend.
- Challenge tally is calculated as a win/loss score between friends.
- Failed or forfeited challenge results now close the challenge and free the pending slot.
- Open challenges expire after 7 days.

Still being improved:

- Wider public testing with multiple real Steam accounts.
- Admin/dashboard tooling.
- Cleaner shared profiles for future games.
- GRID 2 Rival progression validation with more real multiplayer sessions.
- More per-game package polish as new Codemasters restorations are added.

## Install the DiRT Showdown Mod

This is the recommended flow for regular players who only want to use the hosted public server.

Requirements:

- Windows.
- Steam version of DiRT Showdown installed.
- Administrator access on the PC.
- The game must be closed before installing.

Steps:

1. Open the [DiRT Showdown releases page](https://github.com/Berleis/egonet-revival/releases?q=dirt-showdown-v&expanded=true).
2. Download `EgoNet Revival - DiRT Showdown Installer.exe` from the newest `dirt-showdown-v...` release.
3. Right-click the installer.
4. Click `Run as administrator`.
5. Choose the DiRT Showdown installation folder if it was not detected automatically.
6. Accept the Windows permission prompt.
7. Click `Install Mod`.
8. Open DiRT Showdown normally through Steam.
9. Enter RaceNet / Challenges in-game.

The release also includes `install-dirt-showdown-mod.cmd` as a command-line fallback. Development builds of that script live at [`games/dirt-showdown/install-dirt-showdown-mod.cmd`](games/dirt-showdown/install-dirt-showdown-mod.cmd). Developer helper scripts live under [`tools/dirt-showdown`](tools/dirt-showdown).

If you use the `.cmd` fallback and DiRT Showdown is not installed in the default Steam folder, run it from Command Prompt or PowerShell with your custom path:

```powershell
.\install-dirt-showdown-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\DiRT Showdown"
```

The installer is self-contained. It:

- points the RaceNet/EgoNet hostnames to the hosted server;
- downloads and installs the server root certificate;
- patches `showdown.exe` and `showdown_avx.exe` with that certificate;
- creates `.racenet-original.bak` backups before changing executables;
- flushes the Windows DNS cache;
- tests the HTTPS health endpoint.

To undo the executable patch, use Steam's `Verify integrity of game files` option for DiRT Showdown. If you also want to fully remove the redirect, delete the `EgoNet Revival DiRT Showdown` block from the Windows `hosts` file.

## Install the GRID 2 Mod

GRID 2 support is in early public testing. Use this installer when you want to try the hosted replacement server for RaceNet Global Challenge and Rivals.

Requirements:

- Windows.
- Steam version of GRID 2 installed.
- Administrator access on the PC.
- The game must be closed before installing.

Steps:

1. Open the [GRID 2 releases page](https://github.com/Berleis/egonet-revival/releases?q=grid-2-v&expanded=true).
2. Download `EgoNet Revival - GRID 2 Installer.exe` from the newest `grid-2-v...` release.
3. Right-click the installer.
4. Click `Run as administrator`.
5. Choose the GRID 2 installation folder if it was not detected automatically.
6. Click `Install Mod`.
7. Open GRID 2 through Steam.
8. Enter RaceNet / Rivals / Global Challenge in-game.

The release also includes `install-grid-2-mod.cmd` as a command-line fallback. Development builds of that script live at [`games/grid-2/install-grid-2-mod.cmd`](games/grid-2/install-grid-2-mod.cmd). Developer helper scripts live under [`tools/grid-2`](tools/grid-2).

Rivals are not hard-coded to specific testers. The server records normal multiplayer race result payloads and uses recent real opponents as priority Rival candidates.

Global Challenge and Rivals share the same weekly RaceNet cycle: Friday at 10:00 UTC. Rivals are assigned once per cycle and cached until the next reset.

## Use the F1 2018 Event Activator

F1 2018 can still load the old 2019 weekly event payloads, but their original timestamps mark both Events as expired. The F1 2018 Event Activator patches those loaded event timestamps in the running game process so the Events can be started and completed normally.

This is separate from the ASP.NET RaceNet server. It does not edit Steam stats, saves, leaderboard files, game assets, certificates, the Windows `hosts` file, or the F1 2018 executable on disk. The achievement is still triggered by F1 2018 itself after the in-game Event is completed.

Download the activator from the latest `f1-2018-v...` release:

https://github.com/Berleis/egonet-revival/releases?q=f1-2018-v&expanded=true

The recommended file is:

```text
EgoNet Revival - F1 2018 Event Activator.exe
```

Player flow:

1. Open F1 2018.
2. Enter the in-game Events screen so the two weekly event payloads are loaded.
3. Run `EgoNet Revival - F1 2018 Event Activator.exe`.
4. Return to the Events screen or switch between the current/previous Event if the screen was already open.
5. Start an Event and finish it.

The release also includes `activate-f1-2018-events.cmd` as a command-line fallback that runs the `.exe` from the same folder.

Development usage:

```bat
tools\f1-2018\activate-events.cmd
```

The helper script builds and runs:

```bat
src\F12018EventActivator\bin\Debug\net10.0\F12018EventActivator.exe
```

Useful direct options:

```bat
F12018EventActivator.exe --dry-run
F12018EventActivator.exe --days 60
F12018EventActivator.exe --wait 120
```

`--dry-run` reports what would be patched without writing memory. `--days` controls how far into the future the event expiration is moved. `--wait` waits for the F1 2018 process before applying the patch.

## How It Works

DiRT Showdown still tries to talk to the original RaceNet/EgoNet endpoints, but those services are no longer available. EgoNet Revival recreates the parts of that service that the game needs for Challenges.

The installer redirects the game's RaceNet hostnames to the replacement server and installs a local certificate authority that the game executable is patched to trust. After that, the game can make its normal HTTPS requests again.

The server receives the game's original binary EgoNet payloads, reads the requested service function, and returns compatible responses. For DiRT Showdown it stores player profiles, observed friends, issued challenges, ghost uploads, ghost downloads, and challenge results in SQLite. For GRID 2 it stores Global Challenge events, submitted scores, weekly Rival assignments, Rival session data, and recent multiplayer opponents used by Rivals.

This is not an achievement unlocker, save editor, or Steam stats editor. Achievements are still triggered by the games themselves when the restored or reactivated in-game flow is completed.

## Competitive Achievement Tracking Notice

This project is intended for game preservation and for restoring discontinued EgoNet/RaceNet functionality. It is not approved by Steam Hunters and should not be used for competing on Steam Hunters leaderboards or achievement validity tracking. The server does not directly unlock achievements, edit saves, or change Steam stats; it only restores the missing online service flow used by the game.

## Support the Project

EgoNet Revival is free and open source. Financial support is optional and helps cover public server hosting, storage, bandwidth, testing accounts, and future work on other Codemasters games affected by discontinued EgoNet/RaceNet services.

Supporting the project does not buy achievements, private access, priority unlocks, or special treatment. The goal is game preservation and restoring normal in-game online flows.

Support link: [PayPal](https://www.paypal.com/donate/?hosted_button_id=T495EZZJMZHEC)

## Development Setup

Requirements:

- .NET SDK compatible with the solution target framework.
- Visual Studio or the `dotnet` CLI.
- Administrator access if binding directly to ports `80` and `443`.
- DiRT Showdown installed locally for end-to-end testing.

Local flow:

1. Close DiRT Showdown.
2. Run `tools\dirt-showdown\patch-local.cmd` as Administrator.
3. Open `EgoNetRevival.sln` in Visual Studio.
4. Select the `RaceNetShowdown.Server` project.
5. Run the `RaceNetShowdown.Server` profile.
6. Open the game and enter the RaceNet screens.

The local server listens on:

- `http://127.0.0.1:80`
- `https://127.0.0.1:443`

By default, request/response payload capture is disabled:

```json
"CaptureRequests": false,
"RecordCalls": false
```

Use those settings only for local reverse-engineering or diagnostics. Captured payloads and local certificates should not be committed.

GRID 2 now has a player package, while `tools/grid-2` still contains the local discovery scripts used for request capture and diagnostics.

## Public Hosting

The public server can run on a small VPS with Docker.

```sh
git clone https://github.com/Berleis/egonet-revival.git
cd egonet-revival
docker compose up -d --build
```

`docker-compose.yml` exposes:

- `80:80`
- `443:443`

Persistent runtime data:

- `data/certs`: generated server/root certificates.
- `data/db/egonet-revival.db`: SQLite game state.

Do not commit those directories. The `data/certs` directory must stay stable on a public server because players patch their game with the root certificate generated by that deployment.

## Repository Structure

- `games/games.json`: manifest of supported game packages.
- `games/dirt-showdown`: DiRT Showdown package, GUI installer project, command-line fallback installer, and release notes.
- `games/grid-2`: GRID 2 package, GUI installer project, command-line fallback installer, and release notes.
- `.github/workflows/game-releases.yml`: packages per-game release assets from game-specific tags.
- `scripts/package-game-release.ps1`: packages one supported game for CI/release artifacts.
- `src/RaceNetShowdown.Server`: ASP.NET Core server used by DiRT Showdown and GRID 2.
- `src/RaceNetShowdown.Patcher`: developer patching tool used by the local scripts.
- `src/F12018EventActivator`: F1 2018 process-memory activator for the expired weekly Events.
- `src/RaceNetShowdown.TlsProbe`: TLS diagnostics tool for early connection debugging.
- `tools/dirt-showdown`: developer scripts for DiRT Showdown local patching, hosted-server patching, restore, status, certificate regeneration, and TLS diagnostics.
- `tools/grid-2`: GRID 2 local patching, discovery server, and TLS diagnostics scripts.
- `tools/f1-2018`: helper script for the F1 2018 Event Activator.

The internal project names still include `Showdown` because DiRT Showdown is the first implemented game. The intended direction is to extract shared interfaces and per-game profiles as more games are added.

## TLS Probe

`RaceNetShowdown.TlsProbe` is only a diagnostics tool. It is not part of the normal server path and is not needed by players.

It listens on ports `80` and `443` and prints the first bytes sent by the game, especially the `TLS ClientHello`. This helps diagnose failures that happen before a request reaches ASP.NET.
