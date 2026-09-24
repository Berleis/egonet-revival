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

DiRT 4 support is published as public testing. Event templates currently repeat the captured original events at each rotation. Community Event state uses the same configured database as the other server profiles.

## Release Tags

DiRT 4 releases use this tag format:

```text
dirt-4-v0.1.0
```

Creating a tag with that prefix publishes the DiRT 4 installer as a prerelease GitHub asset.
