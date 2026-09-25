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
- Pro Tour is experimental while real multiplayer scoring and promotion still need validation.
- Jam Session uses Steam multiplayer independently of the EgoNet server.

DiRT 4 support is published as public testing. The active development catalogue contains 28 Daily Live choices, 24 Owners Club choices, two choices per Weekly slot and three Monthly choices. Original full events are temporarily excluded from new rounds; their data remains available for existing rounds and as source material for the new combinations. These are 59 slot choices, not 59 newly recovered roads or historic events. Tier times and payouts remain provisional for feedback-driven tuning, and the new combinations still require in-game validation before deployment.

Community Event state uses the same configured database as the other server profiles. Each new round stores its complete event definition, so catalog updates and restarts cannot change an issued event. Existing rounds and pending rewards retain their original definitions.

## Release Tags

DiRT 4 releases use this tag format:

```text
dirt-4-v0.1.0
```

Creating a tag with that prefix publishes the DiRT 4 installer as a prerelease GitHub asset.
