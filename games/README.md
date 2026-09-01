# Game Packages

Each supported game has its own package folder, installer, notes, and release tag prefix.

| Game | Package | Release tag prefix | Installer assets |
| --- | --- | --- | --- |
| DiRT Showdown | `games/dirt-showdown` | `dirt-showdown-v` | `EgoNet Revival - DiRT Showdown Installer.exe`, `install-dirt-showdown-mod.cmd` |

GRID 2 is in local discovery only. It does not have a player package or release tag yet; current developer scripts live in `tools/grid-2`.

Future games should follow the same layout:

```text
games/<game-id>/
  README.md
  RELEASE_NOTES.md
  install-<game-id>-mod.cmd
  installer/
```

Add the game to `games/games.json`, then add its tag prefix to `.github/workflows/game-releases.yml`.
