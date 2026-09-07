# Game Packages

Each supported game has its own package folder, installer, notes, and release tag prefix.

| Game | Package | Release tag prefix | Release assets |
| --- | --- | --- | --- |
| DiRT Showdown | `games/dirt-showdown` | `dirt-showdown-v` | `EgoNet Revival - DiRT Showdown Installer.exe`, `install-dirt-showdown-mod.cmd` |
| GRID 2 | `games/grid-2` | `grid-2-v` | `EgoNet Revival - GRID 2 Installer.exe`, `install-grid-2-mod.cmd` |
| F1 2018 | `games/f1-2018` | `f1-2018-v` | `EgoNet Revival - F1 2018 Event Activator.exe`, `activate-f1-2018-events.cmd` |

GRID 2 is in early public testing. The package is available so testers can install against the hosted server, while Rival progression still needs more real multiplayer validation.

Future games should follow the same layout:

```text
games/<game-id>/
  README.md
  RELEASE_NOTES.md
  <primary-helper>.cmd
  installer/ or source project reference
```

Add the game to `games/games.json`, then add its tag prefix to `.github/workflows/game-releases.yml`.