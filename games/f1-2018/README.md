# F1 2018

This package reactivates the expired in-game Events in F1 2018 on Steam/PC.

## Player Use

Download the activator from the latest `f1-2018-v...` release:

https://github.com/Berleis/egonet-revival/releases?q=f1-2018-v&expanded=true

The recommended file is:

```text
EgoNet Revival - F1 2018 Event Activator.exe
```

Run it after F1 2018 has loaded the Events screen. The activator patches the two loaded 2019 weekly Event payloads in memory so they can be started and completed.

Recommended flow:

1. Open F1 2018 through Steam.
2. Enter the in-game Events screen.
3. Run `EgoNet Revival - F1 2018 Event Activator.exe`.
4. Return to Events or switch between current/previous Event if the screen was already open.
5. Start an Event and finish it.

## Included Assets

- `EgoNet Revival - F1 2018 Event Activator.exe`: recommended self-contained activator for players.
- `activate-f1-2018-events.cmd`: command-line fallback that runs the activator from the same folder.
- `*.sha256`: checksums for activator assets.
- `README.md` and `RELEASE_NOTES.md`: package documentation.

## What It Changes

The activator writes only to the running `F1_2018.exe` or `F1_2018_dx12.exe` process. It does not edit Steam stats, saves, leaderboard files, game assets, certificates, the Windows `hosts` file, or the F1 2018 executable on disk.

The achievement is still triggered by F1 2018 itself after the in-game Event is completed.

Useful direct options:

```bat
"EgoNet Revival - F1 2018 Event Activator.exe" --dry-run
"EgoNet Revival - F1 2018 Event Activator.exe" --days 60
"EgoNet Revival - F1 2018 Event Activator.exe" --wait 120
```

`--dry-run` reports what would be patched without writing memory. `--days` controls how far into the future the event expiration is moved. `--wait` waits for the F1 2018 process before applying the patch.

## Release Tags

F1 2018 releases use this tag format:

```text
f1-2018-v0.1.0
```

Creating a tag with that prefix publishes the F1 2018 Event Activator as a GitHub Release asset.
