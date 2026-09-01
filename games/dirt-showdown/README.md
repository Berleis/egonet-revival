# DiRT Showdown

This package restores the discontinued RaceNet Challenge flow for DiRT Showdown on Steam/PC.

## Player Install

Download the GUI installer from the latest `dirt-showdown-v...` release:

https://github.com/Berleis/egonet-revival/releases?q=dirt-showdown-v&expanded=true

The recommended file is:

```text
EgoNet Revival - DiRT Showdown Installer.exe
```

Run it as Administrator with DiRT Showdown closed. Choose the game folder if it is not detected automatically, then click `Install Mod`.

The installer updates the Windows hosts file, installs the public server certificate, patches the DiRT Showdown executables, flushes DNS, and tests the public server connection.

The release also includes `install-dirt-showdown-mod.cmd` as a command-line fallback. If the game is installed outside the default Steam folder and you use the `.cmd` fallback, pass the game folder manually:

```powershell
.\install-dirt-showdown-mod.cmd 142.93.206.37 "D:\SteamLibrary\steamapps\common\DiRT Showdown"
```

## Included Assets

- `EgoNet Revival - DiRT Showdown Installer.exe`: recommended GUI installer for players.
- `install-dirt-showdown-mod.cmd`: command-line fallback installer.
- `*.sha256`: checksums for installer assets.
- `README.md` and `RELEASE_NOTES.md`: package documentation.

The GUI installer project lives in `installer`. Developer helper scripts live in `tools/dirt-showdown`.

## Current Fixes

- Challenge tally now keeps counting dominated Challenges after they are completed.
- The installer avoids the previous hosts-file write issue and is packaged automatically as a per-game release asset.

## Release Tags

DiRT Showdown releases use this tag format:

```text
dirt-showdown-v0.1.0
```

Creating a tag with that prefix publishes the DiRT Showdown installer as a GitHub Release asset.
