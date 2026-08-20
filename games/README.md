# Game Packages

Each supported game has its own package folder, installer, notes, and release tag prefix.

| Game | Package | Release tag prefix | Installer asset |
| --- | --- | --- | --- |
| DiRT Showdown | `games/dirt-showdown` | `dirt-showdown-v` | `install-dirt-showdown-mod.cmd` |

Future games should follow the same layout:

```text
games/<game-id>/
  README.md
  RELEASE_NOTES.md
  install-<game-id>-mod.cmd
```

Add the game to `games/games.json`, then add its tag prefix to `.github/workflows/game-releases.yml`.
