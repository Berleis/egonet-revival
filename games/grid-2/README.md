# GRID 2

This package restores early RaceNet functionality for GRID 2 on Steam/PC.

## Player Install

Download the GUI installer from the latest `grid-2-v...` release:

https://github.com/Berleis/egonet-revival/releases?q=grid-2-v&expanded=true

The recommended file is:

```text
EgoNet Revival - GRID 2 Installer.exe
```

Run it as Administrator with GRID 2 closed. Choose the game folder if it is not detected automatically, then click `Install Mod`.

The installer updates the Windows hosts file, installs the public server certificate, patches the GRID 2 executables, flushes DNS, and tests the public server connection.

The release also includes `install-grid-2-mod.cmd` as a command-line fallback. If the game is installed outside the default Steam folder and you use the `.cmd` fallback, pass the game folder manually:

```powershell
.\install-grid-2-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\grid 2"
```

## Included Assets

- `EgoNet Revival - GRID 2 Installer.exe`: recommended GUI installer for players.
- `install-grid-2-mod.cmd`: command-line fallback installer.
- `*.sha256`: checksums for installer assets.
- `README.md` and `RELEASE_NOTES.md`: package documentation.

The GUI installer project lives in `installer`. Developer helper scripts live in `tools/grid-2`.

## Current Status

- RaceNet login works.
- Global Challenge events are served by the replacement server and rotate weekly.
- Global Challenge leaderboards store submitted scores.
- Rivals loads weekly, custom, and social slots.
- Multiplayer race results are recorded from the game's normal `DataMining.EndEvent` payload.
- Recent multiplayer opponents are used as priority Rival candidates.
- Rival session data upload/download is stored for follow-up testing.

GRID 2 support is still in early public testing. The core RaceNet screens are working, but rival progression still needs more real multiplayer validation.

## Release Tags

GRID 2 releases use this tag format:

```text
grid-2-v0.1.0
```

Creating a tag with that prefix publishes the GRID 2 installer as a GitHub Release asset.